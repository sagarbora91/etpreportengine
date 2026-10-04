using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Service;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// Service interim (S-2), decision 15, 3 Oct 2026; lane L2. Migration 0048 section B_SERVICE_LANDING: one landing
/// table, file index, locked-day trigger and append procedure per importable Service family, generated from the
/// catalogue. The text shape is pinned by ServiceLandingMigrationTests; these tests prove the SQL behaviour.
/// Synthetic values only.
/// </summary>
public sealed class ServiceLandingSqlTests
{
    private static string Migrations => Path.Combine(AppContext.BaseDirectory, "database", "migrations");

    private static IReadOnlyList<EtpReportFamily> LandingFamilies()
    {
        var importable = ServiceInterimFamilies.Importable.ToHashSet(StringComparer.Ordinal);
        return EtpReportFamilyRegistry.Families.Where(family => importable.Contains(family.ReportCode) && !family.Derived)
            .OrderBy(family => family.ReportCode, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task Bootstrap_creates_every_landing_table_procedure_and_trigger_and_an_inactive_AW330()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var families = LandingFamilies();
            Assert.Equal(36, families.Count);
            foreach (var family in families)
            {
                var table = family.TableName;
                Assert.Equal(1, await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.tables WHERE name=N'{table}' AND schema_id=SCHEMA_ID(N'dbo')"));
                // etp_row_id, import_file_id, source_lineage_id, content_key, then one column per catalogue column.
                Assert.Equal(family.Columns.Count + 4, await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.{table}')"));
                Assert.Equal(1, await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_{table}_file' AND object_id=OBJECT_ID(N'dbo.{table}')"));
                Assert.Equal(1, await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.triggers WHERE name=N'trg_{table}_locked' AND parent_id=OBJECT_ID(N'dbo.{table}')"));
                // @file, @lineage, @key, then @v0..@vN in catalogue Columns order.
                Assert.Equal(family.Columns.Count + 3, await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.parameters WHERE object_id=OBJECT_ID(N'dbo.append_{table}')"));
                Assert.Equal(family.Columns[^1].CanonicalField, await database.ExecuteAsync(
                    $"SELECT TOP(1) name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.{table}') ORDER BY column_id DESC"));
            }
            Assert.Equal(36, await database.ExecuteAsync("SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'etp[_]landing[_]s[0-9][0-9][0-9]'"));
            foreach (var code in new[] { "s001", "s005", "s027", "s028", "s038" })
                Assert.Equal(0, await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.tables WHERE name=N'etp_landing_{code}'"));

            Assert.Equal(1, await database.ExecuteAsync("""
                SELECT COUNT(*) FROM dbo.stores s JOIN dbo.business_units b ON b.business_unit_id=s.business_unit_id
                WHERE s.store_code='AW330' AND s.is_active=0 AND b.business_unit_code='SERVICE'
                """));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Append_accepts_its_own_report_and_refuses_another_report_with_51422()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var family = EtpReportFamilyRegistry.Resolve("S002");
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync();

            // A file of another Service report (S003) cannot write into the S002 table.
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                var (file, lineage) = await OpenFileAsync(connection, transaction, "S003");
                var wrong = await Assert.ThrowsAsync<SqlException>(() => AppendAsync(connection, transaction, family, file, lineage));
                Assert.Equal(51422, wrong.Number);
                await transaction.RollbackAsync();
            }

            // Outside a transaction the procedure refuses before anything else is checked.
            var outside = await Assert.ThrowsAsync<SqlException>(() => AppendAsync(connection, null, family, 1, 1));
            Assert.Equal(51422, outside.Number);

            // Its own report lands one row and one content key; the locked-day trigger is inert for AW330.
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                var (file, lineage) = await OpenFileAsync(connection, transaction, "S002");
                await AppendAsync(connection, transaction, family, file, lineage);
                await transaction.CommitAsync();
            }
            Assert.Equal(1, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.[{family.TableName}]"));
            Assert.Equal(1, await database.ExecuteAsync("""
                SELECT COUNT(*) FROM dbo.etp_import_content c JOIN dbo.import_files f ON f.import_file_id=c.import_file_id
                WHERE f.report_code='S002' AND c.content_key=REPLICATE('b',64)
                """));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Viewer_and_store_manager_cannot_write_landing_tables_and_a_viewer_cannot_append()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await database.ExecuteAsync("""
                CREATE USER service_landing_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER service_landing_viewer;
                CREATE USER service_landing_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER service_landing_manager;
                """);
            foreach (var family in LandingFamilies())
            {
                foreach (var user in new[] { "service_landing_viewer", "service_landing_manager" })
                {
                    var insert = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                        $"EXECUTE AS USER='{user}'; INSERT dbo.[{family.TableName}](import_file_id,source_lineage_id,content_key) VALUES(1,1,'x'); REVERT;"));
                    Assert.Equal(229, insert.Number);
                    var delete = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                        $"EXECUTE AS USER='{user}'; DELETE dbo.[{family.TableName}]; REVERT;"));
                    Assert.Equal(229, delete.Number);
                }
                var execute = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                    $"EXECUTE AS USER='service_landing_viewer'; EXEC dbo.[append_{family.TableName}] 1,1,'x',{string.Join(',', family.Columns.Select(_ => "NULL"))}; REVERT;"));
                Assert.Equal(229, execute.Number);
            }
            Assert.Equal(36, await database.ExecuteAsync("""
                SELECT COUNT(DISTINCT p.major_id) FROM sys.database_permissions p
                JOIN sys.database_principals r ON r.principal_id=p.grantee_principal_id
                WHERE r.name='etp_store_manager' AND p.permission_name='EXECUTE' AND p.state='G'
                  AND OBJECT_NAME(p.major_id) LIKE N'append[_]etp[_]landing[_]s[0-9][0-9][0-9]'
                """));
            Assert.Equal(36, await database.ExecuteAsync("""
                SELECT COUNT(DISTINCT p.major_id) FROM sys.database_permissions p
                JOIN sys.database_principals r ON r.principal_id=p.grantee_principal_id
                WHERE r.name='etp_owner' AND p.permission_name='EXECUTE' AND p.state='G'
                  AND OBJECT_NAME(p.major_id) LIKE N'append[_]etp[_]landing[_]s[0-9][0-9][0-9]'
                """));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task A_second_bootstrap_and_a_rerun_of_0048_change_nothing()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            const string shape = """
                SELECT CONCAT(
                 (SELECT COUNT(*) FROM sys.tables WHERE name LIKE N'etp[_]landing[_]s[0-9][0-9][0-9]'),'|',
                 (SELECT COUNT(*) FROM sys.procedures WHERE name LIKE N'append[_]etp[_]landing[_]s[0-9][0-9][0-9]'),'|',
                 (SELECT COUNT(*) FROM sys.triggers WHERE name LIKE N'trg[_]etp[_]landing[_]s[0-9][0-9][0-9][_]locked'),'|',
                 (SELECT COUNT(*) FROM sys.indexes WHERE name LIKE N'IX[_]etp[_]landing[_]s[0-9][0-9][0-9][_]file'),'|',
                 (SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id WHERE t.name LIKE N'etp[_]landing[_]s[0-9][0-9][0-9]'),'|',
                 (SELECT COUNT(*) FROM dbo.stores WHERE store_code='AW330'),'|',
                 (SELECT COUNT(*) FROM dbo.business_units WHERE business_unit_code='SERVICE'))
                """;
            var before = await database.ExecuteAsync(shape);

            Assert.Empty(await new MigrationRunner(new DirectoryMigrationSource(Migrations), new SqlServerMigrationStore(database.ConnectionString)).RunAsync());
            await new SqlServerDatabaseBootstrapper(database.ConnectionString, new DirectoryMigrationSource(Migrations)).BootstrapAsync();
            Assert.Equal(before, await database.ExecuteAsync(shape));

            // The script itself is idempotent: run again in one transaction, as the runner runs it.
            var script = await File.ReadAllTextAsync(Path.Combine(Migrations, "0048_service_centre_interim.sql"));
            await using (var connection = new SqlConnection(database.ConnectionString))
            {
                await connection.OpenAsync();
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
                await using (var command = new SqlCommand(script, connection, transaction) { CommandTimeout = 300 })
                    await command.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            Assert.Equal(before, await database.ExecuteAsync(shape));
        }
        finally { await database.DisposeAsync(); }
    }

    private static async Task<(long File, long Lineage)> OpenFileAsync(SqlConnection connection, SqlTransaction transaction, string reportCode)
    {
        await using var command = new SqlCommand("""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Processing',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
            VALUES(@batch,'synthetic-service.xlsx',REPLICATE('a',64),1,@report,'AW330','20260928','20260928','20260928',1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Data',2,@record);
            SELECT @file,CONVERT(bigint,SCOPE_IDENTITY());
            """, connection, transaction);
        command.Parameters.AddWithValue("@report", reportCode);
        command.Parameters.AddWithValue("@record", reportCode + "_SOURCE");
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (Convert.ToInt64(reader.GetValue(0)), reader.GetInt64(1));
    }

    private static async Task AppendAsync(SqlConnection connection, SqlTransaction? transaction, EtpReportFamily family, long file, long lineage)
    {
        var parameters = family.Columns.Select((_, index) => $"@v{index}").ToArray();
        await using var command = new SqlCommand($"EXEC dbo.[append_{family.TableName}] @file,@lineage,@key,{string.Join(',', parameters)}", connection, transaction);
        command.Parameters.AddWithValue("@file", file);
        command.Parameters.AddWithValue("@lineage", lineage);
        command.Parameters.AddWithValue("@key", new string('b', 64));
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter, DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
}
