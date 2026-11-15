using Bilanca2Gfi.Core.Gfi;
using Bilanca2Gfi.Core.Mapping;
using Bilanca2Gfi.Core.Model;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;

namespace Bilanca2Gfi.Core.Excel;

/// <summary>
/// Fills the reference page (RefStr): data copied from an earlier GFI (identification, address, codes, contacts),
/// then fixed company data from the config, then values derived from the period. Reports required fields still empty.
/// </summary>
public static class ReferencePageFiller
{
    private const string Area = "Referentna stranica";

    /// <summary>Input cells that normally stay the same from year to year.</summary>
    private static readonly string[] StableCells =
    [
        "G6",                          // subjekt se briše
        "C7",                          // vrsta poslovnog subjekta
        "C17", "C19", "N19",           // vrsta izvještaja, svrha predaje, HSFI/MSFI
        "C21", "I21", "L21", "C23",    // konsolidacija, revizija, OIB revizora, mišljenje revizora
        "C27", "H27", "M27",           // OIB, MB, MBS
        "C29", "C31", "F31", "C33",    // naziv, pošta, naselje, ulica
        "C35", "L35", "C37",           // e-pošta, telefon, web
        "C39", "C42", "C44", "C46", "M46", // općina, NKD, status autonomnosti, matično društvo
        "C50", "C52", "C54", "F54",    // veličina, vlasništvo, porijeklo kapitala
        "I56", "I62", "I64", "I66", "I68", // popis dokumentacije
        "B64", "B66",                  // knjigovodstveni servis
        "C68", "C70", "C72",           // osoba za kontakt
        "A75",                         // ovlaštena osoba
    ];

    /// <summary>(prethodna godina, tekuća godina) pairs: prosjek zaposlenih, zaposleni po satima, mjeseci poslovanja.</summary>
    private static readonly (string Prior, string Current)[] YearPairs = [("C56", "F56"), ("C58", "F58"), ("C60", "F60")];

    /// <summary>Required fields that must not stay empty, with their description.</summary>
    private static readonly (string Cell, string Label)[] Required =
    [
        ("C7", "vrsta poslovnog subjekta (referentnaStranica.vrstaSubjekta)"),
        ("N19", "primjena standarda HSFI/MSFI (referentnaStranica.standard)"),
        ("C17", "vrsta izvještaja (referentnaStranica.vrstaIzvjestaja)"),
        ("C19", "šifra svrhe predaje (referentnaStranica.svrhaPredaje)"),
        ("C27", "OIB"), ("H27", "matični broj"), ("C29", "naziv obveznika"),
        ("C39", "šifra grada/općine (referentnaStranica.sifraOpcine)"),
        ("C42", "šifra NKD (referentnaStranica.sifraNkd)"),
        ("C44", "status autonomnosti (referentnaStranica.statusAutonomnosti)"),
        ("C50", "oznaka veličine (referentnaStranica.oznakaVelicine)"),
        ("C52", "oznaka vlasništva (referentnaStranica.oznakaVlasnistva)"),
        ("C54", "porijeklo kapitala – domaći % (referentnaStranica.kapitalDomaciPosto)"),
        ("F54", "porijeklo kapitala – strani % (referentnaStranica.kapitalStraniPosto)"),
        ("F56", "prosječan broj zaposlenih – tekuća godina"),
        ("F58", "broj zaposlenih prema satima rada – tekuća godina"),
        ("A75", "ovlaštena osoba za zastupanje"),
    ];

    public static void Fill(HSSFWorkbook target, TrialBalance tb, GfiResult result, PriorGfi? source, ReferencePageData fixedData, DiagnosticBag diag)
    {
        var to = target.GetSheet("RefStr");
        if (to == null) return;

        if (source != null) CopyFromSource(source, to, diag);

        foreach (var (cell, value) in fixedData.Values())
        {
            if (value is string s) GetOrCreate(to, cell).SetCellValue(s);
            else GetOrCreate(to, cell).SetCellValue(Convert.ToDouble(value));
        }

        // Months of operation in the current year follow from the reporting period.
        if (tb.DateFrom is { } start && tb.DateTo is { } end)
            SetNumber(to, "F60", (end.Year - start.Year) * 12 + end.Month - start.Month + 1);
        if (IsEmpty(to, "C60") && result.Prior.Values.Values.Any(v => v != 0))
        {
            SetNumber(to, "C60", 12);
            diag.Warn(Area, "Broj mjeseci poslovanja u prethodnoj godini nije bio upisan – pretpostavljeno 12 (provjeri).");
        }

        // Mandatory documents for public disclosure (FINA controls 100, 104, 105): Bilješke, Odluka o raspodjeli
        // dobiti, Odluka o utvrđivanju GFI – required with svrha 2/3 and vrsta izvještaja 10/11, not allowed with svrha 1.
        if (Read(to, "C19") is double purpose && Read(to, "C17") is double reportType && reportType is 10 or 11)
        {
            var flag = purpose > 1 ? "DA" : "NE";
            foreach (var cell in new[] { "I56", "I66", "I68" }) GetOrCreate(to, cell).SetCellValue(flag);
            if (purpose > 1)
                diag.Info(Area, "Svrha predaje uključuje javnu objavu: u popisu dokumentacije označene su Bilješke, Odluka o utvrđivanju GFI i Odluka o raspodjeli dobiti (moraju se priložiti).");
        }

        // Not audited → auditor's opinion code 1 ("izvještaj nije revidiran").
        if (Read(to, "I21") is string audited && audited.Trim() == "NE" && IsEmpty(to, "C23"))
            SetNumber(to, "C23", 1);

        FillMunicipalityCode(to, diag);
        CheckAuditObligation(to, result, diag);

        foreach (var (cell, label) in Required)
            if (IsEmpty(to, cell)) diag.Warn(Area, $"Nije upisano: {label} – RefStr {cell}.");
    }

    private static void CopyFromSource(PriorGfi source, ISheet to, DiagnosticBag diag)
    {
        HSSFWorkbook wb;
        using (var fs = File.OpenRead(source.Path)) wb = new HSSFWorkbook(fs);
        var from = wb.GetSheet("RefStr");
        if (from == null) return;

        var copied = StableCells.Count(address => CopyCell(from, address, to, address));

        // Business activity: last year's "u razdoblju" becomes this year's "u prethodnoj godini".
        if (source.SameYear) { CopyCell(from, "N6", to, "N6"); CopyCell(from, "N7", to, "N7"); }
        else CopyCell(from, "N6", to, "N7");

        foreach (var (priorCell, currentCell) in YearPairs)
        {
            if (source.SameYear) { CopyCell(from, priorCell, to, priorCell); CopyCell(from, currentCell, to, currentCell); }
            else CopyCell(from, currentCell, to, priorCell);
        }

        diag.Info(Area, $"Preneseno {copied} podataka iz {Path.GetFileName(source.Path)} (identifikacija, adresa, šifre, kontakt).");
    }

    /// <summary>If the municipality code is missing, look it up by the place name in the template's own code list (A179:B734).</summary>
    private static void FillMunicipalityCode(ISheet refStr, DiagnosticBag diag)
    {
        if (Read(refStr, "C39") is double d && d > 0) return;
        if (Read(refStr, "F31") is not string place || place.Trim().Length == 0) return;

        for (var i = 178; i <= 733; i++)
        {
            var row = refStr.GetRow(i);
            var codeCell = row?.GetCell(0);
            var name = row?.GetCell(1)?.ToString()?.Trim();
            if (codeCell?.CellType != CellType.Numeric || name == null) continue;
            if (!string.Equals(name, place.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

            SetNumber(refStr, "C39", codeCell.NumericCellValue);
            diag.Warn(Area, $"Šifra grada/općine nije bila upisana – postavljena {codeCell.NumericCellValue} ({name}) prema nazivu naselja; provjeri.");
            return;
        }
    }

    /// <summary>
    /// Same rule as FINA control 42: a k.d., d.d. or d.o.o. must attach an audit report (vrsta 10/11, public disclosure)
    /// when in the PRIOR year it exceeded two of: assets 2.500.000 EUR, sales 5.000.000 EUR, 25 employees.
    /// </summary>
    private static void CheckAuditObligation(ISheet refStr, GfiResult result, DiagnosticBag diag)
    {
        if (Read(refStr, "C7") is not double entityType || entityType is not (2 or 4 or 5)) return;
        if (Read(refStr, "C19") is not double purpose || purpose <= 1) return;
        if (Read(refStr, "C17") is not double reportType || reportType is not (10 or 11)) return;

        var assets = result.Prior[65];
        var sales = result.Prior[129] + result.Prior[130];
        var employees = Read(refStr, "C56") as double? ?? 0;
        var exceeded = new List<string>();
        if (assets > 2_500_000m) exceeded.Add($"aktiva {assets:N2} > 2.500.000,00");
        if (sales > 5_000_000m) exceeded.Add($"prihodi od prodaje {sales:N2} > 5.000.000,00");
        if (employees >= 25) exceeded.Add($"zaposleni {employees} ≥ 25");
        if (exceeded.Count < 2) return;

        var audited = Read(refStr, "I21") as string;
        if (audited?.Trim() != "DA")
            diag.Error(Area, "Društvo je obveznik revizije (u prethodnoj godini: " + string.Join("; ", exceeded) +
                "). Za javnu objavu mora se priložiti revizorsko izvješće – na referentnoj stranici upiši 'Izvještaj je revidiran' = DA, OIB revizora i mišljenje revizora.");
    }

    // ------------------------------------------------------------------ cell helpers

    private static ICell GetOrCreate(ISheet sheet, string address)
    {
        var r = new CellReference(address);
        return (sheet.GetRow(r.Row) ?? sheet.CreateRow(r.Row)).GetCell(r.Col, MissingCellPolicy.CREATE_NULL_AS_BLANK);
    }

    private static object? Read(ISheet sheet, string address)
    {
        var r = new CellReference(address);
        var c = sheet.GetRow(r.Row)?.GetCell(r.Col);
        return c?.CellType switch
        {
            CellType.String => c.StringCellValue,
            CellType.Numeric => c.NumericCellValue,
            _ => null,
        };
    }

    private static bool IsEmpty(ISheet sheet, string address) => Read(sheet, address) switch
    {
        null => true,
        string s => s.Trim().Length == 0,
        _ => false,
    };

    private static bool CopyCell(ISheet from, string fromAddress, ISheet to, string toAddress)
    {
        var value = Read(from, fromAddress);
        if (value == null || (value is string s && s.Trim().Length == 0)) return false;
        var cell = GetOrCreate(to, toAddress);
        if (value is string text) cell.SetCellValue(text);
        else cell.SetCellValue((double)value);
        return true;
    }

    private static void SetNumber(ISheet sheet, string address, double value) => GetOrCreate(sheet, address).SetCellValue(value);
}
