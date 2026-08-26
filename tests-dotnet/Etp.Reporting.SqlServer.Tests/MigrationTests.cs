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
    public async Task Empty_connection_string_is_invalid_without_network_access()
    {
        var result = await new SqlServerHealthCheck(" ").CheckAsync();
        Assert.Equal(DatabaseHealthStatus.InvalidConfiguration, result.Status);
    }

    [Fact]
    public void Bootstrap_rejects_unsafe_or_missing_database_names_without_network_access()
    {
        Assert.Throws<ArgumentException>(() => SqlServerDatabaseBootstrapper.ValidateDatabaseName("reporting]; DROP DATABASE master;--"));
        Assert.Throws<ArgumentException>(() => SqlServerDatabaseBootstrapper.ValidateDatabaseName(" "));
        Assert.Equal("Etp_Reporting-01", SqlServerDatabaseBootstrapper.ValidateDatabaseName("Etp_Reporting-01"));
    }

    [Fact]
    public async Task Foundation_and_fact_migrations_have_required_control_boundaries()
    {
        var root = FindRepositoryRoot();
        var migrations = await new DirectoryMigrationSource(Path.Combine(root, "database", "migrations")).DiscoverAsync();
        Assert.Equal(["0001_foundation", "0002_reporting_facts", "0003_sales_dimensions"], migrations.Select(x => x.Id));
        var facts = Assert.Single(migrations, x => x.Id == "0002_reporting_facts").Sql;
        Assert.Contains("UX_import_files_source_sha256", facts, StringComparison.Ordinal);
        Assert.Contains("UQ_source_lineage", facts, StringComparison.Ordinal);
        Assert.Contains("CK_stock_movements_balance", facts, StringComparison.Ordinal);
        Assert.Contains("sales_invoices", facts, StringComparison.Ordinal);
        Assert.Contains("sales_lines", facts, StringComparison.Ordinal);
        Assert.Contains("sales_tenders", facts, StringComparison.Ordinal);
        Assert.Contains("sales_invoice_controls", facts, StringComparison.Ordinal);
        Assert.Contains("reporting_sales_tenders", facts, StringComparison.Ordinal);
        Assert.Contains("UNRESOLVED_PAYMENTTYPE25", facts, StringComparison.Ordinal);
        Assert.Contains("stock_movements", facts, StringComparison.Ordinal);
        Assert.Contains("stock_snapshots", facts, StringComparison.Ordinal);
        var dimensions = Assert.Single(migrations, x => x.Id == "0003_sales_dimensions").Sql;
        Assert.Contains("brand_segment", dimensions, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_package_keeps_movement_and_snapshot_facts_separate()
    {
        var batch = new ImportBatchRegistration(Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(batch.BatchId, null, "sample.xlsx", new string('a', 64), 1);
        var package = new ImportPersistencePackage(batch, file, [], [], [], []);
        Assert.Empty(package.StockMovements);
        Assert.Empty(package.StockSnapshots);
        Assert.Equal(batch.BatchId, package.File.BatchId);
    }

    [Fact]
    public void Persistence_validation_rejects_cross_batch_files_before_database_access()
    {
        var batch = new ImportBatchRegistration(Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(Guid.NewGuid(), null, "sample.xlsx", new string('a', 64), 1);
        var package = new ImportPersistencePackage(batch, file, [], [], [], []);
        Assert.Throws<ArgumentException>(() => PersistenceValidation.Validate(package));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "database", "migrations"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static MigrationScript Script(string id, string sql) => new(id, MigrationChecksum.Compute(sql), sql, $"{id}.sql");
    private sealed class MemorySource(IReadOnlyList<MigrationScript> scripts) : IMigrationSource
    {
        public Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(scripts);
    }
    private sealed class MemoryStore : IMigrationStore
    {
        public List<AppliedMigration> Applied { get; } = [];
        public Task<IReadOnlyList<AppliedMigration>> GetAppliedAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AppliedMigration>>(Applied);
        public Task ApplyAsync(MigrationScript migration, CancellationToken cancellationToken = default)
        {
            Applied.Add(new(migration.Id, migration.Checksum, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }
    }
}
