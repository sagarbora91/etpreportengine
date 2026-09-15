using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class MigrationTests
{
    [Fact]
    public void Checksum_is_deterministic_and_sensitive_to_content()
    {
        Assert.Equal(MigrationChecksum.Compute("SELECT 1"), MigrationChecksum.Compute("SELECT 1"));
        Assert.NotEqual(MigrationChecksum.Compute("SELECT 1"), MigrationChecksum.Compute("SELECT 2"));
        Assert.Equal(64, MigrationChecksum.Compute("SELECT 1").Length);
    }

    [Fact]
    public void Planner_returns_only_pending_scripts_in_id_order()
    {
        var one = Script("0001", "a");
        var two = Script("0002", "b");
        var plan = MigrationPlanner.Plan([two, one], [new(one.Id, one.Checksum, DateTimeOffset.UtcNow)]);
        Assert.Equal(["0002"], plan.Select(x => x.Id));
    }

    [Fact]
    public void Planner_rejects_changed_applied_script()
    {
        var migration = Script("0001", "new");
        var error = Assert.Throws<MigrationIntegrityException>(() => MigrationPlanner.Plan([migration], [new("0001", MigrationChecksum.Compute("old"), DateTimeOffset.UtcNow)]));
        Assert.Contains("Checksum mismatch", error.Message);
    }

    [Fact]
    public void Planner_rejects_missing_applied_script()
    {
        Assert.Throws<MigrationIntegrityException>(() => MigrationPlanner.Plan([], [new("0001", "abc", DateTimeOffset.UtcNow)]));
    }

    [Fact]
    public async Task Runner_applies_pending_migrations_once()
    {
        var scripts = new[] { Script("0001", "one"), Script("0002", "two") };
        var store = new MemoryStore();
        var runner = new MigrationRunner(new MemorySource(scripts), store);
        Assert.Equal(["0001", "0002"], await runner.RunAsync());
        Assert.Empty(await runner.RunAsync());
        Assert.Equal(2, store.Applied.Count);
    }

    [Fact]
    public async Task Runner_holds_exclusive_lock_across_discovery_planning_and_all_commits()
    {
        var events = new List<string>();
        var scripts = new[] { Script("0001", "one"), Script("0002", "two") };
        var store = new RecordingStore(events);
        var runner = new MigrationRunner(new RecordingSource(scripts, events), store);

        Assert.Equal(["0001", "0002"], await runner.RunAsync());

        Assert.Equal([
            "lock-acquired",
            "discover",
            "get-applied",
            "apply-0001",
            "apply-0002",
            "lock-released",
        ], events);
    }

    [Fact]
    public async Task Runner_releases_migration_lock_when_a_commit_fails()
    {
        var events = new List<string>();
        var store = new RecordingStore(events) { FailOnApply = true };
        var runner = new MigrationRunner(new RecordingSource([Script("0001", "one")], events), store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());

        Assert.Equal("lock-released", events[^1]);
        Assert.Equal(["lock-acquired", "discover", "get-applied", "apply-0001", "lock-released"], events);
    }

    [Fact]
    public async Task Runner_reports_explicit_lock_release_failure_after_successful_migrations()
    {
        var events = new List<string>();
        var store = new RecordingStore(events) { FailOnRelease = true };
        var runner = new MigrationRunner(new RecordingSource([Script("0001", "one")], events), store);

        var error = await Assert.ThrowsAsync<MigrationIntegrityException>(() => runner.RunAsync());

        Assert.Contains("Synthetic lock release failure", error.Message, StringComparison.Ordinal);
        Assert.Equal(["lock-acquired", "discover", "get-applied", "apply-0001", "lock-released"], events);
    }

    [Fact]
    public async Task Runner_aggregates_migration_and_lock_release_failures_without_masking_either()
    {
        var events = new List<string>();
        var store = new RecordingStore(events) { FailOnApply = true, FailOnRelease = true };
        var runner = new MigrationRunner(new RecordingSource([Script("0001", "one")], events), store);

        var error = await Assert.ThrowsAsync<AggregateException>(() => runner.RunAsync());

        Assert.Collection(error.InnerExceptions,
            migration => Assert.Contains("Synthetic migration failure", migration.Message, StringComparison.Ordinal),
            release => Assert.Contains("Synthetic lock release failure", release.Message, StringComparison.Ordinal));
        Assert.Equal("lock-released", events[^1]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(-3)]
    [InlineData(-999)]
    public void Sql_store_fails_closed_when_application_lock_is_not_acquired(int result)
    {
        var error = Assert.Throws<MigrationIntegrityException>(() => SqlServerMigrationStore.EnsureMigrationLockAcquired(result));

        Assert.Contains("migration was not started", error.Message, StringComparison.Ordinal);
        Assert.Contains(result.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Sql_store_accepts_only_successful_application_lock_results(int result)
    {
        SqlServerMigrationStore.EnsureMigrationLockAcquired(result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-999)]
    public void Sql_store_fails_closed_when_explicit_lock_release_is_not_confirmed(int result)
    {
        var error = Assert.Throws<MigrationIntegrityException>(() => SqlServerMigrationStore.EnsureMigrationLockReleased(result));

        Assert.Contains("connection pool was invalidated", error.Message, StringComparison.Ordinal);
        Assert.Contains(result.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Sql_store_accepts_nonnegative_explicit_lock_release_results(int result)
    {
        SqlServerMigrationStore.EnsureMigrationLockReleased(result);
    }

    [Fact]
    public async Task Empty_connection_string_is_invalid_without_network_access()
    {
        var result = await new SqlServerHealthCheck(" ").CheckAsync();
        Assert.Equal(DatabaseHealthStatus.InvalidConfiguration, result.Status);
    }

    [Fact]
    public async Task Malformed_connection_string_does_not_expose_provider_exception_text()
    {
        var result = await new SqlServerHealthCheck("NotAKeyword=secret-value").CheckAsync();

        Assert.Equal(DatabaseHealthStatus.InvalidConfiguration, result.Status);
        Assert.Equal("The SQL Server connection settings are invalid.", result.Message);
        Assert.DoesNotContain("NotAKeyword", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-value", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_rejects_unsafe_or_missing_database_names_without_network_access()
    {
        Assert.Throws<ArgumentException>(() => SqlServerDatabaseBootstrapper.ValidateDatabaseName("reporting]; DROP DATABASE master;--"));
        Assert.Throws<ArgumentException>(() => SqlServerDatabaseBootstrapper.ValidateDatabaseName(" "));
        Assert.Equal("Etp_Reporting-01", SqlServerDatabaseBootstrapper.ValidateDatabaseName("Etp_Reporting-01"));
    }

    [Fact]
    public void Automation_paths_require_distinct_local_non_root_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpAutomationPolicy");
        var settings = AutomationPathPolicy.Validate(Path.Combine(root, "Inbound"), Path.Combine(root, "Processed"),
            Path.Combine(root, "Failed"), Path.Combine(root, "Reports"));
        Assert.EndsWith(Path.Combine("EtpAutomationPolicy", "Inbound"), settings.InboundPath, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => AutomationPathPolicy.Validate(root, Path.Combine(root, "Processed"), Path.Combine(root, "Failed"), Path.Combine(root, "Processed")));
        Assert.Throws<ArgumentException>(() => AutomationPathPolicy.Validate(root, Path.Combine(root, "inside"), Path.Combine(Path.GetTempPath(), "failed"), Path.Combine(Path.GetTempPath(), "reports")));
        Assert.Throws<ArgumentException>(() => AutomationPathPolicy.Validate(Path.GetPathRoot(root)!, Path.Combine(root, "Processed"), Path.Combine(root, "Failed"), Path.Combine(root, "Reports")));
    }

    [Fact]
    public void Import_package_keeps_movement_and_snapshot_facts_separate()
    {
        var batch = new ImportBatchRegistration(Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(batch.BatchId, Etp.Reporting.Import.Profiles.RetailSalesProfiles.R025.Identity, "sample.xlsx", new string('a', 64), 1);
        var package = new ImportPersistencePackage(batch, file, [], [], [], []);
        Assert.Empty(package.StockMovements);
        Assert.Empty(package.StockSnapshots);
        Assert.Equal(batch.BatchId, package.File.BatchId);
    }

    [Fact]
    public void Persistence_validation_rejects_cross_batch_files_before_database_access()
    {
        var batch = new ImportBatchRegistration(Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(Guid.NewGuid(), Etp.Reporting.Import.Profiles.RetailSalesProfiles.R025.Identity, "sample.xlsx", new string('a', 64), 1);
        var package = new ImportPersistencePackage(batch, file, [], [], [], []);
        Assert.Throws<ArgumentException>(() => PersistenceValidation.Validate(package));
    }

    [Fact]
    public void Persistence_validation_requires_complete_restatement_authority()
    {
        var batch = new ImportBatchRegistration(Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(batch.BatchId, Etp.Reporting.Import.Profiles.RetailSalesProfiles.R025.Identity, "replacement.xlsx", new string('a', 64), 1);
        var invalid = new ImportPersistencePackage(batch, file, [], [], [], [])
        {
            Restatement = new(0, "", "")
        };
        Assert.Throws<ArgumentException>(() => PersistenceValidation.Validate(invalid));

        var valid = invalid with { Restatement = new ImportRestatementRequest(12, "admin", "Corrected ETP export") };
        PersistenceValidation.Validate(valid);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "database", "migrations"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static MigrationScript Script(string id, string sql) => new(id, MigrationChecksum.Compute(sql), sql, $"{id}.sql");

    private static HashSet<string> OperationalAuditEventTypes(string sql)
    {
        const string constraintMarker = "CK_operational_audit_type CHECK";
        var constraintStart = sql.LastIndexOf(constraintMarker, StringComparison.Ordinal);
        Assert.True(constraintStart >= 0);
        var constraintEnd = sql.IndexOf("));", constraintStart, StringComparison.Ordinal);
        Assert.True(constraintEnd > constraintStart);
        var constraint = sql[constraintStart..constraintEnd];
        return System.Text.RegularExpressions.Regex.Matches(constraint, "'([^']+)'")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private sealed class MemorySource(IReadOnlyList<MigrationScript> scripts) : IMigrationSource
    {
        public Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(scripts);
    }
    private sealed class MemoryStore : IMigrationStore
    {
        public List<AppliedMigration> Applied { get; } = [];
        public Task<IAsyncDisposable> AcquireMigrationLockAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IAsyncDisposable>(NoOpAsyncDisposable.Instance);
        public Task<IReadOnlyList<AppliedMigration>> GetAppliedAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AppliedMigration>>(Applied);
        public Task ApplyAsync(MigrationScript migration, CancellationToken cancellationToken = default)
        {
            Applied.Add(new(migration.Id, migration.Checksum, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSource(IReadOnlyList<MigrationScript> scripts, List<string> events) : IMigrationSource
    {
        public Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            events.Add("discover");
            return Task.FromResult(scripts);
        }
    }

    private sealed class RecordingStore(List<string> events) : IMigrationStore
    {
        public bool FailOnApply { get; init; }
        public bool FailOnRelease { get; init; }

        public Task<IAsyncDisposable> AcquireMigrationLockAsync(CancellationToken cancellationToken = default)
        {
            events.Add("lock-acquired");
            return Task.FromResult<IAsyncDisposable>(new RecordingLock(events, FailOnRelease));
        }

        public Task<IReadOnlyList<AppliedMigration>> GetAppliedAsync(CancellationToken cancellationToken = default)
        {
            events.Add("get-applied");
            return Task.FromResult<IReadOnlyList<AppliedMigration>>([]);
        }

        public Task ApplyAsync(MigrationScript migration, CancellationToken cancellationToken = default)
        {
            events.Add($"apply-{migration.Id}");
            return FailOnApply
                ? Task.FromException(new InvalidOperationException("Synthetic migration failure."))
                : Task.CompletedTask;
        }
    }

    private sealed class RecordingLock(List<string> events, bool failOnRelease) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            events.Add("lock-released");
            return failOnRelease
                ? ValueTask.FromException(new MigrationIntegrityException("Synthetic lock release failure."))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static NoOpAsyncDisposable Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
