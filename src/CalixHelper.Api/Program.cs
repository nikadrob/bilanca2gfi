using System.Globalization;
using System.Text.Json.Serialization;
using CalixHelper.Api.Features.Bilanca2Gfi;

// Messages produced by the Core library format amounts with the current culture (1.234,56).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("hr-HR");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<Bilanca2GfiOptions>(builder.Configuration.GetSection(Bilanca2GfiOptions.Section));
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 50 * 1024 * 1024);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// The React app (web/, built by Vite into wwwroot) is served by the same host.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapBilanca2Gfi();

// Client-side routes (e.g. /bilanca2gfi) fall back to the SPA; unknown /api routes stay 404.
app.MapFallback("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Entry point marker for integration tests (WebApplicationFactory).</summary>
public partial class Program;
