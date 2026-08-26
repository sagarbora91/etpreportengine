namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record ProductSettings(
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

public sealed record SourceDocumentRow(
    long Id,
    string OriginalFileName,
    string ManagedFilePath,
    string Sha256,
    long SizeBytes,
    string SourceType,
    string? DocumentType,
    string? StoreCode,
    DateOnly? BusinessDate,
    string LifecycleStatus,
    string? ReportCode,
    long? ImportFileId,
    long? ReportGenerationId,
    string ReceivedBy,
    DateTime ReceivedUtc,
    string? SafeMessage);

public sealed record DocumentExtractionResult(
    string Method,
    string Version,
    string Text,
    decimal? Confidence,
    int? PageNumber = null,
    string? BoundingBoxJson = null,
    string? StructuredFieldsJson = null,
    string ReviewStatus = "REVIEW_REQUIRED");

public sealed record DocumentExtractionRow(
    long Id,
    long SourceDocumentId,
    string Method,
    string Version,
    string Text,
    decimal? Confidence,
    string ReviewStatus,
    string? ReviewedBy,
    DateTime? ReviewedUtc,
    string? ReviewReason,
    DateTime CreatedUtc);

public sealed record SharingContactRow(
    int Id,
    string DisplayName,
    string? ContactRole,
    string? EmailAddress,
    string? PhoneE164,
    string? DefaultSubscriptions,
    bool IsActive,
    string ModifiedBy,
    DateTime ModifiedUtc);

public sealed record RegisterEntryRow(
    long Id,
    string RegisterType,
    long? SourceDocumentId,
    string StoreCode,
    DateOnly BusinessDate,
    string DocumentNumber,
    DateOnly? DocumentDate,
    string? Counterparty,
    decimal? Quantity,
    decimal? Amount,
    string? Reference,
    string? ReceivedBy,
    string VerificationStatus,
    string? Remarks,
    string ModifiedBy,
    DateTime ModifiedUtc);

public sealed record ImportConflictRow(long Id, string? StoreCode, DateOnly? BusinessDate, string? ReportCode,
    string BusinessIdentity, string Status, string SafeDifference, DateTime CreatedUtc);

public sealed record DataQualityIssueRow(long Id,string Category,string Severity,string? StoreCode,DateOnly? BusinessDate,
    string TechnicalControlStatus,string WorkflowStatus,string SafeSummary,string? AssignedTo,DateTime ModifiedUtc,string? ResolutionReason);

public sealed record ApprovalRequestRow(long Id, string ApprovalType, string SubjectType, string SubjectId,
    string? StoreCode, DateOnly? BusinessDate, string RequestedBy, DateTime RequestedUtc, string Status,
    string? DecidedBy, DateTime? DecidedUtc, string? DecisionReason);

public sealed record KpiCatalogueRow(string Code, string BusinessName, string Definition, string Formula,
    string DataSource, DateOnly EffectiveDate, int Version, string ApprovalStatus, string? ApprovedBy, bool IsActive);

public sealed record InvestigationResult(string ResultType, string PrimaryReference, string Scope,
    DateOnly? BusinessDate, string Summary, string NavigationHint);

public sealed record AccountingMapping(string BusinessEvent, string DebitLedger, string CreditLedger,
    string NarrationTemplate, string? CostCentre = null);

public sealed record AccountingBusinessEvent(string EventCode, decimal Amount, string SourceReference, string Description);

public sealed record AccountingEntryDraft(int LineNumber, string BusinessEvent, string LedgerName,
    decimal DebitAmount, decimal CreditAmount, string Narration, string? CostCentre, string SourceReference);

public sealed record AccountingBatchDraft(IReadOnlyList<AccountingEntryDraft> Entries, decimal DebitTotal,
    decimal CreditTotal, bool IsBalanced, IReadOnlyList<string> MissingMappings);

public sealed record AccountingBatchRow(long Id, string StoreCode, DateOnly BusinessDate, long ReportGenerationId,
    int AccountingGeneration, decimal DebitTotal, decimal CreditTotal, string Status, string? ApprovedBy,
    DateTime? ExportedUtc, string? TallyReference, DateTime CreatedUtc);

public sealed record ProductHealthItem(string Component, string Status, string Guidance);
