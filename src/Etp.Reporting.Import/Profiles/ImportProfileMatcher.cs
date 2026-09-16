using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Domain.Imports;

namespace Etp.Reporting.Import.Profiles;

public sealed class ImportProfileMatcher
{
    public ImportProfile? Match(IEnumerable<string> sourceHeaders, IEnumerable<ImportProfile> profiles,
        string? fileName = null, string? sheetName = null)
    {
        ArgumentNullException.ThrowIfNull(sourceHeaders);
        ArgumentNullException.ThrowIfNull(profiles);

        var signature = CreateHeaderSignature(sourceHeaders);
        var matches = profiles.Where(x => x.HeaderSignatureSha256 == signature).ToArray();
        if (matches.Length <= 1) return matches.SingleOrDefault();
        var named = EtpReportFamilyRegistry.IdentifyName(fileName) ?? EtpReportFamilyRegistry.IdentifyName(sheetName);
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
