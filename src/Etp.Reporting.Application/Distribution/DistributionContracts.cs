namespace Etp.Reporting.Application.Distribution;

public sealed record InvestigationHit(
    string ResultType,
    string PrimaryReference,
    string Scope,
    DateOnly? BusinessDate,
    string Summary,
    string NavigationHint);

public interface IInvestigationQuery
{
    Task<IReadOnlyList<InvestigationHit>> SearchAsync(
        string term,
        int limit = 200,
        CancellationToken cancellationToken = default);
}

public sealed record CreateReportPackage<TDocument>(
    long GenerationId,
    string OutputPath,
    TDocument Document,
    int GenerationNumber,
    string StoreCode,
    bool IsFinal,
    string CreatedBy);

public sealed record ReportPackageFile(string RelativePath, long SizeBytes, string Sha256);

public sealed record ReportPackageReceipt(
    string Path,
    string Sha256,
    string ManifestJson,
    IReadOnlyList<ReportPackageFile> Files);

public sealed record EmailAttachmentPolicy(
    string ShareFolderPath,
    int MaximumAttachmentMb);

public sealed record RecordDistributionAttempt(
    long GenerationId,
    long? PackageId,
    string Channel,
    string? DestinationSafe,
    string AttachmentPath,
    string Outcome,
    string SafeMessage,
    Guid? AttemptKey = null);

public sealed record ShareDeliveryHistory(long Id, string Channel, string? DestinationSafe, string AttachmentName,
    string Outcome, string Message, string InitiatedBy, DateTime InitiatedUtc, Guid? AttemptKey)
{
    public string OutcomeLabel => Outcome switch
    {
        "SMTP_ACCEPTED" => "Accepted by mail server",
        "HANDOFF_READY" => "Ready in WhatsApp - send manually",
        "UNKNOWN" => "Unconfirmed - check before retrying",
        "INITIATED" => "Started - confirmation pending",
        "FAILED" => "Failed",
        "CANCELLED" => "Cancelled",
        "SUCCEEDED" => "Completed (legacy record)",
        _ => Outcome
    };
}
public sealed record SendReportEmail(long GenerationId, string AttachmentPath, string To, string? Cc = null);
public sealed record EmailSendResult(string Outcome, string Message);

public interface IReportDistributionService<TDocument>
{
    Task SaveSmtpCredentialsAsync(string userName, string password, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("SMTP credentials are unavailable.");
    Task<string> PreparePdfAsync(long generationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("PDF sharing is unavailable.");
    Task<EmailSendResult> SendEmailAsync(SendReportEmail command, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Email sending is unavailable.");
    Task<EmailSendResult> TestEmailAsync(string recipient, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Email sending is unavailable.");
    Task<IReadOnlyList<ShareDeliveryHistory>> LoadHistoryAsync(long generationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ShareDeliveryHistory>>([]);

    Task<ReportPackageReceipt> CreatePackageAsync(
        CreateReportPackage<TDocument> command,
        CancellationToken cancellationToken = default);

    Task<EmailAttachmentPolicy> ValidateEmailAttachmentAsync(
        string attachmentPath,
        CancellationToken cancellationToken = default);

    Task RecordAttemptAsync(
        RecordDistributionAttempt command,
        CancellationToken cancellationToken = default);
}
