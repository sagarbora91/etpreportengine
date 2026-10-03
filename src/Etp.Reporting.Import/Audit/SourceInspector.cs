using System.Diagnostics;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Audit;

/// <summary>What the folder import would make of a workbook before it touches the database (design 5.3 step 1).</summary>
public enum InspectionOutcome
{
    /// <summary>Accepted, with store and date: it goes on to the planner.</summary>
    Ready,
    /// <summary>Accepted, but it holds no rows (<c>empty export</c> once imported).</summary>
    EmptyExport,
    /// <summary>A control workbook or a family the reporting engine does not need.</summary>
    NotNeeded,
    /// <summary>No approved layout matches (<c>Unknown layout</c>).</summary>
    UnknownLayout,
    /// <summary>Refused before the database: a blocker, or a store or date that cannot be found.</summary>
    Failed
}

/// <summary>One block of a source with its identity (design 3.1).</summary>
public sealed record InspectedBlock(SourceBlock Block, BlockIdentity? Identity, int RebuiltRows);

/// <summary>
/// One workbook as ETP sees it (design 3.1): the folder import's verdict, then the engine's description of it. Holds
/// the accepted envelope for the planner-1 prediction; the report takes only counts, codes and hashes from it.
/// </summary>
public sealed record InspectedFile(string SourcePath, string RelativePath)
{
    public string FileName => Path.GetFileName(SourcePath);
    public string? Sha256 { get; init; }
    public long Bytes { get; init; }
    public long ElapsedMs { get; init; }
    public InspectionOutcome Outcome { get; init; } = InspectionOutcome.Failed;
    /// <summary>The failure code of a <see cref="InspectionOutcome.Failed"/> or <see cref="InspectionOutcome.UnknownLayout"/> file.</summary>
    public string? FailureCode { get; init; }
    public string? ReportCode { get; init; }
    public string? FamilyCode { get; init; }
    public string? LayoutVersion { get; init; }
    /// <summary>The store and period the import would use, after the folder's sibling rules (spec 6.4 tier 7).</summary>
    public string? Store { get; init; }
    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }
    /// <summary>The date the planner imports under: the period end, or the siblings' date for an undated file.</summary>
    public DateOnly? BusinessDate { get; init; }
    public int StagedRows { get; init; }
    public SourceKind? SourceKind { get; init; }
    public int? ContractVersion { get; init; }
    public IReadOnlyList<InspectedBlock> Blocks { get; init; } = [];
    /// <summary>The planner-1 snapshot dating (spec 6.4): the date of each run of rows of an undated family.</summary>
    public IReadOnlyList<SnapshotBlock> SnapshotBlocks { get; init; } = [];
    public int VirtualRows { get; init; }
    /// <summary>Authoritative documents after in-source resolution; empty when the family has no identity.</summary>
    public IReadOnlyList<DocumentObservation> Documents { get; init; } = [];
    public bool Projected { get; init; }
    /// <summary>The exception type when the audit's own description step failed; never its message.</summary>
    public string? DescriptionFailure { get; init; }
    public int HeldRows { get; init; }
    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; } = [];
    /// <summary>The accepted import, for the planner-1 prediction. Never written to a report.</summary>
    public MatchedImportEnvelope? Accepted { get; init; }
    /// <summary>The source description, for <c>validate-contract</c>. Never written to a report as a whole.</summary>
    public SourceDescription? Source { get; init; }
    public EtpReportFamily? Family { get; init; }
    public WorkbookSnapshot? Workbook { get; init; }

    public IReadOnlyDictionary<string, int> DocumentsByScope => Documents.GroupBy(document => document.Key.Scope.ToString())
        .OrderBy(group => group.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> HoldsByCode => Documents.Where(document => document.HoldCode is not null)
        .GroupBy(document => document.HoldCode!).OrderBy(group => group.Key, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}

/// <summary>An inspection of every input: the files in the app's import order, plus what discovery skipped.</summary>
public sealed record InspectionRun(IReadOnlyList<InspectedFile> Files, int SkippedCsvFiles, IReadOnlyList<string> InputRoots);

/// <summary>
/// The read-only pipeline of <c>inspect</c> (design 5.1 steps 1-8), composed as the folder import composes it: discovery
/// and ZIP extraction by <see cref="BatchImportSource"/>, reading by <see cref="OpenXmlWorkbookReader"/>, matching,
/// staging and scope by <see cref="MatchedImportEnvelopeFactory"/>, the app's dependency order and its sibling dating
/// (<c>FolderImportService</c>), then the engine's description (<see cref="SourceDescriptionReader"/>), block rebuild
/// and identity (<see cref="ExportIdentity"/>), projection (<see cref="DocumentProjector"/>) and in-source resolution
/// (<see cref="InSourceResolver"/>). It reads no database and writes nothing but ZIP extraction folders, which are
/// removed when it returns. <c>SourceInspectorParityTests</c> pins the composition against the importer.
/// </summary>
public sealed class SourceInspector(IWorkbookReader? workbookReader = null, IReadOnlyList<string>? knownStores = null)
{
    private readonly IWorkbookReader reader = workbookReader ?? new OpenXmlWorkbookReader();
    private readonly IReadOnlyList<string>? stores = knownStores is { Count: > 0 } ? knownStores : null;

    /// <summary>Only these report or family codes (<c>--family</c>); empty for all.</summary>
    public IReadOnlyList<string> Families { get; init; } = [];
    /// <summary>Only this store (<c>--store</c>).</summary>
    public string? Store { get; init; }
    /// <summary>Paths kept as given, never resolved through a ZIP's temporary folder (design 4).</summary>
    public ImportPathPolicy PathPolicy { get; init; } = new();

    public async Task<InspectionRun> InspectAsync(IReadOnlyList<string> inputs, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var sources = new List<BatchImportSource>();
        try
        {
            var paths = new List<(string Path, string Relative)>();
            var csv = 0;
            var roots = new List<string>();
            foreach (var input in inputs)
            {
                // An unusable path, a refused ZIP or a folder with no workbook is an input error (exit 3).
                var source = await BatchImportSource.OpenAsync(input, PathPolicy, cancellationToken).ConfigureAwait(false);
                sources.Add(source);
                var full = Path.GetFullPath(input);
                roots.Add(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                if (Directory.Exists(full)) csv += CountCsv(full);
                foreach (var path in source.WorkbookPaths) paths.Add((path, Relative(full, path)));
            }
            return new(await InspectFilesAsync(paths, progress, cancellationToken).ConfigureAwait(false), csv, roots);
        }
        finally
        {
            foreach (var source in sources) await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Inspects workbooks already discovered, in the app's order. <paramref name="paths"/> pairs each path with its report path.</summary>
    public async Task<IReadOnlyList<InspectedFile>> InspectFilesAsync(IReadOnlyList<(string Path, string Relative)> paths,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var envelopes = new MatchedImportEnvelopeFactory(stores);
        var read = new List<(InspectedFile File, WorkbookSnapshot Workbook, MatchedImportInspection Inspection, long Started)>();
        var results = new List<InspectedFile>();
        var number = 0;
        foreach (var (path, relative) in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            number++;
            var clock = Stopwatch.StartNew();
            var file = new InspectedFile(path, relative);
            var stage = FailureStage.Read;
            try
            {
                var workbook = await reader.ReadAsync(path, cancellationToken).ConfigureAwait(false);
                stage = FailureStage.Match;
                var inspection = envelopes.Inspect(workbook);
                file = file with { Sha256 = workbook.Sha256, Bytes = workbook.FileSizeBytes };
                if (!Selected(inspection.MatchedProfile?.ReportCode, inspection.AcceptedImport?.Scope.StoreCode)) continue;
                read.Add((file, workbook, inspection, clock.ElapsedMilliseconds));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                // The exception text may quote a cell; only its code and the catalogue's message are kept (design 8.1).
                var failure = new SafeImportFailureClassifier().DescribeDetailed(exception, stage);
                results.Add(file with
                {
                    Outcome = InspectionOutcome.Failed, FailureCode = failure.Code, ElapsedMs = clock.ElapsedMilliseconds,
                    Diagnostics = [new(failure.Code, ImportDiagnosticSeverity.Blocker, ImportDiagnosticCatalogue.SafeMessage(failure.Code, failure.SafeMessage))]
                });
            }
            progress?.Report($"[{number}/{paths.Count}] read {relative}");
        }

        // Scope per path, for the empty and undated exports that take their store and date from their folder siblings.
        var detected = read.Where(entry => entry.Inspection.AcceptedImport is not null)
            .ToDictionary(entry => entry.File.SourcePath, entry => entry.Inspection.AcceptedImport!.Scope, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in read.OrderBy(entry => DependencyOrder(entry.Inspection.MatchedProfile?.ReportCode)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clock = Stopwatch.StartNew();
            var file = Decide(entry.File, entry.Inspection, detected);
            file = Describe(file, entry.Workbook, entry.Inspection);
            results.Add(file with { ElapsedMs = entry.Started + clock.ElapsedMilliseconds });
            progress?.Report($"inspected {file.RelativePath}: {file.ReportCode ?? "unknown"} {file.Store ?? ""} {file.Outcome}");
        }
        return results;
    }

    /// <summary>The app's file order: sales before their enrichments, ledgers before closing stock (FolderImportService).</summary>
    public static int DependencyOrder(string? code) => code switch
    {
        "R025" => 0, "R020" => 1, "R022" => 2, "R024" => 3, "R013" => 4, "R003" => 5,
        "STOCK_LEDGER" or "R030" => 6, "R011" or "CLOSING_STOCK" => 7, _ => 10
    };

    private bool Selected(string? reportCode, string? store)
    {
        if (Families.Count > 0)
        {
            if (reportCode is null) return false;
            var family = EtpReportFamilyRegistry.Families.FirstOrDefault(candidate => candidate.ReportCode == reportCode);
            if (!Families.Contains(reportCode, StringComparer.OrdinalIgnoreCase) &&
                !(family is not null && Families.Contains(family.FamilyCode, StringComparer.OrdinalIgnoreCase))) return false;
        }
        return Store is null || string.Equals(store, Store, StringComparison.OrdinalIgnoreCase);
    }

    // FolderImportService.RunFilesCoreAsync, without overrides or restatement: the grid result a file gets before the
    // planner, and the store and date it is imported under.
    private static InspectedFile Decide(InspectedFile file, MatchedImportInspection inspection, IReadOnlyDictionary<string, ImportScope> detected)
    {
        var accepted = inspection.AcceptedImport;
        var profile = inspection.MatchedProfile;
        var family = profile is null ? null : EtpReportFamilyRegistry.Families.FirstOrDefault(candidate => candidate.ReportCode == profile.ReportCode);
        file = file with
        {
            ReportCode = profile?.ReportCode, FamilyCode = family?.FamilyCode, LayoutVersion = profile?.LayoutVersion,
            StagedRows = inspection.StagedRows, Diagnostics = inspection.Diagnostics, Accepted = accepted, Family = family
        };
        if (accepted is null)
        {
            var codes = inspection.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray();
            var unknown = codes.Any(code => code is "LAYOUT_UNKNOWN" or "REQUIRED_COLUMN_MISSING" or "UNEXPECTED_COLUMN");
            var sourceCode = Regex.Match(file.FileName, @"(?:^|[^A-Z0-9])(R\d{3})(?:[^A-Z0-9]|$)", RegexOptions.IgnoreCase);
            var unsupported = sourceCode.Success && !EtpReportFamilyRegistry.Families.Any(candidate =>
                candidate.FamilyCode.Equals(sourceCode.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
            var notNeeded = file.FileName.StartsWith("00_", StringComparison.OrdinalIgnoreCase) || unsupported;
            var first = inspection.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Blocker)
                ?? inspection.Diagnostics.FirstOrDefault();
            return file with
            {
                Outcome = notNeeded ? InspectionOutcome.NotNeeded : unknown ? InspectionOutcome.UnknownLayout : InspectionOutcome.Failed,
                FailureCode = notNeeded ? null : first?.Code ?? "IMPORT_LAYOUT_BLOCKED"
            };
        }

        var scope = accepted.Scope;
        var siblings = detected.Where(item => string.Equals(Path.GetDirectoryName(item.Key), Path.GetDirectoryName(file.SourcePath),
            StringComparison.OrdinalIgnoreCase)).Select(item => item.Value).ToArray();
        var store = scope.StoreCode ?? siblings.Select(item => item.StoreCode).FirstOrDefault(value => value is not null);
        var undated = EtpReportFamilyRegistry.Resolve(accepted.ProfileIdentity.ReportCode).PrimaryDateHeader is null;
        var siblingEnds = siblings.Select(item => item.PeriodEnd).OfType<DateOnly>().Distinct().ToArray();
        var siblingEnd = !undated ? siblings.Select(item => item.PeriodEnd).Max() : siblingEnds.Length == 1 ? siblingEnds[0] : (DateOnly?)null;
        var end = scope.PeriodEnd ?? siblingEnd;
        var diagnostics = inspection.Diagnostics.ToList();
        string? refusal = null;
        if (undated && end is null)
            refusal = siblingEnds.Length > 1 ? ImportCodes.SnapshotDateAmbiguous : ImportCodes.SnapshotDateUnknown;
        else if (undated && scope.PeriodEnd is null)
            diagnostics.Add(new(ImportCodes.SnapshotDateFromSiblings, ImportDiagnosticSeverity.Warning,
                ImportDiagnosticCatalogue.Template(ImportCodes.SnapshotDateFromSiblings)));
        if (refusal is null && (string.IsNullOrWhiteSpace(store) || end is null)) refusal = "SCOPE_NOT_DETECTED";
        if (refusal is not null)
        {
            diagnostics.Add(new(refusal, ImportDiagnosticSeverity.Blocker, ImportDiagnosticCatalogue.Template(refusal)));
            return file with { Outcome = InspectionOutcome.Failed, FailureCode = refusal, Store = store, Diagnostics = diagnostics };
        }
        return file with
        {
            Outcome = accepted.Staging.Rows.Count == 0 ? InspectionOutcome.EmptyExport : InspectionOutcome.Ready,
            Store = scope.StoreCode ?? store, PeriodStart = scope.PeriodStart ?? end, PeriodEnd = scope.PeriodEnd ?? end,
            BusinessDate = end, Diagnostics = diagnostics
        };
    }

    // Steps 5-8: the engine's description of the source, its blocks, documents and in-source holds. A family without an
    // identity, or a file with no store, is described but not projected.
    private static InspectedFile Describe(InspectedFile file, WorkbookSnapshot workbook, MatchedImportInspection inspection)
    {
        var profile = inspection.MatchedProfile;
        if (profile is null || file.Family is not { } family) return file with { Workbook = workbook };
        var sheet = inspection.AcceptedImport?.MatchedSheet ?? new ImportPreflight().Inspect(workbook, ApprovedImportProfileRegistry.All).Sheet;
        if (sheet is null) return file with { Workbook = workbook };
        var diagnostics = file.Diagnostics.ToList();
        try
        {
            var describer = new SourceDescriptionReader(new ConsolidationContractReader(), new LegacyInfoBlockReader());
            var source = describer.Describe(new SourceDescriptionRequest(workbook, family, sheet));
            diagnostics.AddRange(source.Diagnostics.Where(diagnostic => !diagnostics.Contains(diagnostic)));
            var staged = StageAll(workbook, family, sheet, inspection.AcceptedImport?.Staging);
            var blocks = new List<InspectedBlock>();
            var projections = new List<BlockProjection>();
            var projector = new DocumentProjector(FactCanonicalizer.Instance);
            var snapshotBlocks = inspection.AcceptedImport?.Scope.SnapshotBlocks ?? [];
            foreach (var described in source.Blocks)
            {
                var block = Dated(described, snapshotBlocks, file.BusinessDate, ImportScope.IsUndatedFamily(family.ReportCode));
                var rows = SourceDescriptionReader.RebuildRows(source, block, staged);
                var identity = file.Store is { } store
                    ? ExportIdentity.Describe(family, store, block, rows, FactCanonicalizer.Instance)
                    : new BlockIdentity(ExportIdentity.ContentSha256(family, block, rows, FactCanonicalizer.Instance),
                        FactCanonicalizer.CurrentContentHashVersion, null, family.Identity?.RulesetVersion ?? 1);
                blocks.Add(new InspectedBlock(block, identity, rows.Count));
                if (family.Identity is not null && file.Store is { } owner && !source.HasBlockers)
                    projections.Add(projector.Project(new ProjectionRequest(family, owner, block, rows)));
            }
            var documents = (IReadOnlyList<DocumentObservation>)[];
            if (projections.Count > 0)
            {
                var resolution = new InSourceResolver(FactCanonicalizer.Instance).Resolve(source, projections);
                documents = resolution.Authoritative;
                diagnostics.AddRange(projections.SelectMany(projection => projection.Diagnostics));
                diagnostics.AddRange(resolution.Diagnostics);
            }
            return file with
            {
                Workbook = workbook, Source = source, SourceKind = source.Kind, ContractVersion = source.ContractVersion,
                Blocks = blocks, SnapshotBlocks = snapshotBlocks, VirtualRows = source.VirtualRows.Count, Documents = documents,
                Projected = projections.Count > 0, HeldRows = projections.Sum(projection => projection.HeldRows.Count), Diagnostics = diagnostics
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The description is the audit's own step; a failure in it never hides the import verdict above. Only the
            // exception type is kept: its message may quote a cell (design 8.1).
            diagnostics.Add(new(AuditCodes.DescriptionFailed, ImportDiagnosticSeverity.Warning, AuditCodes.DescriptionFailedMessage));
            return file with { Workbook = workbook, Diagnostics = diagnostics, DescriptionFailure = exception.GetType().Name };
        }
    }

    /// <summary>
    /// Every staged row of the workbook, located on its sheet: the data sheet as the import staged it, then a
    /// <c>Snapshot History</c> sheet staged under its extra columns, since a twin row can live on either.
    /// </summary>
    private static IReadOnlyList<SourceRow> StageAll(WorkbookSnapshot workbook, EtpReportFamily family, WorkbookSheet sheet, ImportStagingResult? staging)
    {
        var stager = new ImportRowStager();
        var profile = family.CreateProfile();
        staging ??= stager.Stage(sheet, profile);
        var rows = staging.Rows.Select(row => new SourceRow(new RowLocator(0, sheet.Name, row.SourceRowNumber), row.Values)).ToList();
        if (ContractSheetView.Find(workbook, ConsolidationContractLayout.HistorySheet) is { } history)
        {
            var staged = HistorySheetBlockReader.StagingSheet(history);
            rows.AddRange(stager.Stage(staged, profile).Rows.Select(row => new SourceRow(new RowLocator(0, staged.Name, row.SourceRowNumber), row.Values)));
        }
        return rows;
    }

    /// <summary>
    /// A snapshot block the description left undated (a raw export with no 12-digit name) takes the date planner 1 gives
    /// its rows: the snapshot-date resolver's block, else the folder's sibling date (spec 6.4 tiers 5-7).
    /// </summary>
    private static SourceBlock Dated(SourceBlock block, IReadOnlyList<SnapshotBlock> snapshotBlocks, DateOnly? businessDate, bool undated)
    {
        if (!undated || block.SnapshotDate is not null || block.Origin is not (BlockOrigin.Raw or BlockOrigin.WholeFile)) return block;
        var resolved = snapshotBlocks.FirstOrDefault(candidate => block.FirstRow is { } first && candidate.Contains(first) &&
            string.Equals(candidate.SheetName, block.SheetName, StringComparison.OrdinalIgnoreCase));
        if (resolved is not null) return block with { SnapshotDate = resolved.SnapshotDate, SnapshotDateBasis = resolved.Basis };
        return snapshotBlocks.Count == 0 && businessDate is { } date
            ? block with { SnapshotDate = date, SnapshotDateBasis = SnapshotDateBasis.Sibling }
            : block;
    }

    private static int CountCsv(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.csv", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Count(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>
    /// The path a report shows: relative to the input folder, or the ZIP's name plus the entry path, or the file name.
    /// User-profile folders never appear (design 8.7).
    /// </summary>
    internal static string Relative(string input, string path)
    {
        if (Directory.Exists(input))
        {
            var relative = Path.GetRelativePath(input, path);
            return relative.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(path) : relative;
        }
        if (Path.GetExtension(input).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            // BatchImportSource extracts under %TEMP%\EtpReporting\<guid>\<zip name>\..., so the path after the GUID
            // is the ZIP's name and the entry path.
            var relative = Path.GetRelativePath(Path.Combine(Path.GetTempPath(), "EtpReporting"), path);
            var separator = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
            return relative.StartsWith("..", StringComparison.Ordinal) || separator < 0 ? Path.GetFileName(path) : relative[(separator + 1)..];
        }
        return Path.GetFileName(path);
    }
}

/// <summary>The audit's own diagnostic codes; not import codes, so they never reach an attempt.</summary>
public static class AuditCodes
{
    public const string DescriptionFailed = "AUDIT_DESCRIPTION_FAILED";
    public const string DescriptionFailedMessage = "The audit could not describe this workbook's blocks; the import verdict above still stands.";
    public const string StateChangedDuringRead = "STATE_CHANGED_DURING_READ";
    public const string StateChangedDuringReadMessage = "An import ran while the audit was reading the database. Rerun while ETP is idle.";
    public const string RawNotChecked = "AUDIT_RAW_NOT_CHECKED";
    public const string ContractRequired = "AUDIT_CONTRACT_REQUIRED";
    public const string ContractRequiredMessage = "This workbook is not a contract v1 workbook (--require-contract).";
    public const string ExpectationMismatch = "AUDIT_EXPECTATION_MISMATCH";
    public const string Forbidden = "AUDIT_FORBIDDEN_FILE";
    public const string BlockingCheck = "AUDIT_BLOCKING_CHECK";

    /// <summary>Audit codes and their fixed messages; the report writer prints these, or the import catalogue's.</summary>
    public static IReadOnlyDictionary<string, string> Messages { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [DescriptionFailed] = DescriptionFailedMessage,
        [StateChangedDuringRead] = StateChangedDuringReadMessage,
        [ContractRequired] = ContractRequiredMessage,
        [RawNotChecked] = "Rebuildable blocks whose raw export is not in the --raw folder were not checked.",
        [ExpectationMismatch] = "The prediction does not match the expectation file.",
        [Forbidden] = "The expectation file forbids importing this file.",
        [BlockingCheck] = "A check that blocks the upgrade found rows:"
    };
}
