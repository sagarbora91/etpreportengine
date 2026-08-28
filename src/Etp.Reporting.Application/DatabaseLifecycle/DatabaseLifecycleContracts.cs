namespace Etp.Reporting.Application.DatabaseLifecycle;

public enum DatabaseConnectionStatus
{
    Healthy,
    Unreachable,
    InvalidConfiguration
}

public sealed record DatabaseConnectionHealth(
    DatabaseConnectionStatus Status,
    string Message,
    string? ServerVersion = null,
    TimeSpan? Elapsed = null);

public sealed record BootstrapDatabase(
    string MigrationDirectory);

public sealed record DatabaseBootstrapOutcome(
    bool DatabaseCreated,
    IReadOnlyList<string> AppliedMigrations);

public sealed record RecordOperationalAudit(
    string EventType,
    string Outcome,
    string? SafeDetail = null,
    string? ActorName = null);

public interface IDatabaseLifecycleService
{
    Task<DatabaseConnectionHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default);

    Task<DatabaseBootstrapOutcome> BootstrapAsync(
        BootstrapDatabase command,
        CancellationToken cancellationToken = default);

    Task RecordAuditAsync(
        RecordOperationalAudit command,
        CancellationToken cancellationToken = default);
}
