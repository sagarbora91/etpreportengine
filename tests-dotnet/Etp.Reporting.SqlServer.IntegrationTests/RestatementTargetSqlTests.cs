using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// IF-016 interim: restatement candidates are found by overlap of the declared period; and the
/// pre-upgrade checker (spec 13.1) reads without writing.
/// </summary>
public sealed class RestatementTargetSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Restatement_candidates_are_the_current_files_whose_period_overlaps()
    {
        var batch = Guid.NewGuid();
        await database.ExecuteAsync($"""
            INSERT dbo.import_batches(import_batch_id,status,started_utc,source_row_count) VALUES('{batch}','Completed',SYSUTCDATETIME(),40);
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end)
            VALUES('{batch}',N'cand_whole_month.xlsx',REPLICATE('1',64),10,'R022','CANDSTORE','20260831','20260801','20260831'),
                  ('{batch}',N'cand_one_day.xlsx',REPLICATE('2',64),10,'R022','CANDSTORE','20260825',NULL,NULL),
                  ('{batch}',N'cand_september.xlsx',REPLICATE('3',64),10,'R022','CANDSTORE','20260930','20260901','20260930'),
                  ('{batch}',N'cand_other_store.xlsx',REPLICATE('4',64),10,'R022','OTHERSTORE','20260825','20260825','20260825'),
                  ('{batch}',N'cand_other_report.xlsx',REPLICATE('5',64),10,'R025','CANDSTORE','20260825','20260825','20260825');
            """);
        var repository = new OperationalCompletionRepository(database.ConnectionString);

        var candidates = await repository.FindRestatementCandidatesAsync("r022", "CANDSTORE", new(2026, 8, 20), new(2026, 8, 26));

        Assert.Equal(["cand_whole_month.xlsx", "cand_one_day.xlsx"], candidates.Select(candidate => candidate.FileName));
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 40), (candidates[0].PeriodStart, candidates[0].PeriodEnd, candidates[0].Rows));
        Assert.Equal((new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 25)), (candidates[1].PeriodStart, candidates[1].PeriodEnd));
        Assert.Empty(await repository.FindRestatementCandidatesAsync("R022", "CANDSTORE", new(2026, 10, 1), new(2026, 10, 2)));
        // The end-date lookup used before IF-016 saw only the one file covering 26 Aug.
        Assert.Single(await repository.FindRestatementCandidatesAsync("R022", "CANDSTORE", new(2026, 8, 26), new(2026, 8, 26)));
    }

    [Fact]
    public async Task Upgrade_check_script_runs_select_only_and_reports_its_findings()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "check-import-upgrade.sql"));
        var code = string.Join('\n', script.Split('\n').Select(line => line.Split("--")[0]));
        Assert.DoesNotMatch(new Regex(@"\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE|CREATE|ALTER|DROP|GRANT|DENY|REVOKE|BACKUP|RESTORE|INTO|DBCC|GO)\b",
            RegexOptions.IgnoreCase), code);
        Assert.DoesNotContain("#", code);
        Assert.DoesNotMatch(new Regex(@"\bEXEC(UTE)?\b(?!\s+sys\.sp_executesql\b)", RegexOptions.IgnoreCase), code);

        // A financial-year mismatch that is also an orphan header, and a finalised day.
        await database.ExecuteAsync("""
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('CHECKSTORE',N'SYN-0001',2026,'20260401');
            INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('CHECKSTORE','20260825','LOCKED',N'tester',SYSUTCDATETIME());
            IF DATABASE_PRINCIPAL_ID(N'upgrade_checker') IS NULL
            BEGIN CREATE USER upgrade_checker WITHOUT LOGIN; ALTER ROLE db_datareader ADD MEMBER upgrade_checker; END;
            """);

        var sets = await RunCheckAsync(database.ConnectionString, script);

        var environment = Assert.Single(sets[0].Rows);
        Assert.Equal(await database.ExecuteAsync("SELECT CONVERT(sysname,DATABASEPROPERTYEX(DB_NAME(),'Collation'))"),
            environment[Array.IndexOf(sets[0].Columns, "database_collation")]);
        Assert.False(string.IsNullOrWhiteSpace(environment[Array.IndexOf(sets[0].Columns, "sql_server_version")] as string));
        Assert.Equal(["check_code", "findings", "blocks_upgrade", "detail"], sets[1].Columns);
        var summary = sets[1].Rows.ToDictionary(row => (string)row[0], row => (Findings: Convert.ToInt64(row[1]), Blocks: (bool)row[2]));
        Assert.Equal(14, summary.Count);
        Assert.Equal((1L, true), summary["INVOICE_YEAR_NOT_FINANCIAL_YEAR"]);
        Assert.Equal((1L, false), summary["ORPHAN_INVOICE_HEADERS"]);
        Assert.Equal(1L, summary["LOCKED_DAYS"].Findings);
        Assert.Equal((0L, true), summary["DUPLICATE_CONTROLS"]);
        Assert.Equal(["table_name", "row_count"], sets[2].Columns);
        Assert.Contains(sets[2].Rows, row => (string)row[0] == "sales_invoices" && Convert.ToInt64(row[1]) >= 1);
        var details = sets.Skip(3).Select(set => (string)set.Rows[0][0]).ToArray();
        Assert.Contains("INVOICE_YEAR_NOT_FINANCIAL_YEAR", details);
        Assert.Contains("ORPHAN_INVOICE_HEADERS", details);
        Assert.Contains("LOCKED_DAYS", details);
        Assert.DoesNotContain("DUPLICATE_CONTROLS", details);
    }

    [Fact]
    public async Task Upgrade_check_script_reads_the_line_and_source_columns_once_0038_adds_them()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "check-import-upgrade.sql"));
        var fresh = new SqlDatabaseFixture();
        await fresh.InitializeAsync();
        try
        {
            // The shape 0038 gives the stock tables, added here only when the migration has not done so.
            await fresh.ExecuteAsync("""
                IF COL_LENGTH(N'dbo.stock_movements',N'line_seq') IS NULL ALTER TABLE dbo.stock_movements ADD line_seq int NOT NULL CONSTRAINT DF_check_movement_line DEFAULT(1);
                IF COL_LENGTH(N'dbo.stock_snapshots',N'source_report_code') IS NULL ALTER TABLE dbo.stock_snapshots ADD source_report_code varchar(30) NULL;
                IF COL_LENGTH(N'dbo.stock_snapshots',N'line_seq') IS NULL ALTER TABLE dbo.stock_snapshots ADD line_seq int NOT NULL CONSTRAINT DF_check_snapshot_line DEFAULT(1);
                CREATE USER upgrade_checker WITHOUT LOGIN; ALTER ROLE db_datareader ADD MEMBER upgrade_checker;
                """);
            var sets = await RunCheckAsync(fresh.ConnectionString, script);
            var summary = sets[1].Rows.ToDictionary(row => (string)row[0], row => (Findings: Convert.ToInt64(row[1]), Blocks: (bool)row[2], Detail: (string)row[3]));
            Assert.Equal((0L, true), (summary["REPEATED_MOVEMENT_IDENTITY"].Findings, summary["REPEATED_MOVEMENT_IDENTITY"].Blocks));
            Assert.Contains("and line", summary["REPEATED_MOVEMENT_IDENTITY"].Detail);
            Assert.Equal((0L, true), (summary["REPEATED_SNAPSHOT_IDENTITY"].Findings, summary["REPEATED_SNAPSHOT_IDENTITY"].Blocks));
        }
        finally { await fresh.DisposeAsync(); }
    }

    // Runs the script as a db_datareader-only user, which the caller has created.
    private static async Task<List<(string[] Columns, List<object[]> Rows)>> RunCheckAsync(string connectionString, string script)
    {
        var sets = new List<(string[] Columns, List<object[]> Rows)>();
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using (var impersonate = new SqlCommand("EXECUTE AS USER = N'upgrade_checker';", connection)) await impersonate.ExecuteNonQueryAsync();
            // The reader account cannot write, so the script cannot have written either.
            var write = await Assert.ThrowsAsync<SqlException>(async () =>
            {
                await using var insert = new SqlCommand("INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('X',N'X',2027,'20260401');", connection);
                await insert.ExecuteNonQueryAsync();
            });
            Assert.Equal(229, write.Number);
            await using (var command = new SqlCommand(script, connection) { CommandTimeout = 120 })
            await using (var reader = await command.ExecuteReaderAsync())
            {
                do
                {
                    var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
                    var rows = new List<object[]>();
                    while (await reader.ReadAsync())
                    {
                        var values = new object[reader.FieldCount];
                        reader.GetValues(values);
                        rows.Add(values);
                    }
                    sets.Add((columns, rows));
                } while (await reader.NextResultAsync());
            }
            await using (var revert = new SqlCommand("REVERT;", connection)) await revert.ExecuteNonQueryAsync();
        }

        return sets;
    }
}
