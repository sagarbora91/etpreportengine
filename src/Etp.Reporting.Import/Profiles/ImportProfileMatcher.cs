using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Domain.Imports;

namespace Etp.Reporting.Import.Profiles;

public sealed class ImportProfileMatcher
{
    /// <param name="familyCode">A contract workbook's <c>family_code</c>; it settles a header tie before the file and sheet names.</param>
    public ImportProfile? Match(IEnumerable<string> sourceHeaders, IEnumerable<ImportProfile> profiles,
        string? fileName = null, string? sheetName = null, string? familyCode = null)
    {
        ArgumentNullException.ThrowIfNull(sourceHeaders);
        ArgumentNullException.ThrowIfNull(profiles);

        var signature = CreateHeaderSignature(sourceHeaders);
        var matches = profiles.Where(x => x.HeaderSignatureSha256 == signature).ToArray();
        if (matches.Length <= 1) return matches.SingleOrDefault();
        var declared = string.IsNullOrWhiteSpace(familyCode) ? null : EtpReportFamilyRegistry.Families.FirstOrDefault(family =>
            family.FamilyCode.Equals(familyCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (declared is not null && matches.SingleOrDefault(profile => profile.ReportCode == declared.ReportCode) is { } chosen) return chosen;
        // Only the families that share this signature may be named; profiles outside the catalogue are never candidates.
        // A shared layout whose names carry neither a code nor a known name is not guessed.
        var candidates = EtpReportFamilyRegistry.Families
            .Where(family => matches.Any(profile => profile.ReportCode == family.ReportCode)).ToArray();
        var named = EtpReportFamilyRegistry.IdentifyName(fileName, candidates) ?? EtpReportFamilyRegistry.IdentifyName(sheetName, candidates);
        return named is null ? null : matches.SingleOrDefault(profile => profile.ReportCode == named.ReportCode);
    }

    public static string CreateHeaderSignature(IEnumerable<string> sourceHeaders)
    {
        var headers = sourceHeaders.Select(ImportProfile.NormalizeHeader).ToArray();
        if (headers.Length == 0)
            throw new ArgumentException("At least one source header is required.", nameof(sourceHeaders));

        var canonical = string.Join("\u001f", headers);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
