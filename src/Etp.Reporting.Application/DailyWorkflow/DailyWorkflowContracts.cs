namespace Etp.Reporting.Application.DailyWorkflow;

public enum DailyWorkflowStatus
{
    NotReady,
    Partial,
    ReadyWithWarnings,
    Reconciled,
    Locked
}

public enum DailyControlStatus
{
    NotRun,
    Passed,
    Failed,
    Blocked
}

public sealed record DailyWorkflowScope(string StoreCode, DateOnly BusinessDate);

public sealed record DailyManualInput(
    string FieldCode,
    string DisplayName,
    string ValueKind,
    decimal? NumericValue,
    string? TextValue,
    bool IsRequired,
    DateTime? ModifiedUtc,
    string? ModifiedBy)
{
    public bool IsPresent => NumericValue is not null || TextValue is not null;
}

public sealed record DailyWorkflowState(
    string StoreCode,
    DateOnly BusinessDate,
    DailyWorkflowStatus Status,
    IReadOnlyList<string> ImportedReports,
    IReadOnlyList<string> MissingReports,
    IReadOnlyList<DailyManualInput> ManualInputs,
    IReadOnlyList<string> MissingRequiredInputs,
    bool CanFinalise,
    string StatusMessage);

public sealed record DailyManualStockCount(
    string StoreCode,
    DateOnly BusinessDate,
    string InventoryGroupCode,
    decimal? DisplayQuantity,
    decimal? BackstockQuantity,
    decimal? DefectiveQuantity,
    decimal? YLocationQuantity,
    decimal? CountedPhysicalQuantity,
    string? Remarks,
    DateTime ModifiedUtc,
    string ModifiedBy)
{
    public decimal? ComponentTotal =>
        new[] { DisplayQuantity, BackstockQuantity, DefectiveQuantity, YLocationQuantity }.All(value => value is null)
            ? null
            : (DisplayQuantity ?? 0m) + (BackstockQuantity ?? 0m) + (DefectiveQuantity ?? 0m) + (YLocationQuantity ?? 0m);

    public decimal? CompositionVariance =>
        CountedPhysicalQuantity is null || ComponentTotal is null
            ? null
            : CountedPhysicalQuantity - ComponentTotal;
}

public sealed record DailyStaffSalesTarget(
    string StoreCode,
    string CroNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TargetSales,
    DateTime ModifiedUtc,
    string ModifiedBy);

public sealed record DailyStaffTargetSearch(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<string>? StoreCodes = null);

public sealed record SaveDailyManualInput(
    DailyWorkflowScope Scope,
    string FieldCode,
    decimal? NumericValue,
    string? TextValue,
    string User,
    string Reason);

public sealed record SaveDailyStockCount(
    DailyWorkflowScope Scope,
    string InventoryGroupCode,
    decimal? DisplayQuantity,
    decimal? BackstockQuantity,
    decimal? DefectiveQuantity,
    decimal? YLocationQuantity,
    decimal? CountedPhysicalQuantity,
    string? Remarks,
    string User,
    string Reason);

public sealed record SaveDailyStaffTarget(
    string StoreCode,
    string CroNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TargetSales,
    string User,
    string Reason);

public sealed record FinaliseDailyWorkflow(
    DailyWorkflowScope Scope,
    string User,
    bool HasBlockingReconciliationExceptions);

public sealed record ReopenDailyWorkflow(
    DailyWorkflowScope Scope,
    string User,
    string Reason,
    bool AdministratorApproved);

public sealed record DailyPackSection(
    string Report,
    DailyControlStatus Status,
    decimal? ControlTotal,
    decimal? Variance,
    string Message);

public sealed record DailyPackGeneration<TDocument>(
    string StoreCode,
    DateOnly BusinessDate,
    DailyControlStatus Status,
    IReadOnlyList<DailyPackSection> Sections,
    string Message,
    DateTimeOffset GeneratedAtUtc,
    TDocument Document,
    int GenerationNumber,
    string ContentSha256)
    where TDocument : notnull;

public interface IDailyWorkflowQuery
{
    Task<DailyWorkflowState> LoadAsync(
        DailyWorkflowScope scope,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyManualStockCount>> LoadStockCountsAsync(
        DailyWorkflowScope scope,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyStaffSalesTarget>> LoadStaffTargetsAsync(
        DailyStaffTargetSearch search,
        CancellationToken cancellationToken = default);
}

public interface IDailyWorkflowCommands
{
    Task SaveManualInputAsync(
        SaveDailyManualInput command,
        CancellationToken cancellationToken = default);

    Task SaveStockCountAsync(
        SaveDailyStockCount command,
        CancellationToken cancellationToken = default);

    Task SaveStaffTargetAsync(
        SaveDailyStaffTarget command,
        CancellationToken cancellationToken = default);

    Task FinaliseAsync(
        FinaliseDailyWorkflow command,
        CancellationToken cancellationToken = default);

    Task ReopenAsync(
        ReopenDailyWorkflow command,
        CancellationToken cancellationToken = default);
}

public interface IDailyReportPackGenerator<TDocument> where TDocument : notnull
{
    Task<DailyPackGeneration<TDocument>> GenerateAsync(
        DailyWorkflowScope scope,
        string? generatedBy = null,
        CancellationToken cancellationToken = default);

    Task<TDocument> GenerateCombinedAsync(
        DateOnly businessDate,
        string? generatedBy = null,
        CancellationToken cancellationToken = default);
}
