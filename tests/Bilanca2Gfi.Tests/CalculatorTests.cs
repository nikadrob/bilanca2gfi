using Bilanca2Gfi.Core.Excel;
using Bilanca2Gfi.Core.Gfi;
using Bilanca2Gfi.Core.Mapping;
using Bilanca2Gfi.Core.Model;

namespace Bilanca2Gfi.Tests;

public class CalculatorTests
{
    private static readonly MappingConfig Config = MappingConfig.Load(SampleFiles.Config);

    private static (GfiResult Result, DiagnosticBag Diag) Calculate(decimal? tax = null, bool withPrior = true)
    {
        var diag = new DiagnosticBag();
        var prior = withPrior ? PriorGfiReader.Read(SampleFiles.PriorGfi, 2026, diag) : null;
        var result = new GfiCalculator().Calculate(SampleFiles.TrialBalance, Config, tax, prior, diag);
        return (result, diag);
    }

    [Fact]
    public void Every_konto_is_mapped_and_both_balance_sheets_balance()
    {
        var (r, diag) = Calculate();
        Assert.False(diag.HasErrors, string.Join(Environment.NewLine, diag.Items.Where(d => d.Severity == Severity.Error)));
        Assert.Equal(r.Current[65], r.Current[126]);
        Assert.Equal(r.Prior[65], r.Prior[126]);
        Assert.Equal(2_861_195.40m, r.Current[65]);
    }

    [Fact]
    public void Year_result_is_computed_from_classes_4_and_7()
    {
        var (r, _) = Calculate();
        Assert.Equal(1_825_452.21m, r.Current[180]);
        Assert.Equal(1_773_107.22m, r.Current[181]);
        Assert.Equal(52_344.99m, r.Current[183]);
        Assert.Equal(52_344.99m, r.Current[88]);
        Assert.Equal(93_973.08m, r.Prior[88]); // prior-year profit from the opening balance of konto 95000
        Assert.Equal(93_973.08m, r.Prior[187]); // ... and equal to last year's RDG
    }

    [Fact]
    public void Profit_tax_reduces_profit_and_increases_tax_liability()
    {
        var (without, _) = Calculate();
        var (with, diag) = Calculate(tax: 10_000m);

        Assert.Equal(42_344.99m, with.Current[187]);
        Assert.Equal(42_344.99m, with.Current[88]);
        Assert.Equal(without.Current[121] + 10_000m, with.Current[121]);
        Assert.Equal(with.Current[65], with.Current[126]);
        Assert.False(diag.HasErrors);
    }

    [Theory]
    [InlineData(12, 2_279.94)]      // kontejneri − ak. amortizacija 03901
    [InlineData(14, 45_902.34)]     // oprema + vozila − 039 − 037
    [InlineData(39, 156_725.76)]    // zalihe (sitni inventar se poništava s ispravkom)
    [InlineData(116, 349_478.16)]   // 214 + 252
    [InlineData(104, 34_631.97)]    // 945 leasing – dugoročno
    [InlineData(120, 62_389.68)]    // 230 + 22220
    [InlineData(73, 113_520.50)]    // vlastite dionice kao pozitivna odbitna stavka
    [InlineData(130, 1_815_682.80)]
    [InlineData(139, 767_528.46)]
    [InlineData(145, 174_131.77)]
    [InlineData(147, 1_800.00)]
    public void Maps_konta_to_expected_aop(int aop, double expected)
    {
        var (r, _) = Calculate();
        Assert.Equal((decimal)expected, r.Current[aop]);
    }

    [Fact]
    public void Dodatni_sales_breakdowns_match_rdg_sales()
    {
        var (r, diag) = Calculate();
        var sales = r.Current[129] + r.Current[130];
        Assert.Equal(sales, r.Current[254] + r.Current[255]);
        Assert.Equal(sales, r.Current[243] + r.Current[244] + r.Current[247] + r.Current[248] + r.Current[250] + r.Current[251] + r.Current[252] + r.Current[253]);
        Assert.DoesNotContain(diag.Items, d => d.Area == "Kontrola" && d.Severity == Severity.Warning);
    }

    [Fact]
    public void Prior_year_rdg_comes_from_prior_gfi()
    {
        var (r, _) = Calculate();
        Assert.Equal(6_652_787.33m, r.Prior[130]);
        Assert.Equal(30_047.59m, r.Prior[185]);
    }

    [Theory]
    [InlineData("03901", 12)]
    [InlineData("03910", 14)]
    [InlineData("22220", 120)]
    [InlineData("22000", 118)]
    [InlineData("99001", 127)]
    public void Longest_prefix_rule_wins(string konto, int expectedAop) =>
        Assert.Equal(expectedAop, GfiCalculator.FindRule(Config.Bilanca, konto)!.Aop);

    [Fact]
    public void Sum_expressions_are_parsed()
    {
        Assert.Equal([(71, 1), (72, 1), (73, -1), (74, 1), (75, 1)], AopStructure.Terms("71+72-73+74+75").ToList());
        Assert.Equal([4, 5, 6, 7, 8, 9], AopStructure.Terms("4..9").Select(t => t.Aop).ToList());
    }
}
