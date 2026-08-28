using App = Etp.Reporting.Application.DatabaseLifecycle;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class DatabaseLifecycleServiceBoundaryTests
{
    [Fact]
    public void Production_adapter_requires_windows_integrated_security()
    {
        _ = new SqlServerDatabaseLifecycleService(
            @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True");

        Assert.Throws<ArgumentException>(() => new SqlServerDatabaseLifecycleService(
            @"Server=.\SQLEXPRESS;Database=EtpReporting;User ID=sa;Password=secret"));
    }

    [Theory]
    [InlineData(DatabaseHealthStatus.Healthy, App.DatabaseConnectionStatus.Healthy)]
    [InlineData(DatabaseHealthStatus.Unreachable, App.DatabaseConnectionStatus.Unreachable)]
    [InlineData(DatabaseHealthStatus.InvalidConfiguration, App.DatabaseConnectionStatus.InvalidConfiguration)]
    public async Task Health_maps_the_existing_sql_health_result(
        DatabaseHealthStatus source,
        App.DatabaseConnectionStatus expected)
    {
        var gateway = new FakeGateway
        {
            Health = new(source, "health detail", "16.0", TimeSpan.FromMilliseconds(25))
        };

        var result = await new SqlServerDatabaseLifecycleService(gateway).CheckHealthAsync();

        Assert.Equal(expected, result.Status);
        Assert.Equal("health detail", result.Message);
        Assert.Equal("16.0", result.ServerVersion);
        Assert.Equal(TimeSpan.FromMilliseconds(25), result.Elapsed);
    }

    [Fact]
    public async Task Bootstrap_roots_the_migration_directory_and_maps_applied_migrations()
    {
        var gateway = new FakeGateway
        {
            Bootstrap = new(true, ["001_foundation.sql", "002_reporting.sql"])
        };
        var relative = Path.Combine("database", "migrations");

        var result = await new SqlServerDatabaseLifecycleService(gateway)
            .BootstrapAsync(new(relative));

        Assert.Equal(Path.GetFullPath(relative), gateway.MigrationDirectory);
        Assert.True(result.DatabaseCreated);
        Assert.Equal(["001_foundation.sql", "002_reporting.sql"], result.AppliedMigrations);
    }

    [Fact]
    public async Task Audit_command_preserves_safe_detail_actor_and_cancellation()
    {
        var gateway = new FakeGateway();
        using var cancellation = new CancellationTokenSource();
        var service = new SqlServerDatabaseLifecycleService(gateway);

        await service.RecordAuditAsync(
            new("ReportRun", "Succeeded", "Daily sales report", "STORE\\Owner"),
            cancellation.Token);

        Assert.Equal("ReportRun", gateway.EventType);
        Assert.Equal("Succeeded", gateway.Outcome);
        Assert.Equal("Daily sales report", gateway.SafeDetail);
        Assert.Equal("STORE\\Owner", gateway.ActorName);
        Assert.Equal(cancellation.Token, gateway.CancellationToken);
    }

    [Fact]
    public async Task Production_audit_adapter_keeps_aggregate_only_validation_before_sql_access()
    {
        var service = new SqlServerDatabaseLifecycleService(
            @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True");

        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordAuditAsync(
            new("ReportRun", "Succeeded", @"C:\restricted\report.xlsx")));
    }

    private sealed class FakeGateway : IDatabaseLifecycleSqlGateway
    {
        public DatabaseHealth Health { get; set; } =
            new(DatabaseHealthStatus.Healthy, "SQL Server connection succeeded.");
        public DatabaseBootstrapResult Bootstrap { get; set; } = new(false, []);
        public string? MigrationDirectory { get; private set; }
        public string? EventType { get; private set; }
        public string? Outcome { get; private set; }
        public string? SafeDetail { get; private set; }
        public string? ActorName { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<DatabaseHealth> CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Health);

        public Task<DatabaseBootstrapResult> BootstrapAsync(
            string migrationDirectory,
            CancellationToken cancellationToken)
        {
            MigrationDirectory = migrationDirectory;
            return Task.FromResult(Bootstrap);
        }

        public Task RecordAuditAsync(
            string eventType,
            string outcome,
            string? safeDetail,
            string? actorName,
            CancellationToken cancellationToken)
        {
            EventType = eventType;
            Outcome = outcome;
            SafeDetail = safeDetail;
            ActorName = actorName;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
