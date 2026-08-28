namespace Etp.Reporting.Application.Sharing;

public sealed record SharingContact(
    int Id,
    string DisplayName,
    string? ContactRole,
    string? EmailAddress,
    string? PhoneE164,
    string? DefaultSubscriptions,
    bool IsActive,
    string ModifiedBy,
    DateTime ModifiedUtc);

public sealed record SharingContactDraft(
    int Id,
    string DisplayName,
    string? ContactRole,
    string? EmailAddress,
    string? PhoneE164,
    string? DefaultSubscriptions,
    bool IsActive);

public interface ISharingContactsService
{
    Task<IReadOnlyList<SharingContact>> LoadAsync(CancellationToken cancellationToken = default);

    Task<int> SaveAsync(
        SharingContactDraft contact,
        string reason,
        CancellationToken cancellationToken = default);
}
