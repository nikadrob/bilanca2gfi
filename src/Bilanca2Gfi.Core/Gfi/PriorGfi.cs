namespace Bilanca2Gfi.Core.Gfi;

/// <summary>Leaf AOP values read from an earlier GFI-POD file.</summary>
public sealed class PriorGfi
{
    public required string Path { get; init; }
    public required string ColumnLabel { get; init; }

    /// <summary>True when the file is a (draft) report for the same year, false when it is last year's report.</summary>
    public required bool SameYear { get; init; }
    public required Dictionary<int, decimal> LeafValues { get; init; }
}
