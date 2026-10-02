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
            -- The same document under the label year and under its financial year: the key forbids re-keying the first.
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('CHECKSTORE',N'SYN-0003',2026,'20260402'),('CHECKSTORE',N'SYN-0003',2027,'20260402');
            INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('CHECKSTORE','20260825','LOCKED',N'tester',SYSUTCDATETIME());
            -- A repeated movement identity, a repeated snapshot identity and one tender type twice in different case.
            INSERT dbo.import_batches(import_batch_id,status,started_utc,source_row_count) VALUES('6f2b0c1e-0000-4000-8000-00000000c4ec','Completed',SYSUTCDATETIME(),6);
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end)
            VALUES('6f2b0c1e-0000-4000-8000-00000000c4ec',N'check_stock.xlsx',REPLICATE('c',64),10,'R030','CHECKSTORE','20260825','20260825','20260825');
            DECLARE @file bigint = SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            VALUES(@file,N'S',2,'MOVEMENT'),(@file,N'S',3,'MOVEMENT'),(@file,N'S',4,'CLOSING_STOCK'),(@file,N'S',5,'CLOSING_STOCK'),(@file,N'S',6,'TENDER'),(@file,N'S',7,'TENDER');
            INSERT dbo.stock_movements(store_code,document_number,invoice_year,document_date,product_code,source_transaction_type,opening_quantity,transaction_quantity,closing_quantity,source_lineage_id)
            SELECT 'CHECKSTORE',N'MOV-1',2027,'20260825',N'P-1',N'TRANSFER',0,1,1,source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file AND source_record_type='MOVEMENT';
            INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,quantity,source_lineage_id)
            SELECT 'CHECKSTORE','20260825',N'P-1',1,source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file AND source_record_type='CLOSING_STOCK';
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('CHECKSTORE',N'SYN-0002',2027,'20260825');
            DECLARE @invoice bigint = SCOPE_IDENTITY();
            INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id)
            SELECT @invoice,CASE source_row_number WHEN 6 THEN N'Cash' ELSE N'CASH' END,10,'INR',source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file AND source_record_type='TENDER';
            -- An open stock-ledger conflict on the stored movement's identity (as 1.9.2 logged a later unit row),
            -- and a resolved one that is not listed.
            INSERT dbo.import_conflicts(import_file_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference,status)
            VALUES(@file,'CHECKSTORE','20260825','R003',N'CHECKSTORE/2027/MOV-1/2026-08-25/P-1/TRANSFER//',REPLICATE('a',64),REPLICATE('b',64),N'Stock movement values differ.','OPEN'),
                  (@file,'CHECKSTORE','20260825','R003',N'CHECKSTORE/2027/MOV-1/2026-08-25/P-1/TRANSFER//',REPLICATE('a',64),REPLICATE('b',64),N'Stock movement values differ.','RESOLVED'),
                  (@file,'CHECKSTORE','20260825','R003',N'CHECKSTORE/2027/MOV-2/2026-08-25/P-1/TRANSFER//',REPLICATE('a',64),REPLICATE('b',64),N'No stored movement.','OPEN');
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
        Assert.Equal(18, summary.Count);
        Assert.Equal((1L, false), summary["OPEN_MOVEMENT_CONFLICTS_ON_STORED_ROWS"]);
        Assert.Equal((2L, true), summary["INVOICE_YEAR_NOT_FINANCIAL_YEAR"]);
        Assert.Equal((1L, true), summary["INVOICE_YEAR_TWIN_HEADERS"]);
        Assert.Contains("repair-invoice-financial-year.sql", (string)sets[1].Rows.Single(row => (string)row[0] == "INVOICE_YEAR_NOT_FINANCIAL_YEAR")[3]);
        Assert.Equal((3L, false), summary["ORPHAN_INVOICE_HEADERS"]);
        Assert.Equal(1L, summary["LOCKED_DAYS"].Findings);
        Assert.Equal((0L, true), summary["DUPLICATE_CONTROLS"]);
        Assert.Equal((1L, true), summary["DUPLICATE_TENDERS"]);
        Assert.Equal(1L, summary["TENDER_TYPE_CASE_VARIANTS"].Findings);
        Assert.Equal(1L, summary["REPEATED_MOVEMENT_IDENTITY"].Findings);
        Assert.Equal(1L, summary["REPEATED_SNAPSHOT_IDENTITY"].Findings);
        // Superseded files' family rows are retained by design and reported apart, not as superseded facts.
        Assert.Equal((0L, false), summary["FACTS_OF_SUPERSEDED_FILES"]);
        Assert.Equal((0L, false), summary["SOURCE_ROWS_OF_SUPERSEDED_FILES"]);
        Assert.Equal(["table_name", "row_count"], sets[2].Columns);
        Assert.Contains(sets[2].Rows, row => (string)row[0] == "sales_invoices" && Convert.ToInt64(row[1]) >= 1);
        // Every detail set has rows, and one appears exactly for each summary check that found something.
        Assert.All(sets.Skip(3), set => Assert.NotEmpty(set.Rows));
        var details = sets.Skip(3).Select(set => (string)set.Rows[0][0]).ToArray();
        Assert.Equal(summary.Where(check => check.Value.Findings > 0 && check.Key is not ("TENDER_TYPE_CASE_VARIANTS" or "MOVEMENT_YEAR_NOT_FINANCIAL_YEAR" or "SOURCE_ROWS_OF_SUPERSEDED_FILES")).Select(check => check.Key).Order(),
            details.Order());
        Assert.Contains("INVOICE_YEAR_NOT_FINANCIAL_YEAR", details);
        Assert.Contains("INVOICE_YEAR_TWIN_HEADERS", details);
        var years = sets.Skip(3).Single(set => (string)set.Rows[0][0] == "INVOICE_YEAR_NOT_FINANCIAL_YEAR");
        Assert.Equal(["COMBINE_WITH_TWIN", "RE_KEY"], years.Rows.Select(row => (string)row[Array.IndexOf(years.Columns, "repair")]).Order());
        Assert.Contains("ORPHAN_INVOICE_HEADERS", details);
        Assert.Contains("LOCKED_DAYS", details);
        Assert.DoesNotContain("DUPLICATE_CONTROLS", details);
        Assert.Contains("DUPLICATE_TENDERS", details);
        Assert.Contains("REPEATED_MOVEMENT_IDENTITY", details);
        Assert.Contains("REPEATED_SNAPSHOT_IDENTITY", details);
        var conflicts = Assert.Single(sets.Skip(3), set => (string)set.Rows[0][0] == "OPEN_MOVEMENT_CONFLICTS_ON_STORED_ROWS");
        var conflict = Assert.Single(conflicts.Rows);
        Assert.Equal("OPEN", conflict[Array.IndexOf(conflicts.Columns, "status")]);
        Assert.Equal(2, Convert.ToInt32(conflict[Array.IndexOf(conflicts.Columns, "stored_movements")]));
    }

    [Fact]
    public async Task Upgrade_check_script_lists_R011_rows_the_snapshot_backfill_rebuilds_or_cannot()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "check-import-upgrade.sql"));
        // Outcomes written by the pre-0041 procedure (legacy identity text): R011's SNAP-1
        // was ALREADY_PRESENT against the R010 row, whose values still hash alike; SNAP-2 was a CONFLICT that kept
        // only its hash. An outcome of the 0041 procedure (identity ending /#1) is not a legacy row.
        await database.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(),@r010 bigint,@r011 bigint,@lineage bigint,@hash char(64);
            INSERT dbo.import_batches(import_batch_id,status,started_utc,source_row_count) VALUES(@batch,'Completed',SYSUTCDATETIME(),4);
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end)
            VALUES(@batch,N'r011check_r010.xlsx',REPLICATE('e',64),10,'R010','R011CHECK','20260803','20260803','20260803');
            SET @r010=SCOPE_IDENTITY();
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end)
            VALUES(@batch,N'r011check_r011.xlsx',REPLICATE('f',64),10,'CLOSING_STOCK','R011CHECK','20260803','20260803','20260803');
            SET @r011=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@r010,N'Data',2,'R010_SNAPSHOT'); SET @lineage=SCOPE_IDENTITY();
            INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,batch_number,quantity,source_lineage_id,source_report_code)
            VALUES('R011CHECK','20260803',N'SNAP-1',N'LOT-1',5,@lineage,'R010');
            SELECT @hash=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(ISNULL(ean,N''),N'|',ISNULL(brand_code,N''),N'|',ISNULL(brand_name,N''),N'|',
              ISNULL(cluster,N''),N'|',ISNULL(gender,N''),N'|',ISNULL(batch_number,N''),N'|',ISNULL(source_uid,N''),N'|',quantity,N'|',
              ISNULL(unit_cost,0),N'|',ISNULL(total_cost,0))),2)) FROM dbo.stock_snapshots WHERE source_lineage_id=@lineage;
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@r011,N'Sheet0',2,'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();
            INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message)
            VALUES(@r011,@lineage,N'R011CHECK/2026-08-03/SNAP-1/LOT-1','ALREADY_PRESENT',@hash,N'Identical stock snapshot row already exists.');
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@r011,N'Sheet0',3,'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();
            INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message)
            VALUES(@r011,@lineage,N'R011CHECK/2026-08-03/SNAP-2/LOT-1','CONFLICT',REPLICATE('d',64),N'Stock snapshot identity exists with different content.');
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@r011,N'Sheet0',4,'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();
            INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message)
            VALUES(@r011,@lineage,N'R011CHECK/2026-08-03/CLOSING_STOCK/SNAP-3/LOT-1/#1','ALREADY_PRESENT',REPLICATE('d',64),N'Identical stock snapshot row already exists.');
            IF DATABASE_PRINCIPAL_ID(N'upgrade_checker') IS NULL
            BEGIN CREATE USER upgrade_checker WITHOUT LOGIN; ALTER ROLE db_datareader ADD MEMBER upgrade_checker; END;
            """);

        var sets = await RunCheckAsync(database.ConnectionString, script);

        var summary = sets[1].Rows.ToDictionary(row => (string)row[0], row => (Findings: Convert.ToInt64(row[1]), Blocks: (bool)row[2], Detail: (string)row[3]));
        Assert.Equal((1L, false), (summary["SNAPSHOT_R011_BACKFILL"].Findings, summary["SNAPSHOT_R011_BACKFILL"].Blocks));
        Assert.Contains("rebuilds 1 of them", summary["SNAPSHOT_R011_BACKFILL"].Detail);
        Assert.Contains("; 1 keep their BinWise reading", summary["SNAPSHOT_R011_BACKFILL"].Detail);
        var detail = Assert.Single(sets.Skip(3), set => (string)set.Rows[0][0] == "SNAPSHOT_R011_BACKFILL");
        Assert.Equal(["check_code", "store_code", "snapshot_date", "day_locked", "rows_rebuilt", "rows_without_values"], detail.Columns);
        var row = Assert.Single(detail.Rows);
        Assert.Equal(("R011CHECK", new DateTime(2026, 8, 3), 0, 1, 1),
            ((string)row[1], (DateTime)row[2], Convert.ToInt32(row[3]), Convert.ToInt32(row[4]), Convert.ToInt32(row[5])));
    }

    [Fact]
    public async Task Upgrade_check_script_reads_the_line_and_source_columns_once_0041_adds_them()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "check-import-upgrade.sql"));
        var fresh = new SqlDatabaseFixture();
        await fresh.InitializeAsync();
        try
        {
            // The shape 0041 gives the stock tables, added here only when the migration has not done so.
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

    [Fact]
    public async Task Upgrade_check_script_refuses_a_database_from_before_0017_instead_of_failing_to_compile()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "check-import-upgrade.sql"));
        var migrations = Path.Combine(Path.GetTempPath(), "EtpPre0017_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(migrations);
        var name = "EtpPhase0Test_" + Guid.NewGuid().ToString("N");
        var connectionString = TestSqlConnections.ForDatabase(name);
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "database", "migrations"), "*.sql")
                         .Where(file => string.CompareOrdinal(Path.GetFileName(file), "0017") < 0))
                File.Copy(file, Path.Combine(migrations, Path.GetFileName(file)));
            await new SqlServerDatabaseBootstrapper(connectionString, new DirectoryMigrationSource(migrations)).BootstrapAsync();
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            Assert.Equal(DBNull.Value, await new SqlCommand("SELECT COL_LENGTH(N'dbo.import_files',N'data_truth_version')", connection).ExecuteScalarAsync() ?? DBNull.Value);
            await using var command = new SqlCommand(script, connection) { CommandTimeout = 120 };
            var refused = (string?)await command.ExecuteScalarAsync();
            Assert.Contains("predates migration 0018", refused);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
            await using (var connection = new SqlConnection(master.ConnectionString))
            {
                await connection.OpenAsync();
                await using var drop = new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", connection);
                await drop.ExecuteNonQueryAsync();
            }
            Directory.Delete(migrations, true);
        }
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
