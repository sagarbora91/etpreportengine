using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;
using static Etp.Reporting.TestSupport.StackedBinWiseWorkbooks;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// 1.12.1 lane P6, decision 66. Cross-layer: synthetic workbooks through the real importer (MatchedImportEnvelopeFactory,
// ImportScope, the orchestrators, SqlServerTransactionalImportStore) into SQL (dbo.retire_redated_import,
// dbo.v_stock_snapshots_effective). Each test uses its own store; every value is synthetic.
public sealed class RedatedSnapshotImportSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    // IF-028: the same file (same SHA-256) imported again under a corrected date retires the earlier import by itself.
    [SqlFact]
    public async Task Same_file_imported_under_a_corrected_date_retires_the_earlier_import_as_re_dated()
    {
        const string store = "REDATEA";
        var importer = new EtpFamilySqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString));
        // The pre-1.9.3 reading: the file dated 29 Sep. The corrected reading of the same bytes: 7 Sep.
        var wrong = await importer.PersistAsync(Accept(Single(store, "202609291433", "e1", Units)));
        Assert.Equal("2026-09-29:2", await SnapshotCounts(store));

        var corrected = await importer.PersistAsync(Accept(Single(store, "202609071848", "e1", Units)));

        Assert.NotEqual(wrong, corrected);
        Assert.Equal("2026-09-07:2", await SnapshotCounts(store));
        Assert.Equal(3m, await database.ExecuteAsync($"SELECT SUM(quantity) FROM dbo.v_stock_snapshots_effective WHERE store_code='{store}' AND snapshot_date='20260907'"));
        await AssertRetired(wrong, corrected, "2026-09-07", "2026-09-29");
        Assert.False(await IsSuperseded(corrected));
    }

    // IF-028, the live HEMW state of 9 Oct: the corrected import already exists and the wrongly dated copy is still
    // current. Importing the file again finds it already imported, and still retires the copy.
    [SqlFact]
    public async Task Re_import_of_an_already_corrected_file_retires_the_wrongly_dated_copy_left_beside_it()
    {
        const string store = "REDATEB";
        var importer = new EtpFamilySqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString));
        var files = new SqlServerImportFileRepository(database.ConnectionString);
        var corrected = await importer.PersistAsync(Accept(Single(store, "202609071848", "e2", Units)));
        var wrong = await InsertMisdatedCopy(store, Hash("e2"), new(2026, 9, 29));
        Assert.Equal("2026-09-07:2,2026-09-29:2", await SnapshotCounts(store));
        // While the copy is current the file is not "already imported": the import goes on to the planner.
        Assert.False(await files.ExistsInScopeAsync(Hash("e2"), "R010", store, new(2026, 9, 7), new(2026, 9, 7)));

        var again = await importer.PersistAsync(Accept(Single(store, "202609071848", "e2", Units)));

        Assert.Equal(corrected, again);
        Assert.Equal("2026-09-07:2", await SnapshotCounts(store));
        await AssertRetired(wrong, corrected, "2026-09-07", "2026-09-29");
        Assert.True(await files.ExistsInScopeAsync(Hash("e2"), "R010", store, new(2026, 9, 7), new(2026, 9, 7)));
        // A third import is an ordinary duplicate and changes nothing.
        Assert.Equal(corrected, await importer.PersistAsync(Accept(Single(store, "202609071848", "e2", Units))));
        Assert.Equal(1, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.import_restatements WHERE previous_import_file_id={wrong}"));
    }

    // IF-028: a finalised day under the wrongly dated copy stops the import with nothing changed; it is never forced.
    [SqlFact]
    public async Task Wrongly_dated_copy_on_a_finalised_day_is_refused_and_nothing_changes()
    {
        const string store = "REDATEL";
        var importer = new EtpFamilySqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString));
        var corrected = await importer.PersistAsync(Accept(Single(store, "202609071848", "e3", Units)));
        var wrong = await InsertMisdatedCopy(store, Hash("e3"), new(2026, 9, 29));
        await database.ExecuteAsync($"INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('{store}','20260929','LOCKED',SUSER_SNAME(),SYSUTCDATETIME())");

        var refused = await Assert.ThrowsAsync<SqlException>(() => importer.PersistAsync(Accept(Single(store, "202609071848", "e3", Units))));

        Assert.Equal(51042, refused.Number);
        Assert.False(await IsSuperseded(wrong));
        Assert.False(await IsSuperseded(corrected));
        Assert.Equal("2026-09-07:2,2026-09-29:2", await SnapshotCounts(store));
        Assert.Equal(0, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.import_restatements WHERE previous_import_file_id={wrong}"));
    }

    // IF-032: one dating rule for evening Closing Stock and BinWise exports, and a BinWise fallback counts the retail bin.
    [SqlFact]
    public async Task Evening_closing_stock_and_binwise_share_one_day_and_the_binwise_fallback_counts_the_retail_bin()
    {
        const string store = "REDATEC";
        var transactional = new SqlServerTransactionalImportStore(database.ConnectionString);
        var binWise = new EtpFamilySqlImportOrchestrator(transactional);
        // The 3 Oct Titan pair: BinWise at 17:45 (all bins 3, retail 2) and Closing Stock at 18:00 whose Date column says 2 Oct.
        await binWise.PersistAsync(Accept(BinWise(store, "202610031745", "f1", ("BW-1", 2m, 1m))));
        var closing = await new StockSqlImportOrchestrator(transactional).PersistAsync(ClosingStock(store, "202610031800", "f2", new(2026, 10, 2), ("BW-1", 2m)));

        Assert.Equal("2026-10-03:CLOSING_STOCK:1,2026-10-03:R010:1", await SnapshotSources(store));
        Assert.Equal("2026-10-03|2026-10-03", await database.ExecuteAsync(
            $"SELECT CONCAT(CONVERT(char(10),period_start,23),'|',CONVERT(char(10),period_end,23)) FROM dbo.import_files WHERE import_file_id={closing.ImportFileId}"));
        // The landed source row keeps the Date column as ETP exported it.
        Assert.Equal("2026-10-02", await database.ExecuteAsync(
            $"SELECT CONVERT(char(10),MAX(snapshot_date),23) FROM dbo.etp_r011 WHERE import_file_id={closing.ImportFileId}"));
        // One basis per day: 3 Oct reads the retail closing only, and 2 Oct has no reading.
        Assert.Equal("CLOSING_STOCK:2", await Effective(store, "20261003"));
        Assert.Equal(0, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.v_stock_snapshots_effective WHERE store_code='{store}' AND snapshot_date='20261002'"));

        // A next-morning BinWise (5 Oct 09:30) is the closing of 4 Oct, a day with no Closing Stock: retail bin only.
        await binWise.PersistAsync(Accept(BinWise(store, "202610050930", "f3", ("BW-1", 1m, 1m))));
        Assert.Equal(2m, await database.ExecuteAsync($"SELECT SUM(quantity) FROM dbo.stock_snapshots WHERE store_code='{store}' AND snapshot_date='20261004'"));
        Assert.Equal("R010:1", await Effective(store, "20261004"));
        Assert.Equal(10m, await database.ExecuteAsync($"SELECT SUM(total_cost) FROM dbo.v_stock_snapshots_effective WHERE store_code='{store}' AND snapshot_date='20261004'"));
        var day = new DateOnly(2026, 10, 4);
        var row = Assert.Single(await new OperationalReportRepository(database.ConnectionString).LoadStockInventoryAsync(new ReportingQueryScope(day, day, [store])));
        Assert.Equal(("BW-1", 1m, "BinWise"), (row.ProductCode, row.Quantity, row.SnapshotSource));
    }

    // IF-032 then IF-028: a Closing Stock file stored under its Date column (before the rule) and imported again is
    // re-dated to its export day, and the earlier import is retired.
    [SqlFact]
    public async Task Closing_stock_stored_under_its_column_date_is_re_dated_by_its_re_import()
    {
        const string store = "REDATED";
        var stock = new StockSqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString));
        // Before the rule this 18:00 export was stored for 2 Oct; a name without a time keeps the column date, as then.
        var before = await stock.PersistAsync(ClosingStock(store, "R011", "f4", new(2026, 10, 2), ("CS-1", 3m)));
        Assert.Equal("2026-10-02:CLOSING_STOCK:1", await SnapshotSources(store));

        var after = await stock.PersistAsync(ClosingStock(store, "202610031800", "f4", new(2026, 10, 2), ("CS-1", 3m)));

        Assert.Equal("2026-10-03:CLOSING_STOCK:1", await SnapshotSources(store));
        await AssertRetired(before.ImportFileId, after.ImportFileId, "2026-10-03", "2026-10-02");
    }

    private async Task AssertRetired(long wrong, long replacement, string newDate, string oldDate)
    {
        Assert.True(await IsSuperseded(wrong));
        Assert.Equal(replacement, await database.ExecuteAsync($"SELECT superseded_by_import_file_id FROM dbo.import_files WHERE import_file_id={wrong}"));
        var reason = (string)(await database.ExecuteAsync($"SELECT restatement_reason FROM dbo.import_files WHERE import_file_id={wrong}"))!;
        Assert.StartsWith("re-dated:", reason, StringComparison.Ordinal);
        Assert.Contains(newDate, reason, StringComparison.Ordinal);
        Assert.Contains(oldDate, reason, StringComparison.Ordinal);
        // Audited and never deleted: the restatement row, the archived facts and the import record with its source rows.
        Assert.Equal(1, await database.ExecuteAsync(
            $"SELECT COUNT(*) FROM dbo.import_restatements WHERE previous_import_file_id={wrong} AND replacement_import_file_id={replacement} AND reason LIKE N're-dated:%'"));
        Assert.True(Convert.ToInt32(await database.ExecuteAsync($"""
            SELECT COUNT(*) FROM dbo.restatement_fact_archive a JOIN dbo.import_restatements r ON r.import_restatement_id=a.import_restatement_id
            WHERE r.previous_import_file_id={wrong} AND a.fact_type='StockSnapshot'
            """)) > 0);
        Assert.Equal(0, await database.ExecuteAsync(
            $"SELECT COUNT(*) FROM dbo.stock_snapshots s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id WHERE l.import_file_id={wrong}"));
        Assert.Equal(1, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.import_files WHERE import_file_id={wrong}"));
    }

    // The 1 Oct state of HEMW import 26: a current R010 import of the same bytes, stored under the wrong date before 1.9.3.
    private async Task<long> InsertMisdatedCopy(string store, string hash, DateOnly date) =>
        Convert.ToInt64(await database.ExecuteAsync($"""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@lineage bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count) VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),2);
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
             VALUES(@batch,'R010_BinWise_Stock.xlsx','{hash}',10,'R010','{store}','{date:yyyyMMdd}','{date:yyyyMMdd}','{date:yyyyMMdd}',1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Data',2,'R010_SNAPSHOT'); SET @lineage=SCOPE_IDENTITY();
            EXEC dbo.persist_stock_snapshot @store='{store}',@date='{date:yyyyMMdd}',@product=N'UNIT-A',@batch=N'LOT-1',@qty=1,@lineage=@lineage,@source='R010';
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Data',3,'R010_SNAPSHOT'); SET @lineage=SCOPE_IDENTITY();
            EXEC dbo.persist_stock_snapshot @store='{store}',@date='{date:yyyyMMdd}',@product=N'UNIT-B',@batch=N'LOT-1',@qty=2,@lineage=@lineage,@source='R010';
            SELECT @file;
            """));

    private static MatchedImportEnvelope Accept(WorkbookSnapshot workbook) => new MatchedImportEnvelopeFactory().RequireAccepted(workbook);

    private async Task<string?> SnapshotCounts(string store) => await database.ExecuteAsync($"""
        SELECT STRING_AGG(CONCAT(CONVERT(char(10),snapshot_date,23),':',n),',') WITHIN GROUP(ORDER BY snapshot_date)
        FROM (SELECT snapshot_date,COUNT(*) n FROM dbo.stock_snapshots WHERE store_code='{store}' GROUP BY snapshot_date) x
        """) as string;

    private async Task<string?> SnapshotSources(string store) => await database.ExecuteAsync($"""
        SELECT STRING_AGG(CONCAT(CONVERT(char(10),snapshot_date,23),':',source_report_code,':',n),',') WITHIN GROUP(ORDER BY snapshot_date,source_report_code)
        FROM (SELECT snapshot_date,source_report_code,COUNT(*) n FROM dbo.stock_snapshots WHERE store_code='{store}' GROUP BY snapshot_date,source_report_code) x
        """) as string;

    private async Task<string?> Effective(string store, string date) => await database.ExecuteAsync($"""
        SELECT STRING_AGG(CONCAT(source_report_code,':',CONVERT(int,q)),',') WITHIN GROUP(ORDER BY source_report_code)
        FROM (SELECT source_report_code,SUM(quantity) q FROM dbo.v_stock_snapshots_effective WHERE store_code='{store}' AND snapshot_date='{date}' GROUP BY source_report_code) x
        """) as string;

    private async Task<bool> IsSuperseded(long file) =>
        Convert.ToBoolean(await database.ExecuteAsync($"SELECT is_superseded FROM dbo.import_files WHERE import_file_id={file}"));

    private static WorkbookSnapshot ClosingStock(string store, string exportPrefix, string hashSeed, DateOnly column, params (string Item, decimal Quantity)[] items) =>
        new($"{exportPrefix}_Closing Stock.xlsx", 10, Hash(hashSeed),
        [new("Sheet0", 1, StockImportProfiles.ClosingStockHeaders, items.Select((item, i) => new WorkbookRow(i + 2, new object?[]
        {
            store, "Synthetic", "Retail", "Store", "Region", "State", "City", column.ToDateTime(TimeOnly.MinValue), item.Item, "HSN", "Description",
            "EAN-1", "BR", "Cluster", "U", item.Quantity, 10m, 10m * item.Quantity, "LOT-1", null
        }.Select(value => new WorkbookCell(value)).ToArray())).ToArray())]);

    /// <summary>A raw R010 export with a retail and a service bin; CLOSINGBALANCE is their sum.</summary>
    private static WorkbookSnapshot BinWise(string store, string exportPrefix, string hashSeed, params (string Item, decimal Retail, decimal Service)[] items)
    {
        var family = EtpReportFamilyRegistry.Resolve("R010");
        var columns = family.Headers.Select(header => family.Columns.Single(column => column.SourceHeader == header)).ToArray();
        WorkbookRow Row(int number, (string Item, decimal Retail, decimal Service) item) => new(number, columns.Select(column => new WorkbookCell(column.CanonicalField switch
        {
            "store_code" => store,
            "itemnumber" => item.Item,
            "lotnumber" => "LOT-1",
            "uid" => null,
            "retailbin" => item.Retail,
            "servicebin" => item.Service,
            "closingbalance" => item.Retail + item.Service,
            "ucp" => 10m,
            "totalucp" => 10m * (item.Retail + item.Service),
            _ => column.DataType == Etp.Reporting.Domain.Imports.CanonicalDataType.Decimal ? (object)0m : "SYNTHETIC"
        })).ToArray());
        return new($"{exportPrefix}_BinWise-Stock - BinWise-Stock.xlsx", 10, Hash(hashSeed),
            [new("Data", 1, family.Headers, items.Select((item, i) => Row(i + 2, item)).ToArray())]);
    }
}
