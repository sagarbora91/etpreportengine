using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Etp.Reporting.Import.Audit;

/// <summary>
/// Writes a report as plain text (design 7.1: fixed order, short lines, no colour) and as JSON (design 7.2:
/// <c>etp-import-audit-report/1</c>, camelCase, enums by their C# names, absent fields left out). Everything it writes
/// came through <see cref="AuditReportBuilder"/>, so it holds catalogue messages, counts, codes and hashes only.
/// </summary>
public static class AuditReportWriter
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static string Json(AuditReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static string Text(AuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        var tool = report.Tool;
        var database = report.Database is { } db
            ? $"  database {db.Name} (SELECT-only{(db.Emulated0041 ? ", 0041 emulated" : "")})"
            : "  no database";
        text.Append(CultureInfo.InvariantCulture, $"ETP ImportAudit {tool.Version}{(tool.Commit is null ? "" : $" ({tool.Commit})")}  {report.Command}{database}").AppendLine();
        text.Append(CultureInfo.InvariantCulture, $"Catalogue {Short(tool.CatalogueSha256)}  content hash v{tool.ContentHashVersion}  detail {report.Detail.ToString().ToLowerInvariant()}");
        if (tool.Rulesets.Count > 0)
            text.Append("  rulesets: ").Append(string.Join(", ", tool.Rulesets.Select(pair => $"{pair.Key} {pair.Value}")));
        text.AppendLine();
        if (report.Database is { } stamp)
        {
            if (stamp.LatestMigration is not null) text.Append("Latest migration ").Append(stamp.LatestMigration).AppendLine();
            if (stamp.LoginCouldWrite is { } write) text.Append("This login could write: ").Append(write ? "yes (never used)" : "no").AppendLine();
            foreach (var note in stamp.Notes) text.Append("Note: ").Append(note).AppendLine();
        }
        if (report.OrderNote is not null) text.AppendLine(report.OrderNote);
        foreach (var diagnostic in report.RunDiagnostics)
            text.Append(CultureInfo.InvariantCulture, $"{diagnostic.Severity.ToUpperInvariant()} {diagnostic.Code}: {diagnostic.Message}").AppendLine();

        foreach (var file in report.Files)
        {
            text.AppendLine();
            var title = $"{file.Store ?? "-"}  {file.FamilyCode ?? "?"}{(file.ReportCode is { } code && code != file.FamilyCode ? $" ({code})" : "")}  {file.File}";
            text.Append(title);
            if (file.SourceKind is not null) text.Append("  ").Append(Upper(file.SourceKind));
            if (file.ContractVersion is { } version) text.Append(CultureInfo.InvariantCulture, $" v{version}");
            text.AppendLine();
            text.Append(CultureInfo.InvariantCulture, $"  outcome {file.Outcome}{(file.FailureCode is null ? "" : " " + file.FailureCode)}  rows {file.StagedRows:N0}");
            if (file.PeriodStart is { } start) text.Append(CultureInfo.InvariantCulture, $"  period {start:yyyy-MM-dd}..{file.PeriodEnd:yyyy-MM-dd}");
            text.AppendLine();
            foreach (var block in file.Blocks)
            {
                text.Append(CultureInfo.InvariantCulture, $"  block {block.BlockNo} {block.Origin} {block.Completeness}  {block.Sheet}");
                if (block.FirstRow is { } first) text.Append(CultureInfo.InvariantCulture, $" {first}:{block.LastRow}");
                text.Append(CultureInfo.InvariantCulture, $"  rows {block.Rows:N0}  export time {block.ExportTime ?? "UNKNOWN"}");
                if (block.SnapshotDate is { } snapshot) text.Append(CultureInfo.InvariantCulture, $"  snapshot {snapshot:yyyy-MM-dd} ({block.SnapshotDateBasis})");
                if (block.PeriodFrom is { } from) text.Append(CultureInfo.InvariantCulture, $"  period {from:yyyy-MM-dd}..{block.PeriodTo:yyyy-MM-dd} ({block.PeriodBasis})");
                if (block.VirtualRows > 0) text.Append(CultureInfo.InvariantCulture, $"  virtual {block.VirtualRows}");
                if (block.ContentSha256 is { } content) text.Append("  content ").Append(Short(content));
                text.AppendLine();
            }
            if (file.SnapshotDates.Count > 0)
                text.Append("  snapshot dates: ").Append(string.Join(", ", file.SnapshotDates.Select(date =>
                    string.Create(CultureInfo.InvariantCulture, $"{date.Date:yyyy-MM-dd} ({date.Rows:N0} rows, {date.Basis})")))).AppendLine();
            if (file.Documents is { } documents)
            {
                if (documents.Projected)
                    text.Append(CultureInfo.InvariantCulture,
                        $"  documents {documents.Total:N0}{Scopes(documents.ByScope)}  held rows {documents.HeldRows:N0}  in-source holds {documents.Holds.Values.Sum():N0}");
                else text.Append("  documents not projected (no family identity or store)");
                text.AppendLine();
            }
            if (file.Contract is { } contract)
            {
                text.Append(CultureInfo.InvariantCulture, $"  contract: {contract.Kind}  blockers {contract.Blockers}  rebuild checked {contract.RebuildChecked}, not checked {contract.RebuildNotChecked}");
                if (contract.LegacyInfoTiles is { } tiles) text.Append(tiles ? "  Info blocks tile" : "  Info blocks unusable");
                if (contract.MatchedByName > 0) text.Append(CultureInfo.InvariantCulture, $"  matched by name {contract.MatchedByName}");
                text.AppendLine();
            }
            if (file.Planner1 is { } planner)
            {
                text.Append(CultureInfo.InvariantCulture, $"  planner 1 (1.9.3):  {planner.Result}{(planner.Code is null ? "" : " " + planner.Code)}   new {planner.Rows.New:N0}  present {planner.Rows.Present:N0}  conflict {planner.Rows.Conflict:N0}");
                if (planner.PromotesOver.Count > 0) text.Append("  promotes over ").Append(string.Join(", ", planner.PromotesOver));
                text.AppendLine();
                if (planner.SnapshotRows is { Count: > 0 } snapshots)
                    text.Append("  snapshot rows: ").Append(string.Join(", ", snapshots.Select(pair => $"{pair.Key} {pair.Value:N0}"))).AppendLine();
                if (planner.Message is not null) text.Append("    ").Append(planner.Message).AppendLine();
                foreach (var sample in planner.ConflictSamples ?? []) text.Append("    conflict ").Append(sample).AppendLine();
            }
            if (file.Expectation is { } expectation)
            {
                text.Append("  expectation: ").Append(expectation.Pass ? "PASS" : "MISMATCH").AppendLine();
                foreach (var mismatch in expectation.Mismatches) text.Append("    ").Append(mismatch).AppendLine();
            }
            foreach (var diagnostic in file.Diagnostics)
            {
                text.Append(CultureInfo.InvariantCulture, $"  {diagnostic.Severity.ToLowerInvariant()} {diagnostic.Code}");
                if (diagnostic.Occurrences > 1) text.Append(CultureInfo.InvariantCulture, $" x{diagnostic.Occurrences}");
                if (diagnostic.Block is { } blockNo) text.Append(CultureInfo.InvariantCulture, $" block {blockNo}");
                if (diagnostic.Sheet is not null) text.Append(CultureInfo.InvariantCulture, $" {diagnostic.Sheet}{(diagnostic.Row is { } row ? $"!{row}" : "")}");
                if (diagnostic.Column is not null) text.Append(" [").Append(diagnostic.Column).Append(']');
                if (diagnostic.DocumentRef is not null) text.Append(" (").Append(diagnostic.DocumentRef).Append(')');
                text.Append(": ").Append(diagnostic.Message).AppendLine();
            }
            foreach (var document in file.DocumentList ?? [])
                text.Append(CultureInfo.InvariantCulture, $"  document {document.KeyText}  block {document.Block}  rows {document.RowCount}  facts {Short(document.FactSha256)}{(document.Hold is null ? "" : "  held " + document.Hold)}").AppendLine();
            text.Append("  verdict ").Append(file.Verdict).AppendLine();
        }

        if (report.Baseline is { } baseline)
        {
            foreach (var set in baseline.ResultSets)
            {
                text.AppendLine().Append(set.Name).AppendLine();
                text.Append("  ").Append(string.Join(" | ", set.Columns)).AppendLine();
                foreach (var row in set.Rows) text.Append("  ").Append(string.Join(" | ", row.Select(value => value ?? "NULL"))).AppendLine();
            }
            if (baseline.Differences is { } differences)
            {
                text.AppendLine().Append(CultureInfo.InvariantCulture, $"Compared with the earlier baseline: {differences.Count} difference(s)").AppendLine();
                foreach (var difference in differences) text.Append("  ").Append(difference).AppendLine();
            }
        }

        var summary = report.Summary;
        text.AppendLine();
        foreach (var hold in summary.LegacyHolds)
            text.Append(CultureInfo.InvariantCulture, $"legacy hold  {hold.File}  {hold.Code} x{hold.Occurrences}").AppendLine();
        if (summary.SkippedCsvFiles > 0)
            text.Append(CultureInfo.InvariantCulture, $"skipped {summary.SkippedCsvFiles} CSV file(s): not an ETP workbook (CSV, not yet supported)").AppendLine();
        text.Append(CultureInfo.InvariantCulture,
            $"SUMMARY  files {summary.Files}  pass {summary.Pass}  findings {summary.Findings}  blocked {summary.Blocked}  errors {summary.Error}  unexpected blockers {summary.UnexpectedBlockers}  legacy holds {summary.LegacyHolds.Count}   exit {summary.ExitCode}");
        text.AppendLine();
        return text.ToString();
    }

    private static string Scopes(IReadOnlyDictionary<string, int> byScope) =>
        byScope.Count == 0 ? "" : " (" + string.Join(", ", byScope.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key} {pair.Value:N0}"))) + ")";

    private static string Short(string hash) => hash.Length <= 12 ? hash : hash[..12];

    // ConsolidatedLegacy -> CONSOLIDATED_LEGACY, as import_files.source_kind spells it.
    private static string Upper(string name)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index])) builder.Append('_');
            builder.Append(char.ToUpperInvariant(name[index]));
        }
        return builder.ToString();
    }
}

/// <summary>
/// Where a report may be written (design 8.6): by default under <c>%LOCALAPPDATA%\EtpReporting\ImportAudit</c>; never
/// inside a git working tree, unless every input is a synthetic fixture under <c>tests-dotnet/fixtures/</c>.
/// </summary>
public static class AuditOutputLocation
{
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "ImportAudit");

    public static string DefaultFolder(AuditCommandKind kind, DateTimeOffset now) =>
        Path.Combine(DefaultRoot, $"{now.ToLocalTime():yyyyMMdd-HHmmss}-{AuditCommandLine.Name(kind)}");

    /// <summary>The folder to write to, or the reason it is refused (a usage error, exit 2).</summary>
    public static (string? Folder, string? Refusal) Resolve(string? requested, AuditCommandKind kind, IReadOnlyList<string> inputs, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(requested)) return (DefaultFolder(kind, now), null);
        var folder = Path.GetFullPath(requested);
        if (WorkTreeRoot(folder) is not { } root) return (folder, null);
        var fixtures = Path.Combine(root, "tests-dotnet", "fixtures") + Path.DirectorySeparatorChar;
        var synthetic = inputs.Count > 0 && inputs.All(input =>
            Path.GetFullPath(input).StartsWith(fixtures, StringComparison.OrdinalIgnoreCase));
        return synthetic
            ? (folder, null)
            : (null, "--out is inside a git working tree. Reports about business workbooks never go into the repository; choose a folder outside it.");
    }

    /// <summary>The nearest folder at or above <paramref name="path"/> holding <c>.git</c> (a folder, or a worktree's file).</summary>
    public static string? WorkTreeRoot(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
        {
            var git = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return current.FullName;
        }
        return null;
    }
}
