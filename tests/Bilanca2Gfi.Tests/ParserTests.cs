using Bilanca2Gfi.Core.Model;
using Bilanca2Gfi.Core.Parsing;

namespace Bilanca2Gfi.Tests;

public class ParserTests
{
    [Fact]
    public void Parses_header_and_all_accounts_without_errors()
    {
        var diag = new DiagnosticBag();
        var tb = new TrialBalancePdfParser().Parse(SampleFiles.Pdf, diag);

        Assert.False(diag.HasErrors, string.Join(Environment.NewLine, diag.Items));
        Assert.Equal("AMBRA WATER SOLUTIONS d.o.o.", tb.CompanyName);
        Assert.Equal(new DateOnly(2026, 1, 1), tb.DateFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), tb.DateTo);
        Assert.Equal(287, tb.Accounts.Count);
        Assert.Equal(5_825_370.07m, tb.GrandTotal![Column.SaldoD]);
    }

    [Fact]
    public void Assigns_amounts_to_the_right_columns()
    {
        var a = SampleFiles.TrialBalance.Accounts.Single(x => x.Konto == "03901").Amounts;
        Assert.Equal(4_532.69m, a[Column.PsP]);
        Assert.Equal(450.00m, a[Column.PrometD]);
        Assert.Equal(0m, a[Column.PrometP]);
        Assert.Equal(4_082.69m, a[Column.SaldoP]);

        var kupci = SampleFiles.TrialBalance.Accounts.Single(x => x.Konto == "12000").Amounts;
        Assert.Equal(247_494.67m, kupci[Column.PsD]);
        Assert.Equal(2_032_578.36m, kupci[Column.PrometD]);
        Assert.Equal(1_767_833.57m, kupci[Column.PrometP]);
        Assert.Equal(512_239.46m, kupci[Column.SaldoD]);
    }

    [Fact]
    public void Reads_negative_amounts_and_wrapped_names()
    {
        var predujmovi = SampleFiles.TrialBalance.Accounts.Single(x => x.Konto == "19002");
        Assert.Equal(-46_085.20m, predujmovi.Amounts[Column.PrometD]);
        Assert.Equal("OSNOVICA PLAĆENIH PREDUJMOVA (KOR.PDV)", predujmovi.Naziv);

        var sixDigit = SampleFiles.TrialBalance.Accounts.Single(x => x.Konto == "032002");
        Assert.Equal(53_863.20m, sixDigit.Amounts[Column.SaldoD]);
    }

    [Theory]
    [InlineData("1.234.567,89", 1_234_567.89)]
    [InlineData("-46.085,20", -46_085.20)]
    [InlineData("0,07", 0.07)]
    public void Parses_croatian_amounts(string text, double expected) =>
        Assert.Equal((decimal)expected, TrialBalancePdfParser.ParseAmount(text));
}
