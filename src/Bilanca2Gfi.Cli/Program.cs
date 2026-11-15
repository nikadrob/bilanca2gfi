using System.Globalization;
using System.Text;
using Bilanca2Gfi.Core.Model;
using Bilanca2Gfi.Core.Services;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("hr-HR");

try
{
    var options = Options.Parse(args);
    if (options.Help) { Options.PrintHelp(); return 0; }
    return Run(options);
}
catch (Exception ex) when (ex is UsageException or GfiInputException)
{
    Console.Error.WriteLine("Greška: " + ex.Message);
    Console.Error.WriteLine("Za upute pokreni: bilanca2gfi --pomoc");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Neočekivana greška: " + ex.Message);
    return 1;
}

static int Run(Options o)
{
    var generator = new GfiGenerator(AppContext.BaseDirectory);
    var r = generator.Run(new GfiRequest
    {
        Folder = o.Folder ?? Directory.GetCurrentDirectory(),
        PdfPath = o.Pdf,
        PriorPath = o.Prior,
        NoPrior = o.NoPrior,
        TemplatePath = o.Template,
        ConfigPath = o.Config,
        OutputPath = o.Output,
        ProfitTax = o.ProfitTax,
        Employees = o.Employees,
        EmployeesByHours = o.EmployeesByHours,
    });

    Console.WriteLine($"Bruto bilanca:    {r.PdfPath}");
    Console.WriteLine($"Tvrtka:           {r.Company}, razdoblje {r.DateFrom:dd.MM.yyyy} – {r.DateTo:dd.MM.yyyy}, {r.AccountCount} konta");
    Console.WriteLine($"Pravila:          {r.ConfigName}");
    Console.WriteLine($"Prethodna godina: {(r.PriorPath != null ? Path.GetFileName(r.PriorPath) : "—")}");
    Console.WriteLine();
    foreach (var d in r.Diagnostics.Where(d => d.Severity != Severity.Info)) WriteColored(d);
    Console.WriteLine();
    Console.WriteLine($"Aktiva / pasiva:   {r.Totals.Aktiva,16:N2} / {r.Totals.Pasiva,16:N2}");
    Console.WriteLine($"Dobit prije poreza:{r.Totals.DobitPrijePoreza,16:N2}");
    Console.WriteLine($"Dobit razdoblja:   {r.Totals.DobitRazdoblja,16:N2}");
    Console.WriteLine();
    Console.WriteLine($"GFI:       {r.OutputPath}");
    Console.WriteLine($"Izvještaj: {r.ReportPath}");
    Console.WriteLine($"Greške: {r.ErrorCount}, upozorenja: {r.WarningCount}");
    return r.ErrorCount > 0 ? 2 : 0;
}

static void WriteColored(Diagnostic d)
{
    var old = Console.ForegroundColor;
    Console.ForegroundColor = d.Severity == Severity.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
    Console.WriteLine(d);
    Console.ForegroundColor = old;
}

sealed class UsageException(string message) : Exception(message);

sealed class Options
{
    public string? Folder { get; private set; }
    public string? Pdf { get; private set; }
    public string? Template { get; private set; }
    public string? Config { get; private set; }
    public string? Prior { get; private set; }
    public string? Output { get; private set; }
    public decimal? ProfitTax { get; private set; }
    public decimal? Employees { get; private set; }
    public decimal? EmployeesByHours { get; private set; }
    public bool NoPrior { get; private set; }
    public bool Help { get; private set; }

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new UsageException($"Opcija {args[i]} traži vrijednost.");
            switch (args[i].ToLowerInvariant())
            {
                case "--mapa": o.Folder = Next(); break;
                case "--pdf": o.Pdf = Next(); break;
                case "--predlozak": o.Template = Next(); break;
                case "--config": o.Config = Next(); break;
                case "--prethodni": o.Prior = Next(); break;
                case "--bez-prethodnog": o.NoPrior = true; break;
                case "--izlaz": o.Output = Next(); break;
                case "--porez": o.ProfitTax = ParseAmount(Next()); break;
                case "--zaposleni": o.Employees = ParseAmount(Next()); break;
                case "--zaposleni-sati": o.EmployeesByHours = ParseAmount(Next()); break;
                case "--pomoc" or "-h" or "--help" or "/?": o.Help = true; break;
                default: throw new UsageException($"Nepoznata opcija: {args[i]}");
            }
        }
        return o;
    }

    private static decimal ParseAmount(string s)
    {
        // Accept "12.345,67", "12345,67" and "12345.67".
        var normalized = s.Contains(',') ? s.Replace(".", "").Replace(',', '.') : s;
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) || d < 0)
            throw new UsageException($"Neispravan broj: {s}");
        return d;
    }

    public static void PrintHelp() => Console.WriteLine("""
        bilanca2gfi – bruto bilanca (PDF) → GFI-POD (Bilanca, RDG, Dodatni podaci)

        Upotreba: bilanca2gfi [opcije]

          --mapa <mapa>        Radna mapa (zadano: trenutna). Iz nje se uzima PDF bilanca i prošlogodišnji GFI.
          --pdf <datoteka>     PDF bruto bilance (zadano: jedini PDF s bruto bilancom u radnoj mapi).
          --porez <iznos>      Porez na dobit tekuće godine (npr. 9.876,54). Ako se ne zada, upisuje se 0.
          --zaposleni <broj>   Prosječan broj zaposlenih u tekućoj godini (referentna stranica).
          --zaposleni-sati <b> Broj zaposlenih prema satima rada u tekućoj godini.
          --prethodni <xls>    Prošlogodišnji GFI (.xls): RDG i Dodatni podaci prethodne godine te podaci
                               referentne stranice (zadano: jedina .xls datoteka u radnoj mapi).
          --bez-prethodnog     Ne koristi prošlogodišnji GFI.
          --predlozak <xls>    FINA predložak (zadano: templates/GFI-POD-6.0.2.xls uz program).
          --config <json>      Pravila mapiranja konto → AOP (zadano: prema nazivu tvrtke iz mape config).
          --izlaz <xls>        Izlazna datoteka (zadano: izlaz/GFI_<tvrtka>_<godina>.xls).

        Izlazni kod: 0 = uspješno, 2 = izrađeno, ali s greškama u kontrolama, 1 = prekid.
        """);
}
