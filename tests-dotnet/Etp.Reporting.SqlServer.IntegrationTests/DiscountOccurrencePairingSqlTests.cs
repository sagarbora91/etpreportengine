using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// P12 (1.12.1, DQ-UNMATCHED-ENRICHMENT). An invoice that repeats the same item on 2..18 identical R025 lines made every
/// R003 row for that key Ambiguous at import (137 live Titan rows on 11 invoices, DATA-05 CRITICAL). An Ambiguous R003 row is
/// now paired by occurrence like R013 when every R003 row for the key is Ambiguous and the R003 row count equals the R025 line
/// count; any other shape stays Ambiguous. All data is synthetic.
/// </summary>
public sealed class DiscountOccurrencePairingSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    // One invoice for @store with @lines identical R025 lines of one item and @discounts identical R003 rows, all stored
    // @status except the first R003 row, which is stored @firstStatus (Matched to the first line when 'Matched').
    private const string Seed = """
        DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
        INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
        INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes)
        VALUES(@batch,'synthetic.xlsx',LOWER(CONVERT(char(64),HASHBYTES('SHA2_256',@store),2)),1);
        SET @file=SCOPE_IDENTITY();
        WITH n AS (SELECT TOP(40) ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) n FROM sys.all_objects)
        INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
        SELECT @file,'Sales',n,'sale' FROM n WHERE n<=@lines
        UNION ALL SELECT @file,'Discount',n,'discount' FROM n WHERE n<=@discounts;
        INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,'D1',2027,@date);
        INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_tax_amount,currency_code,source_lineage_id)
        SELECT i.sales_invoice_id,CONVERT(nvarchar(10),s.source_row_number),'ITEMR','INV',1,1180,1000,180,'INR',s.source_lineage_id
        FROM dbo.sales_invoices i JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Sales'
        WHERE i.store_code=@store;
        INSERT dbo.sales_line_enrichments(enrichment_type,store_code,transaction_date,document_number,invoice_year,product_code,source_transaction_type,source_quantity,source_net_value,source_gross_value,
          scheme_discount,user_discount,matched_sales_line_id,match_status,source_lineage_id,content_key)
        SELECT 'R003',@store,@date,'D1',2027,'ITEMR','INV',1,1000,1180,50,25,
               CASE WHEN s.source_row_number=1 AND @firstStatus='Matched'
                    THEN (SELECT MIN(l.sales_line_id) FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id WHERE i.store_code=@store) END,
               CASE WHEN s.source_row_number=1 THEN @firstStatus ELSE @status END,s.source_lineage_id,CONCAT(@store,':D:',s.source_row_number)
        FROM dbo.source_lineage s WHERE s.import_file_id=@file AND s.sheet_name='Discount';
        """;

    private Task SeedAsync(string store, DateOnly date, int lines, int discounts, string status, string firstStatus) =>
        database.ExecuteAsync($"""
            DECLARE @store varchar(30)='{store}',@date date='{date:yyyyMMdd}',@lines int={lines},@discounts int={discounts},
                    @status varchar(20)='{status}',@firstStatus varchar(20)='{firstStatus}';
            {Seed}
            """);

    private async Task<int> ScalarAsync(string sql) => Convert.ToInt32(await database.ExecuteAsync(sql));

    [SqlTheory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(11)]
    [InlineData(18)]
    public async Task Ambiguous_discount_rows_on_identical_repeated_lines_pair_one_to_one_in_source_order(int lines)
    {
        var store = $"DQEQ{lines:00}";
        var date = new DateOnly(2026, 8, 29);
        await SeedAsync(store, date, lines, lines, "Ambiguous", "Ambiguous");

        var trend = Assert.Single(await new Phase2OperationsRepository(database.ConnectionString).LoadManagementTrendAsync(date, date), x => x.StoreCode == store);
        Assert.Equal(0, trend.UnmatchedEnrichmentRows);

        // Every line is taken exactly once, and the nth R003 row (source order) takes the nth line (line order).
        var e = EnrichmentOccurrencePairing.EffectiveEnrichments;
        Assert.Equal(lines, await ScalarAsync($"SELECT COUNT(DISTINCT e.effective_sales_line_id) FROM {e} e WHERE e.store_code='{store}' AND e.effective_match_status='Matched'"));
        Assert.Equal(0, await ScalarAsync($"""
            SELECT COUNT(*) FROM {e} e JOIN dbo.source_lineage d ON d.source_lineage_id=e.source_lineage_id
            JOIN dbo.sales_lines l ON l.sales_line_id=e.effective_sales_line_id
            WHERE e.store_code='{store}' AND l.line_identifier<>CONVERT(nvarchar(10),d.source_row_number)
            """));
        // The stored status is not rewritten (no migration); only the report-time reading changes.
        Assert.Equal(lines, await ScalarAsync($"SELECT COUNT(*) FROM dbo.sales_line_enrichments WHERE store_code='{store}' AND match_status='Ambiguous'"));
    }

    [SqlTheory]
    [InlineData(2, 4)]   // two discounts on each of two lines: which row belongs to which line is unknown
    [InlineData(3, 2)]   // fewer discount rows than lines
    [InlineData(18, 17)]
    [InlineData(18, 19)]
    public async Task Unequal_counts_stay_ambiguous(int lines, int discounts)
    {
        var store = $"DQNE{lines:00}{discounts:00}";
        var date = new DateOnly(2026, 8, 30);
        await SeedAsync(store, date, lines, discounts, "Ambiguous", "Ambiguous");

        var trend = Assert.Single(await new Phase2OperationsRepository(database.ConnectionString).LoadManagementTrendAsync(date, date), x => x.StoreCode == store);
        Assert.Equal(discounts, trend.UnmatchedEnrichmentRows);
        Assert.Equal(discounts, await ScalarAsync(
            $"SELECT COUNT(*) FROM {EnrichmentOccurrencePairing.EffectiveEnrichments} e WHERE e.store_code='{store}' AND e.effective_match_status='Ambiguous' AND e.effective_sales_line_id IS NULL"));
    }

    [SqlFact]
    public async Task A_key_with_a_stored_matched_discount_row_is_not_re_paired()
    {
        // One row was stored Matched (imported while the invoice had one line); re-pairing the others by occurrence could give
        // that line a second row, so the Ambiguous rows keep their status and the Matched row keeps its line.
        var store = "DQMIX";
        var date = new DateOnly(2026, 8, 31);
        await SeedAsync(store, date, lines: 3, discounts: 3, status: "Ambiguous", firstStatus: "Matched");

        var trend = Assert.Single(await new Phase2OperationsRepository(database.ConnectionString).LoadManagementTrendAsync(date, date), x => x.StoreCode == store);
        Assert.Equal(2, trend.UnmatchedEnrichmentRows);
    }

    [SqlFact]
    public async Task Imported_invoice_with_repeated_identical_lines_has_no_unmatched_discount_rows_and_labels_the_rest_R003()
    {
        // Cross-layer: the real import path (R025 then R003) stores every R003 row Ambiguous; the pairing, the data quality
        // summary (DATA-05 / issue 4), the Trends count and the daily exceptions then read it.
        var own = new SqlDatabaseFixture();
        try
        {
            await own.InitializeAsync();
            var date = new DateOnly(2026, 8, 25);
            var persistence = new SqlServerImportPersistenceUseCase(own.ConnectionString);
            foreach (var family in new[] { "R025", "R003" })
            {
                var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), family + "_*.xlsx").Single();
                var sample = await new OpenXmlWorkbookReader().ReadAsync(path);
                var sheet = sample.Sheets[0];
                var rows = new List<WorkbookRow>();
                // Invoice 1: the same item on 5 identical lines, one identical R003 row per line.
                for (var i = 0; i < 5; i++) rows.Add(Row(sheet, family, rows.Count + 2, "900000001", "SYNTH-REPEAT", date, 10m));
                // Invoice 2: the same item on 2 identical lines; R003 has two discounts per line (4 rows): genuinely ambiguous.
                if (family == "R025")
                    for (var i = 0; i < 2; i++) rows.Add(Row(sheet, family, rows.Count + 2, "900000002", "SYNTH-TWICE", date, 10m));
                else
                    foreach (var discount in new[] { 10m, 10m, 20m, 20m }) rows.Add(Row(sheet, family, rows.Count + 2, "900000002", "SYNTH-TWICE", date, discount));
                var workbook = sample with
                {
                    Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                    Sheets = [sheet with { Rows = rows }]
                };
                var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook);
                var result = await persistence.PersistAsync(new(accepted, date, "HEMW", "R003 pairing SQL test"));
                Assert.Equal(family == "R025" ? 7 : 9, result.PersistedRows);
            }

            // As imported: every R003 row is stored Ambiguous (more than one line for its key).
            Assert.Equal(9, Convert.ToInt32(await own.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_line_enrichments WHERE enrichment_type='R003' AND match_status='Ambiguous'")));

            var operations = new Phase2OperationsRepository(own.ConnectionString);
            var unmatched = Assert.Single(await operations.LoadDataQualitySummaryAsync(), x => x.Code == "UNMATCHED_ENRICHMENT");
            Assert.Equal(4, unmatched.Count);
            Assert.Equal(4, Assert.Single(await operations.LoadManagementTrendAsync(date, date), x => x.StoreCode == "HEMW").UnmatchedEnrichmentRows);

            var exceptions = (await new OperationalReportRepository(own.ConnectionString).LoadDailyExceptionsAsync("HEMW", date))
                .Where(x => x.Area.EndsWith("enrichment", StringComparison.Ordinal)).ToArray();
            Assert.Equal(4, exceptions.Length);
            Assert.All(exceptions, x =>
            {
                Assert.Equal("R003_AMBIGUOUS", x.Code);
                Assert.Equal("Discount enrichment", x.Area);
                Assert.Equal("900000002", x.DocumentNumber);
                Assert.DoesNotContain("staff/CRO enrichment", x.Message, StringComparison.Ordinal);
            });
        }
        finally
        {
            await own.DisposeAsync();
        }
    }

    private static WorkbookRow Row(WorkbookSheet sheet, string family, int rowNumber, string invoice, string item, DateOnly date, decimal schemeDiscount)
    {
        var columns = EtpReportFamilyRegistry.Resolve(family).Columns;
        var cells = sheet.Rows[0].Cells.ToArray();
        void Set(string field, object value)
        {
            var column = columns.SingleOrDefault(column => column.CanonicalField == field);
            if (column is not null) cells[sheet.Headers.ToList().IndexOf(column.SourceHeader)] = new WorkbookCell(value);
        }
        Set("invoice_number", invoice);
        Set("product_code", item);
        Set("transaction_date", date);
        if (family == "R003") Set("scheme_discount", schemeDiscount);
        return new WorkbookRow(rowNumber, cells);
    }
}
