using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class DataTruthReportingSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Manual_periods_sum_available_days_count_explicit_zero_and_report_missing_days()
    {
        await database.ExecuteAsync("""
            INSERT dbo.manual_operational_inputs(store_code,business_date,field_code,numeric_value,entered_by,modified_by,change_reason) VALUES
            ('PARTIAL','20260801','WALK_INS',5,'test','test','Test available days'),
            ('PARTIAL','20260803','WALK_INS',0,'test','test','Explicit zero counts'),
            ('PARTIAL','20260801','SERVICE_CASH',100,'test','test','Test service'),
            ('PARTIAL','20260801','SERVICE_CARD',20,'test','test','Test service'),
            ('PARTIAL','20260801','SERVICE_UPI',0,'test','test','Test service'),
            ('PARTIAL','20260802','SERVICE_CASH',30,'test','test','Partial service day'),
            ('PARTIAL','20260802','SERVICE_WDC',8,'test','test','New definition'),
            ('PARTIAL','20260802','WCC_WALKIN',3,'test','test','New definition'),
            ('PARTIAL','20260802','WCC_SALES',75,'test','test','New definition'),
            ('PARTIAL','20260802','WDC_BILLS',1,'test','test','New definition');
            """);
        var masters = new DataTruthMasterRepository(database.ConnectionString);
        var aggregate = await masters.LoadManualAggregateAsync("PARTIAL", "WALK_INS", new(2026, 8, 1), new(2026, 8, 3));
        Assert.Equal(new ManualInputAggregate("WALK_INS", 5m, 2, 1), aggregate);
        var absent = await masters.LoadManualAggregateAsync("PARTIAL", "WALK_INS", new(2025, 8, 1), new(2025, 8, 3));
        Assert.Equal(new ManualInputAggregate("WALK_INS", 0m, 0, 3), absent);
        var reports = new OperationalReportRepository(database.ConnectionString);
        var dsr = await reports.LoadDsrAsync(new(2026, 8, 3), ["PARTIAL"]);
        var mtd = Assert.Single(dsr, x => x.Store == "PARTIAL" && x.Period == "MTD");
        Assert.Equal(5m, mtd.WalkIns);
        Assert.Equal(1, mtd.WalkInMissingDays);
        var ytd = Assert.Single(dsr, x => x.Store == "PARTIAL" && x.Period == "YTD");
        Assert.Equal(5m, ytd.WalkIns);
        Assert.Equal(new DateOnly(2026, 8, 3).DayNumber - new DateOnly(2026, 4, 1).DayNumber - 1, ytd.WalkInMissingDays);
        var service = await reports.LoadServiceSalesAsync(new(2026, 8, 3), ["PARTIAL"]);
        var serviceMtd = Assert.Single(service, x => x.Period == "MTD");
        Assert.Equal(150m, serviceMtd.Total);
        Assert.Equal(130m, serviceMtd.Cash);
        Assert.Equal(2, serviceMtd.MissingDays);
        Assert.Equal(3, serviceMtd.LastYearMissingDays);
        Assert.Contains("Partial", serviceMtd.Availability);
        Assert.Equal(150m, Assert.Single(service, x => x.Period == "YTD").Total);
    }

    [Fact]
    public async Task Targets_are_saved_once_per_month_and_staff_lookup_overlaps_day_and_year_scopes()
    {
        var masters = new DataTruthMasterRepository(database.ConnectionString);
        await masters.SaveMonthlyTargetAsync(new("TARGET", new(2026, 8, 4), 1000m));
        await masters.SaveMonthlyTargetAsync(new("TARGET", new(2026, 8, 29), 1500m));
        Assert.Equal(1500m, await database.ExecuteAsync("SELECT SUM(target_sales) FROM dbo.monthly_targets WHERE store_code='TARGET'"));
        Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.monthly_targets WHERE store_code='TARGET'"));
        var operations = new OperationalCompletionRepository(database.ConnectionString);
        await operations.SaveStaffTargetAsync("TARGET", "CRO1", new(2026, 8, 4), new(2026, 8, 4), 800m, "test", "Monthly target");
        await operations.SaveStaffTargetAsync("TARGET", "CRO1", new(2026, 8, 20), new(2026, 8, 25), 900m, "test", "Update same month");
        await operations.SaveStaffTargetAsync("TARGET", "CRO1", new(2026, 7, 1), new(2026, 7, 31), 700m, "test", "Previous month");
        var day = await operations.LoadStaffTargetsAsync(new(new(2026, 8, 25), new(2026, 8, 25), ["TARGET"]));
        var value = Assert.Single(day);
        Assert.Equal(new DateOnly(2026, 8, 1), value.PeriodStart);
        Assert.Equal(new DateOnly(2026, 8, 31), value.PeriodEnd);
        Assert.Equal(900m, value.TargetSales);
        var year = await operations.LoadStaffTargetsAsync(new(new(2026, 4, 1), new(2026, 8, 25), ["TARGET"]));
        Assert.Equal(1600m, year.Sum(x => x.TargetSales));
        await Assert.ThrowsAsync<ArgumentException>(() => operations.SaveStaffTargetAsync("TARGET", "CRO1", new(2026, 7, 1), new(2026, 8, 31), 100m, "test", "Ambiguous span"));
    }

    [Fact]
    public async Task Reports_use_gst_inclusive_sales_negative_cancellation_cro_names_and_editable_tender_mapping()
    {
        await database.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'synthetic.xlsx',REPLICATE('a',64),1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            VALUES(@file,'Sales',1,'sale'),(@file,'Sales',2,'sale'),(@file,'Staff',1,'staff'),(@file,'Staff',2,'staff'),(@file,'Tender',1,'tender'),(@file,'Tender',2,'tender'),(@file,'Revenue',1,'control'),(@file,'Revenue',2,'control');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('REPORT','I1',2027,'20260825'),('REPORT','BC1',2027,'20260825');
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_tax_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,'1','ITEM',CASE WHEN i.document_number='I1' THEN 'INV' ELSE 'BC' END,
             CASE WHEN i.document_number='I1' THEN 2 ELSE -1 END,CASE WHEN i.document_number='I1' THEN 236 ELSE -118 END,
             CASE WHEN i.document_number='I1' THEN 200 ELSE -100 END,CASE WHEN i.document_number='I1' THEN 36 ELSE -18 END,'INR',s.source_lineage_id
            FROM dbo.sales_invoices i JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Sales' AND s.source_row_number=CASE WHEN i.document_number='I1' THEN 1 ELSE 2 END WHERE i.store_code='REPORT';
            INSERT dbo.sales_line_enrichments(enrichment_type,store_code,transaction_date,document_number,invoice_year,product_code,source_transaction_type,source_quantity,source_net_value,source_gross_value,source_cro_number,staff_name,matched_sales_line_id,match_status,source_lineage_id)
            SELECT 'R013',i.store_code,i.transaction_date,i.document_number,i.invoice_year,l.product_code,l.source_transaction_type,l.source_quantity,l.source_net_amount,l.source_gross_amount,
             CASE WHEN i.document_number='I1' THEN 'CRO1' ELSE 'CRO2' END,CASE WHEN i.document_number='I1' THEN 'Sample Staff One' ELSE 'Sample Staff Two' END,l.sales_line_id,'Matched',s.source_lineage_id
            FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Staff' AND s.source_row_number=CASE WHEN i.document_number='I1' THEN 1 ELSE 2 END WHERE i.store_code='REPORT';
            INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,l.source_transaction_type,l.source_quantity,l.source_gross_amount,'INR',s.source_lineage_id FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Revenue' AND s.source_row_number=CASE WHEN i.document_number='I1' THEN 1 ELSE 2 END WHERE i.store_code='REPORT';
            INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,CASE WHEN i.document_number='I1' THEN 'PAYMENTTYPE25' ELSE 'CASH_REFUND' END,l.source_gross_amount,'INR',s.source_lineage_id FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Tender' AND s.source_row_number=CASE WHEN i.document_number='I1' THEN 1 ELSE 2 END WHERE i.store_code='REPORT';
            """);
        var scope = new ReportingQueryScope(new(2026, 8, 25), new(2026, 8, 25), ["REPORT"]);
        var reports = new OperationalReportRepository(database.ConnectionString);
        var masters = new DataTruthMasterRepository(database.ConnectionString);
        await masters.SaveStaffAsync(new("REPORT", "CRO2", "Corrected Staff Name", false));
        Assert.Contains(await masters.LoadStaffAsync(), x => x.Code == "CRO2" && !x.Active && x.Name == "Corrected Staff Name");
        var staff = await reports.LoadStaffPerformanceAsync(scope);
        Assert.Equal(ReconciliationStatus.Passed, staff.Status);
        Assert.Equal(118m, staff.CanonicalSales);
        var cancelled = Assert.Single(staff.Rows, x => x.CroNumber == "CRO2");
        Assert.Equal(-118m, cancelled.NetSales);
        Assert.Equal("Corrected Staff Name", cancelled.CroName);
        Assert.Equal(118m, (await reports.LoadInvoiceSummaryAsync(scope)).Sum(x => x.NetValue));
        Assert.Equal(118m, Assert.Single(await reports.LoadDsrAsync(new(2026, 8, 25), ["REPORT"]), x => x.Store == "REPORT" && x.Period == "FTD").TySales);
        var query = new SqlServerReportingQueryRepository(database.ConnectionString);
        var executor = new SqlBackedReportingExecutor(query, RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);
        Assert.Equal(118m, Assert.Single((await executor.ExecuteSalesSummaryAsync(scope, SalesSummaryDimension.Store)).Rows).SourceSignedNetAmount);
        var tender = await executor.ExecuteTenderReconciliationAsync(scope);
        Assert.Equal(ReconciliationStatus.Passed, tender.Status);
        Assert.Equal(118m, tender.TenderTotal);
        Assert.Contains("UPI: 236", tender.Message);
        await masters.SaveTenderModeAsync(new("PAYMENTTYPE25", "AIRPAY", "Card", true));
        Assert.Contains(await query.LoadTendersAsync(scope), x => x.TenderType == "Card" && x.SourceAmount == 236m);
        await masters.SaveTenderModeAsync(new("PAYMENTTYPE25", "AIRPAY", "Card", false));
        Assert.Equal(ReconciliationStatus.Blocked, (await executor.ExecuteTenderReconciliationAsync(scope)).Status);
        await database.ExecuteAsync("INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final) VALUES('REPORT','20260825',1,REPLICATE('b',64),'{}','test',1)");
        var accounting = await new ProductisationRepository(database.ConnectionString).LoadAccountingSourceAsync("REPORT", new(2026, 8, 25));
        Assert.Equal(118m, Assert.Single(accounting.Events, x => x.EventCode == "NET_SALES").Amount);
    }
}
