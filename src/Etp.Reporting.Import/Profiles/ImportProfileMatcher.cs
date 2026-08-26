using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Domain.Imports;

namespace Etp.Reporting.Import.Profiles;

public sealed class ImportProfileMatcher
{
    public ImportProfile? Match(IEnumerable<string> sourceHeaders, IEnumerable<ImportProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(sourceHeaders);
        ArgumentNullException.ThrowIfNull(profiles);

        var signature = CreateHeaderSignature(sourceHeaders);
        var matches = profiles.Where(x => x.HeaderSignatureSha256 == signature).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException("Multiple active import profiles share the same header signature.")
        };
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
