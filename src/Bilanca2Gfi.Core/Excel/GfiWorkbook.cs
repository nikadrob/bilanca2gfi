using System.Globalization;
using Bilanca2Gfi.Core.Gfi;
using Bilanca2Gfi.Core.Model;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;

namespace Bilanca2Gfi.Core.Excel
{
    /// <summary>Locates AOP rows in the FINA GFI-POD .xls template (sheets Bilanca, RDG, Dodatni).</summary>
    internal static class GfiLayout
    {
        public const int ColPrior = 8;    // I - prethodna godina
        public const int ColCurrent = 9;  // J - tekuća godina
        public const int ColSignMarker = 11; // L - "+" (must be ≥ 0) or "=" (may be negative)

        public static readonly (string Sheet, int AopCol)[] Sheets = [("Bilanca", 6), ("RDG", 6), ("Dodatni", 7)];

        public sealed record AopRow(string Sheet, IRow Row, int Aop, bool MayBeNegative);

        public static IEnumerable<AopRow> AopRows(IWorkbook wb)
        {
            foreach (var (sheetName, aopCol) in Sheets)
            {
                var sheet = wb.GetSheet(sheetName) ?? throw new InvalidDataException($"Predložak nema radni list '{sheetName}'.");
                for (var i = 0; i <= sheet.LastRowNum; i++)
                {
                    var row = sheet.GetRow(i);
                    var aopCell = row?.GetCell(aopCol);
                    var marker = row?.GetCell(ColSignMarker);
                    if (aopCell == null || marker == null) continue;
                    var markerText = marker.CellType == CellType.String ? marker.StringCellValue.Trim() : "";
                    if (markerText is not ("+" or "=")) continue; // header/title rows have no marker
                    var aop = aopCell.CellType switch
                    {
                        CellType.Numeric => (int)aopCell.NumericCellValue,
                        CellType.String when int.TryParse(aopCell.StringCellValue, out var n) => n,
                        _ => 0,
                    };
                    if (aop > 0) yield return new AopRow(sheetName, row!, aop, markerText == "=");
                }
            }
        }

        public static bool IsInput(ICell? cell) => cell != null && !cell.CellStyle.IsLocked && cell.CellType != CellType.Formula;

        public static decimal ReadNumber(ICell? cell) => cell?.CellType switch
        {
            CellType.Numeric => (decimal)cell.NumericCellValue,
            CellType.String when decimal.TryParse(cell.StringCellValue.Replace(".", "").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
            _ => 0m,
        };

        public static DateOnly? ReadDate(ISheet refStr, string address)
        {
            var r = new NPOI.SS.Util.CellReference(address);
            var cell = refStr.GetRow(r.Row)?.GetCell(r.Col);
            if (cell == null) return null;
            if (cell.CellType == CellType.Numeric) return DateOnly.FromDateTime(cell.DateCellValue ?? DateTime.FromOADate(cell.NumericCellValue));
            if (cell.CellType == CellType.String && DateOnly.TryParseExact(cell.StringCellValue.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
            return null;
        }
    }

    public static class GfiTemplateInfo
    {
        /// <summary>AOP → position name (column A of the template), whitespace collapsed.</summary>
        public static Dictionary<int, string> ReadLabels(string templatePath)
        {
            using var fs = File.OpenRead(templatePath);
            var wb = new HSSFWorkbook(fs);
            var labels = new Dictionary<int, string>();
            foreach (var row in GfiLayout.AopRows(wb))
            {
                var text = row.Row.GetCell(0)?.ToString() ?? "";
                labels.TryAdd(row.Aop, string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
            }
            return labels;
        }
    }

    public static class PriorGfiReader
    {
        /// <summary>
        /// Reads prior-year leaf values. If the file is last year's report its "tekuća godina" column is used;
        /// if it is an (earlier draft of the) current year's report, its "prethodna godina" column is used.
        /// </summary>
        public static PriorGfi Read(string path, int currentYear, DiagnosticBag diag)
        {
            using var fs = File.OpenRead(path);
            var wb = new HSSFWorkbook(fs);
            var refStr = wb.GetSheet("RefStr");
            var periodEnd = refStr == null ? null : GfiLayout.ReadDate(refStr, "F4");
            int col;
            string label;
            if (periodEnd?.Year == currentYear)
            {
                col = GfiLayout.ColPrior;
                label = "prethodna godina";
            }
            else
            {
                col = GfiLayout.ColCurrent;
                label = "tekuća godina";
                if (periodEnd != null && periodEnd.Value.Year != currentYear - 1)
                    diag.Warn("Prethodna godina", $"{Path.GetFileName(path)} je za razdoblje do {periodEnd:dd.MM.yyyy}, a ne za {currentYear - 1}. godinu.");
            }

            var values = new Dictionary<int, decimal>();
            foreach (var row in GfiLayout.AopRows(wb))
            {
                if (AopStructure.IsComputed(row.Aop)) continue;
                values[row.Aop] = GfiLayout.ReadNumber(row.Row.GetCell(col));
            }
            return new PriorGfi { Path = path, ColumnLabel = label, LeafValues = values, SameYear = col == GfiLayout.ColPrior };
        }
    }

    public static class GfiTemplateWriter
    {
        /// <summary>
        /// Copies the FINA template and fills the input cells of Bilanca, RDG and Dodatni (both columns),
        /// plus the reporting period on RefStr. Sum cells keep their template formulas.
        /// </summary>
        public static void Write(string templatePath, string outputPath, TrialBalance tb, GfiResult result, DiagnosticBag diag,
            PriorGfi? referenceSource = null, Mapping.ReferencePageData? fixedReferenceData = null)
        {
            HSSFWorkbook wb;
            using (var fs = File.OpenRead(templatePath)) wb = new HSSFWorkbook(fs);

            var negatives = new List<string>();
            var written = 0;
            foreach (var row in GfiLayout.AopRows(wb))
            {
                foreach (var (colIndex, column) in new[] { (GfiLayout.ColPrior, result.Prior), (GfiLayout.ColCurrent, result.Current) })
                {
                    var cell = row.Row.GetCell(colIndex) ?? row.Row.CreateCell(colIndex);
                    var value = Math.Round(column[row.Aop], 2);

                    if (!row.MayBeNegative && value < 0)
                        negatives.Add($"AOP {row.Aop:000} ({(colIndex == GfiLayout.ColPrior ? "prethodna" : "tekuća")}) = {value:N2}");

                    if (!GfiLayout.IsInput(cell))
                    {
                        // Sum cell: formula stays; our own value is used only for the cross-check below.
                        continue;
                    }
                    cell.SetCellValue((double)value);
                    written++;
                }
            }

            foreach (var n in negatives) diag.Warn("Obrazac", $"Negativan iznos u poziciji koja mora biti ≥ 0: {n}");

            ReferencePageFiller.Fill(wb, tb, result, referenceSource, fixedReferenceData ?? new Mapping.ReferencePageData(), diag);
            FillReferencePage(wb, tb);

            // Recalculate cached formula results so the file shows correct sums even before Excel recalculates.
            wb.ForceFormulaRecalculation = true;
            var formulaErrors = RecalculateFormulas(wb);
            if (formulaErrors > 0)
                diag.Info("Obrazac", $"{formulaErrors} formula nije moguće izračunati izvan Excela; Excel ih računa pri otvaranju.");

            CrossCheckSums(wb, result, diag);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            using (var outFs = File.Create(outputPath)) wb.Write(outFs);
            diag.Info("Obrazac", $"Upisano {written} ulaznih ćelija u {Path.GetFileName(outputPath)}.");
        }

        private static void FillReferencePage(HSSFWorkbook wb, TrialBalance tb)
        {
            var refStr = wb.GetSheet("RefStr");
            if (refStr == null) return;
            // The template validates text dates in the Croatian format (dd.MM.yyyy), as FINA's own files do.
            if (tb.DateFrom is { } from) SetString(refStr, "C4", from.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            if (tb.DateTo is { } to)
            {
                SetString(refStr, "F4", to.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
                var r = new NPOI.SS.Util.CellReference("F12");
                (refStr.GetRow(r.Row) ?? refStr.CreateRow(r.Row)).GetCell(r.Col, MissingCellPolicy.CREATE_NULL_AS_BLANK).SetCellValue(to.Year);
            }
        }

        private static void SetString(ISheet sheet, string address, string value)
        {
            var r = new NPOI.SS.Util.CellReference(address);
            (sheet.GetRow(r.Row) ?? sheet.CreateRow(r.Row)).GetCell(r.Col, MissingCellPolicy.CREATE_NULL_AS_BLANK).SetCellValue(value);
        }

        private static int RecalculateFormulas(HSSFWorkbook wb)
        {
            var evaluator = wb.GetCreationHelper().CreateFormulaEvaluator();
            var failed = 0;
            for (var s = 0; s < wb.NumberOfSheets; s++)
            {
                var sheet = wb.GetSheetAt(s);
                for (var i = 0; i <= sheet.LastRowNum; i++)
                {
                    var row = sheet.GetRow(i);
                    if (row == null) continue;
                    foreach (var cell in row.Cells)
                    {
                        if (cell.CellType != CellType.Formula) continue;
                        try { evaluator.EvaluateFormulaCell(cell); }
                        catch { failed++; }
                    }
                }
            }
            return failed;
        }

        /// <summary>Compares the template's own sum formulas with the values this program computed.</summary>
        private static void CrossCheckSums(HSSFWorkbook wb, GfiResult result, DiagnosticBag diag)
        {
            var mismatches = new List<string>();
            foreach (var row in GfiLayout.AopRows(wb))
            {
                foreach (var (colIndex, column) in new[] { (GfiLayout.ColPrior, result.Prior), (GfiLayout.ColCurrent, result.Current) })
                {
                    var cell = row.Row.GetCell(colIndex);
                    if (cell?.CellType != CellType.Formula || cell.CachedFormulaResultType != CellType.Numeric) continue;
                    var excel = Math.Round((decimal)cell.NumericCellValue, 2);
                    var ours = Math.Round(column[row.Aop], 2);
                    if (excel != ours) mismatches.Add($"AOP {row.Aop:000}: obrazac {excel:N2}, program {ours:N2}");
                }
            }
            if (mismatches.Count > 0) diag.Error("Obrazac", "Zbrojevi u obrascu ne odgovaraju izračunu: " + string.Join("; ", mismatches));
            else diag.Info("Obrazac", "Zbirne pozicije obrasca (formule predloška) odgovaraju izračunu programa.");
        }
    }
}
