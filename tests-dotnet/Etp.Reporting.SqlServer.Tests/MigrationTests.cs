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
        Assert.Equal(["0001_foundation", "0002_reporting_facts", "0003_sales_dimensions", "0004_operational_audit", "0005_daily_reporting_workflow", "0006_backfill_import_business_scope", "0007_sales_enrichment_facts", "0008_service_cash_inputs", "0009_locked_day_fact_guards", "0010_operational_completion", "0011_phase2_operations", "0012_windows_database_access", "0013_store_manager_permission_guards"], migrations.Select(x => x.Id));
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
        var audit = Assert.Single(migrations, x => x.Id == "0004_operational_audit").Sql;
        Assert.Contains("operational_audit", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("document_number", audit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stock_snapshots", facts, StringComparison.Ordinal);
        var dimensions = Assert.Single(migrations, x => x.Id == "0003_sales_dimensions").Sql;
        Assert.Contains("brand_segment", dimensions, StringComparison.Ordinal);
        var daily = Assert.Single(migrations, x => x.Id == "0005_daily_reporting_workflow").Sql;
        Assert.Contains("business_date", daily, StringComparison.Ordinal);
        Assert.Contains("source_report_date", daily, StringComparison.Ordinal);
        Assert.Contains("imported_by", daily, StringComparison.Ordinal);
        Assert.Contains("manual_operational_inputs", daily, StringComparison.Ordinal);
        Assert.Contains("daily_reporting_days", daily, StringComparison.Ordinal);
        Assert.Contains("trg_import_files_protect_locked", daily, StringComparison.Ordinal);
        Assert.Contains("trg_manual_operational_inputs_protect_locked", daily, StringComparison.Ordinal);
        Assert.DoesNotContain("CUSTOMERNAME", daily, StringComparison.OrdinalIgnoreCase);
        var backfill = Assert.Single(migrations, x => x.Id == "0006_backfill_import_business_scope").Sql;
        Assert.Contains("LEGACY_IMPORT", backfill, StringComparison.Ordinal);
        Assert.Contains("source_report_date", backfill, StringComparison.Ordinal);
        var enrichment = Assert.Single(migrations, x => x.Id == "0007_sales_enrichment_facts").Sql;
        Assert.Contains("sales_line_enrichments", enrichment, StringComparison.Ordinal);
        Assert.Contains("source_cro_number", enrichment, StringComparison.Ordinal);
        Assert.DoesNotContain("customer", enrichment, StringComparison.OrdinalIgnoreCase);
        var service = Assert.Single(migrations, x => x.Id == "0008_service_cash_inputs").Sql;
        Assert.Contains("SERVICE_CASH", service, StringComparison.Ordinal);
        Assert.Contains("CLOSING_CASH_COUNTED", service, StringComparison.Ordinal);
        var guards = Assert.Single(migrations, x => x.Id == "0009_locked_day_fact_guards").Sql;
        Assert.Contains("trg_sales_lines_protect_locked", guards, StringComparison.Ordinal);
        Assert.Contains("trg_sales_tenders_protect_locked", guards, StringComparison.Ordinal);
        Assert.Contains("trg_stock_snapshots_protect_locked", guards, StringComparison.Ordinal);
        Assert.Contains("trg_sales_enrichments_protect_locked", guards, StringComparison.Ordinal);
        var completion = Assert.Single(migrations, x => x.Id == "0010_operational_completion").Sql;
        Assert.Contains("prepare_import_restatement", completion, StringComparison.Ordinal);
        Assert.Contains("restatement_fact_archive", completion, StringComparison.Ordinal);
        Assert.Contains("manual_stock_counts", completion, StringComparison.Ordinal);
        Assert.Contains("staff_sales_targets", completion, StringComparison.Ordinal);
        Assert.Contains("daily_report_generations", completion, StringComparison.Ordinal);
        Assert.Contains("trg_sales_invoices_protect_locked", completion, StringComparison.Ordinal);
        Assert.Contains("trg_source_lineage_protect_locked", completion, StringComparison.Ordinal);
        Assert.Contains("trg_daily_report_generations_immutable", completion, StringComparison.Ordinal);
        Assert.Contains("trg_restatement_archive_immutable", completion, StringComparison.Ordinal);
        Assert.Contains("RestoreDrill", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("customername", completion, StringComparison.OrdinalIgnoreCase);
        var operations = Assert.Single(migrations, x => x.Id == "0011_phase2_operations").Sql;
        Assert.Contains("application_users", operations, StringComparison.Ordinal);
        Assert.Contains("controlled_master_values", operations, StringComparison.Ordinal);
        Assert.Contains("watch_folder_settings", operations, StringComparison.Ordinal);
        Assert.Contains("report_pack_schedules", operations, StringComparison.Ordinal);
        Assert.Contains("automation_runs", operations, StringComparison.Ordinal);
        Assert.Contains("report_document_json", operations, StringComparison.Ordinal);
        Assert.Contains("trg_application_users_history", operations, StringComparison.Ordinal);
        Assert.Contains("trg_daily_report_generations_immutable", operations, StringComparison.Ordinal);
        Assert.DoesNotContain("customername", operations, StringComparison.OrdinalIgnoreCase);
        var access = Assert.Single(migrations, x => x.Id == "0012_windows_database_access").Sql;
        Assert.Contains("NT AUTHORITY\\SYSTEM", access, StringComparison.Ordinal);
        Assert.Contains("db_datareader", access, StringComparison.Ordinal);
        Assert.Contains("db_datawriter", access, StringComparison.Ordinal);
        Assert.Contains("STORE_MANAGER", access, StringComparison.Ordinal);
        var permissionGuards = Assert.Single(migrations, x => x.Id == "0013_store_manager_permission_guards").Sql;
        Assert.Contains("DENY DELETE ON SCHEMA::dbo", permissionGuards, StringComparison.Ordinal);
        Assert.Contains("DENY INSERT,UPDATE,DELETE ON dbo.application_users", permissionGuards, StringComparison.Ordinal);
        Assert.Contains("DENY INSERT,UPDATE,DELETE ON dbo.schema_migrations", permissionGuards, StringComparison.Ordinal);
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

    [Fact]
    public void Persistence_validation_requires_complete_restatement_authority()
    {
        var batch = new ImportBatchRegistration(Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(batch.BatchId, null, "replacement.xlsx", new string('a', 64), 1);
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
