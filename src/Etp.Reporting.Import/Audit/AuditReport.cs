using System.Reflection;
using System.Security.Cryptography;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Audit;

/// <summary>A file's verdict (design 7.2).</summary>
public enum AuditVerdict { Pass, Findings, Blocked, Error }

/// <summary>The machine report, schema <c>etp-import-audit-report/1</c> (design 7.2). Field names are camelCase in JSON.</summary>
public sealed record AuditReport
{
    public const string SchemaName = "etp-import-audit-report/1";
    public string Schema { get; init; } = SchemaName;
    public required string Command { get; init; }
    /// <summary>The arguments, with every path reduced to its last name (design 7.2, 8.7).</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public required AuditToolStamp Tool { get; init; }
    public AuditDatabaseStamp? Database { get; init; }
    public AuditDetail Detail { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public long ElapsedMs { get; init; }
    /// <summary>Planner-1 predictions assume this order, the app's (design 6.3).</summary>
    public string? OrderNote { get; init; }
    public IReadOnlyList<AuditFileReport> Files { get; init; } = [];
    public IReadOnlyList<AuditDiagnosticReport> RunDiagnostics { get; init; } = [];
    public required AuditSummary Summary { get; init; }
    /// <summary><c>baseline</c>: the script's result sets.</summary>
    public AuditBaselineReport? Baseline { get; init; }
}

/// <summary>Which engine made the report (design 5.2): a prediction from another commit must not be trusted.</summary>
public sealed record AuditToolStamp(string Version, string? Commit, string CatalogueSha256, int ContentHashVersion,
    IReadOnlyDictionary<string, int> Rulesets)
{
    /// <summary>The stamp of the engine in this process; rulesets for the families named (all when none are).</summary>
    public static AuditToolStamp Current(IEnumerable<string>? familyCodes = null)
    {
        var assembly = typeof(AuditToolStamp).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        var version = plus < 0 ? informational : informational[..plus];
        var commit = plus < 0 ? null : informational[(plus + 1)..];
        if (commit is { Length: > 12 }) commit = commit[..12];
        var wanted = familyCodes?.ToHashSet(StringComparer.Ordinal);
        var rulesets = EtpReportFamilyRegistry.Families
            .Where(family => family.Identity is not null && (wanted is null || wanted.Contains(family.FamilyCode)))
            .OrderBy(family => family.FamilyCode, StringComparer.Ordinal)
            .ToDictionary(family => family.FamilyCode, family => family.Identity!.RulesetVersion, StringComparer.Ordinal);
        return new(version, commit, CatalogueHash(), FactCanonicalizer.CurrentContentHashVersion, rulesets);
    }

    private static string CatalogueHash()
    {
        using var stream = typeof(EtpReportFamilyRegistry).Assembly.GetManifestResourceStream("Etp.Reporting.Import.Profiles.EtpReportFamilies.json");
        return stream is null ? "unknown" : Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

/// <summary>The database a command read, SELECT-only.</summary>
public sealed record AuditDatabaseStamp(string Server, string Name)
{
    public string? LatestMigration { get; init; }
    /// <summary>The database predates 0041; its effects (line_seq, snapshot source) are emulated (design 6.2).</summary>
    public bool Emulated0041 { get; init; }
    /// <summary>Whether this login could write, as information: the audit never exercised it (design 6.4.4).</summary>
    public bool? LoginCouldWrite { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>One file of a report (design 7.2).</summary>
public sealed record AuditFileReport
{
    public required string File { get; init; }
    public string? Sha256 { get; init; }
    public long Bytes { get; init; }
    public long ElapsedMs { get; init; }
    public string? ReportCode { get; init; }
    public string? FamilyCode { get; init; }
    public string? Store { get; init; }
    public string? Layout { get; init; }
    /// <summary>The folder import's verdict before the planner: Ready, EmptyExport, NotNeeded, UnknownLayout or Failed.</summary>
    public required string Outcome { get; init; }
    public string? FailureCode { get; init; }
    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }
    public string? SourceKind { get; init; }
    public int? ContractVersion { get; init; }
    public int StagedRows { get; init; }
    public IReadOnlyList<AuditBlockReport> Blocks { get; init; } = [];
    public IReadOnlyList<AuditSnapshotDateReport> SnapshotDates { get; init; } = [];
    public int VirtualRows { get; init; }
    public AuditDocumentCounts? Documents { get; init; }
    public AuditContractReport? Contract { get; init; }
    public AuditPlannerOneReport? Planner1 { get; init; }
    public IReadOnlyList<AuditDiagnosticReport> Diagnostics { get; init; } = [];
    /// <summary><c>--detail documents</c> and above only.</summary>
    public IReadOnlyList<AuditDocumentReport>? DocumentList { get; init; }
    public AuditExpectationResult? Expectation { get; init; }
    public AuditVerdict Verdict { get; init; }
}

/// <summary>Every <see cref="Sources.SourceBlock"/> field plus the block's identity.</summary>
public sealed record AuditBlockReport(int BlockNo, string Sheet, int? FirstRow, int? LastRow, int Rows, string Completeness, string Origin)
{
    public string? ExportTime { get; init; }
    public string? ExportTimeBasis { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public string? PeriodBasis { get; init; }
    public DateOnly? SnapshotDate { get; init; }
    public string? SnapshotDateBasis { get; init; }
    public int? RawRows { get; init; }
    public int? ExcludedRows { get; init; }
    public int? SupersededRows { get; init; }
    public int VirtualRows { get; init; }
    public int RebuiltRows { get; init; }
    public string? SourceFileName { get; init; }
    public string? SourceSha256 { get; init; }
    public string? ContentSha256 { get; init; }
    public int? ContentHashVersion { get; init; }
    public string? ExportKey { get; init; }
}

/// <summary>The snapshot date planner 1 stamps on a run of rows (spec 6.4).</summary>
public sealed record AuditSnapshotDateReport(DateOnly Date, int Rows, string Basis);

public sealed record AuditDocumentCounts(int Total, IReadOnlyDictionary<string, int> ByScope, int HeldRows, IReadOnlyDictionary<string, int> Holds)
{
    /// <summary>False when the family has no identity yet, or the file has no store: its documents were not projected.</summary>
    public bool Projected { get; init; } = true;
}

/// <summary><c>validate-contract</c>: the workbook's kind and the raw rebuild check.</summary>
public sealed record AuditContractReport(string Kind)
{
    public int? ContractVersion { get; init; }
    /// <summary>Legacy Info table: whether its blocks tile the Data sheet.</summary>
    public bool? LegacyInfoTiles { get; init; }
    public int RebuildChecked { get; init; }
    public int RebuildNotChecked { get; init; }
    /// <summary>Legacy blocks whose source file name matches a raw export (OQ-5); information only.</summary>
    public int MatchedByName { get; init; }
    public int Blockers { get; init; }
}

/// <summary>What 1.9.3 (planner 1) will do with the file (design 5.3).</summary>
public sealed record AuditPlannerOneReport(string Result)
{
    public string? Code { get; init; }
    public string? Message { get; init; }
    public AuditRowCounts Rows { get; init; } = new(0, 0, 0);
    public IReadOnlyList<long> PromotesOver { get; init; } = [];
    /// <summary>Snapshot rows per date the import would store (undated families).</summary>
    public IReadOnlyDictionary<string, int>? SnapshotRows { get; init; }
    /// <summary><c>--detail rows</c> only: business identity of up to 20 conflicting rows.</summary>
    public IReadOnlyList<string>? ConflictSamples { get; init; }
}

public sealed record AuditRowCounts(int New, int Present, int Conflict);

/// <summary>A diagnostic as a report may hold it: the catalogue's message only (design 8.1).</summary>
public sealed record AuditDiagnosticReport(string Code, string Severity, string Message)
{
    public int? Block { get; init; }
    public string? Sheet { get; init; }
    public int? Row { get; init; }
    public string? Column { get; init; }
    public string? DocumentRef { get; init; }
    public int Occurrences { get; init; } = 1;
}

/// <summary>One document of <c>--detail documents</c>: key, date and hashes; never a value.</summary>
public sealed record AuditDocumentReport(string KeyHash, string KeyText, DateOnly? Date, int Block, int RowCount, string FactSha256)
{
    public string? Hold { get; init; }
}

public sealed record AuditExpectationResult(bool Pass, IReadOnlyList<string> Mismatches);

public sealed record AuditLegacyHold(string File, string Code, int Occurrences);

public sealed record AuditSummary
{
    public int Files { get; init; }
    public int Pass { get; init; }
    public int Findings { get; init; }
    public int Blocked { get; init; }
    public int Error { get; init; }
    public int UnexpectedBlockers { get; init; }
    public IReadOnlyList<AuditLegacyHold> LegacyHolds { get; init; } = [];
    /// <summary>CSV files in an input folder: not an ETP workbook yet (OD-6a).</summary>
    public int SkippedCsvFiles { get; init; }
    public int ExitCode { get; init; }
}

/// <summary><c>baseline</c>: the five result sets of <c>check-import-upgrade.sql</c> (samples only with <c>--detail rows</c>).</summary>
public sealed record AuditBaselineReport(IReadOnlyList<AuditResultSet> ResultSets)
{
    public IReadOnlyList<string>? Differences { get; init; }
}

public sealed record AuditResultSet(string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows);
