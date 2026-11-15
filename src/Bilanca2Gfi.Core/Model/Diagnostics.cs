namespace Bilanca2Gfi.Core.Model;

public enum Severity { Info, Warning, Error }

public sealed record Diagnostic(Severity Severity, string Area, string Message)
{
    public override string ToString() => $"[{Severity switch { Severity.Error => "GREŠKA", Severity.Warning => "UPOZORENJE", _ => "INFO" }}] {Area}: {Message}";
}

public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = [];
    public IReadOnlyList<Diagnostic> Items => _items;

    public void Info(string area, string message) => _items.Add(new(Severity.Info, area, message));
    public void Warn(string area, string message) => _items.Add(new(Severity.Warning, area, message));
    public void Error(string area, string message) => _items.Add(new(Severity.Error, area, message));

    public bool HasErrors => _items.Any(d => d.Severity == Severity.Error);
    public int Count(Severity s) => _items.Count(d => d.Severity == s);
}
