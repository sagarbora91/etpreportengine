using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Etp.Reporting.Domain.Imports;

namespace Etp.Reporting.Import.Profiles;

public sealed record EtpSourceColumn(string SourceHeader, string CanonicalField, CanonicalDataType DataType, bool IsRequired)
{
    /// <summary>How the column takes part in identity and change detection (spec 7.1). Unlisted columns are facts.</summary>
    public ColumnRole Role { get; init; } = ColumnRole.Fact;
}

public sealed record EtpReportFamily(
    string FamilyCode, string ReportCode, string Name, bool IsTyped, string TableName,
    string? PrimaryDateHeader, IReadOnlyList<string> Headers, IReadOnlyList<EtpSourceColumn> Columns)
{
    public BusinessUnit BusinessUnit { get; init; } = BusinessUnit.Retail;
    /// <summary>A consolidation-made union with no raw export (S001): reported <c>Not needed</c> with <c>FAMILY_DERIVED</c>.</summary>
    public bool Derived { get; init; }
    /// <summary>Trailing columns a legacy consolidated workbook may add (S027, S028: SourcePeriodFrom, SourcePeriodTo, SourceFile).</summary>
    public IReadOnlyList<string> ConsolidationColumns { get; init; } = [];
    /// <summary>The ETP report names raw Service files carry (OD-6).</summary>
    public IReadOnlyList<string> RawNamePatterns { get; init; } = [];
    /// <summary>The document identity and row rules (spec 7.1); null until the family is described.</summary>
    public EtpFamilyIdentity? Identity { get; init; }

    public IEnumerable<EtpSourceColumn> ColumnsWithRole(ColumnRole role) => Columns.Where(column => column.Role == role);

    public ImportProfile CreateProfile() => new(ReportCode, "ETP_2026_09", "2",
        ImportProfileMatcher.CreateHeaderSignature(Headers),
        Columns.Select(column => new ImportFieldMapping(column.SourceHeader, column.CanonicalField,
            column.DataType, column.IsRequired)), Headers);
}

/// <summary>Exact export schemas, captured from the approved raw and consolidated corpus; contains no customer data.</summary>
public static class EtpReportFamilyRegistry
{
    public static IReadOnlyList<EtpReportFamily> Families { get; } = Load();

    public static EtpReportFamily Resolve(string reportCode) => Families.Single(family =>
        family.ReportCode == reportCode || family.FamilyCode == reportCode);

    public static EtpReportFamily? IdentifyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var code = Regex.Match(name, @"(?:^|[^A-Z0-9])(R\d{3})(?:[^A-Z0-9]|$)", RegexOptions.IgnoreCase);
        if (code.Success) return Families.FirstOrDefault(f => f.FamilyCode.Equals(code.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        var normalized = NormalizeName(name);
        // Longest first prevents the OMNI variant matching ordinary variant sales.
        return Families.OrderByDescending(f => NormalizeName(f.Name).Length)
            .FirstOrDefault(f => normalized.Contains(NormalizeName(f.Name), StringComparison.Ordinal));
    }

    private static string NormalizeName(string value) => Regex.Replace(value.ToUpperInvariant().Replace("RECIEPT", "RECEIPT"), "[^A-Z0-9]", "");

    /// <summary>Reads a catalogue in the shape of <c>EtpReportFamilies.json</c>, e.g. a small test catalogue.</summary>
    public static IReadOnlyList<EtpReportFamily> Parse(Stream json)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return Array.AsReadOnly(JsonSerializer.Deserialize<EtpReportFamily[]>(json, options)!);
    }

    private static IReadOnlyList<EtpReportFamily> Load()
    {
        using var stream = typeof(EtpReportFamilyRegistry).Assembly.GetManifestResourceStream(
            "Etp.Reporting.Import.Profiles.EtpReportFamilies.json")!;
        return Parse(stream);
    }
}
