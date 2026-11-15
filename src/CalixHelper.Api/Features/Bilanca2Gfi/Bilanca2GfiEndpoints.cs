using System.Globalization;
using Bilanca2Gfi.Core.Parsing;
using Bilanca2Gfi.Core.Services;
using Microsoft.Extensions.Options;

namespace CalixHelper.Api.Features.Bilanca2Gfi;

public sealed class Bilanca2GfiOptions
{
    public const string Section = "Bilanca2Gfi";

    /// <summary>Folder for uploaded inputs and generated files (one subfolder per run). Relative to the content root.</summary>
    public string JobsPath { get; set; } = "App_Data/jobs";

    /// <summary>Runs older than this are deleted.</summary>
    public int RetentionDays { get; set; } = 7;

    public long MaxUploadBytes { get; set; } = 20 * 1024 * 1024;
}

public sealed record DiagnosticDto(string Severity, string Area, string Message);
public sealed record FileDto(string Name, string Url);

public sealed record Bilanca2GfiResponse(
    string JobId,
    string Company,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    int AccountCount,
    string ConfigName,
    string PdfFile,
    string? PriorFile,
    GfiTotals Totals,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    FileDto Xls,
    FileDto Report);

/// <summary>
/// POST /api/bilanca2gfi (multipart): pdf, prethodniGfi?, porez?, zaposleni?, zaposleniSati?
/// GET  /api/bilanca2gfi/{jobId}/xls | /izvjestaj – download of the generated files.
/// </summary>
public static class Bilanca2GfiEndpoints
{
    private const string PdfName = "bruto-bilanca.pdf";
    private const string PriorName = "prethodni-gfi.xls";

    public static IEndpointRouteBuilder MapBilanca2Gfi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bilanca2gfi").WithTags("Bilanca2Gfi").RequireAuthorization();

        group.MapPost("/", CreateAsync)
            .DisableAntiforgery() // the auth cookie is SameSite=Strict, so cross-site posts carry no credentials
            .Accepts<IFormFile>("multipart/form-data");

        group.MapGet("/{jobId}/xls", (string jobId, IOptions<Bilanca2GfiOptions> o, IWebHostEnvironment env) =>
            Download(jobId, "*.xls", "application/vnd.ms-excel", o.Value, env));
        group.MapGet("/{jobId}/izvjestaj", (string jobId, IOptions<Bilanca2GfiOptions> o, IWebHostEnvironment env) =>
            Download(jobId, "*.izvjestaj.txt", "text/plain; charset=utf-8", o.Value, env));

        return app;
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request, IOptions<Bilanca2GfiOptions> options, IWebHostEnvironment env, ILoggerFactory loggers, CancellationToken ct)
    {
        var log = loggers.CreateLogger("Bilanca2Gfi");
        var opt = options.Value;
        if (!request.HasFormContentType) return Problem("Zahtjev mora biti multipart/form-data.");

        var form = await request.ReadFormAsync(ct);
        var pdf = form.Files.GetFile("pdf");
        var prior = form.Files.GetFile("prethodniGfi");

        if (pdf is null || pdf.Length == 0) return Problem("Odaberi PDF bruto bilance.");
        if (!HasExtension(pdf, ".pdf")) return Problem("Bruto bilanca mora biti PDF datoteka.");
        if (prior is { Length: > 0 } && !HasExtension(prior, ".xls")) return Problem("Prošlogodišnji GFI mora biti .xls datoteka (FINA obrazac).");
        if (pdf.Length > opt.MaxUploadBytes || prior?.Length > opt.MaxUploadBytes) return Problem("Datoteka je prevelika.");

        if (!TryParseNumber(form["porez"], out var tax)) return Problem("Porez na dobit nije ispravan broj (npr. 1.234,56).");
        if (!TryParseNumber(form["zaposleni"], out var employees)) return Problem("Prosječan broj zaposlenih nije ispravan broj.");
        if (!TryParseNumber(form["zaposleniSati"], out var employeesByHours)) return Problem("Broj zaposlenih prema satima rada nije ispravan broj.");

        var jobsRoot = JobsRoot(opt, env);
        CleanupOldJobs(jobsRoot, opt.RetentionDays, log);

        var jobId = Guid.NewGuid().ToString("N");
        var jobDir = Directory.CreateDirectory(Path.Combine(jobsRoot, jobId)).FullName;
        var pdfPath = Path.Combine(jobDir, PdfName);
        await Save(pdf, pdfPath, ct);
        if (!TrialBalancePdfParser.LooksLikeTrialBalance(pdfPath))
        {
            TryDelete(jobDir);
            return Problem($"'{pdf.FileName}' nije bruto bilanca po kontima (u PDF-u nema naslova \"Brutto bilanca po kontima\").");
        }

        string? priorPath = null;
        if (prior is { Length: > 0 })
        {
            priorPath = Path.Combine(jobDir, PriorName);
            await Save(prior, priorPath, ct);
        }

        try
        {
            var generator = new GfiGenerator(AppContext.BaseDirectory);
            var r = await Task.Run(() => generator.Run(new GfiRequest
            {
                Folder = jobDir,
                PdfPath = pdfPath,
                PriorPath = priorPath,
                NoPrior = priorPath == null,
                ProfitTax = tax,
                Employees = employees,
                EmployeesByHours = employeesByHours,
            }), ct);

            log.LogInformation("GFI {JobId} izrađen za {Company}: {Errors} grešaka, {Warnings} upozorenja", jobId, r.Company, r.ErrorCount, r.WarningCount);

            var baseUrl = $"/api/bilanca2gfi/{jobId}";
            return Results.Ok(new Bilanca2GfiResponse(
                jobId, r.Company, r.DateFrom, r.DateTo, r.AccountCount, r.ConfigName,
                pdf.FileName, prior?.FileName, r.Totals, r.ErrorCount, r.WarningCount,
                r.Diagnostics.Select(d => new DiagnosticDto(d.Severity.ToString(), d.Area, d.Message)).ToList(),
                new FileDto(Path.GetFileName(r.OutputPath), $"{baseUrl}/xls"),
                new FileDto(Path.GetFileName(r.ReportPath), $"{baseUrl}/izvjestaj")));
        }
        catch (GfiInputException ex)
        {
            TryDelete(jobDir);
            return Problem(ex.Message.Replace(AppContext.BaseDirectory, "")); // no server paths in user messages
        }
        catch (Exception ex)
        {
            log.LogError(ex, "GFI {JobId} nije izrađen", jobId);
            return Problem("Obrada nije uspjela: " + ex.Message, StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static IResult Download(string jobId, string pattern, string contentType, Bilanca2GfiOptions opt, IWebHostEnvironment env)
    {
        // jobId comes from the URL: accept only our own GUID format, so it can never escape the jobs folder.
        if (!Guid.TryParseExact(jobId, "N", out _)) return Results.NotFound();
        var outDir = Path.Combine(JobsRoot(opt, env), jobId, "izlaz");
        if (!Directory.Exists(outDir)) return Results.NotFound();

        var file = Directory.GetFiles(outDir, pattern).FirstOrDefault(f => pattern != "*.xls" || !f.EndsWith(".izvjestaj.txt"));
        return file is null
            ? Results.NotFound()
            : Results.File(file, contentType, Path.GetFileName(file));
    }

    // ------------------------------------------------------------------ helpers

    private static string JobsRoot(Bilanca2GfiOptions opt, IWebHostEnvironment env) =>
        Path.GetFullPath(Path.Combine(env.ContentRootPath, opt.JobsPath));

    private static IResult Problem(string message, int status = StatusCodes.Status400BadRequest) =>
        Results.Problem(title: message, statusCode: status);

    private static bool HasExtension(IFormFile file, string extension) =>
        string.Equals(Path.GetExtension(file.FileName), extension, StringComparison.OrdinalIgnoreCase);

    private static async Task Save(IFormFile file, string path, CancellationToken ct)
    {
        await using var target = File.Create(path);
        await file.CopyToAsync(target, ct);
    }

    /// <summary>Empty → null. Accepts "12.345,67", "12345,67" and "12345.67".</summary>
    private static bool TryParseNumber(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var s = text.Trim();
        var normalized = s.Contains(',') ? s.Replace(".", "").Replace(',', '.') : s;
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) || d < 0) return false;
        value = d;
        return true;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort; old jobs are cleaned up anyway */ }
    }

    private static void CleanupOldJobs(string jobsRoot, int retentionDays, ILogger log)
    {
        if (!Directory.Exists(jobsRoot)) return;
        var limit = DateTime.UtcNow.AddDays(-retentionDays);
        foreach (var dir in Directory.GetDirectories(jobsRoot))
        {
            try
            {
                if (Directory.GetCreationTimeUtc(dir) < limit) Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Stari posao {Dir} nije obrisan", dir);
            }
        }
    }
}
