using App = Etp.Reporting.Application.DatabaseLifecycle;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// SQL Server adapter for database bootstrap and health, operational audit,
/// and current-import lifecycle queries used by the desktop shell.
/// </summary>
public sealed class SqlServerDatabaseLifecycleService : App.IDatabaseLifecycleService
{
    private readonly IDatabaseLifecycleSqlGateway gateway;

    public SqlServerDatabaseLifecycleService(string connectionString)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(
            connectionString,
            nameof(connectionString));
        gateway = new DatabaseLifecycleSqlGateway(validated);
    }

    internal SqlServerDatabaseLifecycleService(IDatabaseLifecycleSqlGateway gateway)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public async Task<App.DatabaseConnectionHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var health = await gateway.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        return new(Map(health.Status), health.Message, health.ServerVersion, health.Elapsed);
    }

    public async Task<App.DatabaseBootstrapOutcome> BootstrapAsync(
        App.BootstrapDatabase command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.MigrationDirectory))
            throw new ArgumentException("The migration directory is required.", nameof(command));

        var result = await gateway.BootstrapAsync(
            Path.GetFullPath(command.MigrationDirectory),
            cancellationToken).ConfigureAwait(false);
        return new(result.DatabaseCreated, result.AppliedMigrations);
    }

    public Task RecordAuditAsync(
        App.RecordOperationalAudit command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return gateway.RecordAuditAsync(
            command.EventType,
            command.Outcome,
            command.SafeDetail,
            command.ActorName,
            cancellationToken);
    }

    private static App.DatabaseConnectionStatus Map(DatabaseHealthStatus status) => status switch
    {
        DatabaseHealthStatus.Healthy => App.DatabaseConnectionStatus.Healthy,
        DatabaseHealthStatus.Unreachable => App.DatabaseConnectionStatus.Unreachable,
        DatabaseHealthStatus.InvalidConfiguration => App.DatabaseConnectionStatus.InvalidConfiguration,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown database health status.")
    };
}

internal interface IDatabaseLifecycleSqlGateway
{
    Task<DatabaseHealth> CheckHealthAsync(CancellationToken cancellationToken);

    Task<DatabaseBootstrapResult> BootstrapAsync(
        string migrationDirectory,
        CancellationToken cancellationToken);

    Task RecordAuditAsync(
        string eventType,
        string outcome,
        string? safeDetail,
        string? actorName,
        CancellationToken cancellationToken);
}

internal sealed class DatabaseLifecycleSqlGateway(string connectionString) : IDatabaseLifecycleSqlGateway
{
    public Task<DatabaseHealth> CheckHealthAsync(CancellationToken cancellationToken) =>
        new SqlServerHealthCheck(connectionString).CheckAsync(cancellationToken);

    public Task<DatabaseBootstrapResult> BootstrapAsync(
        string migrationDirectory,
        CancellationToken cancellationToken) =>
        new SqlServerDatabaseBootstrapper(
            connectionString,
            new DirectoryMigrationSource(migrationDirectory)).BootstrapAsync(cancellationToken);

    public Task RecordAuditAsync(
        string eventType,
        string outcome,
        string? safeDetail,
        string? actorName,
        CancellationToken cancellationToken) =>
        new OperationalAuditRepository(connectionString).RecordAsync(
            eventType,
            outcome,
            safeDetail,
            actorName,
            cancellationToken);
}
