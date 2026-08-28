using Etp.Reporting.Application.Access;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Adapts the existing Windows-account access repository to the application contract.
/// </summary>
public sealed class SqlServerAccessSessionQuery : IAccessSessionQuery
{
    private readonly Phase2OperationsRepository repository;

    public SqlServerAccessSessionQuery(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));

        var settings = new SqlConnectionStringBuilder(connectionString);
        if (!settings.IntegratedSecurity)
            throw new ArgumentException(
                "Access-session queries require Windows-integrated SQL Server security.",
                nameof(connectionString));

        repository = new Phase2OperationsRepository(connectionString);
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
