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
    string SafeMessage);

public interface IReportDistributionService<TDocument>
{
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
