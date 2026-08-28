using Etp.Reporting.Application.Access;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Adapts the existing Windows-account access repository to the application contract.
/// </summary>
public sealed class SqlServerAccessSessionQuery : IAccessSessionQuery
{
    private readonly Phase2OperationsRepository repository;

    public SqlServerAccessSessionQuery(string connectionString)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString));
        repository = new Phase2OperationsRepository(validated);
    }

    public async Task<AccessSession> LoadCurrentAsync(CancellationToken cancellationToken = default) =>
        Map(await repository.LoadCurrentAccessAsync(cancellationToken).ConfigureAwait(false));

    public static AccessSession Map(ApplicationAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        return new(
            access.WindowsIdentity,
            access.DisplayName,
            MapRole(access.Role),
            access.IsActive);
    }

    public static AccessRole MapRole(ApplicationRole role) => role switch
    {
        ApplicationRole.Viewer => AccessRole.Viewer,
        ApplicationRole.StoreManager => AccessRole.StoreManager,
        ApplicationRole.Owner => AccessRole.Owner,
        _ => AccessRole.None
    };
}
