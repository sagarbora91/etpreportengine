using Etp.Reporting.Application.Access;

namespace Etp.Reporting.Application.OperationsAdministration;

public sealed record OperationsPeriod(DateOnly From, DateOnly To, int AutomationRunLimit = 100);

public sealed record WatchFolderConfiguration(
    string InboundPath,
    string ProcessedPath,
    string FailedPath,
    string ReportOutputPath,
    int PollMinutes,
    bool IsEnabled,
    DateTime ModifiedUtc,
    string ModifiedBy);

public sealed record ManagementTrendPoint(
    DateOnly BusinessDate,
    string StoreCode,
    decimal NetSales,
    decimal Units,
    int Invoices,
    decimal TenderVariance,
    int UnmatchedEnrichmentRows);

public sealed record DataQualityFinding(
    string Severity,
    string Area,
    string Code,
    long Count,
    DateTime? LatestUtc,
    string Message);

public sealed record DataQualityIssue(
    long Id,
    string Category,
    string Severity,
    string? StoreCode,
    DateOnly? BusinessDate,
    string TechnicalControlStatus,
    string WorkflowStatus,
    string SafeSummary,
    string? AssignedTo,
    DateTime ModifiedUtc,
    string? ResolutionReason);

public sealed record ReportSchedule(
    int Id,
    string Name,
    TimeOnly LocalRunTime,
    bool IsEnabled,
    bool ExportExcel,
    bool ExportPdf,
    DateOnly? LastBusinessDate,
    DateTime? LastRunUtc,
    string? LastStatus,
    string? LastMessage);

public sealed record AutomationRun(
    long Id,
    string RunType,
    string? SourceFileName,
    string? StoreCode,
    DateOnly? BusinessDate,
    string Outcome,
    string SafeMessage,
    DateTime StartedUtc,
    DateTime CompletedUtc,
    string RunBy);

public sealed record OperationsDashboard(
    WatchFolderConfiguration WatchFolders,
    IReadOnlyList<ManagementTrendPoint> Trend,
    IReadOnlyList<DataQualityFinding> Quality,
    IReadOnlyList<DataQualityIssue> Issues,
    IReadOnlyList<ReportSchedule> Schedules,
    IReadOnlyList<AutomationRun> AutomationRuns);

public sealed record AutomationExecution(
    int SourcesProcessed,
    int SourcesFailed,
    int DuplicateWorkbooks,
    int PacksGenerated,
    string Message);

public sealed record SaveWatchFolderConfiguration(
    string InboundPath,
    string ProcessedPath,
    string FailedPath,
    string ReportOutputPath,
    int PollMinutes,
    bool IsEnabled,
    string Reason);

public sealed record SaveReportSchedule(
    int Id,
    TimeOnly LocalRunTime,
    bool IsEnabled,
    bool ExportExcel,
    bool ExportPdf,
    string Reason);

public sealed record UpdateDataQualityIssue(long IssueId, string Status, string Reason);

public sealed record SubmitAdjustment(
    string StoreCode,
    DateOnly BusinessDate,
    string AdjustmentType,
    decimal Amount,
    string Reason,
    long? SourceDocumentId = null);

public sealed record ApprovalRequest(
    long Id,
    string ApprovalType,
    string SubjectType,
    string SubjectId,
    string? StoreCode,
    DateOnly? BusinessDate,
    string RequestedBy,
    DateTime RequestedUtc,
    string Status,
    string? DecidedBy,
    DateTime? DecidedUtc,
    string? DecisionReason);

public sealed record DecideApproval(long ApprovalId, bool Approve, string Reason);

public interface IOperationsAdministrationService
{
    Task<OperationsDashboard> LoadDashboardAsync(OperationsPeriod period, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApprovalRequest>> LoadApprovalsAsync(string? status = "PENDING", CancellationToken cancellationToken = default);
    Task<AutomationExecution> RunAutomationOnceAsync(CancellationToken cancellationToken = default);
    Task SaveWatchFoldersAsync(SaveWatchFolderConfiguration command, CancellationToken cancellationToken = default);
    Task SaveScheduleAsync(SaveReportSchedule command, CancellationToken cancellationToken = default);
    Task UpdateIssueAsync(UpdateDataQualityIssue command, CancellationToken cancellationToken = default);
    Task<long> SubmitAdjustmentAsync(SubmitAdjustment command, CancellationToken cancellationToken = default);
    Task DecideApprovalAsync(DecideApproval command, CancellationToken cancellationToken = default);
}

public sealed record ControlledMaster(
    string MasterType,
    string Code,
    string DisplayName,
    string ApprovalStatus,
    bool IsActive,
    DateTime? ModifiedUtc,
    string? ModifiedBy);

public sealed record ApplicationUser(
    int Id,
    string WindowsIdentity,
    string DisplayName,
    AccessRole Role,
    bool IsActive,
    DateTime ModifiedUtc,
    string ModifiedBy);

public sealed record KpiDefinition(
    string Code,
    string BusinessName,
    string Definition,
    string Formula,
    string DataSource,
    DateOnly EffectiveDate,
    int Version,
    string ApprovalStatus,
    string? ApprovedBy,
    bool IsActive);

public sealed record ProductHealth(string Component, string Status, string Guidance);

public sealed record ProductConfiguration(
    string DocumentRepositoryPath,
    string ShareFolderPath,
    string? OcrHelperPath,
    string? OcrModelPath,
    string? SmtpHost,
    int? SmtpPort,
    bool SmtpUseTls,
    string? SmtpFromAddress,
    int MaximumAttachmentMb,
    DateTime ModifiedUtc,
    string ModifiedBy);

public sealed record AdministrationDashboard(
    IReadOnlyList<ControlledMaster> Masters,
    IReadOnlyList<ApplicationUser> Users,
    IReadOnlyList<KpiDefinition> Kpis,
    IReadOnlyList<ProductHealth> ProductHealth,
    ProductConfiguration ProductConfiguration);

public sealed record SaveControlledMaster(
    string MasterType,
    string Code,
    string DisplayName,
    string ApprovalStatus,
    bool IsActive,
    string Reason);

public sealed record SaveApplicationUser(
    string WindowsIdentity,
    string DisplayName,
    AccessRole Role,
    bool IsActive,
    string Reason);

public sealed record SaveProductConfiguration(
    string DocumentRepositoryPath,
    string ShareFolderPath,
    string? OcrHelperPath,
    string? OcrModelPath,
    string? SmtpHost,
    int? SmtpPort,
    bool SmtpUseTls,
    string? SmtpFromAddress,
    int MaximumAttachmentMb,
    string Reason);

public interface IAdministrationService
{
    Task<AdministrationDashboard> LoadAsync(string masterType, CancellationToken cancellationToken = default);
    Task SaveMasterAsync(SaveControlledMaster command, CancellationToken cancellationToken = default);
    Task SaveUserAsync(SaveApplicationUser command, CancellationToken cancellationToken = default);
    Task SaveProductConfigurationAsync(SaveProductConfiguration command, CancellationToken cancellationToken = default);
}
