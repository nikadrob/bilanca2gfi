using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Bilanca2Gfi.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _jobs = Path.Combine(Path.GetTempPath(), $"gfi-api-tests-{Guid.NewGuid():N}");

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.UseSetting("Bilanca2Gfi:JobsPath", _jobs));
    }

    public void Dispose()
    {
        if (Directory.Exists(_jobs)) Directory.Delete(_jobs, recursive: true);
    }

    private static MultipartFormDataContent Form(string pdfPath, string? priorPath = null, params (string Name, string Value)[] fields)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(File.ReadAllBytes(pdfPath)), "pdf", Path.GetFileName(pdfPath) } };
        if (priorPath != null) form.Add(new ByteArrayContent(File.ReadAllBytes(priorPath)), "prethodniGfi", Path.GetFileName(priorPath));
        foreach (var (name, value) in fields) form.Add(new StringContent(value), name);
        return form;
    }

    [Fact]
    public async Task Creates_gfi_and_serves_the_files()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/bilanca2gfi",
            Form(SampleFiles.Pdf, SampleFiles.PriorGfi, ("porez", "1.000,00"), ("zaposleni", "21")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AMBRA WATER SOLUTIONS d.o.o.", json.GetProperty("company").GetString());
        Assert.Equal(51_344.99m, json.GetProperty("totals").GetProperty("dobitRazdoblja").GetDecimal());
        Assert.Equal(json.GetProperty("totals").GetProperty("aktiva").GetDecimal(), json.GetProperty("totals").GetProperty("pasiva").GetDecimal());

        var xls = await client.GetAsync(json.GetProperty("xls").GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, xls.StatusCode);
        Assert.Equal("application/vnd.ms-excel", xls.Content.Headers.ContentType?.MediaType);
        Assert.Equal("GFI_AMBRA_WATER_SOLUTIONS_2026.xls", xls.Content.Headers.ContentDisposition?.FileNameStar ?? xls.Content.Headers.ContentDisposition?.FileName);

        var report = await client.GetStringAsync(json.GetProperty("report").GetProperty("url").GetString());
        Assert.Contains("IZVJEŠTAJ O IZRADI GFI-POD", report);
    }

    [Fact]
    public async Task Rejects_a_pdf_that_is_not_a_trial_balance()
    {
        var response = await _factory.CreateClient().PostAsync("/api/bilanca2gfi", Form(Path.Combine(SampleFiles.Root, "kontni_plan.pdf")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("nije bruto bilanca", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rejects_invalid_numbers()
    {
        var response = await _factory.CreateClient().PostAsync("/api/bilanca2gfi", Form(SampleFiles.Pdf, null, ("porez", "abc")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/bilanca2gfi/not-a-guid/xls")]
    [InlineData("/api/bilanca2gfi/00000000000000000000000000000000/xls")]
    public async Task Download_of_unknown_job_is_404(string url)
    {
        var response = await _factory.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
