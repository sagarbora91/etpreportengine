namespace Etp.Reporting.Domain.Imports;

public sealed record SourceLineage
{
    public SourceLineage(string fileSha256, string sheetName, int sourceRowNumber)
    {
        FileSha256 = ValidateSha256(fileSha256);
        SheetName = string.IsNullOrWhiteSpace(sheetName)
            ? throw new ArgumentException("Sheet name is required.", nameof(sheetName))
            : sheetName.Trim();
        SourceRowNumber = sourceRowNumber > 0
            ? sourceRowNumber
            : throw new ArgumentOutOfRangeException(nameof(sourceRowNumber), "Source row number must be positive.");
    }

    public string FileSha256 { get; }
    public string SheetName { get; }
    public int SourceRowNumber { get; }

    private static string ValidateSha256(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("A 64-character SHA-256 value is required.", nameof(value));
        return normalized;
    }
}
