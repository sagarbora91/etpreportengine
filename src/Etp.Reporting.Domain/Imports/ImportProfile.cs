namespace Etp.Reporting.Domain.Imports;

public enum CanonicalDataType
{
    Text,
    Identifier,
    Date,
    Decimal,
    Integer,
    Boolean
}

public sealed record ImportFieldMapping(
    string SourceHeader,
    string CanonicalField,
    CanonicalDataType DataType,
    bool IsRequired,
    string? Transformation = null);

public sealed record ImportProfileIdentity
{
    public ImportProfileIdentity(
        string reportCode,
        string layoutVersion,
        string profileVersion,
        string headerSignatureSha256)
    {
        ReportCode = RequiredToken(reportCode, nameof(reportCode));
        LayoutVersion = RequiredToken(layoutVersion, nameof(layoutVersion));
        ProfileVersion = RequiredToken(profileVersion, nameof(profileVersion));
        HeaderSignatureSha256 = ValidateSha256(headerSignatureSha256);
    }

    public string ReportCode { get; }
    public string LayoutVersion { get; }
    public string ProfileVersion { get; }
    public string HeaderSignatureSha256 { get; }

    private static string RequiredToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);
        return value.Trim();
    }

    private static string ValidateSha256(string value)
    {
        var normalized = RequiredToken(value, nameof(value)).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("A lowercase 64-character SHA-256 value is required.", nameof(value));
        return normalized;
    }
}

public sealed class ImportProfile
{
    public ImportProfile(
        string reportCode,
        string layoutVersion,
        string profileVersion,
        string headerSignatureSha256,
        IEnumerable<ImportFieldMapping> fields,
        IEnumerable<string>? expectedSourceHeaders = null)
    {
        ReportCode = RequiredToken(reportCode, nameof(reportCode));
        LayoutVersion = RequiredToken(layoutVersion, nameof(layoutVersion));
        ProfileVersion = RequiredToken(profileVersion, nameof(profileVersion));
        HeaderSignatureSha256 = ValidateSha256(headerSignatureSha256);
        Identity = new ImportProfileIdentity(ReportCode, LayoutVersion, ProfileVersion, HeaderSignatureSha256);

        var materialized = fields?.ToArray() ?? throw new ArgumentNullException(nameof(fields));
        if (materialized.Length == 0)
            throw new ArgumentException("An import profile requires at least one field mapping.", nameof(fields));

        EnsureUnique(materialized.Select(x => NormalizeHeader(x.SourceHeader)), "source header");
        EnsureUnique(materialized.Select(x => x.CanonicalField), "canonical field");
        Fields = Array.AsReadOnly(materialized);

        var expected = (expectedSourceHeaders ?? materialized.Select(x => x.SourceHeader))
            .Select(RequiredExpectedHeader)
            .ToArray();
        if (expected.Length == 0)
            throw new ArgumentException("An import profile requires at least one expected source header.", nameof(expectedSourceHeaders));
        EnsureUnique(expected.Select(NormalizeHeader), "expected source header");
        ExpectedSourceHeaders = Array.AsReadOnly(expected);
    }

    public string ReportCode { get; }
    public string LayoutVersion { get; }
    public string ProfileVersion { get; }
    public string HeaderSignatureSha256 { get; }
    public ImportProfileIdentity Identity { get; }
    public IReadOnlyList<ImportFieldMapping> Fields { get; }
    public IReadOnlyList<string> ExpectedSourceHeaders { get; }

    public static string NormalizeHeader(string value) =>
        string.Join(' ', RequiredToken(value, nameof(value)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToUpperInvariant();

    private static string RequiredToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);
        return value.Trim();
    }

    private static string RequiredExpectedHeader(string value) => RequiredToken(value, nameof(ExpectedSourceHeaders));

    private static string ValidateSha256(string value)
    {
        var normalized = RequiredToken(value, nameof(value)).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("A lowercase 64-character SHA-256 value is required.", nameof(value));
        return normalized;
    }

    private static void EnsureUnique(IEnumerable<string> values, string label)
    {
        if (values.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new ArgumentException($"Duplicate {label} mappings are not allowed.");
    }
}
