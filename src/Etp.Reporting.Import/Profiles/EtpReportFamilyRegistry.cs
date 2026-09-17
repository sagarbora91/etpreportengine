using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Etp.Reporting.Domain.Imports;

namespace Etp.Reporting.Import.Profiles;

public sealed record EtpSourceColumn(string SourceHeader, string CanonicalField, CanonicalDataType DataType, bool IsRequired);

public sealed record EtpReportFamily(
    string FamilyCode, string ReportCode, string Name, bool IsTyped, string TableName,
    string? PrimaryDateHeader, IReadOnlyList<string> Headers, IReadOnlyList<EtpSourceColumn> Columns)
{
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

    private static IReadOnlyList<EtpReportFamily> Load()
    {
        using var stream = typeof(EtpReportFamilyRegistry).Assembly.GetManifestResourceStream(
            "Etp.Reporting.Import.Profiles.EtpReportFamilies.json")!;
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return Array.AsReadOnly(JsonSerializer.Deserialize<EtpReportFamily[]>(stream, options)!);
    }
}
