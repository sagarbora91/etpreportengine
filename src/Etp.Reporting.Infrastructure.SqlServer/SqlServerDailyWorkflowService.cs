using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// SQL adapter for daily readiness, governed operational input, finalisation,
/// reopening, and deterministic daily pack generation.
/// </summary>
public sealed class SqlServerDailyWorkflowService :
    IDailyWorkflowQuery,
    IDailyWorkflowCommands,
    IDailyReportPackGenerator<ReportPackDocument>
{
    private readonly DailyReportingWorkflowRepository workflow;
    private readonly OperationalCompletionRepository completion;
    private readonly DailyReportingPackService packs;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;

    public SqlServerDailyWorkflowService(string connectionString) : this(connectionString, null)
    {
    }

    internal SqlServerDailyWorkflowService(
        string connectionString,
        Func<CancellationToken, Task<ApplicationAccess>>? loadAccess)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString));
        workflow = new(validated);
        completion = new(validated);
        packs = new(validated);
        this.loadAccess = loadAccess ?? new Phase2OperationsRepository(validated).LoadCurrentAccessAsync;
    }

    public async Task<DailyWorkflowState> LoadAsync(
        DailyWorkflowScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await RequireViewAsync(cancellationToken).ConfigureAwait(false);
        return Map(await workflow.LoadAsync(
            scope.StoreCode,
            scope.BusinessDate,
            cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<DailyManualStockCount>> LoadStockCountsAsync(
        DailyWorkflowScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await RequireViewAsync(cancellationToken).ConfigureAwait(false);
        var rows = await completion.LoadManualStockCountsAsync(
            scope.StoreCode,
            scope.BusinessDate,
            cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<DailyStaffSalesTarget>> LoadStaffTargetsAsync(
        DailyStaffTargetSearch search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        await RequireViewAsync(cancellationToken).ConfigureAwait(false);
        var scope = new ReportingQueryScope(search.PeriodStart, search.PeriodEnd, search.StoreCodes);
        var rows = await completion.LoadStaffTargetsAsync(scope, cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }

    public async Task SaveManualInputAsync(
        SaveDailyManualInput command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Scope);
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        await workflow.SaveManualInputAsync(
            command.Scope.StoreCode,
            command.Scope.BusinessDate,
            command.FieldCode,
            command.NumericValue,
            command.TextValue,
            command.User,
            command.Reason,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveStockCountAsync(
        SaveDailyStockCount command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Scope);
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        await completion.SaveManualStockCountAsync(
            command.Scope.StoreCode,
            command.Scope.BusinessDate,
            command.InventoryGroupCode,
            command.DisplayQuantity,
            command.BackstockQuantity,
            command.DefectiveQuantity,
            command.YLocationQuantity,
            command.CountedPhysicalQuantity,
            command.Remarks,
            command.User,
            command.Reason,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveStaffTargetAsync(
        SaveDailyStaffTarget command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        await completion.SaveStaffTargetAsync(
            command.StoreCode,
            command.CroNumber,
            command.PeriodStart,
            command.PeriodEnd,
            command.TargetSales,
            command.User,
            command.Reason,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task FinaliseAsync(
        FinaliseDailyWorkflow command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Scope);
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        await workflow.FinaliseAsync(
            command.Scope.StoreCode,
            command.Scope.BusinessDate,
            command.User,
            command.HasBlockingReconciliationExceptions,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ReopenAsync(
        ReopenDailyWorkflow command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Scope);
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        await workflow.ReopenAsync(
            command.Scope.StoreCode,
            command.Scope.BusinessDate,
            command.User,
            command.Reason,
            command.AdministratorApproved,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DailyPackGeneration<ReportPackDocument>> GenerateAsync(
        DailyWorkflowScope scope,
        string? generatedBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await RequireViewAsync(cancellationToken).ConfigureAwait(false);
        return Map(await packs.GenerateAsync(
            scope.StoreCode,
            scope.BusinessDate,
            generatedBy,
            cancellationToken).ConfigureAwait(false));
    }

    public async Task<ReportPackDocument> GenerateCombinedAsync(
        DateOnly businessDate,
        string? generatedBy = null,
        CancellationToken cancellationToken = default)
    {
        await RequireViewAsync(cancellationToken).ConfigureAwait(false);
        return await packs.GenerateCombinedAsync(businessDate, generatedBy, cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireViewAsync(CancellationToken cancellationToken)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanView)
            throw new UnauthorizedAccessException("This Windows account does not have application access.");
    }

    private async Task RequireImportAsync(CancellationToken cancellationToken)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanImport)
            throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
    }

    private async Task RequireOwnerAsync(CancellationToken cancellationToken)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    public static DailyWorkflowState Map(DailyWorkflowSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.StoreCode,
            source.BusinessDate,
            Map(source.Status),
            source.ImportedReports,
            source.MissingReports,
            source.ManualInputs.Select(Map).ToArray(),
            source.MissingRequiredInputs,
            source.CanFinalise,
            source.StatusMessage);
    }

    public static DailyManualInput Map(ManualInputValue source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.FieldCode,
            source.DisplayName,
            source.ValueKind,
            source.NumericValue,
            source.TextValue,
            source.IsRequired,
            source.ModifiedUtc,
            source.ModifiedBy);
    }

    public static DailyManualStockCount Map(ManualStockCount source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.StoreCode,
            source.BusinessDate,
            source.InventoryGroupCode,
            source.DisplayQuantity,
            source.BackstockQuantity,
            source.DefectiveQuantity,
            source.YLocationQuantity,
            source.CountedPhysicalQuantity,
            source.Remarks,
            source.ModifiedUtc,
            source.ModifiedBy);
    }

    public static DailyStaffSalesTarget Map(StaffSalesTarget source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.StoreCode,
            source.CroNumber,
            source.PeriodStart,
            source.PeriodEnd,
            source.TargetSales,
            source.ModifiedUtc,
            source.ModifiedBy);
    }

    public static DailyPackGeneration<ReportPackDocument> Map(DailyReportPackResult source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.StoreCode,
            source.BusinessDate,
            Map(source.Status),
            source.Sections.Select(Map).ToArray(),
            source.Message,
            source.GeneratedAtUtc,
            source.Document,
            source.GenerationNumber,
            source.ContentSha256);
    }

    public static DailyPackSection Map(DailyReportPackSection source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.Report,
            Map(source.Status),
            source.ControlTotal,
            source.Variance,
            source.Message);
    }

    public static DailyWorkflowStatus Map(DailyReadinessStatus status) => status switch
    {
        DailyReadinessStatus.Partial => DailyWorkflowStatus.Partial,
        DailyReadinessStatus.ReadyWithWarnings => DailyWorkflowStatus.ReadyWithWarnings,
        DailyReadinessStatus.Reconciled => DailyWorkflowStatus.Reconciled,
        DailyReadinessStatus.Locked => DailyWorkflowStatus.Locked,
        _ => DailyWorkflowStatus.NotReady
    };

    public static DailyControlStatus Map(ReconciliationStatus status) => status switch
    {
        ReconciliationStatus.Passed => DailyControlStatus.Passed,
        ReconciliationStatus.Failed => DailyControlStatus.Failed,
        ReconciliationStatus.Blocked => DailyControlStatus.Blocked,
        _ => DailyControlStatus.NotRun
    };
}
