using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Sources;

/// <summary>
/// The identity of one export as a block carries it (spec 6.8; <c>import_file_blocks</c>), with the rule versions it
/// was computed under. Two blocks are comparable by content only when both hashes exist under the same
/// <see cref="ContentHashVersion"/>.
/// </summary>
/// <param name="ContentSha256"><c>content_sha256</c>; null for trimmed and legacy blocks.</param>
/// <param name="ContentHashVersion">The canonicaliser's <c>content_hash_version</c>.</param>
/// <param name="ExportKey"><c>export_key</c>; null when the export time is unknown.</param>
/// <param name="RulesetVersion">The family's identity <c>RulesetVersion</c> the block is decided under.</param>
public sealed record BlockIdentity(string? ContentSha256, int ContentHashVersion, string? ExportKey, int RulesetVersion);

/// <summary>How an incoming block's identity relates to a stored block of the same export (spec 6.8).</summary>
public enum ExportContentMatch
{
    /// <summary>Not the same export: neither the source SHA-256 nor the export key matches.</summary>
    DifferentExport,
    /// <summary>The same export, but the content cannot be compared (a missing hash or different hash versions): land and decide it.</summary>
    NotComparable,
    /// <summary>The same export with equal content under the same hash version: a candidate for <c>ATTESTED</c>.</summary>
    SameContent,
    /// <summary>The same export with different content under the same hash version: <c>EXPORT_CONTENT_MISMATCH</c>.</summary>
    ContentMismatch
}

/// <summary>
/// Pure computations of spec 6.8: the content hash of a rebuilt block and the export key. Registration (ATTESTED,
/// the decision-pass and upgrade conditions) belongs to the planner, which reads the stored side.
/// </summary>
public static partial class ExportIdentity
{
    /// <summary>
    /// <c>content_sha256</c>: the multiset hash of the canonical row hash (every staged field except Ignored ones) of
    /// every row of the <b>rebuilt</b> block, physical and virtual. Null for trimmed and legacy blocks, which do not
    /// hold their export's full row multiset. An empty export hashes its empty multiset, so the raw and the contract
    /// routes agree on it too. A delta block rebuilt with fewer virtual rows than its map gives it (a twin that was not
    /// among the staged rows) is not its full export either and has no hash, so it is never compared and never reported
    /// as a content mismatch.
    /// </summary>
    public static string? ContentSha256(EtpReportFamily family, SourceBlock block, IReadOnlyList<SourceRow> rebuiltRows, IFactCanonicalizer canonicalizer)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(rebuiltRows);
        ArgumentNullException.ThrowIfNull(canonicalizer);
        if (block.Completeness is not (BlockCompleteness.Complete or BlockCompleteness.Delta or BlockCompleteness.Empty)) return null;
        if (rebuiltRows.Count(row => row.Locator.IsVirtual) != block.VirtualRowCount) return null;
        return canonicalizer.MultisetHash(rebuiltRows.Select(row => canonicalizer.Canonicalize(family, row.Values).ContentHash));
    }

    /// <summary>
    /// <c>export_key</c>: SHA-256 (UTF-8, lowercase hex) of
    /// <c>REPORT|STORE|instant|basis|period_from..period_to or snapshot date|normalised report name</c>.
    /// Null when the export time is unknown: an export without a known instant has no key.
    /// </summary>
    public static string? ExportKey(string reportCode, string storeCode, SourceBlock block)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        ArgumentNullException.ThrowIfNull(block);
        if (!block.ExportTime.IsKnown) return null;
        var text = string.Join('|',
            reportCode.Trim().ToUpperInvariant(),
            storeCode.Trim().ToUpperInvariant(),
            block.ExportTime.ToContractText(),
            block.ExportTime.Basis.ToDatabaseCode(),
            Selection(block),
            NormalisedReportName(block.SourceFileName));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>The block's identity under the family's current ruleset and the canonicaliser's hash version.</summary>
    public static BlockIdentity Describe(EtpReportFamily family, string storeCode, SourceBlock block, IReadOnlyList<SourceRow> rebuiltRows,
        IFactCanonicalizer canonicalizer) =>
        new(ContentSha256(family, block, rebuiltRows, canonicalizer), canonicalizer.ContentHashVersion,
            ExportKey(family.ReportCode, storeCode, block), family.Identity?.RulesetVersion ?? 1);

    /// <summary>
    /// Compares an incoming block with a stored one (spec 6.8): the same export when the source SHA-256 or the export
    /// key matches; then equal content only under equal hash versions. Differing hash versions are never a conflict.
    /// </summary>
    public static ExportContentMatch Compare(SourceBlock incoming, BlockIdentity incomingIdentity, string? storedSourceSha256, BlockIdentity stored)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(incomingIdentity);
        ArgumentNullException.ThrowIfNull(stored);
        var sameSha = incoming.SourceSha256 is { } sha && storedSourceSha256 is { } other && string.Equals(sha, other, StringComparison.OrdinalIgnoreCase);
        var sameKey = incomingIdentity.ExportKey is { } key && string.Equals(key, stored.ExportKey, StringComparison.Ordinal);
        if (!sameSha && !sameKey) return ExportContentMatch.DifferentExport;
        if (incomingIdentity.ContentSha256 is null || stored.ContentSha256 is null ||
            incomingIdentity.ContentHashVersion != stored.ContentHashVersion)
            return ExportContentMatch.NotComparable;
        return string.Equals(incomingIdentity.ContentSha256, stored.ContentSha256, StringComparison.Ordinal)
            ? ExportContentMatch.SameContent
            : ExportContentMatch.ContentMismatch;
    }

    /// <summary>
    /// The ETP report name a file name carries, for the export key: the name without folder, extension and export
    /// time (<c>yyyyMMddHHmm_</c> prefix, <c>_yyyyMMddHHmmss</c> suffix, <c>dd.MM.yyyy</c> date), upper-case letters and
    /// digits only. A contract block's <c>source_file</c> is the raw export's own name, so both routes give the same name.
    /// </summary>
    public static string NormalisedReportName(string? fileName)
    {
        var name = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName ?? "").Trim());
        name = TimeTokens().Replace(name, " ");
        return NonAlphanumeric().Replace(name.ToUpperInvariant(), "");
    }

    private static string Selection(SourceBlock block) => block switch
    {
        { PeriodFrom: { } from, PeriodTo: { } to } => $"{Iso(from)}..{Iso(to)}",
        { SnapshotDate: { } date } => Iso(date),
        _ => ""
    };

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\d{12}_|_\d{14}$|(?<!\d)\d{2}\.\d{2}\.\d{4}(?!\d)")]
    private static partial Regex TimeTokens();

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlphanumeric();
}
