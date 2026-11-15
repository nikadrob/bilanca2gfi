using Bilanca2Gfi.Core.Excel;
using Bilanca2Gfi.Core.Gfi;
using Bilanca2Gfi.Core.Mapping;
using Bilanca2Gfi.Core.Model;
using NPOI.HSSF.UserModel;

namespace Bilanca2Gfi.Tests;

public class WriterTests
{
    [Fact]
    public void Writes_input_cells_keeps_formulas_and_sets_period()
    {
        var diag = new DiagnosticBag();
        var tb = SampleFiles.TrialBalance;
        var result = new GfiCalculator().Calculate(tb, MappingConfig.Load(SampleFiles.Config), 0m, null, diag);
        var output = Path.Combine(Path.GetTempPath(), $"gfi-test-{Guid.NewGuid():N}.xls");

        try
        {
            GfiTemplateWriter.Write(SampleFiles.Template, output, tb, result, diag);
            Assert.False(diag.HasErrors, string.Join(Environment.NewLine, diag.Items.Where(d => d.Severity == Severity.Error)));

            using var fs = File.OpenRead(output);
            var wb = new HSSFWorkbook(fs);
            var bilanca = wb.GetSheet("Bilanca");

            // Row 20 = AOP 012 (input cell), row 73 = AOP 065 (template formula, recalculated cache).
            Assert.Equal(2_279.94, bilanca.GetRow(19).GetCell(9).NumericCellValue, 2);
            var aktiva = bilanca.GetRow(72).GetCell(9);
            Assert.Equal(NPOI.SS.UserModel.CellType.Formula, aktiva.CellType);
            Assert.Equal(2_861_195.40, aktiva.NumericCellValue, 2);

            var refStr = wb.GetSheet("RefStr");
            Assert.Equal("01.01.2026", refStr.GetRow(3).GetCell(2).StringCellValue);
            Assert.Equal("31.12.2026", refStr.GetRow(3).GetCell(5).StringCellValue);
            Assert.Equal(2026, refStr.GetRow(11).GetCell(5).NumericCellValue);
            Assert.Equal(10, wb.NumberOfSheets);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void Fills_reference_page_from_prior_gfi_and_config()
    {
        var diag = new DiagnosticBag();
        var tb = SampleFiles.TrialBalance;
        var config = MappingConfig.Load(SampleFiles.Config);
        config.ReferentnaStranica.ZaposleniProsjek = 21;
        var prior = PriorGfiReader.Read(SampleFiles.PriorGfi, 2026, diag);
        var result = new GfiCalculator().Calculate(tb, config, 0m, prior, diag);
        var output = Path.Combine(Path.GetTempPath(), $"gfi-test-{Guid.NewGuid():N}.xls");

        try
        {
            GfiTemplateWriter.Write(SampleFiles.Template, output, tb, result, diag, prior, config.ReferentnaStranica);

            using var fs = File.OpenRead(output);
            var refStr = new HSSFWorkbook(fs).GetSheet("RefStr");
            string Text(int row, int col) => refStr.GetRow(row).GetCell(col).ToString()!.Trim();

            Assert.Equal("61476632561", Text(26, 2));                  // C27 OIB – copied
            Assert.Equal("AMBRA WATER SOLUTIONS  d.o.o.", Text(28, 2)); // C29 naziv – copied
            Assert.Equal("5", Text(6, 2));                            // C7 vrsta subjekta – config
            Assert.Equal("HSFI", Text(18, 13));                       // N19 – config
            Assert.Equal("180", Text(38, 2));                         // C39 šifra općine – config
            Assert.Equal("1", Text(22, 2));                           // C23 mišljenje revizora – not audited
            Assert.Equal("12", Text(59, 5));                          // F60 mjeseci – from period
            Assert.Equal("21", Text(55, 5));                          // F56 zaposleni – option
            Assert.Equal("31.12.2026", Text(3, 5));                   // F4 period end – from PDF, not from the source file
        }
        finally
        {
            File.Delete(output);
        }
    }
}
