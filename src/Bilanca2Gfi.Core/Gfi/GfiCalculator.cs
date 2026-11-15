using Bilanca2Gfi.Core.Mapping;
using Bilanca2Gfi.Core.Model;

namespace Bilanca2Gfi.Core.Gfi;

public sealed record TraceItem(string Konto, string Naziv, decimal Value);

/// <summary>AOP values of one column (prethodna or tekuća godina) plus where each value came from.</summary>
public sealed class GfiColumn
{
    public Dictionary<int, decimal> Values { get; } = [];
    public Dictionary<int, List<TraceItem>> Trace { get; } = [];

    public decimal this[int aop] => Values.GetValueOrDefault(aop);

    public void Add(int aop, decimal value, string konto, string naziv)
    {
        Values[aop] = Values.GetValueOrDefault(aop) + value;
        if (!Trace.TryGetValue(aop, out var list)) Trace[aop] = list = [];
        list.Add(new TraceItem(konto, naziv, value));
    }

    public void Set(int aop, decimal value) => Values[aop] = value;
}

public sealed class GfiResult
{
    public GfiColumn Prior { get; } = new();
    public GfiColumn Current { get; } = new();
    public decimal ProfitTax { get; init; }
    public string PriorSource { get; set; } = "";
}

public sealed class GfiCalculator
{
    /// <summary>AOP of "Obveze za poreze, doprinose i slična davanja" – receives the unbooked profit tax.</summary>
    private const int TaxLiabilityAop = 121;

    public GfiResult Calculate(TrialBalance tb, MappingConfig config, decimal? profitTax, PriorGfi? prior, DiagnosticBag diag)
    {
        var result = new GfiResult { ProfitTax = profitTax ?? 0m };

        MapAccounts(tb, config, result, diag);

        // --- Profit tax (entered by the user; not booked in the trial balance)
        if (profitTax == null)
            diag.Warn("Porez", "Porez na dobit nije zadan (--porez). Upisano 0; dobit razdoblja jednaka je dobiti prije oporezivanja.");
        else if (profitTax != 0)
        {
            result.Current.Add(185, profitTax.Value, "—", "Porez na dobit (uneseno)");
            result.Current.Add(TaxLiabilityAop, profitTax.Value, "—", "Obveza za porez na dobit tekuće godine (uneseno)");
        }

        // --- Prior year RDG and Dodatni come from last year's GFI
        ApplyPrior(tb, config, result, prior, diag);

        // --- Dodatni podaci (current year)
        foreach (var rule in config.Dodatni) ApplyAdditional(tb, rule, result.Current, opening: false);
        foreach (var (aop, value) in config.RucneVrijednosti) { result.Current.Set(aop, value); }

        // --- Sums: RDG first, then carry the year's result into the balance sheet
        ComputeSums(result.Current, GfiSheet.Rdg);
        ComputeSums(result.Prior, GfiSheet.Rdg);

        var yearResult = result.Current[186];
        if (tb.Accounts.Any(a => a.Razred is 4 or 7 && a.Amounts.SaldoNet != 0))
        {
            result.Current.Add(88, yearResult, "4+7", "Rezultat tekuće godine iz RDG-a (AOP 186)");
        }
        else
        {
            diag.Warn("Rezultat", "Razredi 4 i 7 nemaju salda (godina je možda već zaključena) – rezultat se uzima samo s konta rezultata.");
        }

        SplitProfitLoss(result.Current);
        SplitProfitLoss(result.Prior);

        foreach (var sheet in new[] { GfiSheet.Bilanca, GfiSheet.Dodatni })
        {
            ComputeSums(result.Current, sheet);
            ComputeSums(result.Prior, sheet);
        }

        Validate(result, diag);
        return result;
    }

    // ------------------------------------------------------------------ konto → AOP

    private static void MapAccounts(TrialBalance tb, MappingConfig config, GfiResult result, DiagnosticBag diag)
    {
        var unmapped = new List<string>();
        foreach (var account in tb.Accounts)
        {
            var isRdg = account.Razred is 4 or 7;
            var rules = isRdg ? config.Rdg : config.Bilanca;
            var rule = FindRule(rules, account.Konto);
            var saldo = account.Amounts.SaldoNet;
            var opening = account.Amounts.PsNet;

            if (rule == null)
            {
                if (saldo != 0 || (!isRdg && opening != 0))
                    unmapped.Add($"{account.Konto} {account.Naziv} (saldo {saldo:N2}, PS {opening:N2})");
                continue;
            }

            var sign = rule.Predznak * AopStructure.NaturalSign(rule.Aop);
            if (saldo != 0) result.Current.Add(rule.Aop, sign * saldo, account.Konto, account.Naziv);
            if (!isRdg && opening != 0) result.Prior.Add(rule.Aop, sign * opening, account.Konto, account.Naziv);
        }

        foreach (var u in unmapped) diag.Error("Mapiranje", "Konto nema pravilo za AOP: " + u);
    }

    public static AccountRule? FindRule(IEnumerable<AccountRule> rules, string konto) =>
        rules.Where(r => konto.StartsWith(r.Konto, StringComparison.Ordinal))
             .OrderByDescending(r => r.Konto.Length)
             .FirstOrDefault();

    private static void ApplyAdditional(TrialBalance tb, AdditionalRule rule, GfiColumn column, bool opening)
    {
        foreach (var a in tb.Accounts)
        {
            if (!rule.Konta.Any(p => a.Konto.StartsWith(p, StringComparison.Ordinal))) continue;
            if (rule.Osim.Any(p => a.Konto.StartsWith(p, StringComparison.Ordinal))) continue;

            var x = a.Amounts;
            var value = (rule.Mjera, opening) switch
            {
                (Measure.SaldoD, false) => x.SaldoNet,
                (Measure.SaldoP, false) => -x.SaldoNet,
                (Measure.PrometD, false) => x[Column.PrometD],
                (Measure.PrometP, false) => x[Column.PrometP],
                (Measure.SaldoD, true) => x.PsNet,
                (Measure.SaldoP, true) => -x.PsNet,
                _ => 0m,
            };
            if (value != 0) column.Add(rule.Aop, value, a.Konto, a.Naziv);
        }
        column.Values.TryAdd(rule.Aop, 0m);
    }

    // ------------------------------------------------------------------ prior year

    private static void ApplyPrior(TrialBalance tb, MappingConfig config, GfiResult result, PriorGfi? prior, DiagnosticBag diag)
    {
        if (prior == null)
        {
            result.PriorSource = "Početno stanje iz PDF-a (bez prošlogodišnjeg GFI-a)";
            diag.Warn("Prethodna godina", "Prošlogodišnji GFI nije zadan: RDG prethodne godine ostaje prazan, Dodatni podaci prethodne godine računaju se samo za stanja (iz početnog stanja).");
            foreach (var rule in config.Dodatni.Where(r => r.Mjera is Measure.SaldoD or Measure.SaldoP))
                ApplyAdditional(tb, rule, result.Prior, opening: true);
            return;
        }

        result.PriorSource = $"{Path.GetFileName(prior.Path)} (stupac {prior.ColumnLabel})";
        diag.Info("Prethodna godina", $"RDG i Dodatni podaci prethodne godine preuzeti iz {result.PriorSource}.");

        foreach (var (aop, value) in prior.LeafValues)
        {
            var sheet = AopStructure.SheetOf(aop);
            if (sheet is GfiSheet.Rdg or GfiSheet.Dodatni && !AopStructure.IsComputed(aop))
                result.Prior.Add(aop, value, "GFI", "Prošlogodišnji GFI");
        }

        // Cross-check: the balance sheet built from opening balances should match last year's filed balance sheet.
        var diffs = new List<string>();
        foreach (var (aop, filed) in prior.LeafValues.Where(kv => AopStructure.SheetOf(kv.Key) == GfiSheet.Bilanca && !AopStructure.IsComputed(kv.Key)))
        {
            var fromPs = result.Prior[aop];
            if (aop is 88 or 89 or 85 or 86) continue; // checked after the profit/loss split
            if (Math.Abs(fromPs - filed) > 0.005m) diffs.Add($"AOP {aop:000}: PS {fromPs:N2} / GFI {filed:N2}");
        }
        if (diffs.Count > 0)
            diag.Warn("Prethodna godina", "Bilanca iz početnog stanja razlikuje se od prošlogodišnjeg GFI-a: " + string.Join("; ", diffs));
        else
            diag.Info("Prethodna godina", "Bilanca iz početnog stanja odgovara prošlogodišnjem GFI-u.");
    }

    // ------------------------------------------------------------------ sums

    private static void SplitProfitLoss(GfiColumn column)
    {
        foreach (var (profitAop, lossAop) in AopStructure.ProfitLossPairs)
        {
            var net = column[profitAop] - column[lossAop];
            column.Set(profitAop, net > 0 ? net : 0);
            column.Set(lossAop, net < 0 ? -net : 0);
        }
    }

    private static void ComputeSums(GfiColumn column, GfiSheet sheet)
    {
        var cache = new Dictionary<int, decimal>();
        foreach (var aop in AopStructure.Sums.Keys.Concat(AopStructure.Parts.Keys).Where(a => AopStructure.SheetOf(a) == sheet).Order())
            column.Set(aop, Evaluate(column, aop, cache));
    }

    private static decimal Evaluate(GfiColumn column, int aop, Dictionary<int, decimal> cache)
    {
        if (cache.TryGetValue(aop, out var cached)) return cached;
        decimal value;
        if (AopStructure.Sums.TryGetValue(aop, out var expr))
            value = AopStructure.Terms(expr).Sum(t => t.Sign * Evaluate(column, t.Aop, cache));
        else if (AopStructure.Parts.TryGetValue(aop, out var part))
        {
            var source = Evaluate(column, part.Source, cache);
            value = part.Positive ? Math.Max(source, 0) : Math.Max(-source, 0);
        }
        else value = column[aop];
        value = Math.Round(value, 2);
        cache[aop] = value;
        return value;
    }

    // ------------------------------------------------------------------ checks

    private static void Validate(GfiResult r, DiagnosticBag diag)
    {
        foreach (var (label, col) in new[] { ("tekuća", r.Current), ("prethodna", r.Prior) })
        {
            var aktiva = col[65];
            var pasiva = col[126];
            if (aktiva != pasiva)
                diag.Error("Kontrola", $"Bilanca ({label} godina): aktiva AOP 065 = {aktiva:N2}, pasiva AOP 126 = {pasiva:N2}, razlika {aktiva - pasiva:N2}.");
            else
                diag.Info("Kontrola", $"Bilanca ({label} godina): aktiva = pasiva = {aktiva:N2}.");

            var sales = col[129] + col[130];
            if (sales != 0 || col[254] + col[255] != 0)
            {
                if (col[254] + col[255] != sales)
                    diag.Warn("Kontrola", $"Dodatni ({label}): prodaja u zemlji + inozemstvu (AOP 254+255 = {col[254] + col[255]:N2}) ≠ prihodi od prodaje (AOP 129+130 = {sales:N2}).");
                var byActivity = col[243] + col[244] + col[247] + col[248] + col[250] + col[251] + col[252] + col[253];
                if (byActivity != sales)
                    diag.Warn("Kontrola", $"Dodatni ({label}): prihodi po djelatnostima (AOP 243..253 = {byActivity:N2}) ≠ prihodi od prodaje (AOP 129+130 = {sales:N2}).");
            }
        }

        var rdgResult = r.Current[187] - r.Current[188];
        var bilResult = r.Current[88] - r.Current[89];
        if (rdgResult != bilResult)
            diag.Warn("Kontrola", $"Rezultat u RDG-u (AOP 187−188 = {rdgResult:N2}) razlikuje se od rezultata u Bilanci (AOP 088−089 = {bilResult:N2}) – provjeri konta rezultata (950).");
        else
            diag.Info("Kontrola", $"Rezultat razdoblja u RDG-u i Bilanci je jednak: {rdgResult:N2}.");
    }
}
