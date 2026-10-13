using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.Morning;

namespace Etp.Reporting.Infrastructure.SqlServer.Morning;

/// <summary>
/// Reads the shop result files of one business day for the Morning page (1.12 U1): <c>ShopResults\pending</c> (written by
/// ETP, not yet sent) and <c>ShopResults\published</c> (signed and sent by <c>Publish-ShopResults.ps1</c>). Only the role,
/// day, sequence, overall verdict and file names are read; the files hold no amounts and no names (C1). A folder that
/// does not exist or cannot be read gives no results, never an error.
/// </summary>
public sealed partial class ShopResultFolder(string resultsFolder)
{
    public static string DefaultFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Operations", "ShopResults");

    public ShopResultFolder() : this(DefaultFolder()) { }

    /// <summary>The C1 file name: <c>ROLE-yyyy-MM-dd-seq.json</c>.</summary>
    [GeneratedRegex(@"^(?<role>TITAN|HELIOS|SERVICE)-(?<day>\d{4}-\d{2}-\d{2})-(?<seq>\d{1,3})\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    /// <summary>Largest result file read; a C1 result is a few kilobytes.</summary>
    public const long MaximumFileBytes = 256 * 1024;

    public IReadOnlyList<MorningShopResult> Read(DateOnly businessDate)
    {
        var results = new List<MorningShopResult>();
        foreach (var (sub, published) in new[] { ("pending", false), ("published", true) })
        {
            var folder = Path.Combine(resultsFolder, sub);
            IEnumerable<string> files;
            try { files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.json").ToArray() : []; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            foreach (var path in files)
            {
                if (TryRead(path, published, businessDate) is { } result) results.Add(result);
            }
        }
        return results;
    }

    internal static MorningShopResult? TryRead(string path, bool published, DateOnly businessDate)
    {
        var match = FileNamePattern().Match(Path.GetFileName(path));
        if (!match.Success
            || !DateOnly.TryParseExact(match.Groups["day"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            || day != businessDate) return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumFileBytes) return null;
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            var overall = root.TryGetProperty("overall", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            var names = root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array
                ? files.EnumerateArray()
                    .Select(file => file.ValueKind == JsonValueKind.Object && file.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null)
                    .OfType<string>().ToArray()
                : [];
            return new(match.Groups["role"].Value.ToUpperInvariant(), day, int.Parse(match.Groups["seq"].Value, CultureInfo.InvariantCulture), published,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), overall, names);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            return null;
        }
    }
}
