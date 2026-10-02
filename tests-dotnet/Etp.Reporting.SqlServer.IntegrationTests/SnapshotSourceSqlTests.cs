using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Stock;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// Migration 0041 section D (IF-020): snapshot facts carry their source and line, and readers use
// dbo.v_stock_snapshots_effective, so an R010 and an R011 reading of one store-day are never summed.
public sealed class SnapshotSourceSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task R010_and_R011_same_day_report_reads_R011_only()
    {
        var store = new SqlServerTransactionalImportStore(database.ConnectionString);
        var day = new DateOnly(2026, 8, 25);
        await new StockSqlImportOrchestrator(store).PersistAsync(ClosingStock(day, "1111", ("SNAP-A", 4m)));
        await new EtpFamilySqlImportOrchestrator(store).PersistAsync(
            new MatchedImportEnvelopeFactory().RequireAccepted(BinWise("202608251457", "2222", ("SNAP-A", 5m), ("SNAP-B", 2m))));
        // A store-day with only a BinWise reading still reports, from R010.
        await new EtpFamilySqlImportOrchestrator(store).PersistAsync(
            new MatchedImportEnvelopeFactory().RequireAccepted(BinWise("202608241457", "3333", ("SNAP-A", 7m))));

        Assert.Equal("CLOSING_STOCK:1,R010:3", await database.ExecuteAsync(
            "SELECT STRING_AGG(CONCAT(source_report_code,':',n),',') WITHIN GROUP(ORDER BY source_report_code) FROM (SELECT source_report_code,COUNT(*) n FROM dbo.stock_snapshots WHERE store_code='HEMW' AND snapshot_date BETWEEN '20260824' AND '20260825' GROUP BY source_report_code) x"));
        Assert.Equal(4m, await database.ExecuteAsync("SELECT SUM(quantity) FROM dbo.v_stock_snapshots_effective WHERE store_code='HEMW' AND snapshot_date='20260825'"));

        var reports = new OperationalReportRepository(database.ConnectionString);
        var sameDay = await reports.LoadStockInventoryAsync(new ReportingQueryScope(day, day, ["HEMW"]));
        var row = Assert.Single(sameDay);
        Assert.Equal(("SNAP-A", 4m, "Closing Stock"), (row.ProductCode, row.Quantity, row.SnapshotSource));
        var binWiseOnly = await reports.LoadStockInventoryAsync(new ReportingQueryScope(day.AddDays(-1), day.AddDays(-1), ["HEMW"]));
        Assert.Equal(("SNAP-A", 7m, "BinWise"), (Assert.Single(binWiseOnly).ProductCode, binWiseOnly[0].Quantity, binWiseOnly[0].SnapshotSource));

    }

    [Fact]
    public async Task Repeated_identical_snapshot_rows_both_stored()
    {
        var store = new SqlServerTransactionalImportStore(database.ConnectionString);
        await new StockSqlImportOrchestrator(store).PersistAsync(ClosingStock(new(2026, 8, 20), "4444", ("SNAP-R", 1m), ("SNAP-R", 1m), ("SNAP-S", 3m)));

        Assert.Equal("1,2", await database.ExecuteAsync("SELECT STRING_AGG(line_seq,',') WITHIN GROUP(ORDER BY line_seq) FROM dbo.stock_snapshots WHERE store_code='HEMW' AND snapshot_date='20260820' AND product_code='SNAP-R'"));
        Assert.Equal(3, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_row_outcomes o JOIN dbo.stock_snapshots s ON s.source_lineage_id=o.source_lineage_id WHERE s.snapshot_date='20260820' AND o.outcome='NEW'"));
        Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE business_identity LIKE N'HEMW/2026-08-20/%' AND outcome IN('ALREADY_PRESENT','CONFLICT')"));
        Assert.Equal(5m, await database.ExecuteAsync("SELECT SUM(quantity) FROM dbo.v_stock_snapshots_effective WHERE store_code='HEMW' AND snapshot_date='20260820'"));

        // The same rows in another order are the same content: the stored multiset does not depend on row order.
        await new StockSqlImportOrchestrator(store).PersistAsync(ClosingStock(new(2026, 8, 20), "5555", ("SNAP-S", 3m), ("SNAP-R", 1m), ("SNAP-R", 1m)));
        Assert.Equal(3, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.stock_snapshots WHERE store_code='HEMW' AND snapshot_date='20260820'"));
        // Each repeat is matched by its own line: line 2 is already present, line 3 is new.
        Assert.Equal("ALREADY_PRESENT,NEW", await database.ExecuteAsync("""
            DECLARE @lineage bigint,@file bigint,@ean nvarchar(80),@brand nvarchar(80),@brandname nvarchar(200),@cluster nvarchar(100),@gender nvarchar(50),@unit decimal(19,4),@total decimal(19,4);
            SELECT @file=l.import_file_id,@ean=s.ean,@brand=s.brand_code,@brandname=s.brand_name,@cluster=s.cluster,@gender=s.gender,@unit=s.unit_cost,@total=s.total_cost
             FROM dbo.stock_snapshots s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id WHERE s.snapshot_date='20260820' AND s.product_code='SNAP-R' AND s.line_seq=2;
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet0',90,'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();
            EXEC dbo.persist_stock_snapshot @store='HEMW',@date='20260820',@product=N'SNAP-R',@ean=@ean,@brand=@brand,@brandname=@brandname,@cluster=@cluster,@gender=@gender,@batch=N'LOT-1',@qty=1,@unit=@unit,@total=@total,@lineage=@lineage,@source='CLOSING_STOCK',@line_seq=2;
            EXEC dbo.persist_stock_snapshot @store='HEMW',@date='20260820',@product=N'SNAP-R',@ean=@ean,@brand=@brand,@brandname=@brandname,@cluster=@cluster,@gender=@gender,@batch=N'LOT-1',@qty=1,@unit=@unit,@total=@total,@lineage=@lineage,@source='CLOSING_STOCK',@line_seq=3;
            SELECT STRING_AGG(outcome,',') WITHIN GROUP(ORDER BY import_row_outcome_id) FROM dbo.import_row_outcomes WHERE source_lineage_id=@lineage;
            """));
    }

    [Fact]
    public async Task Reordered_snapshot_file_matches_each_repeated_line_through_the_importer()
    {
        var store = new SqlServerTransactionalImportStore(database.ConnectionString);
        var day = new DateOnly(2026, 8, 18);
        await new StockSqlImportOrchestrator(store).PersistAsync(ClosingStock(day, "8888", ("SNAP-R", 1m), ("SNAP-S", 3m), ("SNAP-R", 1m)));
        // Another file for the day with the same rows in another order and one more item: not a content duplicate,
        // so every row reaches persist_stock_snapshot and each repeat is matched by its own line.
        await new StockSqlImportOrchestrator(store).PersistAsync(ClosingStock(day, "9999", ("SNAP-S", 3m), ("SNAP-R", 1m), ("SNAP-T", 2m), ("SNAP-R", 1m)));

        Assert.Equal("SNAP-R:1,SNAP-R:2,SNAP-S:1,SNAP-T:1", await database.ExecuteAsync(
            "SELECT STRING_AGG(CONCAT(product_code,':',line_seq),',') WITHIN GROUP(ORDER BY product_code,line_seq) FROM dbo.stock_snapshots WHERE store_code='HEMW' AND snapshot_date='20260818'"));
        Assert.Equal("ALREADY_PRESENT:3,NEW:1", await database.ExecuteAsync("""
            SELECT STRING_AGG(CONCAT(outcome,':',n),',') WITHIN GROUP(ORDER BY outcome) FROM (
             SELECT o.outcome,COUNT(*) n FROM dbo.import_row_outcomes o JOIN dbo.import_files f ON f.import_file_id=o.import_file_id
             WHERE f.source_sha256 LIKE '9999%' AND o.business_identity LIKE N'HEMW/2026-08-18/%' GROUP BY o.outcome) x
            """));
        Assert.Equal(7m, await database.ExecuteAsync("SELECT SUM(quantity) FROM dbo.v_stock_snapshots_effective WHERE store_code='HEMW' AND snapshot_date='20260818'"));
    }

    [Fact]
    public async Task Source_that_contradicts_the_lineage_is_refused()
    {
        var refused = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@closing bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count) VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),1);
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
             VALUES(@batch,'WRONG-SOURCE.xlsx',REPLICATE('4',64),2048,'CLOSING_STOCK','WRONGSRC','20260814','20260814','20260814',1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet0',2,'CLOSING_STOCK'); SET @closing=SCOPE_IDENTITY();
            EXEC dbo.persist_stock_snapshot @store='WRONGSRC',@date='20260814',@product=N'SNAP-W',@batch=N'LOT-1',@qty=1,@lineage=@closing,@source='R010';
            """));
        Assert.Equal(51760, refused.Number);
        Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.stock_snapshots WHERE store_code='WRONGSRC'"));
    }

    [Fact]
    public async Task Caller_without_source_gets_it_from_lineage()
    {
        // An older caller passes neither @source nor @line_seq.
        await database.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@binwise bigint,@closing bigint,@again bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count) VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),3);
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
             VALUES(@batch,'LEGACY-SNAPSHOT.xlsx',REPLICATE('6',64),2048,'R010','LEGACY','20260815','20260815','20260815',1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Data',2,'R010_SNAPSHOT'); SET @binwise=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet0',2,'CLOSING_STOCK'); SET @closing=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet0',3,'CLOSING_STOCK'); SET @again=SCOPE_IDENTITY();
            EXEC dbo.persist_stock_snapshot @store='LEGACY',@date='20260815',@product=N'SNAP-L',@batch=N'LOT-1',@qty=2,@lineage=@binwise;
            EXEC dbo.persist_stock_snapshot @store='LEGACY',@date='20260815',@product=N'SNAP-L',@batch=N'LOT-1',@qty=2,@lineage=@closing;
            EXEC dbo.persist_stock_snapshot @store='LEGACY',@date='20260815',@product=N'SNAP-L',@batch=N'LOT-1',@qty=9,@lineage=@again;
            """);

        Assert.Equal("CLOSING_STOCK:1,R010:1", await database.ExecuteAsync(
            "SELECT STRING_AGG(CONCAT(source_report_code,':',line_seq),',') WITHIN GROUP(ORDER BY source_report_code) FROM dbo.stock_snapshots WHERE store_code='LEGACY'"));
        Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.stock_snapshots WHERE source_report_code IS NULL"));
        // The differing R011 reading conflicts with its own source, and the conflict names that source.
        Assert.Equal("CLOSING_STOCK", await database.ExecuteAsync("SELECT report_code FROM dbo.import_conflicts WHERE store_code='LEGACY'"));
        Assert.Equal("LEGACY/2026-08-15/CLOSING_STOCK/SNAP-L/LOT-1/#1", await database.ExecuteAsync("SELECT business_identity FROM dbo.import_conflicts WHERE store_code='LEGACY'"));
        Assert.Equal(StockSnapshotSources.BinWise, StockSnapshotSources.FromLineageRecordType("R010_SNAPSHOT"));
        Assert.Equal(StockSnapshotSources.ClosingStock, StockSnapshotSources.FromLineageRecordType("CLOSING_STOCK"));
    }

    [Fact]
    public async Task Locked_snapshot_day_backfill_succeeds_with_trigger_list()
    {
        var name = "EtpPhase0Test_SnapshotSource_" + Guid.NewGuid().ToString("N");
        var connectionString = TestSqlConnections.ForDatabase(name, pooling: false);
        try
        {
            var source = new DirectoryMigrationSource(database.MigrationDirectory);
            await new SqlServerDatabaseBootstrapper(connectionString, new BeforeImportEngine(source)).BootstrapAsync();
            await Execute(connectionString, """
                DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@lineage bigint;
                INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count) VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),4);
                INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
                 VALUES(@batch,'LOCKED-SNAPSHOT.xlsx',REPLICATE('7',64),2048,'CLOSING_STOCK','LOCKSNAP','20260810','20260810','20260810',1);
                SET @file=SCOPE_IDENTITY();
                INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet0',2,'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();
                INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,batch_number,quantity,source_lineage_id) VALUES('LOCKSNAP','20260810','SNAP-K','LOT-1',2,@lineage);
                INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet0',3,'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();
                INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,batch_number,quantity,source_lineage_id) VALUES('LOCKSNAP','20260810','SNAP-K','LOT-1',1,@lineage);
                INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Data',2,'R010_SNAPSHOT'); SET @lineage=SCOPE_IDENTITY();
                INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,batch_number,quantity,source_lineage_id) VALUES('LOCKSNAP','20260810','SNAP-K','LOT-1',2,@lineage);
                INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('LOCKSNAP','20260810','LOCKED',SUSER_SNAME(),SYSUTCDATETIME());
                """);
            // Without the trigger in the migration's list the backfill would throw 51034 on the locked day.
            var refused = await Assert.ThrowsAsync<SqlException>(() => Execute(connectionString, "UPDATE dbo.stock_snapshots SET quantity=quantity WHERE store_code='LOCKSNAP'"));
            Assert.Equal(51034, refused.Number);

            await new MigrationRunner(source, new SqlServerMigrationStore(connectionString)).RunAsync();

            Assert.Equal("CLOSING_STOCK:1:1,CLOSING_STOCK:2:2,R010:2:1", await Execute(connectionString,
                "SELECT STRING_AGG(CONCAT(source_report_code,':',CONVERT(int,quantity),':',line_seq),',') WITHIN GROUP(ORDER BY source_report_code,quantity) FROM dbo.stock_snapshots WHERE store_code='LOCKSNAP'"));
            Assert.Equal("NO", await Execute(connectionString, "SELECT CASE COLUMNPROPERTY(OBJECT_ID(N'dbo.stock_snapshots'),'source_report_code','AllowsNull') WHEN 1 THEN 'YES' ELSE 'NO' END"));
            Assert.Equal(false, await Execute(connectionString, "SELECT is_disabled FROM sys.triggers WHERE name='trg_stock_snapshots_protect_locked'"));
            Assert.Equal(2, await Execute(connectionString, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.stock_snapshots') AND name IN('UX_stock_snapshots_identity','IX_stock_snapshots_source')"));
            Assert.Equal(3m, await Execute(connectionString, "SELECT SUM(quantity) FROM dbo.v_stock_snapshots_effective WHERE store_code='LOCKSNAP'"));
            foreach (var role in new[] { "etp_viewer", "etp_store_manager", "etp_owner" })
                Assert.Equal(1, await Execute(connectionString, $"SELECT COUNT(*) FROM sys.database_permissions p WHERE p.major_id=OBJECT_ID(N'dbo.v_stock_snapshots_effective') AND p.permission_name='SELECT' AND p.state='G' AND USER_NAME(p.grantee_principal_id)='{role}'"));
            // The guard is back on: the locked day still refuses a change.
            var stillLocked = await Assert.ThrowsAsync<SqlException>(() => Execute(connectionString, "UPDATE dbo.stock_snapshots SET quantity=quantity WHERE store_code='LOCKSNAP'"));
            Assert.Equal(51034, stillLocked.Number);
        }
        finally { await Drop(connectionString, name); }
    }

    [Fact]
    public async Task Legacy_day_with_R010_imported_first_keeps_its_R011_rows_after_backfill()
    {
        var name = "EtpPhase0Test_SnapshotLegacy_" + Guid.NewGuid().ToString("N");
        var connectionString = TestSqlConnections.ForDatabase(name, pooling: false);
        try
        {
            var source = new DirectoryMigrationSource(database.MigrationDirectory);
            await new SqlServerDatabaseBootstrapper(connectionString, new BeforeImportEngine(source)).BootstrapAsync();
            // The pre-0041 procedure: R010 first, then R011. The identical R011 rows were logged ALREADY_PRESENT
            // against the R010 row and not stored; the differing one was a CONFLICT; SNAP-N was new.
            await Execute(connectionString, """
                DECLARE @batch uniqueidentifier=NEWID(),@r010 bigint,@r011 bigint,@lineage bigint;
                INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count) VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),7);
                INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
                 VALUES(@batch,'LEGACY-R010.xlsx',REPLICATE('2',64),2048,'R010','MIXSNAP','20260805','20260805','20260805',1);
                SET @r010=SCOPE_IDENTITY();
                INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
                 VALUES(@batch,'LEGACY-R011.xlsx',REPLICATE('3',64),2048,'CLOSING_STOCK','MIXSNAP','20260805','20260805','20260805',1);
                SET @r011=SCOPE_IDENTITY();
                INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@r010,'Data',2,'R010_SNAPSHOT'); SET @lineage=SCOPE_IDENTITY();
                EXEC dbo.persist_stock_snapshot @store='MIXSNAP',@date='20260805',@product=N'SNAP-M',@batch=N'LOT-1',@qty=2,@unit=10,@total=20,@lineage=@lineage;
                INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@r010,'Data',3,'R010_SNAPSHOT'); SET @lineage=SCOPE_IDENTITY();
                EXEC dbo.persist_stock_snapshot @store='MIXSNAP',@date='20260805',@product=N'SNAP-P',@batch=N'LOT-1',@qty=5,@lineage=@lineage;
                INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@r011,'Sheet0',2,'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();
                EXEC dbo.persist_stock_snapshot @store='MIXSNAP',@date='20260805',@product=N'SNAP-M',@batch=N'LOT-1',@qty=2,@unit=10,@total=20,@lineage=@lineage;
                INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@r011,'Sheet0',3,'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();
                EXEC dbo.persist_stock_snapshot @store='MIXSNAP',@date='20260805',@product=N'SNAP-M',@batch=N'LOT-1',@qty=2,@unit=10,@total=20,@lineage=@lineage;
                INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@r011,'Sheet0',4,'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();
                EXEC dbo.persist_stock_snapshot @store='MIXSNAP',@date='20260805',@product=N'SNAP-P',@batch=N'LOT-1',@qty=6,@lineage=@lineage;
                INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@r011,'Sheet0',5,'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();
                EXEC dbo.persist_stock_snapshot @store='MIXSNAP',@date='20260805',@product=N'SNAP-N',@batch=N'LOT-1',@qty=3,@lineage=@lineage;
                """);
            Assert.Equal("ALREADY_PRESENT:2,CONFLICT:1,NEW:3", await Execute(connectionString,
                "SELECT STRING_AGG(CONCAT(outcome,':',n),',') WITHIN GROUP(ORDER BY outcome) FROM (SELECT outcome,COUNT(*) n FROM dbo.import_row_outcomes WHERE business_identity LIKE N'MIXSNAP/%' GROUP BY outcome) x"));

            await new MigrationRunner(source, new SqlServerMigrationStore(connectionString)).RunAsync();

            // Both repeated SNAP-M lines of the R011 file are stored as Closing Stock; the R010 rows stay as they were.
            Assert.Equal("CLOSING_STOCK:SNAP-M:2:1,CLOSING_STOCK:SNAP-M:2:2,CLOSING_STOCK:SNAP-N:3:1,R010:SNAP-M:2:1,R010:SNAP-P:5:1", await Execute(connectionString,
                "SELECT STRING_AGG(CONCAT(source_report_code,':',product_code,':',CONVERT(int,quantity),':',line_seq),',') WITHIN GROUP(ORDER BY source_report_code,product_code,line_seq) FROM dbo.stock_snapshots WHERE store_code='MIXSNAP'"));
            Assert.Equal(7m, await Execute(connectionString, "SELECT SUM(quantity) FROM dbo.v_stock_snapshots_effective WHERE store_code='MIXSNAP'"));
            // The differing SNAP-P reading stays with the conflict review; only its hash was ever kept.
            Assert.Equal(1, await Execute(connectionString, "SELECT COUNT(*) FROM dbo.import_conflicts WHERE store_code='MIXSNAP'"));
            Assert.Equal(0, await Execute(connectionString, "SELECT COUNT(*) FROM sys.database_permissions WHERE major_id=OBJECT_ID(N'dbo.v_stock_snapshots_effective') AND state='D' AND USER_NAME(grantee_principal_id)='etp_owner'"));
        }
        finally { await Drop(connectionString, name); }
    }

    private static WorkbookSnapshot ClosingStock(DateOnly date, string hashDigit, params (string Item, decimal Quantity)[] items) =>
        new($"{date:yyyyMMdd}1500_R011_Closing_Stock_{hashDigit}.xlsx", 10, string.Concat(Enumerable.Repeat(hashDigit, 16)),
        [new("Sheet0", 1, StockImportProfiles.ClosingStockHeaders, items.Select((item, i) => new WorkbookRow(i + 2, new object?[]
        {
            "HEMW", "Synthetic", "Retail", "Store", "Region", "State", "City", date.ToDateTime(TimeOnly.MinValue), item.Item, "HSN", "Description",
            "EAN-1", "BR", "Cluster", "U", item.Quantity, 10m, 10m * item.Quantity, "LOT-1", null
        }.Select(value => new WorkbookCell(value)).ToArray())).ToArray())]);

    private static WorkbookSnapshot BinWise(string exportPrefix, string hashDigit, params (string Item, decimal Quantity)[] items)
    {
        var family = EtpReportFamilyRegistry.Resolve("R010");
        var columns = family.Headers.Select(header => family.Columns.Single(column => column.SourceHeader == header)).ToArray();
        WorkbookRow Row(int number, string item, decimal quantity) => new(number, columns.Select(column => new WorkbookCell(column.CanonicalField switch
        {
            "store_code" => "HEMW",
            "itemnumber" => item,
            "lotnumber" => "LOT-1",
            "uid" => null,
            "closingbalance" => quantity,
            "ucp" => 10m,
            "totalucp" => 10m * quantity,
            _ => column.DataType == Etp.Reporting.Domain.Imports.CanonicalDataType.Decimal ? (object)0m : "SYNTHETIC"
        })).ToArray());
        return new($"{exportPrefix}_R010_BinWise_Stock.xlsx", 10, string.Concat(Enumerable.Repeat(hashDigit, 16)),
            [new("Data", 1, family.Headers, items.Select((item, i) => Row(i + 2, item.Item, item.Quantity)).ToArray())]);
    }

    private static async Task<object?> Execute(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task Drop(string connectionString, string name)
    {
        if (!name.StartsWith("EtpPhase0Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture name.");
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class BeforeImportEngine(IMigrationSource source) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken token = default) =>
            (await source.DiscoverAsync(token)).Where(migration => string.CompareOrdinal(migration.Id, "0041") < 0).ToArray();
    }
}
