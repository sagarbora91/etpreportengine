using System.Text.RegularExpressions;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.ShopResults;

namespace Etp.Reporting.Infrastructure.SqlServer.ImportSourceLocations;

/// <summary>The problem row a Fix button acts on: the file as the attempt recorded it, and its store and day when known.</summary>
public sealed record ImportFixTarget(string FileName, string? SourceSha256, string? StoreCode, DateOnly? BusinessDate);

/// <summary>Where ETP keeps the file of a problem row, whether it is still there, and whether it is inside a configured folder.</summary>
public sealed record ImportFixLocation(ImportSourceLocation Location, bool Exists, bool UnderConfiguredRoot);

/// <summary>What a Fix button did: whether the row's file is now imported, one plain sentence, and the run's file results.</summary>
public sealed record ImportFixOutcome(bool Imported, string Message, FolderImportSummary? Summary)
{
    /// <summary>The automation-style note when the shop's new result file could not be written ("" when it was, or not a shop zip).</summary>
    public string ShopResultNote { get; init; } = "";
}

/// <summary>The Fix buttons' work on Imports > Problems and History (1.12 U2, wave plan C-c). Every action is audited as ImportFixAction.</summary>
public interface IImportFixService
{
    /// <summary>Where the row's file went (0069), or null when ETP does not know (older database, Viewer, a file never routed).</summary>
    Task<ImportFixLocation?> LocateAsync(ImportFixTarget target, CancellationToken cancellationToken = default);

    /// <summary>Imports the routed file again as it is. Owner or Store Manager.</summary>
    Task<ImportFixOutcome> RetryAsync(ImportFixLocation location, ImportFixTarget target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens <paramref name="storeCode"/> on <paramref name="businessDate"/> with the Owner's reason (the same Owner-only reopen as Close
    /// day), then imports the routed file again. When the import fails after the reopen, the day stays open and the sentence says so.
    /// </summary>
    Task<ImportFixOutcome> ReopenAndReapplyAsync(ImportFixLocation location, ImportFixTarget target, string storeCode, DateOnly businessDate,
        string reason, CancellationToken cancellationToken = default);
}

public sealed partial class SqlServerImportFixService : IImportFixService
{
    public const string RetryAuditDetail = "Retry of a routed file";
    public const string ReapplyAuditDetail = "Reopen day and re-apply";

    private readonly IEtpConnectionFactory connections;
    private readonly Action<FolderImportFailure>? reportFailure;
    private readonly IImportSourceLocationStore locations;
    private readonly ShopResultWriter shopResults;
    private readonly Func<IDailyWorkflowCommands> workflow;
    private readonly Func<string, string, string, CancellationToken, Task> audit;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> configuredRoots;

    public SqlServerImportFixService(IEtpConnectionFactory connections, Action<FolderImportFailure>? reportFailure = null)
        : this(connections, reportFailure, null, null, null, null, null) { }

    internal SqlServerImportFixService(IEtpConnectionFactory connections, Action<FolderImportFailure>? reportFailure,
        IImportSourceLocationStore? locations, ShopResultWriter? shopResults, Func<IDailyWorkflowCommands>? workflow,
        Func<string, string, string, CancellationToken, Task>? audit, Func<CancellationToken, Task<IReadOnlyList<string>>>? configuredRoots)
    {
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
        this.reportFailure = reportFailure;
        this.locations = locations ?? new SqlServerImportSourceLocationStore(connections);
        this.shopResults = shopResults ?? new ShopResultWriter();
        this.workflow = workflow ?? (() => new SqlServerDailyWorkflowService(Legacy()));
        this.audit = audit ?? ((type, outcome, detail, token) => new OperationalAuditRepository(Legacy()).RecordAsync(type, outcome, detail, cancellationToken: token));
        this.configuredRoots = configuredRoots ?? LoadConfiguredRootsAsync;
    }

    private string Legacy() => connections.LegacyConnectionString();

    public async Task<ImportFixLocation?> LocateAsync(ImportFixTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var location = await locations.FindLatestAsync(target.FileName, target.SourceSha256, cancellationToken).ConfigureAwait(false);
        if (location is null) return null;
        var roots = await configuredRoots(cancellationToken).ConfigureAwait(false);
        return new(location, File.Exists(location.RoutedPath), IsUnder(location.RoutedPath, roots));
    }

    public async Task<ImportFixOutcome> RetryAsync(ImportFixLocation location, ImportFixTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(target);
        var outcome = await ImportAgainAsync(location, target, cancellationToken).ConfigureAwait(false);
        await AuditAsync(outcome.Imported, RetryAuditDetail, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    public async Task<ImportFixOutcome> ReopenAndReapplyAsync(ImportFixLocation location, ImportFixTarget target, string storeCode, DateOnly businessDate,
        string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(storeCode)) throw new ArgumentException("Choose the store whose day is reopened.", nameof(storeCode));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Enter the reason for reopening the day.", nameof(reason));
        if (!location.Exists) throw new InvalidOperationException(MissingFileMessage);
        // The same Owner-only reopen as Today > Close day (the service checks the role; the trigger keeps the history).
        await workflow().ReopenAsync(new ReopenDailyWorkflow(new(storeCode.Trim(), businessDate), Identity(), reason.Trim()), cancellationToken).ConfigureAwait(false);
        await audit("DayReopened", "Succeeded", "Business day reopened", CancellationToken.None).ConfigureAwait(false);
        var outcome = await ImportAgainAsync(location, target, cancellationToken).ConfigureAwait(false);
        await AuditAsync(outcome.Imported, ReapplyAuditDetail, cancellationToken).ConfigureAwait(false);
        return outcome.Imported ? outcome
            : outcome with { Message = $"The day is reopened, but the file was not imported: {outcome.Message} Finalise the day again when it is complete." };
    }

    public const string MissingFileMessage = "ETP no longer finds this file where it put it. Import it again from the shop's copy.";

    private async Task<ImportFixOutcome> ImportAgainAsync(ImportFixLocation location, ImportFixTarget target, CancellationToken cancellationToken)
    {
        if (!location.Exists || !File.Exists(location.Location.RoutedPath)) throw new InvalidOperationException(MissingFileMessage);
        var path = location.Location.RoutedPath;
        var legacy = Legacy();
        var knownStores = await new StoreCatalogRepository(legacy).ActiveCodesAsync(cancellationToken).ConfigureAwait(false);
        var service = new FolderImportService(new SqlServerImportPersistenceUseCase(legacy), knownStores: knownStores, reportFailure: reportFailure);
        var summary = await service.RunAsync(path, new FolderImportOptions(Identity()), cancellationToken: cancellationToken).ConfigureAwait(false);
        // A new location row (route Hand): the file stays where it is, and History can show who re-applied it.
        try { await locations.RecordAsync(AutomatedOperationsService.LocationsFor(path, path, ImportSourceRoute.Hand, summary.Files), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { /* the import's outcome stands; the next Fix finds the older row */ }
        var shopNote = WriteShopResult(path, summary.Files);
        var row = MatchingResult(summary, target);
        var imported = row is not null && (row.Status is "Imported" or "empty export" || AutomatedOperationsService.IsSavedDespiteFailure(row));
        var message = row is null ? "The file was read again, but this row's file was not among its files."
            : imported ? $"{row.FileName} is imported." + (row.NewRows > 0 ? $" {row.NewRows:N0} new rows." : "")
            : ImportProblemsMessage(row);
        return new(imported, message, summary) { ShopResultNote = shopNote };
    }

    /// <summary>The result of the problem row's own file in a run (by hash, else by name).</summary>
    internal static FolderImportFileResult? MatchingResult(FolderImportSummary summary, ImportFixTarget target)
    {
        var sha = SqlServerImportSourceLocationStore.NormalisedSha(target.SourceSha256);
        return (sha is null ? null : summary.Files.LastOrDefault(file => string.Equals(file.SourceSha256, sha, StringComparison.OrdinalIgnoreCase)))
            ?? summary.Files.LastOrDefault(file => string.Equals(Path.GetFileName(file.FileName), Path.GetFileName(target.FileName), StringComparison.OrdinalIgnoreCase));
    }

    private static string ImportProblemsMessage(FolderImportFileResult row) =>
        row.Status.StartsWith("Duplicate", StringComparison.Ordinal) || row.Status == "Already present"
            ? $"{row.FileName} was already imported; nothing changed."
            : $"{row.FileName} was refused again: {ImportDiagnosticCatalogue.Template(row.Failure?.Code)}";

    private async Task AuditAsync(bool imported, string detail, CancellationToken cancellationToken)
    {
        // The import's outcome stands even when the audit row cannot be written; the attempt row is in History either way.
        try { await audit(OperationalAuditRepository.ImportFixActionType, imported ? "Succeeded" : "Failed", detail, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException && !cancellationToken.IsCancellationRequested) { }
    }

    /// <summary>A routed shop zip ("20261012...-TITAN-2026-10-11.zip") gets a new result file, the next sequence of that day.</summary>
    private string WriteShopResult(string routedPath, IReadOnlyList<FolderImportFileResult> files)
    {
        if (!TryParseRoutedShopZip(routedPath, out var role, out var day)) return "";
        var sequence = shopResults.NextSequence(role, day);
        var version = typeof(SqlServerImportFixService).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var result = ShopResultBuilder.Build(role, day, sequence, version, DateTime.UtcNow, files);
        if (result.Files.Count == 0) return "";
        shopResults.TryWrite(result, out var failure);
        return ShopResultWriter.FailureSentence(failure);
    }

    [GeneratedRegex(@"^(?:\d{17}|[0-9a-fA-F]{32})-", RegexOptions.CultureInvariant)]
    private static partial Regex RoutingPrefix();

    /// <summary>The shop zip behind a routed name: the run prefixes a time (or a GUID) to the zip's own name when it moves it.</summary>
    internal static bool TryParseRoutedShopZip(string routedPath, out string role, out DateOnly day)
    {
        var name = Path.GetFileName(routedPath ?? "");
        var original = RoutingPrefix().Replace(name, "", 1);
        return ShopResultBuilder.TryParseShopZip(original, out role, out day, out _);
    }

    internal static bool IsUnder(string path, IReadOnlyList<string> roots)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        return roots.Where(root => !string.IsNullOrWhiteSpace(root)).Any(root =>
        {
            try
            {
                var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
                return full.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        });
    }

    private async Task<IReadOnlyList<string>> LoadConfiguredRootsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await new Phase2OperationsRepository(Legacy()).LoadWatchFolderSettingsAsync(cancellationToken).ConfigureAwait(false);
            return [settings.InboundPath, settings.ProcessedPath, settings.FailedPath];
        }
        catch (Exception exception) when (exception is InvalidOperationException or Microsoft.Data.SqlClient.SqlException) { return []; }
    }

    private static string Identity() => $"{Environment.UserDomainName}\\{Environment.UserName}";
}
