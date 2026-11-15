using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bilanca2Gfi.Core.Mapping;

/// <summary>
/// Konto → AOP rules for one chart of accounts (kontni plan). Loaded from a JSON file in /config.
/// </summary>
public sealed class MappingConfig
{
    /// <summary>Human readable name of the rule set.</summary>
    public string Naziv { get; set; } = "";

    /// <summary>Text that must appear in the PDF company name for this config to be picked automatically.</summary>
    public List<string> Prepoznavanje { get; set; } = [];

    /// <summary>Balance sheet rules (classes 0-3, 9 and any class 2 konto). Longest konto prefix wins.</summary>
    public List<AccountRule> Bilanca { get; set; } = [];

    /// <summary>Income statement rules (classes 4 and 7). Longest konto prefix wins.</summary>
    public List<AccountRule> Rdg { get; set; } = [];

    /// <summary>Dodatni podaci (AOP 230-299). Each item is computed independently; a konto may feed several items.</summary>
    public List<AdditionalRule> Dodatni { get; set; } = [];

    /// <summary>Fixed values for AOPs that cannot be derived from konta (key = AOP number).</summary>
    public Dictionary<int, decimal> RucneVrijednosti { get; set; } = [];

    /// <summary>Fixed reference-page data of the company; overrides values copied from last year's GFI.</summary>
    public ReferencePageData ReferentnaStranica { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static MappingConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MappingConfig>(json, JsonOptions)
               ?? throw new InvalidDataException($"Prazna konfiguracija: {path}");
    }
}

/// <summary>Reference page (RefStr) codes – see FINA "Upute", chapter 6 (šifrarnici). Null = leave as is.</summary>
public sealed class ReferencePageData
{
    public int? VrstaSubjekta { get; set; }          // C7  (5 = d.o.o.)
    public string? Standard { get; set; }            // N19 (HSFI / MSFI)
    public int? SvrhaPredaje { get; set; }           // C19 (1 statistika, 2 javna objava, 3 oboje)
    public int? VrstaIzvjestaja { get; set; }        // C17 (10 = kalendarska godina bez statusnih promjena)
    public int? StatusAutonomnosti { get; set; }     // C44
    public int? OznakaVelicine { get; set; }         // C50 (1 mikro … 4 veliki)
    public int? OznakaVlasnistva { get; set; }       // C52
    public int? KapitalDomaciPosto { get; set; }     // C54
    public int? KapitalStraniPosto { get; set; }     // F54
    public int? SifraOpcine { get; set; }            // C39
    public string? SifraNkd { get; set; }            // C42
    public string? Revidiran { get; set; }           // I21 (DA / NE)
    public string? OibRevizora { get; set; }         // L21
    public int? MisljenjeRevizora { get; set; }      // C23 (1 nije revidiran, 2 pozitivno, 3 s rezervom, 4 suzdržano, 5 negativno)
    public decimal? ZaposleniProsjek { get; set; }   // F56 – tekuća godina (obično iz naredbenog retka)
    public decimal? ZaposleniSati { get; set; }      // F58 – tekuća godina (obično iz naredbenog retka)

    public IEnumerable<(string Cell, object Value)> Values()
    {
        if (VrstaSubjekta is { } a) yield return ("C7", a);
        if (Standard is { } b) yield return ("N19", b);
        if (SvrhaPredaje is { } c) yield return ("C19", c);
        if (VrstaIzvjestaja is { } d) yield return ("C17", d);
        if (StatusAutonomnosti is { } e) yield return ("C44", e);
        if (OznakaVelicine is { } f) yield return ("C50", f);
        if (OznakaVlasnistva is { } g) yield return ("C52", g);
        if (KapitalDomaciPosto is { } h) yield return ("C54", h);
        if (KapitalStraniPosto is { } i) yield return ("F54", i);
        if (SifraOpcine is { } j) yield return ("C39", j);
        if (SifraNkd is { } k) yield return ("C42", k);
        if (Revidiran is { } r) yield return ("I21", r);
        if (OibRevizora is { } o) yield return ("L21", o);
        if (MisljenjeRevizora is { } p) yield return ("C23", p);
        if (Revidiran is { } r2 && r2.Trim() == "DA") yield return ("I62", "DA"); // revizorsko izvješće u popisu dokumentacije
        if (ZaposleniProsjek is { } l) yield return ("F56", l);
        if (ZaposleniSati is { } m) yield return ("F58", m);
    }
}

public sealed class AccountRule
{
    /// <summary>Konto prefix ("039") or full konto ("03901").</summary>
    public string Konto { get; set; } = "";

    public int Aop { get; set; }

    /// <summary>
    /// +1 keeps the natural sign of the AOP side (aktiva/rashod = D−P, pasiva/prihod = P−D); −1 inverts it,
    /// e.g. for an odbitna stavka booked on the debit side but shown as a positive amount in the form.
    /// </summary>
    public int Predznak { get; set; } = 1;

    public string? Opis { get; set; }
}

public enum Measure
{
    /// <summary>Closing balance D−P.</summary>
    SaldoD,
    /// <summary>Closing balance P−D.</summary>
    SaldoP,
    /// <summary>Debit turnover of the period (without opening balance).</summary>
    PrometD,
    /// <summary>Credit turnover of the period (without opening balance).</summary>
    PrometP,
}

public sealed class AdditionalRule
{
    public int Aop { get; set; }

    /// <summary>Konto prefixes included.</summary>
    public List<string> Konta { get; set; } = [];

    /// <summary>Konto prefixes excluded (more specific than an included prefix).</summary>
    public List<string> Osim { get; set; } = [];

    public Measure Mjera { get; set; } = Measure.SaldoD;

    public string? Opis { get; set; }
}
