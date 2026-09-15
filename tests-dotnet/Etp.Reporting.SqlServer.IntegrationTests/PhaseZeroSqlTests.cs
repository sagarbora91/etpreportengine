using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseZeroSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task All_migrations_apply_once_and_indexes_have_the_requested_columns()
    {
        var result = await new MigrationRunner(new DirectoryMigrationSource(database.MigrationDirectory),
            new SqlServerMigrationStore(database.ConnectionString)).RunAsync();
        Assert.Empty(result);
        Assert.Equal(16, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.schema_migrations")));
        Assert.Equal("transaction_date,store_code", await database.ExecuteAsync("SELECT STRING_AGG(c.name, ',') WITHIN GROUP(ORDER BY ic.key_ordinal) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.name='IX_sales_invoices_date' AND ic.key_ordinal>0"));
        Assert.Equal("document_number,sales_invoice_id", await database.ExecuteAsync("SELECT STRING_AGG(c.name, ',') WITHIN GROUP(ORDER BY ic.index_column_id) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.name='IX_sales_invoices_date' AND ic.is_included_column=1"));
        Assert.Equal("store_code,product_code,snapshot_date", await database.ExecuteAsync("SELECT STRING_AGG(c.name, ',') WITHIN GROUP(ORDER BY ic.key_ordinal) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.name='IX_stock_snapshots_product' AND ic.key_ordinal>0"));
        Assert.True((bool)(await database.ExecuteAsync("SELECT ic.is_descending_key FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id WHERE i.name='IX_stock_snapshots_product' AND ic.key_ordinal=3"))!);
    }

    [Theory]
    [InlineData("UPDATE dbo.application_users SET role_code='VIEWER' WHERE role_code='OWNER'")]
    [InlineData("UPDATE dbo.application_users SET is_active=0 WHERE role_code='OWNER'")]
    [InlineData("DELETE dbo.application_users WHERE role_code='OWNER'")]
    public async Task Final_owner_cannot_be_demoted_deactivated_or_deleted(string sql)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(sql));
        Assert.Equal(51230, error.Number);
        Assert.Contains("Keep at least one active Owner", error.Message);
        Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.application_users WHERE role_code='OWNER' AND is_active=1")));
    }

    [Fact]
    public async Task Guard_handles_multiple_rows_and_allows_changes_when_another_owner_remains()
    {
        await database.ExecuteAsync("INSERT dbo.application_users(windows_identity,display_name,role_code,modified_by,change_reason) VALUES(N'Phase0SyntheticOwner',N'Test Owner','OWNER',SUSER_SNAME(),N'Integration test')");
        try
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("UPDATE dbo.application_users SET role_code='VIEWER' WHERE role_code='OWNER'"));
            Assert.Equal(51230, error.Number);
            await database.ExecuteAsync("UPDATE dbo.application_users SET role_code='VIEWER' WHERE windows_identity=N'Phase0SyntheticOwner'");
            Assert.Equal("VIEWER", await database.ExecuteAsync("SELECT role_code FROM dbo.application_users WHERE windows_identity=N'Phase0SyntheticOwner'"));
        }
        finally { await database.ExecuteAsync("DELETE dbo.application_users WHERE windows_identity=N'Phase0SyntheticOwner'"); }
    }

    [Fact]
    public async Task Failed_import_opens_owner_dashboard_and_severity_sync_inserts_updates_and_resolves()
    {
        await database.ExecuteAsync("INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(NEWID(),'Failed',SYSUTCDATETIME())");
        var dashboard = await new SqlServerOperationsAdministrationService(database.ConnectionString)
            .LoadDashboardAsync(new OperationsPeriod(new DateOnly(2026,8,25), new DateOnly(2026,8,25)));
        Assert.NotNull(dashboard);
        Assert.Equal("CRITICAL", await database.ExecuteAsync("SELECT severity FROM dbo.data_quality_issues WHERE category='FAILED_IMPORT_BATCH'"));
        var repository = new ProductisationRepository(database.ConnectionString);
        await repository.SyncDataQualityIssuesAsync([new("INFORMATION", "Test", "RESTATEMENT", 1, null, "Test restatement")]);
        Assert.Equal("INFO", await database.ExecuteAsync("SELECT severity FROM dbo.data_quality_issues WHERE category='RESTATEMENT'"));
        await repository.SyncDataQualityIssuesAsync([new("FAIL", "Test", "RESTATEMENT", 2, null, "Test failure")]);
        Assert.Equal("CRITICAL", await database.ExecuteAsync("SELECT severity FROM dbo.data_quality_issues WHERE category='RESTATEMENT'"));
        Assert.Equal("FAIL", await database.ExecuteAsync("SELECT technical_control_status FROM dbo.data_quality_issues WHERE category='RESTATEMENT'"));
        await repository.SyncDataQualityIssuesAsync([]);
        Assert.Equal("PASS", await database.ExecuteAsync("SELECT technical_control_status FROM dbo.data_quality_issues WHERE category='RESTATEMENT'"));
    }

    [Fact]
    public async Task Headless_modes_use_saved_database_instead_of_the_constructor_fallback()
    {
        var folder = Path.Combine(Path.GetTempPath(), "EtpPhase0Settings_" + Guid.NewGuid().ToString("N"));
        try
        {
            new DesktopSettingsStore(folder).Save(database.ConnectionString);
            var root = new DesktopCompositionRoot(AppContext.BaseDirectory,
                @"Server=invalid;Database=MustNeverBeUsed;Integrated Security=True;Connect Timeout=1", folder);
            Assert.Equal(database.Name, new SqlConnectionStringBuilder(root.LoadConnectionString()).InitialCatalog);
            await root.InitializeDatabaseAsync();
            Assert.Equal(0, await root.RunAutomationOnceAsync());
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
