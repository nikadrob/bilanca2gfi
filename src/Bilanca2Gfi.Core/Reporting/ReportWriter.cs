using System.Text;
using Bilanca2Gfi.Core.Gfi;
using Bilanca2Gfi.Core.Model;

namespace Bilanca2Gfi.Core.Reporting;

public sealed record RunInfo(
    string PdfPath, string TemplatePath, string ConfigPath, string? PriorPath, string OutputPath, decimal? ProfitTax);

/// <summary>Plain-text report: diagnostics, then every non-zero AOP with the konta it was built from.</summary>
public static class ReportWriter
{
    public static string Build(TrialBalance tb, GfiResult result, DiagnosticBag diag, RunInfo run, IReadOnlyDictionary<int, string> labels)
    {
        var sb = new StringBuilder();
        sb.AppendLine("IZVJEŠTAJ O IZRADI GFI-POD");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"Tvrtka:            {tb.CompanyName}");
        sb.AppendLine($"Razdoblje:         {tb.DateFrom:dd.MM.yyyy} – {tb.DateTo:dd.MM.yyyy}");
        sb.AppendLine($"Bruto bilanca:     {run.PdfPath}");
        sb.AppendLine($"Predložak:         {run.TemplatePath}");
        sb.AppendLine($"Pravila mapiranja: {run.ConfigPath}");
        sb.AppendLine($"Prethodna godina:  {result.PriorSource}");
        sb.AppendLine($"Porez na dobit:    {(run.ProfitTax is { } t ? t.ToString("N2") : "nije zadan (0,00)")}");
        sb.AppendLine($"Izlaz:             {run.OutputPath}");
        sb.AppendLine();

        sb.AppendLine($"KONTROLE: {diag.Count(Severity.Error)} grešaka, {diag.Count(Severity.Warning)} upozorenja");
        sb.AppendLine(new string('-', 78));
        foreach (var d in diag.Items.OrderByDescending(d => d.Severity)) sb.AppendLine(d.ToString());
        sb.AppendLine();

        foreach (var (title, from, to) in new[] { ("BILANCA", 1, 127), ("RAČUN DOBITI I GUBITKA", 128, 229), ("DODATNI PODACI", 230, 299) })
        {
            sb.AppendLine(title);
            sb.AppendLine(new string('-', 78));
            sb.AppendLine($"{"AOP",-5} {"Pozicija",-44} {"Prethodna",14} {"Tekuća",14}");
            for (var aop = from; aop <= to; aop++)
            {
                var prior = result.Prior[aop];
                var current = result.Current[aop];
                if (prior == 0 && current == 0) continue;
                var label = labels.TryGetValue(aop, out var l) ? l : "";
                if (label.Length > 44) label = label[..43] + "…";
                var marker = AopStructure.IsComputed(aop) ? "Σ" : " ";
                sb.AppendLine($"{aop:000}{marker}  {label,-44} {prior,14:N2} {current,14:N2}");

                if (!AopStructure.IsComputed(aop) && result.Current.Trace.TryGetValue(aop, out var trace))
                    foreach (var item in trace.OrderBy(x => x.Konto))
                        sb.AppendLine($"        {item.Konto,-8} {Trim(item.Naziv, 48),-48} {item.Value,14:N2}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Σ = zbirna pozicija (formula u obrascu). Ispod svake pozicije navedena su konta tekuće godine iz kojih je nastala.");
        return sb.ToString();
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
