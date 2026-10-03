using System.Security.Cryptography;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Audit;

/// <summary>
/// <c>--raw</c>: the SHA-256 of every <c>.xlsx</c> under a folder, hashed in 64 KB buffers. A raw workbook is parsed
/// only when a block names its hash (design 10.2).
/// </summary>
public sealed class RawExportIndex
{
    private readonly Dictionary<string, string> pathsBySha = new(StringComparer.Ordinal);
    private readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

    private RawExportIndex()
    {
    }

    public static RawExportIndex Empty { get; } = new();

    public int Count => pathsBySha.Count;

    public static async Task<RawExportIndex> BuildAsync(string folder, ImportPathPolicy? policy = null, CancellationToken cancellationToken = default)
    {
        policy ??= new ImportPathPolicy();
        var root = policy.ValidateExistingSource(folder);
        if (!Directory.Exists(root)) throw new ImportSourceException("IMPORT_RAW_FOLDER", "--raw must name a folder of raw exports.");
        var index = new RawExportIndex();
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = policy.MaximumFolderDepth };
        foreach (var path in Directory.EnumerateFiles(root, "*.xlsx", options).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal)) continue;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            index.pathsBySha.TryAdd(sha, path);
            index.names.Add(Path.GetFileName(path));
        }
        return index;
    }

    public bool Contains(string? sha256) => sha256 is not null && pathsBySha.ContainsKey(sha256.Trim().ToLowerInvariant());

    public bool ContainsName(string? fileName) => fileName is not null && names.Contains(Path.GetFileName(fileName));

    /// <summary>The raw workbooks for these hashes, read now; hashes not in the index are left out.</summary>
    public async Task<IReadOnlyDictionary<string, WorkbookSnapshot>> LoadAsync(IEnumerable<string> sha256s, IWorkbookReader reader,
        CancellationToken cancellationToken = default)
    {
        var loaded = new Dictionary<string, WorkbookSnapshot>(StringComparer.Ordinal);
        foreach (var sha in sha256s.Select(value => value.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal))
            if (pathsBySha.TryGetValue(sha, out var path))
                loaded[sha] = await reader.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        return loaded;
    }
}

/// <summary>
/// <c>validate-contract</c> (design 3.2): every contract section 8 rule through <see cref="ConsolidationContractValidator"/>,
/// and with <c>--raw</c> the rebuild of each block whose raw export is in the folder. Legacy and raw workbooks are
/// reported by kind, which is not a finding unless <c>--require-contract</c> is set.
/// </summary>
public static class ContractAudit
{
    public static async Task<(AuditContractReport Report, IReadOnlyList<ImportDiagnostic> Diagnostics)> CheckAsync(InspectedFile file,
        RawExportIndex raw, bool requireContract, IWorkbookReader reader, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ImportDiagnostic>();
        var source = file.Source;
        if (source is null || file.Family is not { } family || file.Workbook is not { } workbook)
        {
            if (requireContract) diagnostics.Add(RequireContract());
            return (new AuditContractReport(AuditReportBuilder.KindName(file.SourceKind)) { Blockers = diagnostics.Count }, diagnostics);
        }

        if (source.Kind != SourceKind.Consolidated || source.Contract is not { } contract)
        {
            if (requireContract) diagnostics.Add(RequireContract());
            var legacy = source.Kind == SourceKind.ConsolidatedLegacy && file.Accepted?.MatchedSheet is { } sheet
                ? new LegacyInfoBlockReader().Read(workbook, sheet) : null;
            return (new AuditContractReport(source.Kind.ToString())
            {
                LegacyInfoTiles = legacy is { Found: true } ? legacy.Tiles : null,
                MatchedByName = source.Blocks.Count(block => block.Completeness == BlockCompleteness.Legacy && raw.ContainsName(block.SourceFileName)),
                Blockers = diagnostics.Count
            }, diagnostics);
        }

        var rebuildable = source.Blocks.Where(block => block.IsRebuildable && block.SourceSha256 is not null).ToArray();
        var checkable = rebuildable.Where(block => raw.Contains(block.SourceSha256)).ToArray();
        if (checkable.Length > 0)
        {
            var exports = await raw.LoadAsync(checkable.Select(block => block.SourceSha256!), reader, cancellationToken).ConfigureAwait(false);
            var result = new ConsolidationContractValidator().Validate(new ContractValidationRequest(workbook, contract, family) { RawExports = exports });
            // Validation is deterministic, so what the run with raws adds is exactly the rebuild check.
            diagnostics.AddRange(result.Diagnostics.Where(diagnostic => !source.Diagnostics.Contains(diagnostic)));
        }
        var notChecked = rebuildable.Length - checkable.Length;
        if (notChecked > 0 && raw.Count > 0)
            diagnostics.Add(new(AuditCodes.RawNotChecked, ImportDiagnosticSeverity.Information, AuditCodes.Messages[AuditCodes.RawNotChecked]) { Occurrences = notChecked });
        var blockers = source.Diagnostics.Concat(diagnostics).Count(AuditReportBuilder.IsBlocker);
        return (new AuditContractReport(source.Kind.ToString())
        {
            ContractVersion = source.ContractVersion,
            RebuildChecked = checkable.Length,
            RebuildNotChecked = notChecked,
            Blockers = blockers
        }, diagnostics);
    }

    private static ImportDiagnostic RequireContract() =>
        new(AuditCodes.ContractRequired, ImportDiagnosticSeverity.Blocker, AuditCodes.ContractRequiredMessage);
}
