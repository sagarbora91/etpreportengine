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

    public SqlServerDailyWorkflowService(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
        workflow = new(connectionString);
        completion = new(connectionString);
        packs = new(connectionString);
    }

    public async Task<DailyWorkflowState> LoadAsync(
        DailyWorkflowScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
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
        var scope = new ReportingQueryScope(search.PeriodStart, search.PeriodEnd, search.StoreCodes);
        var rows = await completion.LoadStaffTargetsAsync(scope, cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }

    public Task SaveManualInputAsync(
        SaveDailyManualInput command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Scope);
        return workflow.SaveManualInputAsync(
            command.Scope.StoreCode,
            command.Scope.BusinessDate,
            command.FieldCode,
            command.NumericValue,
            command.TextValue,
            command.User,
            command.Reason,
            cancellationToken);
    }

    public Task SaveStockCountAsync(
        SaveDailyStockCount command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Scope);
        return completion.SaveManualStockCountAsync(
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
            cancellationToken);
    }

    public Task SaveStaffTargetAsync(
        SaveDailyStaffTarget command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return completion.SaveStaffTargetAsync(
            command.StoreCode,
            command.CroNumber,
            command.PeriodStart,
            command.PeriodEnd,
            command.TargetSales,
            command.User,
            command.Reason,
            cancellationToken);
    }

    public Task FinaliseAsync(
        FinaliseDailyWorkflow command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Scope);
        return workflow.FinaliseAsync(
            command.Scope.StoreCode,
            command.Scope.BusinessDate,
            command.User,
            command.HasBlockingReconciliationExceptions,
            cancellationToken);
    }

    public Task ReopenAsync(
        ReopenDailyWorkflow command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Scope);
        return workflow.ReopenAsync(
            command.Scope.StoreCode,
            command.Scope.BusinessDate,
            command.User,
            command.Reason,
            command.AdministratorApproved,
            cancellationToken);
    }

    public async Task<DailyPackGeneration<ReportPackDocument>> GenerateAsync(
        DailyWorkflowScope scope,
        string? generatedBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return Map(await packs.GenerateAsync(
            scope.StoreCode,
            scope.BusinessDate,
            generatedBy,
            cancellationToken).ConfigureAwait(false));
    }

    public Task<ReportPackDocument> GenerateCombinedAsync(
        DateOnly businessDate,
        string? generatedBy = null,
        CancellationToken cancellationToken = default) =>
        packs.GenerateCombinedAsync(businessDate, generatedBy, cancellationToken);

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
