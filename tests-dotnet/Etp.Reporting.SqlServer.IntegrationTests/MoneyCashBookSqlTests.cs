using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// Gap #6 (MONEY-TEST-GAPS.md): the cash book with real tenders. Synthetic data, hand-computed values.
/// Closing = Opening + Cash mode (CASH + CASH_REFUND + ROUND_OFF) + SERVICE_CASH - EXPENSES - CASH_DEPOSIT + CASH_ADJUSTMENT.
/// </summary>
public sealed class MoneyCashBookSqlTests(SqlDatabaseFixture db) : IClassFixture<SqlDatabaseFixture>
{
    private static async Task Save(DailyReportingWorkflowRepository inputs, string store, DateOnly date, string field, decimal amount) =>
        await inputs.SaveManualInputAsync(store, date, field, amount, null, "test", "Verified opening or evening entry");

    [SqlFact]
    public async Task Cash_book_carries_across_the_month_end_with_cash_sales_refunds_and_an_adjustment()
    {
        // The R022 file covers 30 Sep - 2 Oct 2030 only, so 3 Oct has no tender source.
        await db.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(),@f bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date,period_start,period_end,data_truth_version)
            VALUES(@batch,'m3-cash-r022.xlsx',LOWER(CONVERT(char(64),HASHBYTES('SHA2_256','m3-cashbook-1'),2)),1,'CASHM','R022','20300930','20300930','20301002',1);
            SET @f=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            VALUES(@f,'Revenue',1,'revenue'),(@f,'Revenue',2,'revenue'),(@f,'Revenue',3,'revenue'),(@f,'Revenue',4,'revenue'),(@f,'Revenue',5,'revenue');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date)
            VALUES('CASHM','T1',2031,'20300930'),('CASHM','R1',2031,'20300930'),('CASHM','T2',2031,'20301001'),('CASHM','X1',2031,'20301002');
            INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,v.tender,v.amount,'INR',s.source_lineage_id
            FROM (VALUES('T1',1,'CASH',5000),('T1',2,'UPI',2000),('R1',3,'CASH_REFUND',-300),('T2',4,'CASH',2500),('X1',5,'XYZ',100)) v(doc,rowNo,tender,amount)
            JOIN dbo.sales_invoices i ON i.store_code='CASHM' AND i.document_number=v.doc
            JOIN dbo.source_lineage s ON s.import_file_id=@f AND s.source_row_number=v.rowNo;
            """);
        var inputs = new DailyReportingWorkflowRepository(db.ConnectionString);
        var d30 = new DateOnly(2030, 9, 30); var d1 = new DateOnly(2030, 10, 1); var d2 = new DateOnly(2030, 10, 2);
        // 30 Sep: opening 1,000; service cash 200; card/UPI 0; expenses 150; deposit 4,000; adjustment -50.
        await Save(inputs, "CASHM", d30, "OPENING_CASH", 1000);
        foreach (var (field, amount) in new[] { ("SERVICE_CASH", 200m), ("SERVICE_CARD", 0m), ("SERVICE_UPI", 0m), ("EXPENSES", 150m), ("CASH_DEPOSIT", 4000m), ("CASH_ADJUSTMENT", -50m) })
            await Save(inputs, "CASHM", d30, field, amount);
        // 1 Oct and 2 Oct: no opening entered, every evening field 0.
        foreach (var date in new[] { d1, d2 })
            foreach (var field in new[] { "SERVICE_CASH", "SERVICE_CARD", "SERVICE_UPI", "EXPENSES", "CASH_DEPOSIT" })
                await Save(inputs, "CASHM", date, field, 0);
        await Save(inputs, "CASHM", d1, "CLOSING_CASH_COUNTED", 4150);

        var r = new OperationalReportRepository(db.ConnectionString);
        var days = await r.LoadCashBookAsync("CASHM", d30, new(2030, 10, 3));
        Assert.Equal(4, days.Count);

        // 30 Sep: Cash mode = 5,000 + (-300) = 4,700; UPI 2,000.
        var sep30 = days[0];
        Assert.Equal(1000m, sep30.Opening); Assert.StartsWith("Opening entered", sep30.OpeningSource);
        Assert.Equal(4700m, sep30.Modes["Cash"]); Assert.Equal(2000m, sep30.Modes["UPI"]); Assert.Equal(2, sep30.Modes.Count);
        Assert.Equal(6700m, sep30.RetailTotal);
        Assert.Equal(6900m, sep30.AllReceipts);   // 6,700 + service cash 200 + card 0 + UPI 0
        Assert.Equal(6900m, sep30.TotalSale);     // no TC tender
        Assert.Equal(-50m, sep30.Adjustment);
        // 1,000 + 4,700 + 200 - 150 - 4,000 + (-50) = 1,700
        Assert.Equal(1700m, sep30.Closing);
        Assert.Equal("Complete", sep30.Status);

        // 1 Oct: opening carried 1,700; + 2,500 cash; closing 4,200; counted 4,150 -> variance -50.
        var oct1 = days[1];
        Assert.Equal(1700m, oct1.Opening); Assert.Contains("Carried from 30", oct1.OpeningSource);
        Assert.Equal(2500m, oct1.Modes["Cash"]); Assert.Equal(0m, oct1.Adjustment);
        Assert.Equal(4200m, oct1.Closing); Assert.Equal(4150m, oct1.Counted);
        Assert.Equal("Counted cash variance", oct1.Status);
        var recon = await r.LoadCashReconciliationAsync("CASHM", d1);
        Assert.Equal(1700m, recon.OpeningCash); Assert.Equal(2500m, recon.RetailCash);
        Assert.Equal(4200m, recon.CalculatedClosing); Assert.Equal(4150m, recon.CountedClosing);
        Assert.Equal(-50m, recon.Variance); Assert.Equal(ReconciliationStatus.Failed, recon.Status);
        // 30 Sep was never counted: no variance, not a failure.
        var recon30 = await r.LoadCashReconciliationAsync("CASHM", d30);
        Assert.Equal(1700m, recon30.CalculatedClosing); Assert.Null(recon30.Variance); Assert.Equal(ReconciliationStatus.Passed, recon30.Status);

        // 2 Oct: an unmapped tender code XYZ. Opening still carries the calculated 4,200 (not the counted 4,150); closing unknown.
        var oct2 = days[2];
        Assert.Equal(4200m, oct2.Opening);
        Assert.Equal(100m, oct2.Modes["Unmapped: XYZ"]);
        Assert.Null(oct2.Closing);
        Assert.Equal("Tender source missing or unmapped", oct2.Status);
        Assert.Equal(ReconciliationStatus.Blocked, (await r.LoadCashReconciliationAsync("CASHM", d2)).Status);

        // 3 Oct: the chain is broken (no previous closing) and no R022 covers the day.
        var oct3 = days[3];
        Assert.Null(oct3.Opening); Assert.Null(oct3.Closing);
        Assert.Equal("Opening or previous closing required", oct3.OpeningSource);
        Assert.False(oct3.TenderSourceImported); Assert.Equal(CashBookTables.TenderSourceMissing, oct3.Status);

        // Asking for 1 Oct alone reaches back to the 30 Sep anchor and gives the same opening.
        Assert.Equal(1700m, Assert.Single(await r.LoadCashBookAsync("CASHM", d1, d1)).Opening);
    }

    [SqlFact]
    public async Task Cash_book_keeps_paise_round_off_and_a_refund_larger_than_the_cash_sales()
    {
        // Day 1: opening 500.25, CASH 1,129.00 + ROUND_OFF 0.40 (Cash mode), expenses 0.15 -> 500.25 + 1,129.40 - 0.15 = 1,629.50.
        // Day 2: a cash refund of 2,000.10, no cash sale: 1,629.50 - 2,000.10 = -370.60 (negative cash is shown, not hidden).
        await db.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(),@f bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date,period_start,period_end,data_truth_version)
            VALUES(@batch,'m3-cash-paise.xlsx',LOWER(CONVERT(char(64),HASHBYTES('SHA2_256','m3-cashbook-2'),2)),1,'CASHP','R022','20300801','20300801','20300802',1);
            SET @f=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            VALUES(@f,'Revenue',1,'revenue'),(@f,'Revenue',2,'revenue'),(@f,'Revenue',3,'revenue');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date)
            VALUES('CASHP','P1',2031,'20300801'),('CASHP','P2',2031,'20300802');
            INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,v.tender,v.amount,'INR',s.source_lineage_id
            FROM (VALUES('P1',1,'CASH',1129.00),('P1',2,'ROUND_OFF',0.40),('P2',3,'CASH_REFUND',-2000.10)) v(doc,rowNo,tender,amount)
            JOIN dbo.sales_invoices i ON i.store_code='CASHP' AND i.document_number=v.doc
            JOIN dbo.source_lineage s ON s.import_file_id=@f AND s.source_row_number=v.rowNo;
            """);
        var inputs = new DailyReportingWorkflowRepository(db.ConnectionString);
        var a = new DateOnly(2030, 8, 1); var b = new DateOnly(2030, 8, 2);
        foreach (var date in new[] { a, b })
            foreach (var field in new[] { "SERVICE_CASH", "SERVICE_CARD", "SERVICE_UPI", "EXPENSES", "CASH_DEPOSIT" })
                await Save(inputs, "CASHP", date, field, 0);
        await Save(inputs, "CASHP", a, "OPENING_CASH", 500.25m);
        await Save(inputs, "CASHP", a, "EXPENSES", 0.15m);

        var days = await new OperationalReportRepository(db.ConnectionString).LoadCashBookAsync("CASHP", a, b);
        Assert.Equal(1129.40m, days[0].Modes["Cash"]);
        Assert.Equal(1629.50m, days[0].Closing);
        Assert.Equal(-2000.10m, days[1].Modes["Cash"]);
        Assert.Equal(1629.50m, days[1].Opening);
        Assert.Equal(-370.60m, days[1].Closing);
    }
}
