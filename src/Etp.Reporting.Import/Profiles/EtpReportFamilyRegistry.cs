using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Etp.Reporting.Domain.Imports;

namespace Etp.Reporting.Import.Profiles;

public sealed record EtpSourceColumn(string SourceHeader, string CanonicalField, CanonicalDataType DataType, bool IsRequired);

// BusinessUnit is last and defaulted so the Retail catalogue entries, which do not carry it, stay unchanged.
public sealed record EtpReportFamily(
    string FamilyCode, string ReportCode, string Name, bool IsTyped, string TableName,
    string? PrimaryDateHeader, IReadOnlyList<string> Headers, IReadOnlyList<EtpSourceColumn> Columns,
    string BusinessUnit = EtpReportFamily.RetailBusinessUnit)
{
    public const string RetailBusinessUnit = "RETAIL";
    public const string ServiceBusinessUnit = "SERVICE";

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

    /// <summary>Retail R### and Service Centre S### family codes as they appear in consolidated file names.</summary>
    public static Regex FamilyCodePattern { get; } = new(@"(?:^|[^A-Z0-9])([RS]\d{3})(?:[^A-Z0-9]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Names a family from a file or sheet name. <paramref name="among"/> limits the answer to the
    /// families whose header signature already matched, so a Service name such as RevenueReport cannot
    /// take a Retail Revenue Report file (or the reverse) by catalogue order.</summary>
    public static EtpReportFamily? IdentifyName(string? name, IEnumerable<EtpReportFamily>? among = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var pool = (among ?? Families).ToArray();
        var code = FamilyCodePattern.Match(name);
        // A code in the name decides; a code outside the pool names no family rather than falling back to the words.
        if (code.Success) return pool.FirstOrDefault(f => f.FamilyCode.Equals(code.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        var normalized = NormalizeName(name);
        // Longest first prevents the OMNI variant matching ordinary variant sales and RepairRegister
        // taking the RepairRegister_SRNINV view.
        return pool.OrderByDescending(f => NormalizeName(f.Name).Length)
            .FirstOrDefault(f => normalized.Contains(NormalizeName(f.Name), StringComparison.Ordinal));
    }

    /// <summary>True unless the code is a catalogued Service Centre family. A code the catalogue does not
    /// know (a legacy profile) keeps today's Retail behaviour.</summary>
    public static bool IsRetail(string reportCode) =>
        Families.FirstOrDefault(f => f.ReportCode == reportCode || f.FamilyCode == reportCode)?.BusinessUnit
            != EtpReportFamily.ServiceBusinessUnit;

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
