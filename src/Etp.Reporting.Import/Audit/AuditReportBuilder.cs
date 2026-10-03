using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;

namespace Etp.Reporting.Import.Audit;

/// <summary>
/// Turns inspected files into report entries under the privacy rules (design 8): messages from the catalogue only,
/// never a Descriptive value, document keys only at <see cref="AuditDetail.Documents"/>, sheet and row locators only at
/// <see cref="AuditDetail.Rows"/>, a column name only when it is a catalogue header (a header cell can hold anything).
/// </summary>
public static class AuditReportBuilder
{
    private static readonly HashSet<string> CatalogueHeaders = EtpReportFamilyRegistry.Families
        .SelectMany(family => family.Headers.Concat(family.Columns.Select(column => column.SourceHeader)))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The codes a source itself holds a document or row for; listed, and findings only with <c>--strict</c>.</summary>
    public static readonly IReadOnlySet<string> HoldCodes = new HashSet<string>(StringComparer.Ordinal)
        { ImportCodes.InSourceConflict, ImportCodes.LegacyBlocksDiffer, ImportCodes.RowDateMissing };

    /// <summary>The message a report may print for a code: the audit's own, else the import catalogue's (design 8.1).</summary>
    public static string SafeMessage(string code, string? message) =>
        AuditCodes.Messages.TryGetValue(code, out var own) ? own : ImportDiagnosticCatalogue.SafeMessage(code, message);

    public static AuditDiagnosticReport Diagnostic(ImportDiagnostic diagnostic, AuditDetail detail) =>
        new(diagnostic.Code, diagnostic.Severity.ToString(), SafeMessage(diagnostic.Code, diagnostic.Message))
        {
            Block = diagnostic.BlockNo,
            Occurrences = diagnostic.Occurrences,
            Sheet = detail == AuditDetail.Rows ? diagnostic.SheetName : null,
            Row = detail == AuditDetail.Rows ? diagnostic.RowNumber : null,
            Column = detail == AuditDetail.Rows && diagnostic.ColumnName is { } column && CatalogueHeaders.Contains(column) ? column : null,
            DocumentRef = detail >= AuditDetail.Documents ? diagnostic.DocumentRef : null
        };

    /// <summary>The report entry of one inspected file, before any planner or contract result is added.</summary>
    public static AuditFileReport File(InspectedFile file, AuditDetail detail)
    {
        var snapshotRows = file.SnapshotBlocks.GroupBy(block => (block.SnapshotDate, block.Basis))
            .Select(group => new AuditSnapshotDateReport(group.Key.SnapshotDate, group.Sum(block => block.LastRow - block.FirstRow + 1), group.Key.Basis.ToString()))
            .OrderBy(entry => entry.Date).ToArray();
        return new AuditFileReport
        {
            File = file.RelativePath,
            Sha256 = file.Sha256,
            Bytes = file.Bytes,
            ElapsedMs = file.ElapsedMs,
            ReportCode = file.ReportCode,
            FamilyCode = file.FamilyCode,
            Store = file.Store,
            Layout = file.LayoutVersion,
            Outcome = file.Outcome.ToString(),
            FailureCode = file.FailureCode,
            PeriodStart = file.PeriodStart,
            PeriodEnd = file.PeriodEnd,
            SourceKind = file.SourceKind?.ToString(),
            ContractVersion = file.ContractVersion,
            StagedRows = file.StagedRows,
            Blocks = file.Blocks.Select(Block).ToArray(),
            SnapshotDates = snapshotRows,
            VirtualRows = file.VirtualRows,
            Documents = file.Source is null ? null : new AuditDocumentCounts(file.Documents.Count, file.DocumentsByScope, file.HeldRows, file.HoldsByCode)
                { Projected = file.Projected },
            Diagnostics = Distinct(file.Diagnostics).Select(diagnostic => Diagnostic(diagnostic, detail)).ToArray(),
            DocumentList = detail >= AuditDetail.Documents
                ? file.Documents.OrderBy(document => document.Key.KeyText, StringComparer.Ordinal).Select(document =>
                    new AuditDocumentReport(document.Key.Hash, document.Key.KeyText, document.DocumentDate, document.BlockNo, document.RowCount, document.FactSha256)
                    { Hold = document.HoldCode }).ToArray()
                : null
        };
    }

    private static AuditBlockReport Block(InspectedBlock inspected)
    {
        var block = inspected.Block;
        return new(block.BlockNo, block.SheetName, block.FirstRow, block.LastRow, block.RowCount, block.Completeness.ToString(), block.Origin.ToString())
        {
            ExportTime = block.ExportTime.ToContractText(),
            ExportTimeBasis = block.ExportTime.Basis.ToString(),
            PeriodFrom = block.PeriodFrom,
            PeriodTo = block.PeriodTo,
            PeriodBasis = block.PeriodBasis.ToString(),
            SnapshotDate = block.SnapshotDate,
            SnapshotDateBasis = block.SnapshotDateBasis?.ToString(),
            RawRows = block.RawRows,
            ExcludedRows = block.ExcludedRows,
            SupersededRows = block.SupersededRows,
            VirtualRows = block.VirtualRowCount,
            RebuiltRows = inspected.RebuiltRows,
            SourceFileName = block.SourceFileName is { } name ? Path.GetFileName(name) : null,
            SourceSha256 = block.SourceSha256,
            ContentSha256 = inspected.Identity?.ContentSha256,
            ContentHashVersion = inspected.Identity?.ContentHashVersion,
            ExportKey = inspected.Identity?.ExportKey
        };
    }

    // The same diagnostic reaches a file from the preflight and from the description; it is reported once.
    private static IEnumerable<ImportDiagnostic> Distinct(IEnumerable<ImportDiagnostic> diagnostics)
    {
        var seen = new HashSet<(string, ImportDiagnosticSeverity, string, string?, int?, string?, int?)>();
        foreach (var diagnostic in diagnostics)
            if (seen.Add((diagnostic.Code, diagnostic.Severity, diagnostic.Message, diagnostic.SheetName, diagnostic.RowNumber, diagnostic.ColumnName, diagnostic.BlockNo)))
                yield return diagnostic;
    }

    /// <summary>
    /// The verdict of one file (design 9): Blocked when it would fail, Findings for an expectation mismatch (and, with
    /// <c>--strict</c>, for holds and warnings), Pass otherwise.
    /// </summary>
    public static AuditVerdict Verdict(AuditFileReport file, bool blocked, bool strict)
    {
        if (blocked) return AuditVerdict.Blocked;
        if (file.Expectation is { Pass: false }) return AuditVerdict.Findings;
        if (strict && (file.Documents is { } documents && (documents.HeldRows > 0 || documents.Holds.Count > 0)
            || file.Diagnostics.Any(diagnostic => diagnostic.Severity == nameof(ImportDiagnosticSeverity.Warning))))
            return AuditVerdict.Findings;
        return AuditVerdict.Pass;
    }

    /// <summary>The run's summary and exit code: 0 when every file passes, 1 when any has findings.</summary>
    public static AuditSummary Summary(IReadOnlyList<AuditFileReport> files, int skippedCsv, bool runFinding = false,
        Func<AuditFileReport, bool>? expectedBlocker = null)
    {
        var holds = new List<AuditLegacyHold>();
        foreach (var file in files)
        {
            if (file.Documents is not { } documents) continue;
            foreach (var (code, count) in documents.Holds) holds.Add(new(file.File, code, count));
            if (documents.HeldRows > 0) holds.Add(new(file.File, ImportCodes.RowDateMissing, documents.HeldRows));
        }
        var blocked = files.Count(file => file.Verdict == AuditVerdict.Blocked);
        var unexpected = files.Count(file => file.Verdict == AuditVerdict.Blocked && !(expectedBlocker?.Invoke(file) ?? false));
        var clean = !runFinding && files.All(file => file.Verdict == AuditVerdict.Pass ||
            (file.Verdict == AuditVerdict.Blocked && (expectedBlocker?.Invoke(file) ?? false)));
        return new AuditSummary
        {
            Files = files.Count,
            Pass = files.Count(file => file.Verdict == AuditVerdict.Pass),
            Findings = files.Count(file => file.Verdict == AuditVerdict.Findings),
            Blocked = blocked,
            Error = files.Count(file => file.Verdict == AuditVerdict.Error),
            UnexpectedBlockers = unexpected,
            LegacyHolds = holds,
            SkippedCsvFiles = skippedCsv,
            ExitCode = clean ? AuditExitCodes.Clean : AuditExitCodes.Findings
        };
    }

    /// <summary>A command's arguments with each path reduced to its last name, so no user folder is echoed (design 8.7).</summary>
    public static IReadOnlyList<string> EchoArguments(IReadOnlyList<string> args)
    {
        // The values of these options are not paths; every other bare token is a path, or the command itself.
        var plainValues = new HashSet<string>(StringComparer.Ordinal)
            { "--format", "--detail", "--family", "--store", "--stores", "--database", "--server", "--planner" };
        var echoed = new List<string>();
        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            var keep = token.StartsWith("--", StringComparison.Ordinal) || (index == 0 && IsCommand(token))
                || (index > 0 && plainValues.Contains(args[index - 1]));
            echoed.Add(keep ? token : Leaf(token));
        }
        return echoed;

        static bool IsCommand(string value) => value is "inspect" or "validate-contract" or "check-import" or "baseline" or "seed";
    }

    private static string Leaf(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? "<path>" : name;
    }

    /// <summary>Whether a contract diagnostic code blocks the workbook (contract 8).</summary>
    public static bool IsBlocker(ImportDiagnostic diagnostic) => diagnostic.Severity == ImportDiagnosticSeverity.Blocker;

    /// <summary>Report entries for the source kinds a block table lists, for the contract report.</summary>
    public static string KindName(SourceKind? kind) => kind?.ToString() ?? "Unknown";
}
