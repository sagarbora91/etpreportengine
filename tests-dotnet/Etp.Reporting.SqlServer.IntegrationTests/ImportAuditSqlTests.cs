using System.Data;
using System.Text.Json;
using Etp.Reporting.Import.Audit;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Audit;
using Etp.Reporting.TestSupport;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// ImportAudit against a scratch database (design 12.3). The prediction must equal what the import then does, and
/// no audit command may change a row.
/// </summary>
public sealed class ImportAuditSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>, IDisposable
{
    private readonly string input = AuditFixtureWorkbooks.NewFolder();
    private readonly string reports = AuditFixtureWorkbooks.NewFolder("reports");

    public void Dispose()
    {
        AuditFixtureWorkbooks.Delete(input);
        AuditFixtureWorkbooks.Delete(reports);
    }

    private string Server => new SqlConnectionStringBuilder(database.ConnectionString).DataSource;

    private async Task<(int Code, JsonElement Report)> AuditAsync(string name, params string[] args)
    {
        var folder = Path.Combine(reports, name);
        var code = await new AuditRunner(TextWriter.Null, TextWriter.Null)
            { MigrationsDirectory = database.MigrationDirectory }
            .RunAsync([.. args, "--database", database.Name, "--server", Server, "--out", folder, "--format", "json", "--quiet"]);
        var path = Path.Combine(folder, "report.json");
        return (code, File.Exists(path) ? JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement : default);
    }

    /// <summary>Row count and checksum of every user table, for the change-nothing check.</summary>
    private async Task<string> FingerprintAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = new SqlCommand("SELECT QUOTENAME(SCHEMA_NAME(schema_id))+'.'+QUOTENAME(name) FROM sys.tables WHERE is_ms_shipped=0 ORDER BY 1", connection))
        await using (var reader = await list.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        var parts = new List<string>();
        foreach (var table in tables)
        {
            // Tables with columns BINARY_CHECKSUM cannot read (xml, varbinary(max) evidence) are counted only.
            await using var command = new SqlCommand($"SELECT COUNT_BIG(*) FROM {table}", connection);
            var count = await command.ExecuteScalarAsync();
            string checksum;
            try
            {
                await using var sum = new SqlCommand($"SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM {table}", connection);
                checksum = Convert.ToString(await sum.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
            catch (SqlException) { checksum = "n/a"; }
            parts.Add($"{table}:{count}:{checksum}");
        }
        return string.Join("\n", parts);
    }

    private async Task<IReadOnlyList<Etp.Reporting.Application.Imports.FolderImportFileResult>> ImportAsync() =>
        (await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString))
            .RunAsync(input, new("ImportAudit prediction test"))).Files;

    [Fact]
    public async Task Prediction_matches_real_import()
    {
        AuditFixtureWorkbooks.Write("r030-per-unit-ledger.json", input);
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", input);
        AuditFixtureWorkbooks.WriteSnapshot(Etp.Reporting.Import.Tests.SyntheticConsolidatedWorkbooks.LegacyStackedBinWise(), Path.Combine(input, "stacked"));

        foreach (var round in new[] { "first", "second" })
        {
            var (_, report) = await AuditAsync(round, "check-import", input);
            var predicted = report.GetProperty("files").EnumerateArray().ToDictionary(file => Path.GetFileName(file.GetProperty("file").GetString()!));

            var imported = await ImportAsync();

            Assert.Equal(predicted.Count, imported.Count);
            foreach (var actual in imported)
            {
                var planner = predicted[actual.FileName].GetProperty("planner1");
                Assert.True(planner.GetProperty("result").GetString() == actual.Status, $"{round} {actual.FileName}: predicted {planner.GetProperty("result").GetString()}, imported {actual.Status}");
                var rows = planner.GetProperty("rows");
                Assert.Equal(actual.NewRows, rows.GetProperty("new").GetInt32());
                Assert.Equal(actual.AlreadyPresentRows, rows.GetProperty("present").GetInt32());
                Assert.Equal(actual.ConflictRows, rows.GetProperty("conflict").GetInt32());
                if (planner.TryGetProperty("snapshotRows", out var snapshots))
                    foreach (var date in snapshots.EnumerateObject())
                        Assert.Equal(date.Value.GetInt32(), Convert.ToInt32(await database.ExecuteAsync(
                            $"SELECT COUNT(*) FROM dbo.stock_snapshots WHERE store_code='WLMHW' AND snapshot_date='{date.Name}' AND source_report_code='R010'")));
            }
        }
    }

    [Fact]
    public async Task Audit_commands_change_nothing()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", input);
        await ImportAsync();
        AuditFixtureWorkbooks.Write("r030-per-unit-ledger.json", input);
        var before = await FingerprintAsync();

        await AuditAsync("check", "check-import", input, "--detail", "rows");
        await AuditAsync("baseline", "baseline", "--detail", "rows");

        Assert.Equal(before, await FingerprintAsync());
    }

    [Fact]
    public async Task Every_audit_query_runs_as_db_datareader_only()
    {
        await database.ExecuteAsync("""
            IF DATABASE_PRINCIPAL_ID(N'audit_reader') IS NULL CREATE USER audit_reader WITHOUT LOGIN;
            ALTER ROLE db_datareader ADD MEMBER audit_reader;
            """);
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var impersonate = new SqlCommand("EXECUTE AS USER = N'audit_reader';", connection)) await impersonate.ExecuteNonQueryAsync();
        try
        {
            foreach (var sql in AuditQueries.All.Append(BaselineQuery.Script()))
            {
                await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
                command.Parameters.Add("@hash", SqlDbType.Char, 64).Value = new string('0', 64);
                command.Parameters.Add("@report", SqlDbType.VarChar, 30).Value = "R025";
                command.Parameters.Add("@store", SqlDbType.VarChar, 30).Value = "WLMHW";
                command.Parameters.Add("@type", SqlDbType.VarChar, 10).Value = "R013";
                command.Parameters.Add("@start", SqlDbType.Date).Value = new DateOnly(2026, 7, 1);
                command.Parameters.Add("@end", SqlDbType.Date).Value = new DateOnly(2026, 9, 30);
                command.Parameters.Add("@keys", SqlDbType.NVarChar, -1).Value = """[{"y":2027,"d":"SYN100001"}]""";
                command.Parameters.Add("@ids", SqlDbType.NVarChar, -1).Value = "[1]";
                command.Parameters.Add("@dates", SqlDbType.NVarChar, -1).Value = """["2026-09-29"]""";
                await using var reader = await command.ExecuteReaderAsync();
                do { while (await reader.ReadAsync()) { } } while (await reader.NextResultAsync());
            }
        }
        finally
        {
            await using var revert = new SqlCommand("REVERT;", connection);
            await revert.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Baseline_matches_script_output()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", input);
        await ImportAsync();

        var (code, report) = await AuditAsync("baseline", "baseline");

        Assert.Equal(AuditExitCodes.Clean, code);
        var counts = report.GetProperty("baseline").GetProperty("resultSets").EnumerateArray().Single(set => set.GetProperty("name").GetString() == "fact counts");
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(BaselineQuery.Script(), connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.NextResultAsync();
        await reader.NextResultAsync();
        var direct = new List<string>();
        while (await reader.ReadAsync()) direct.Add($"{reader.GetValue(0)}={reader.GetValue(1)}");
        Assert.Equal(direct, counts.GetProperty("rows").EnumerateArray().Select(row => $"{row[0].GetString()}={row[1].GetString()}"));
    }

    [Fact]
    public async Task Check_import_reports_the_database_and_its_migration()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", input);

        var (code, report) = await AuditAsync("stamp", "check-import", input);

        Assert.Equal(AuditExitCodes.Clean, code);
        var stamp = report.GetProperty("database");
        Assert.Equal(database.Name, stamp.GetProperty("name").GetString());
        Assert.False(stamp.GetProperty("emulated0041").GetBoolean());
        Assert.StartsWith("004", stamp.GetProperty("latestMigration").GetString());
    }
}

/// <summary><c>seed</c> writes only scratch databases (design 3.7, 12.3).</summary>
public sealed class SeedCommandSqlTests
{
    [Fact]
    public async Task Refuses_non_scratch_and_bootstraps_scratch()
    {
        Assert.Equal(AuditExitCodes.Usage, await new AuditRunner(TextWriter.Null, TextWriter.Null).RunAsync(["seed", "--database", "EtpReporting"]));

        var name = "EtpPhase1Test_" + Guid.NewGuid().ToString("N");
        var server = new SqlConnectionStringBuilder(TestSqlConnections.ForDatabase("master")).DataSource;
        try
        {
            var code = await new AuditRunner(TextWriter.Null, TextWriter.Null)
                { MigrationsDirectory = Path.Combine(AppContext.BaseDirectory, "database", "migrations") }
                .RunAsync(["seed", "--database", name, "--server", server, "--migrate-only"]);

            Assert.Equal(AuditExitCodes.Clean, code);
            await using var connection = new SqlConnection(TestSqlConnections.ForDatabase(name));
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT MAX(migration_id) FROM dbo.schema_migrations", connection);
            Assert.StartsWith("004", (string)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(TestSqlConnections.ForDatabase("master"));
            await master.OpenAsync();
            await using var drop = new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", master);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
