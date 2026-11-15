using System.Globalization;
using System.Text.RegularExpressions;
using Bilanca2Gfi.Core.Model;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Bilanca2Gfi.Core.Parsing;

/// <summary>
/// Reads a "Brutto bilanca po kontima" PDF. Amounts are assigned to the eight columns by their x-position
/// (right-aligned numbers), because empty columns are simply missing from the text stream.
/// </summary>
public sealed partial class TrialBalancePdfParser
{
    private const double LineTolerance = 2.5;
    private const double KontoMaxLeft = 56;     // konto codes start at x≈10
    private const double AmountMinLeft = 180;   // amounts start right of the name column
    private const double ContinuationMaxGap = 30; // a wrapped name line is at most this far below its row

    [GeneratedRegex(@"^\d{5,6}$")] private static partial Regex KontoRegex();
    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})*,\d{2}$")] private static partial Regex AmountRegex();
    [GeneratedRegex(@"^\d{2}\.\d{2}\.\d{4}$")] private static partial Regex DateRegex();

    /// <summary>Quick check that a PDF is a bruto bilanca (used to pick the input file from a folder).</summary>
    public static bool LooksLikeTrialBalance(string path)
    {
        try
        {
            using var doc = PdfDocument.Open(path);
            var text = string.Join(" ", doc.GetPage(1).GetWords().Select(w => w.Text));
            return text.Contains("bilanca", StringComparison.OrdinalIgnoreCase)
                && text.Contains("kontima", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public TrialBalance Parse(string path, DiagnosticBag diag)
    {
        using var doc = PdfDocument.Open(path);
        TrialBalance? tb = null;
        AccountLine? lastAccount = null;
        double lastRowY = double.NaN;
        Amounts? grandTotalPending = null;

        foreach (var page in doc.GetPages())
        {
            var lines = GroupIntoLines(page.GetWords());
            tb ??= ParseHeader(lines);

            var columnAnchors = FindColumnAnchors(lines) ?? DefaultAnchors;
            var bodyStarted = false;
            lastRowY = double.NaN;

            foreach (var line in lines)
            {
                if (!bodyStarted)
                {
                    // Body starts after the "D P D P D P D P" header line.
                    if (line.Words.Count == 8 && line.Words.All(w => w.Text is "D" or "P")) bodyStarted = true;
                    continue;
                }

                var words = line.Words;
                var amountWords = words.Where(w => w.BoundingBox.Left >= AmountMinLeft && AmountRegex().IsMatch(w.Text)).ToList();

                if (grandTotalPending != null)
                {
                    // Second line of SVEUKUPNO carries the credit-side totals.
                    if (amountWords.Count > 0) { FillAmounts(grandTotalPending, amountWords, columnAnchors); }
                    tb.GrandTotal = grandTotalPending;
                    grandTotalPending = null;
                    continue;
                }

                var kontoWord = words.FirstOrDefault(w => w.BoundingBox.Left < KontoMaxLeft && KontoRegex().IsMatch(w.Text));
                if (kontoWord != null)
                {
                    var amounts = new Amounts();
                    FillAmounts(amounts, amountWords, columnAnchors);
                    var name = string.Join(" ", words
                        .Where(w => w != kontoWord && w.BoundingBox.Left >= KontoMaxLeft && w.BoundingBox.Left < AmountMinLeft)
                        .OrderBy(w => w.BoundingBox.Left)
                        .Select(w => w.Text));
                    lastAccount = new AccountLine(kontoWord.Text, name, amounts);
                    tb.Accounts.Add(lastAccount);
                    lastRowY = line.Y;
                    continue;
                }

                var first = words[0].Text;
                if (first == "Grupa:" && words.Count > 1)
                {
                    var amounts = new Amounts();
                    FillAmounts(amounts, amountWords, columnAnchors);
                    tb.Groups.Add(new GroupTotal(words[1].Text, amounts));
                    lastAccount = null;
                    continue;
                }

                if (first.StartsWith("SVEUKUPNO", StringComparison.OrdinalIgnoreCase))
                {
                    grandTotalPending = new Amounts();
                    FillAmounts(grandTotalPending, amountWords, columnAnchors);
                    lastAccount = null;
                    continue;
                }

                if (first is "Klasa:" or "RAZRED" || amountWords.Count > 0)
                {
                    lastAccount = null; // class subtotal lines (validated through groups instead)
                    continue;
                }

                // Wrapped account name: text only, directly below the account row (also across a page break).
                if (lastAccount != null && (double.IsNaN(lastRowY) || lastRowY - line.Y <= ContinuationMaxGap))
                {
                    var extra = string.Join(" ", words.Where(w => w.BoundingBox.Left >= KontoMaxLeft && w.BoundingBox.Left < AmountMinLeft).Select(w => w.Text));
                    if (extra.Length > 0)
                    {
                        var idx = tb.Accounts.Count - 1;
                        lastAccount = lastAccount with { Naziv = (lastAccount.Naziv + " " + extra).Trim() };
                        tb.Accounts[idx] = lastAccount;
                        lastRowY = line.Y;
                    }
                }
            }
        }

        if (tb == null) throw new InvalidDataException("PDF nema stranica.");
        if (grandTotalPending != null) tb.GrandTotal = grandTotalPending;
        Validate(tb, diag);
        return tb;
    }

    // ---------------------------------------------------------------- header

    private static TrialBalance ParseHeader(List<TextLine> lines)
    {
        DateOnly? from = null, to = null;
        string company = "";
        foreach (var line in lines.Take(4))
        {
            var dates = line.Words.Where(w => DateRegex().IsMatch(w.Text)).ToList();
            if (dates.Count >= 2 && from == null)
            {
                from = DateOnly.ParseExact(dates[0].Text, "dd.MM.yyyy", CultureInfo.InvariantCulture);
                to = DateOnly.ParseExact(dates[1].Text, "dd.MM.yyyy", CultureInfo.InvariantCulture);
                // Company name sits left of the address block on the same line.
                var nameWords = line.Words.Where(w => w.BoundingBox.Left < 250 && w.BoundingBox.Left > 40).OrderBy(w => w.BoundingBox.Left);
                company = string.Join(" ", nameWords.Select(w => w.Text));
            }
        }
        return new TrialBalance { CompanyName = company, DateFrom = from, DateTo = to };
    }

    // ---------------------------------------------------------------- columns

    /// <summary>Right edge of numbers in each of the 8 columns (measured on the sample report).</summary>
    private static readonly double[] DefaultAnchors = [238, 314, 389, 465, 541, 617, 693, 769];

    /// <summary>Derives column anchors from the D/P header letters: numbers end ≈34pt right of the letter.</summary>
    private static double[]? FindColumnAnchors(List<TextLine> lines)
    {
        var header = lines.FirstOrDefault(l => l.Words.Count == 8 && l.Words.All(w => w.Text is "D" or "P"));
        if (header == null) return null;
        return header.Words.OrderBy(w => w.BoundingBox.Left).Select(w => w.BoundingBox.Right + 34).ToArray();
    }

    private static void FillAmounts(Amounts target, List<Word> amountWords, double[] anchors)
    {
        foreach (var w in amountWords)
        {
            var right = w.BoundingBox.Right;
            var col = 0;
            var best = double.MaxValue;
            for (var i = 0; i < anchors.Length; i++)
            {
                var d = Math.Abs(anchors[i] - right);
                if (d < best) { best = d; col = i; }
            }
            target[(Column)col] = ParseAmount(w.Text);
        }
    }

    public static decimal ParseAmount(string text) =>
        decimal.Parse(text.Replace(".", "").Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- lines

    private sealed record TextLine(double Y, List<Word> Words);

    private static List<TextLine> GroupIntoLines(IEnumerable<Word> words)
    {
        var lines = new List<TextLine>();
        foreach (var w in words.OrderByDescending(w => w.BoundingBox.Bottom))
        {
            var y = w.BoundingBox.Bottom;
            var line = lines.Count > 0 && Math.Abs(lines[^1].Y - y) <= LineTolerance ? lines[^1] : null;
            if (line == null) { line = new TextLine(y, []); lines.Add(line); }
            line.Words.Add(w);
        }
        foreach (var l in lines) l.Words.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        return lines;
    }

    // ---------------------------------------------------------------- validation

    private static void Validate(TrialBalance tb, DiagnosticBag diag)
    {
        const string area = "PDF";
        if (tb.Accounts.Count == 0) { diag.Error(area, "Nije pronađeno nijedno konto."); return; }
        diag.Info(area, $"Pročitano {tb.Accounts.Count} konta i {tb.Groups.Count} grupa; razdoblje {tb.DateFrom:dd.MM.yyyy} – {tb.DateTo:dd.MM.yyyy}.");

        var dupes = tb.Accounts.GroupBy(a => a.Konto).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0) diag.Error(area, "Konto se pojavljuje više puta: " + string.Join(", ", dupes));

        // Every row: Ukupno = PS + Promet, Saldo = |Ukupno D - Ukupno P| on the right side.
        foreach (var a in tb.Accounts)
        {
            var x = a.Amounts;
            if (x[Column.UkupnoD] != x[Column.PsD] + x[Column.PrometD] || x[Column.UkupnoP] != x[Column.PsP] + x[Column.PrometP])
                diag.Error(area, $"Konto {a.Konto}: ukupan promet ≠ početno stanje + promet (provjeri očitanje stupaca).");
            if (x[Column.UkupnoD] - x[Column.UkupnoP] != x[Column.SaldoD] - x[Column.SaldoP])
                diag.Error(area, $"Konto {a.Konto}: saldo ≠ ukupno D − ukupno P (provjeri očitanje stupaca).");
        }

        // Group subtotals: sum of accounts whose code starts with the group code.
        foreach (var g in tb.Groups)
        {
            var sum = new Amounts();
            foreach (var a in tb.Accounts.Where(a => a.Konto.StartsWith(g.Code, StringComparison.Ordinal))) sum.Add(a.Amounts);
            // Saldo columns of a group are netted (one side only), so compare the net saldo; others column by column.
            var ok = sum[Column.PsD] == g.Amounts[Column.PsD] && sum[Column.PsP] == g.Amounts[Column.PsP]
                  && sum[Column.PrometD] == g.Amounts[Column.PrometD] && sum[Column.PrometP] == g.Amounts[Column.PrometP]
                  && sum.SaldoNet == g.Amounts.SaldoNet;
            if (!ok) diag.Error(area, $"Grupa {g.Code}: zbroj konta ne odgovara međuzbroju grupe u PDF-u.");
        }

        var total = new Amounts();
        foreach (var a in tb.Accounts) total.Add(a.Amounts);
        if (total.SaldoNet != 0)
            diag.Error(area, $"Bruto bilanca nije u ravnoteži: saldo D − P = {total.SaldoNet:N2}.");
        if (tb.GrandTotal != null)
        {
            if (total[Column.PsD] != tb.GrandTotal[Column.PsD] || total[Column.PrometD] != tb.GrandTotal[Column.PrometD]
                || total[Column.PrometP] != tb.GrandTotal[Column.PrometP] || total[Column.PsP] != tb.GrandTotal[Column.PsP])
                diag.Error(area, "Zbroj svih konta ne odgovara retku SVEUKUPNO.");
            else
                diag.Info(area, "Zbroj svih konta odgovara retku SVEUKUPNO; svi međuzbrojevi grupa provjereni.");
        }
        else diag.Warn(area, "Redak SVEUKUPNO nije pronađen.");
    }
}
