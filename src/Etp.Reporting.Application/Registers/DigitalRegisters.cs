namespace Etp.Reporting.Application.Registers;

public sealed record DigitalRegisterEntry(
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

public sealed record DigitalRegisterEntryDraft(
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
    string? Remarks);

public interface IDigitalRegisterService
{
    Task<IReadOnlyList<DigitalRegisterEntry>> LoadAsync(
        string? search = null,
        int limit = 500,
        CancellationToken cancellationToken = default);

    Task<long> SaveAsync(
        DigitalRegisterEntryDraft entry,
        string reason,
        CancellationToken cancellationToken = default);
}
