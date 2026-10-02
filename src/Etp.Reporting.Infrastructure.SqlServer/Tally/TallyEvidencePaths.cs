using System.Globalization;
using System.Text.RegularExpressions;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <summary>Builds and checks paths inside the Tally evidence root (plan task 4):
/// <c>&lt;profile_code&gt;\&lt;store_code&gt;\&lt;yyyy-MM&gt;\batch-&lt;id&gt;\&lt;file&gt;</c>.
/// Only fixed names and code-shaped segments are accepted, so no customer name or phone number
/// can ever become part of a path.</summary>
public static class TallyEvidencePaths
{
    private static readonly Regex CodeSegment = new("^[A-Z0-9][A-Z0-9_-]{0,29}$", RegexOptions.CultureInvariant);
    private static readonly Regex LongDigitRun = new(@"\d{10,}", RegexOptions.CultureInvariant);
    private static readonly Regex MonthSegment = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.CultureInvariant);
    private static readonly Regex BatchSegment = new(@"^batch-[1-9]\d{0,18}$", RegexOptions.CultureInvariant);
    private static readonly Regex AttemptFile = new(@"^attempt-[1-9]\d{0,18}-(request|response)\.xml$", RegexOptions.CultureInvariant);
    private static readonly Regex ReadbackFile = new(@"^readback-[1-9]\d{0,18}\.xml$", RegexOptions.CultureInvariant);
    private static readonly Regex RunFile = new(@"^run-[1-9]\d{0,18}\.json$", RegexOptions.CultureInvariant);
    private static readonly Regex RecoveryPlanFile = new(@"^recovery-plan-[1-9]\d{0,18}\.json$", RegexOptions.CultureInvariant);

    /// <summary>The files that sit directly in a batch folder (plan task 21).</summary>
    public static IReadOnlyList<string> BatchFiles { get; } =
    [
        "manifest.json", "source-snapshot.json", "expected-postings.json", "validation.json", "payload.xml", "approvals.json"
    ];

    public static string BatchFolder(string profileCode, string storeCode, DateOnly month, long batchId)
    {
        if (batchId < 1) throw new ArgumentOutOfRangeException(nameof(batchId), "The batch id must be positive.");
        var folder = string.Join('\\', profileCode, storeCode, month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            string.Create(CultureInfo.InvariantCulture, $"batch-{batchId}"));
        ValidateSegments(folder.Split('\\'), requireFile: false);
        return folder;
    }

    /// <summary>Returns the path with <c>\</c> separators, or throws <see cref="ArgumentException"/>.</summary>
    public static string Validate(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("An evidence path is required.", nameof(relativePath));
        var segments = relativePath.Split('\\', '/');
        ValidateSegments(segments, requireFile: true);
        return string.Join('\\', segments);
    }

    private static void ValidateSegments(string[] segments, bool requireFile)
    {
        var expected = requireFile ? "<profile>\\<store>\\<yyyy-MM>\\batch-<id>\\[attempts|actuals|reconciliation\\]<file>" : "<profile>\\<store>\\<yyyy-MM>\\batch-<id>";
        if (requireFile ? segments.Length is not (5 or 6) : segments.Length != 4)
            throw Rejected($"An evidence path must have the form {expected}.");

        RequireCode(segments[0], "profile code");
        RequireCode(segments[1], "store code");
        if (!MonthSegment.IsMatch(segments[2])) throw Rejected("The month folder must be yyyy-MM.");
        if (!BatchSegment.IsMatch(segments[3])) throw Rejected("The batch folder must be batch-<id>.");
        if (!requireFile) return;

        if (segments.Length == 5)
        {
            if (!BatchFiles.Contains(segments[4], StringComparer.Ordinal) && !RecoveryPlanFile.IsMatch(segments[4]))
                throw Rejected("Only the fixed evidence file names may be written in a batch folder.");
            return;
        }

        var valid = segments[4] switch
        {
            "attempts" => AttemptFile.IsMatch(segments[5]),
            "actuals" => ReadbackFile.IsMatch(segments[5]),
            "reconciliation" => RunFile.IsMatch(segments[5]),
            _ => false
        };
        if (!valid) throw Rejected("Only attempts\\attempt-<id>-request|response.xml, actuals\\readback-<id>.xml and reconciliation\\run-<id>.json are allowed below a batch folder.");
    }

    private static void RequireCode(string segment, string label)
    {
        if (!CodeSegment.IsMatch(segment) || LongDigitRun.IsMatch(segment))
            throw Rejected($"The {label} folder must be 1-30 upper-case letters, digits, '_' or '-', and must not look like a phone number.");
    }

    private static ArgumentException Rejected(string message) => new(message);
}
