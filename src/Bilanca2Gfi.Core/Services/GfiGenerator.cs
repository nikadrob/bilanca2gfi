using System.Text;
using Bilanca2Gfi.Core.Excel;
using Bilanca2Gfi.Core.Gfi;
using Bilanca2Gfi.Core.Mapping;
using Bilanca2Gfi.Core.Model;
using Bilanca2Gfi.Core.Parsing;
using Bilanca2Gfi.Core.Reporting;

namespace Bilanca2Gfi.Core.Services;

/// <summary>Input of one GFI run. Only <see cref="Folder"/> is required; everything else has a default.</summary>
public sealed record GfiRequest
{
    /// <summary>Working folder: holds the bruto bilanca PDF and (optionally) last year's GFI; output goes to its "izlaz" subfolder.</summary>
    public required string Folder { get; init; }
    public string? PdfPath { get; init; }
    public string? PriorPath { get; init; }
    public bool NoPrior { get; init; }
    public string? TemplatePath { get; init; }
    public string? ConfigPath { get; init; }
    public string? OutputPath { get; init; }
    public decimal? ProfitTax { get; init; }
    public decimal? Employees { get; init; }
    public decimal? EmployeesByHours { get; init; }
}

public sealed record GfiTotals(
    decimal Aktiva, decimal Pasiva, decimal AktivaPrethodna,
    decimal Prihodi, decimal Rashodi, decimal DobitPrijePoreza, decimal Porez, decimal DobitRazdoblja);

public sealed record GfiRunResult
{
    public required string Company { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public required int AccountCount { get; init; }
    public required string PdfPath { get; init; }
    public required string? PriorPath { get; init; }
    public required string ConfigName { get; init; }
    public required string OutputPath { get; init; }
    public required string ReportPath { get; init; }
    public required GfiTotals Totals { get; init; }
    public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }
    public int ErrorCount => Diagnostics.Count(d => d.Severity == Severity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == Severity.Warning);
}

/// <summary>Problem with the request itself (missing files, ambiguous input) – shown to the user as is.</summary>
public sealed class GfiInputException(string message) : Exception(message);

/// <summary>Bruto bilanca PDF → GFI-POD .xls + text report. Shared by the console app and the web API.</summary>
public sealed class GfiGenerator(string resourceDirectory)
{
    /// <summary>Folder containing "templates" and "config" (by default the application folder).</summary>
    public string ResourceDirectory { get; } = resourceDirectory;

    public GfiRunResult Run(GfiRequest request)
    {
        var workDir = Path.GetFullPath(request.Folder);
        if (!Directory.Exists(workDir)) throw new GfiInputException($"Mapa ne postoji: {workDir}");

        var diag = new DiagnosticBag();

        // --- 1. Inputs
        var pdfPath = request.PdfPath ?? FindTrialBalancePdf(workDir);
        if (!File.Exists(pdfPath)) throw new GfiInputException($"PDF ne postoji: {pdfPath}");
        var templatePath = request.TemplatePath ?? Path.Combine(ResourceDirectory, "templates", "GFI-POD-6.0.2.xls");
        if (!File.Exists(templatePath)) throw new GfiInputException($"Predložak ne postoji: {templatePath}");

        var tb = new TrialBalancePdfParser().Parse(pdfPath, diag);

        var configPath = request.ConfigPath ?? FindConfig(Path.Combine(ResourceDirectory, "config"), tb.CompanyName);
        var config = MappingConfig.Load(configPath);
        if (request.Employees != null) config.ReferentnaStranica.ZaposleniProsjek = request.Employees;
        if (request.EmployeesByHours != null) config.ReferentnaStranica.ZaposleniSati = request.EmployeesByHours;

        var priorPath = request.NoPrior ? null : request.PriorPath ?? FindPriorGfi(workDir, diag);
        var prior = priorPath == null ? null : PriorGfiReader.Read(priorPath, tb.Year, diag);

        // --- 2. Calculation
        var result = new GfiCalculator().Calculate(tb, config, request.ProfitTax, prior, diag);

        // --- 3. Output
        var outputPath = request.OutputPath ?? Path.Combine(workDir, "izlaz", $"GFI_{SafeName(tb.CompanyName)}_{tb.Year}.xls");
        GfiTemplateWriter.Write(templatePath, outputPath, tb, result, diag, prior, config.ReferentnaStranica);

        var labels = GfiTemplateInfo.ReadLabels(templatePath);
        var run = new RunInfo(pdfPath, templatePath, configPath, priorPath, outputPath, request.ProfitTax);
        var reportPath = Path.ChangeExtension(outputPath, ".izvjestaj.txt");
        File.WriteAllText(reportPath, ReportWriter.Build(tb, result, diag, run, labels), new UTF8Encoding(true));

        var c = result.Current;
        return new GfiRunResult
        {
            Company = tb.CompanyName,
            DateFrom = tb.DateFrom,
            DateTo = tb.DateTo,
            AccountCount = tb.Accounts.Count,
            PdfPath = pdfPath,
            PriorPath = priorPath,
            ConfigName = config.Naziv,
            OutputPath = outputPath,
            ReportPath = reportPath,
            Totals = new GfiTotals(
                Aktiva: c[65], Pasiva: c[126], AktivaPrethodna: result.Prior[65],
                Prihodi: c[180], Rashodi: c[181],
                DobitPrijePoreza: c[183] - c[184], Porez: c[185], DobitRazdoblja: c[187] - c[188]),
            Diagnostics = diag.Items.OrderByDescending(d => d.Severity).ToList(),
        };
    }

    private static string FindTrialBalancePdf(string dir)
    {
        var candidates = Directory.GetFiles(dir, "*.pdf").Where(TrialBalancePdfParser.LooksLikeTrialBalance).ToList();
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new GfiInputException($"U mapi {dir} nema PDF-a s bruto bilancom."),
            _ => throw new GfiInputException("Pronađeno više PDF-ova s bruto bilancom (" + string.Join(", ", candidates.Select(Path.GetFileName)) + "). Ostavi samo jedan ili ga zadaj izričito."),
        };
    }

    private static string FindConfig(string configDir, string companyName)
    {
        if (!Directory.Exists(configDir)) throw new GfiInputException($"Mapa s pravilima ne postoji: {configDir}");
        foreach (var file in Directory.GetFiles(configDir, "*.json"))
        {
            var cfg = MappingConfig.Load(file);
            if (cfg.Prepoznavanje.Any(p => companyName.Contains(p, StringComparison.OrdinalIgnoreCase))) return file;
        }
        throw new GfiInputException($"Nema pravila mapiranja za tvrtku '{companyName}'. Dodaj JSON u {configDir}.");
    }

    /// <summary>The only .xls in the top folder (generated files go to the "izlaz" subfolder).</summary>
    private static string? FindPriorGfi(string dir, DiagnosticBag diag)
    {
        var candidates = Directory.GetFiles(dir, "*.xls");
        if (candidates.Length == 1) return candidates[0];
        if (candidates.Length > 1)
            diag.Warn("Prethodna godina", "U mapi je više .xls datoteka – prošlogodišnji GFI nije odabran automatski.");
        return null;
    }

    private static string SafeName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .TakeWhile(w => !w.StartsWith("d.o.o", StringComparison.OrdinalIgnoreCase) && !w.StartsWith("d.d", StringComparison.OrdinalIgnoreCase));
        var safe = new string(string.Join('_', words).Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        return safe.Length > 0 ? safe : "tvrtka";
    }
}
