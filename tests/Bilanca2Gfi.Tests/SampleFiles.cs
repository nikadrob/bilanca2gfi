using Bilanca2Gfi.Core.Model;
using Bilanca2Gfi.Core.Parsing;

namespace Bilanca2Gfi.Tests;

/// <summary>Locates the sample files in the repository root (bruto bilanca PDF, FINA template, config).</summary>
internal static class SampleFiles
{
    public static readonly string Root = FindRoot();

    public static string Pdf => Path.Combine(Root, "bilanca ambara.pdf");
    public static string Template => Path.Combine(Root, "templates", "GFI-POD-6.0.2.xls");
    public static string Config => Path.Combine(Root, "config", "ambra.json");
    public static string PriorGfi => Path.Combine(Root, "gfi_ambra_water_sol.xls");

    private static readonly Lazy<TrialBalance> Parsed = new(() => new TrialBalancePdfParser().Parse(Pdf, new DiagnosticBag()));
    public static TrialBalance TrialBalance => Parsed.Value;

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bilanca2Gfi.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (Bilanca2Gfi.sln) not found.");
    }
}
