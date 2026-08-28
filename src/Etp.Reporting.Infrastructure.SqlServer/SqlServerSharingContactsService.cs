using Etp.Reporting.Application.Sharing;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class SqlServerSharingContactsService : ISharingContactsService
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<SharingContactRow>>> load;
    private readonly Func<SharingContactRow, string, CancellationToken, Task<int>> save;

    public SqlServerSharingContactsService(string connectionString)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(
            connectionString,
            nameof(connectionString));
        var repository = new ProductisationRepository(validated);
        load = repository.LoadSharingContactsAsync;
        save = repository.SaveSharingContactAsync;
    }

    internal SqlServerSharingContactsService(
        Func<CancellationToken, Task<IReadOnlyList<SharingContactRow>>> load,
        Func<SharingContactRow, string, CancellationToken, Task<int>> save)
    {
        this.load = load ?? throw new ArgumentNullException(nameof(load));
        this.save = save ?? throw new ArgumentNullException(nameof(save));
    }

    public async Task<IReadOnlyList<SharingContact>> LoadAsync(
        CancellationToken cancellationToken = default) =>
        (await load(cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();

    public Task<int> SaveAsync(
        SharingContactDraft contact,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return save(Map(contact), reason, cancellationToken);
    }

    internal static SharingContact Map(SharingContactRow row) =>
        new(
            row.Id,
            row.DisplayName,
            row.ContactRole,
            row.EmailAddress,
            row.PhoneE164,
            row.DefaultSubscriptions,
            row.IsActive,
            row.ModifiedBy,
            row.ModifiedUtc);

    internal static SharingContactRow Map(SharingContactDraft draft) =>
        new(
            draft.Id,
            draft.DisplayName,
            draft.ContactRole,
            draft.EmailAddress,
            draft.PhoneE164,
            draft.DefaultSubscriptions,
            draft.IsActive,
            string.Empty,
            DateTime.MinValue);
}
