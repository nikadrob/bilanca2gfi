namespace Bilanca2Gfi.Core.Model;

/// <summary>The eight amount columns of a "Brutto bilanca po kontima" row.</summary>
public enum Column
{
    PsD = 0,      // Početno stanje - duguje
    PsP = 1,      // Početno stanje - potražuje
    PrometD = 2,  // Promet bez početnog stanja - duguje
    PrometP = 3,
    UkupnoD = 4,  // Ukupan promet - duguje
    UkupnoP = 5,
    SaldoD = 6,
    SaldoP = 7,
}

public sealed class Amounts
{
    public const int Count = 8;
    private readonly decimal[] _values = new decimal[Count];

    public decimal this[Column c]
    {
        get => _values[(int)c];
        set => _values[(int)c] = value;
    }

    /// <summary>Closing balance, debit minus credit.</summary>
    public decimal SaldoNet => this[Column.SaldoD] - this[Column.SaldoP];

    /// <summary>Opening balance (= prior year closing), debit minus credit.</summary>
    public decimal PsNet => this[Column.PsD] - this[Column.PsP];

    public bool IsZero => _values.All(v => v == 0m);

    public void Add(Amounts other)
    {
        for (var i = 0; i < Count; i++) _values[i] += other._values[i];
    }

    public override string ToString() => string.Join(" | ", _values.Select(v => v.ToString("N2")));
}

public sealed record AccountLine(string Konto, string Naziv, Amounts Amounts)
{
    public int Razred => Konto[0] - '0';
}

public sealed record GroupTotal(string Code, Amounts Amounts);

public sealed class TrialBalance
{
    public string CompanyName { get; init; } = "";
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public List<AccountLine> Accounts { get; } = [];
    public List<GroupTotal> Groups { get; } = [];
    public Amounts? GrandTotal { get; set; }

    public int Year => DateTo?.Year ?? DateFrom?.Year ?? DateTime.Today.Year;
}
