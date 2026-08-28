using App = Etp.Reporting.Application.OperationsAdministration;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// SQL adapter for the Operations Center, including controlled automation,
/// issue workflow, adjustments and Owner approval decisions.
/// </summary>
public sealed class SqlServerOperationsAdministrationService : App.IOperationsAdministrationService
{
    private readonly IOperationsAdministrationSqlGateway gateway;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;

    public SqlServerOperationsAdministrationService(string connectionString)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString));
        gateway = new OperationsAdministrationSqlGateway(
            new Phase2OperationsRepository(validated),
            new ProductisationRepository(validated),
            new AutomatedOperationsService(validated));
        loadAccess = new Phase2OperationsRepository(validated).LoadCurrentAccessAsync;
    }

    internal SqlServerOperationsAdministrationService(
        IOperationsAdministrationSqlGateway gateway,
        Func<CancellationToken, Task<ApplicationAccess>> loadAccess)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        this.loadAccess = loadAccess ?? throw new ArgumentNullException(nameof(loadAccess));
    }

    public async Task<App.OperationsDashboard> LoadDashboardAsync(
        App.OperationsPeriod period,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(period);
        var access = await loadAccess(cancellationToken).ConfigureAwait(false);
        if (!access.CanView)
            throw new UnauthorizedAccessException("This Windows account does not have application access.");

        var settingsTask = gateway.LoadWatchFoldersAsync(cancellationToken);
        var trendTask = gateway.LoadTrendAsync(period.From, period.To, cancellationToken);
        var qualityTask = gateway.LoadQualityAsync(cancellationToken);
        var schedulesTask = gateway.LoadSchedulesAsync(cancellationToken);
        var runsTask = gateway.LoadAutomationRunsAsync(period.AutomationRunLimit, cancellationToken);
        await Task.WhenAll(settingsTask, trendTask, qualityTask, schedulesTask, runsTask).ConfigureAwait(false);

        var quality = await qualityTask.ConfigureAwait(false);
        // Viewers can inspect the Operations Center using read-only SQL membership.
        // Synchronising computed issues is an operational write reserved for Store Managers and Owners.
        if (access.CanEnterOperations)
            await gateway.SyncIssuesAsync(quality, cancellationToken).ConfigureAwait(false);
        var issues = await gateway.LoadIssuesAsync(cancellationToken).ConfigureAwait(false);
        return new(
            Map(await settingsTask.ConfigureAwait(false)),
            (await trendTask.ConfigureAwait(false)).Select(Map).ToArray(),
            quality.Select(Map).ToArray(),
            issues.Select(Map).ToArray(),
            (await schedulesTask.ConfigureAwait(false)).Select(Map).ToArray(),
            (await runsTask.ConfigureAwait(false)).Select(Map).ToArray());
    }

    public async Task<IReadOnlyList<App.ApprovalRequest>> LoadApprovalsAsync(
        string? status = "PENDING",
        CancellationToken cancellationToken = default)
    {
        await RequireViewAsync(cancellationToken).ConfigureAwait(false);
        return (await gateway.LoadApprovalsAsync(status, cancellationToken).ConfigureAwait(false))
            .Select(Map).ToArray();
    }

    public async Task<App.AutomationExecution> RunAutomationOnceAsync(CancellationToken cancellationToken = default)
    {
        await RequireOperationsAsync(cancellationToken).ConfigureAwait(false);
        var result = await gateway.RunAutomationOnceAsync(cancellationToken).ConfigureAwait(false);
        return new(result.SourcesProcessed, result.SourcesFailed, result.DuplicateWorkbooks, result.PacksGenerated, result.Message);
    }

    public async Task SaveWatchFoldersAsync(
        App.SaveWatchFolderConfiguration command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        await gateway.SaveWatchFoldersAsync(
            new(command.InboundPath, command.ProcessedPath, command.FailedPath, command.ReportOutputPath,
                command.PollMinutes, command.IsEnabled, DateTime.MinValue, string.Empty),
            command.Reason,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveScheduleAsync(App.SaveReportSchedule command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        await gateway.SaveScheduleAsync(command.Id, command.LocalRunTime, command.IsEnabled,
            command.ExportExcel, command.ExportPdf, command.Reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateIssueAsync(App.UpdateDataQualityIssue command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireOperationsAsync(cancellationToken).ConfigureAwait(false);
        await gateway.UpdateIssueAsync(command.IssueId, command.Status, command.Reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> SubmitAdjustmentAsync(App.SubmitAdjustment command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireOperationsAsync(cancellationToken).ConfigureAwait(false);
        return await gateway.SubmitAdjustmentAsync(command.StoreCode, command.BusinessDate, command.AdjustmentType,
            command.Amount, command.Reason, command.SourceDocumentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DecideApprovalAsync(App.DecideApproval command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        await gateway.DecideApprovalAsync(command.ApprovalId, command.Approve, command.Reason, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RequireViewAsync(CancellationToken cancellationToken)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanView)
            throw new UnauthorizedAccessException("This Windows account does not have application access.");
    }

    private async Task RequireOperationsAsync(CancellationToken cancellationToken)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanEnterOperations)
            throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
    }

    private async Task RequireOwnerAsync(CancellationToken cancellationToken)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private static void ValidatePeriod(App.OperationsPeriod? period)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (period.To < period.From || period.To.DayNumber - period.From.DayNumber > 366)
            throw new ArgumentException("Select a valid trend period of at most 366 days.", nameof(period));
        if (period.AutomationRunLimit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(period), "Automation run limit must be between 1 and 500.");
    }

    private static App.WatchFolderConfiguration Map(WatchFolderSettings row) =>
        new(row.InboundPath, row.ProcessedPath, row.FailedPath, row.ReportOutputPath,
            row.PollMinutes, row.IsEnabled, row.ModifiedUtc, row.ModifiedBy);
    private static App.ManagementTrendPoint Map(ManagementTrendRow row) =>
        new(row.BusinessDate, row.StoreCode, row.NetSales, row.Units, row.Invoices,
            row.TenderVariance, row.UnmatchedEnrichmentRows);
    private static App.DataQualityFinding Map(DataQualitySummaryRow row) =>
        new(row.Severity, row.Area, row.Code, row.Count, row.LatestUtc, row.Message);
    private static App.DataQualityIssue Map(DataQualityIssueRow row) =>
        new(row.Id, row.Category, row.Severity, row.StoreCode, row.BusinessDate,
            row.TechnicalControlStatus, row.WorkflowStatus, row.SafeSummary, row.AssignedTo,
            row.ModifiedUtc, row.ResolutionReason);
    private static App.ReportSchedule Map(ReportPackSchedule row) =>
        new(row.Id, row.Name, row.LocalRunTime, row.IsEnabled, row.ExportExcel, row.ExportPdf,
            row.LastBusinessDate, row.LastRunUtc, row.LastStatus, row.LastMessage);
    private static App.AutomationRun Map(AutomationRunRow row) =>
        new(row.Id, row.RunType, row.SourceFileName, row.StoreCode, row.BusinessDate, row.Outcome,
            row.SafeMessage, row.StartedUtc, row.CompletedUtc, row.RunBy);
    private static App.ApprovalRequest Map(ApprovalRequestRow row) =>
        new(row.Id, row.ApprovalType, row.SubjectType, row.SubjectId, row.StoreCode, row.BusinessDate,
            row.RequestedBy, row.RequestedUtc, row.Status, row.DecidedBy, row.DecidedUtc, row.DecisionReason);
}

internal interface IOperationsAdministrationSqlGateway
{
    Task<WatchFolderSettings> LoadWatchFoldersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagementTrendRow>> LoadTrendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<DataQualitySummaryRow>> LoadQualityAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportPackSchedule>> LoadSchedulesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AutomationRunRow>> LoadAutomationRunsAsync(int limit, CancellationToken cancellationToken);
    Task SyncIssuesAsync(IReadOnlyList<DataQualitySummaryRow> findings, CancellationToken cancellationToken);
    Task<IReadOnlyList<DataQualityIssueRow>> LoadIssuesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ApprovalRequestRow>> LoadApprovalsAsync(string? status, CancellationToken cancellationToken);
    Task<AutomatedOperationsSummary> RunAutomationOnceAsync(CancellationToken cancellationToken);
    Task SaveWatchFoldersAsync(WatchFolderSettings settings, string reason, CancellationToken cancellationToken);
    Task SaveScheduleAsync(int id, TimeOnly time, bool enabled, bool excel, bool pdf, string reason, CancellationToken cancellationToken);
    Task UpdateIssueAsync(long issueId, string status, string reason, CancellationToken cancellationToken);
    Task<long> SubmitAdjustmentAsync(string storeCode, DateOnly date, string type, decimal amount, string reason, long? documentId, CancellationToken cancellationToken);
    Task DecideApprovalAsync(long approvalId, bool approve, string reason, CancellationToken cancellationToken);
}

internal sealed class OperationsAdministrationSqlGateway(
    Phase2OperationsRepository operations,
    ProductisationRepository productisation,
    AutomatedOperationsService automation) : IOperationsAdministrationSqlGateway
{
    public Task<WatchFolderSettings> LoadWatchFoldersAsync(CancellationToken token) => operations.LoadWatchFolderSettingsAsync(token);
    public Task<IReadOnlyList<ManagementTrendRow>> LoadTrendAsync(DateOnly from, DateOnly to, CancellationToken token) => operations.LoadManagementTrendAsync(from, to, token);
    public Task<IReadOnlyList<DataQualitySummaryRow>> LoadQualityAsync(CancellationToken token) => operations.LoadDataQualitySummaryAsync(token);
    public Task<IReadOnlyList<ReportPackSchedule>> LoadSchedulesAsync(CancellationToken token) => operations.LoadSchedulesAsync(token);
    public Task<IReadOnlyList<AutomationRunRow>> LoadAutomationRunsAsync(int limit, CancellationToken token) => operations.LoadAutomationRunsAsync(limit, token);
    public Task SyncIssuesAsync(IReadOnlyList<DataQualitySummaryRow> findings, CancellationToken token) => productisation.SyncDataQualityIssuesAsync(findings, token);
    public Task<IReadOnlyList<DataQualityIssueRow>> LoadIssuesAsync(CancellationToken token) => productisation.LoadDataQualityIssuesAsync(token);
    public Task<IReadOnlyList<ApprovalRequestRow>> LoadApprovalsAsync(string? status, CancellationToken token) => productisation.LoadApprovalsAsync(status, token);
    public Task<AutomatedOperationsSummary> RunAutomationOnceAsync(CancellationToken token) => automation.RunOnceAsync(token);
    public Task SaveWatchFoldersAsync(WatchFolderSettings settings, string reason, CancellationToken token) => operations.SaveWatchFolderSettingsAsync(settings, reason, token);
    public Task SaveScheduleAsync(int id, TimeOnly time, bool enabled, bool excel, bool pdf, string reason, CancellationToken token) => operations.SaveScheduleAsync(id, time, enabled, excel, pdf, reason, token);
    public Task UpdateIssueAsync(long issueId, string status, string reason, CancellationToken token) => productisation.UpdateIssueWorkflowAsync(issueId, status, reason, token);
    public Task<long> SubmitAdjustmentAsync(string storeCode, DateOnly date, string type, decimal amount, string reason, long? documentId, CancellationToken token) => productisation.CreateAdjustmentRequestAsync(storeCode, date, type, amount, reason, documentId, token);
    public Task DecideApprovalAsync(long approvalId, bool approve, string reason, CancellationToken token) => productisation.DecideApprovalAsync(approvalId, approve, reason, token);
}
