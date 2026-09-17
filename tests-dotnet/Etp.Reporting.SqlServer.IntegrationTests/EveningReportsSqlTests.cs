using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Reporting;
using Xunit.Abstractions;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class EveningReportsSqlTests(SqlDatabaseFixture db, ITestOutputHelper output) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Cash_carries_calculated_closing_requires_reason_and_preserves_counted_variance()
    {
        await db.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID();
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date,period_start,period_end,data_truth_version)
            VALUES(@batch,'empty-payment.xlsx',REPLICATE('d',64),1,'CARRY','R022','20300901','20300901','20300903',1);
            """);
        var inputs=new DailyReportingWorkflowRepository(db.ConnectionString);
        async Task Save(int day,string field,decimal amount)=>await inputs.SaveManualInputAsync("CARRY",new(2030,9,day),field,amount,null,"test","Verified opening or evening entry");
        for(var day=1;day<=3;day++)foreach(var field in new[]{"SERVICE_CASH","SERVICE_CARD","SERVICE_UPI","EXPENSES","CASH_DEPOSIT"})await Save(day,field,0);
        await Save(1,"OPENING_CASH",100);await Save(1,"SERVICE_CASH",20);await Save(1,"EXPENSES",5);await Save(1,"CASH_DEPOSIT",10);
        await Save(2,"SERVICE_CASH",30);await Save(2,"EXPENSES",4);await Save(2,"CASH_DEPOSIT",11);
        var r=new OperationalReportRepository(db.ConnectionString);
        var days=await r.LoadCashBookAsync("CARRY",new(2030,9,1),new(2030,9,3));
        Assert.Equal(new decimal?[]{105,120,120},days.Select(x=>x.Closing));
        Assert.Equal(105m,days[1].Opening);Assert.Contains("Carried",days[1].OpeningSource);
        Assert.Equal(120m,Assert.Single(await r.LoadCashBookAsync("CARRY",new(2030,9,3),new(2030,9,3))).Opening);
        await Assert.ThrowsAsync<ArgumentException>(()=>inputs.SaveManualInputAsync("CARRY",new(2030,9,2),"OPENING_CASH",50,null,"test",""));
        await Save(2,"OPENING_CASH",50);await Save(2,"CLOSING_CASH_COUNTED",70);
        var recon=await r.LoadCashReconciliationAsync("CARRY",new(2030,9,2));
        Assert.Equal(65m,recon.CalculatedClosing);Assert.Equal(5m,recon.Variance);Assert.Equal(ReconciliationStatus.Failed,recon.Status);
        Assert.Equal(65m,Assert.Single(await r.LoadCashBookAsync("CARRY",new(2030,9,3),new(2030,9,3))).Opening);
        Assert.Null(Assert.Single(await r.LoadCashBookAsync("CARRY",new(2030,9,4),new(2030,9,4))).Closing);
    }

    [Fact]
    public async Task Brand_master_edit_is_atomic_and_monthly_targets_are_not_halved()
    {
        var masters=new EveningMasterRepository(db.ConnectionString);
        await masters.SaveBrandAsync(new(0,"MASTERTEST","One",10,"A,B"));
        await masters.SaveBrandAsync(new(0,"MASTERTEST","Two",20,"C"));
        var one=(await masters.LoadBrandsAsync()).Single(x=>x.StoreCode=="MASTERTEST"&&x.Label=="One");
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>masters.SaveBrandAsync(one with{SourceCodes="A,C"}));
        Assert.Contains("B",(await masters.LoadBrandsAsync()).Single(x=>x.Id==one.Id).SourceCodes);
        await masters.SaveBrandAsync(one with{Label="Renamed",Order=30,SourceCodes="A,B,D"});
        Assert.Equal("Renamed",(await masters.LoadBrandsAsync()).Single(x=>x.Id==one.Id).Label);
        await new DataTruthMasterRepository(db.ConnectionString).SaveMonthlyTargetAsync(new("WLMHW",new(2031,8,1),31000));
        var r=new OperationalReportRepository(db.ConnectionString);
        var sheets=await r.LoadEveningSheetsAsync(new(2031,8,25),await r.LoadDsrAsync(new(2031,8,25),["WLMHW","HEMW"]));
        Assert.Equal(31000m,sheets.Single(x=>x.StoreCode=="WLMHW").StoreTarget);
        Assert.Equal(1000m,sheets.Single(x=>x.StoreCode=="WLMHW").DayTarget);
        Assert.Null(sheets.Single(x=>x.StoreCode=="COMBINED").StoreTarget);
        Assert.Equal("Gift Card",await db.ExecuteAsync("SELECT mode FROM dbo.tender_modes WHERE source_tender_code='GIFTCARD'"));
        Assert.Equal("Bank",await db.ExecuteAsync("SELECT mode FROM dbo.tender_modes WHERE source_tender_code='CHEQUE'"));
    }

    [PrivatePhaseOneCorpus]
    public async Task Six_evening_reports_use_real_25_Aug_sources_and_export_without_changing_source_facts()
    {
        var folders=new[]{Path.Combine(PrivatePhaseOneCorpusAttribute.Root,"WLMHW","TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026"),Path.Combine(PrivatePhaseOneCorpusAttribute.Root,"HEMW","HELIOS ALL REPORT 01 JULY 2026 TO 25 AUG 2026")};
        var import=await new FolderImportService(new SqlServerImportPersistenceUseCase(db.ConnectionString)).RunFilesAsync(folders.SelectMany(x=>Directory.GetFiles(x,"*.xlsx")).ToArray(),new("Phase2 acceptance"));
        Assert.All(import.Files,x=>Assert.Contains(x.Status,new[]{"Imported","empty export"}));
        var date=new DateOnly(2026,8,25);var r=new OperationalReportRepository(db.ConnectionString);
        var input=new DailyReportingWorkflowRepository(db.ConnectionString);
        foreach(var store in new[]{"WLMHW","HEMW"})
        {
            foreach(var (field,value) in new[]{("OPENING_CASH",1000m),("SERVICE_WDC",0m),("SERVICE_CASH",700m),("SERVICE_CARD",1807m),("SERVICE_UPI",577m),("EXPENSES",0m),("CASH_DEPOSIT",0m),("WALK_INS",10m)})
                await input.SaveManualInputAsync(store,date,field,value,null,"test","Synthetic manual inputs for report validation; source sales unchanged");
            await new DataTruthMasterRepository(db.ConnectionString).SaveMonthlyTargetAsync(new(store,new(2026,8,1),store=="WLMHW"?1600000:1300000));
            var invoice=await r.LoadInvoiceSummaryAsync(new(date,date,[store]));
            Assert.Equal(store=="WLMHW"?34215m:29290m,invoice.Sum(x=>x.NetValue));
            Assert.Equal(store=="WLMHW"?5m:2m,invoice.Sum(x=>x.Quantity));
            Assert.All(invoice,x=>Assert.False(string.IsNullOrWhiteSpace(x.CustomerName)));
            var staff=await r.LoadStaffPerformanceAsync(new(date,date,[store]));Assert.Equal(invoice.Sum(x=>x.NetValue),staff.AttributedSales);Assert.Equal(0,staff.Variance);
            var service=await r.LoadServiceSalesAsync(date,[store]);Assert.Equal(3084m,service.Single(x=>x.Period=="FTD").Total);
            var cash=Assert.Single(await r.LoadCashBookAsync(store,date,date));Assert.Equal(invoice.Sum(x=>x.NetValue),cash.RetailTotal);Assert.Equal("Complete",cash.Status);
            var stocks=await r.LoadBrandPhysicalStockAsync(store,date);Assert.NotEmpty(stocks);Assert.All(stocks,x=>Assert.Null(x.ComponentTotal));
            output.WriteLine($"{store}: stock system total {stocks.Sum(x=>x.SystemQuantity)}; {stocks.Count} brands; customer names complete; sales/staff/cash {cash.RetailTotal}; service fixture 3084");
            foreach(var stock in stocks)
                await new OperationalCompletionRepository(db.ConnectionString).SaveManualStockCountAsync(store,date,stock.InventoryGroupCode,stock.SystemQuantity,0,0,0,stock.SystemQuantity,"Synthetic matching count","test","Test fixture, not an asserted shop count");
            Assert.All(await r.LoadBrandPhysicalStockAsync(store,date),x=>Assert.Equal("PASS",x.Status));
            var pack=await new DailyReportingPackService(db.ConnectionString).GenerateAsync(store,date,"test");
            var dir=Path.Combine(Path.GetTempPath(),"EtpPhase2Review",db.Name);Directory.CreateDirectory(dir);
            foreach(var table in pack.Document.Tables.Where(x=>new[]{"Invoice Summary","DSR","Service Sales","Cash Book","Physical Stock","Staff Performance"}.Contains(x.Name)))
            {
                var metadata=new ExcelReportMetadata(table.Name,date,date,table.Status,RetailReportingPolicy.Version,table.Message,DateTimeOffset.UtcNow);
                var name=Path.Combine(dir,store+"-"+table.Name.Replace(' ','-'));
                new OpenXmlReportExporter().Export(name+".xlsx",metadata,table.Data);
                new SimplePdfReportExporter().Export(name+".pdf",metadata,table.Data);
            }
            new OpenXmlReportPackExporter().Export(Path.Combine(dir,store+"-pack.xlsx"),pack.Document);
        }
        // Aggregate goldens independently recomputed from the private R013 workbook.
        // Identities and source rows are deliberately absent from this assertion.
        // SR/BC values retain the source return sign; only INV documents divide ATV/AUPT.
        var monthlyCro = (await r.LoadStaffPerformanceAsync(new(new(2026,8,1),date,["WLMHW"]))).Rows.OrderByDescending(x=>x.NetSales).ToArray();
        var croGolden = new (decimal Sales, decimal Quantity, int Invoices)[]
        {
            (213660.5m,39m,37), (177432.5m,37m,38), (166775.5m,35m,33),
            (132041.5m,26m,19), (129547.5m,29m,27), (89880.5m,24m,22), (28859m,6m,2)
        };
        Assert.Equal(croGolden,monthlyCro.Select(x=>(x.NetSales,x.NetQuantity,x.Transactions)).ToArray());
        for(var index=0;index<croGolden.Length;index++)
        {
            Assert.Equal(croGolden[index].Sales/croGolden[index].Invoices,monthlyCro[index].Atv);
            Assert.Equal(croGolden[index].Quantity/croGolden[index].Invoices,monthlyCro[index].Upt);
        }
        var doc=await r.LoadDailySalesReportDocumentAsync(date);
        var titan=doc.EveningSheets.Single(x=>x.StoreCode=="WLMHW");var helios=doc.EveningSheets.Single(x=>x.StoreCode=="HEMW");
        Assert.Equal(34215m,titan.Rows.Single(x=>x.Metric=="VALUE").Ftd);Assert.Equal(938197m,titan.Rows.Single(x=>x.Metric=="VALUE").Mtd);
        Assert.Equal(774868.60m,helios.Rows.Single(x=>x.Metric=="VALUE").Mtd);
        Assert.Equal(1m,titan.Rows.Single(x=>x.Metric=="AUPT").Ftd);Assert.Equal(6843m,titan.Rows.Single(x=>x.Metric=="AVPT").Ftd);
        Assert.Equal(1600000m-938197m,titan.Balance);Assert.Equal(titan.Balance/7,titan.RequiredAds);
        Assert.Equal(63505m,doc.EveningSheets.Single(x=>x.StoreCode=="COMBINED").Rows.Single(x=>x.Metric=="VALUE").Ftd);
        foreach(var sheet in new[]{titan,helios})
        {
            Assert.Equal(sheet.Rows.Single(x=>x.Metric=="VALUE").Ftd,sheet.Rows.Where(x=>x.Format=="currency"&&x.Metric is not ("VALUE" or "AVPT" or "WCC SALES")).Sum(x=>x.Ftd));
            output.WriteLine($"{sheet.StoreCode}: INV-only MTD {sheet.Rows.Single(x=>x.Metric=="INVOICE").Mtd}; LY {sheet.Rows.Single(x=>x.Metric=="VALUE").Ly}");
        }
        var review=Path.Combine(Path.GetTempPath(),"EtpPhase2Review",db.Name);
        new DailySalesReportPdfExporter().Export(Path.Combine(review,"Evening-DSR.pdf"),doc);
        File.WriteAllText(Path.Combine(review,"dsr.json"),System.Text.Json.JsonSerializer.Serialize(doc));
        var history=Path.Combine(PrivatePhaseOneCorpusAttribute.Root,"HEMW","till 6 sep 26","R025_SDB_VariantwiseSales.xlsx");
        var historical=await new FolderImportService(new SqlServerImportPersistenceUseCase(db.ConnectionString)).RunFilesAsync([history],new("Phase2 historical comparison"));
        Assert.Equal("Imported",Assert.Single(historical.Files).Status);
        var historicalDsr=await r.LoadDailySalesReportDocumentAsync(date);
        var valueRow=historicalDsr.EveningSheets.Single(x=>x.StoreCode=="HEMW").Rows.Single(x=>x.Metric=="VALUE");
        Assert.Equal(46797m,valueRow.Ly);Assert.Equal(2186215.10m,valueRow.LyYtd);
        Assert.NotNull(valueRow.Growth);
        Assert.Null(historicalDsr.EveningSheets.Single(x=>x.StoreCode=="WLMHW").Rows.Single(x=>x.Metric=="VALUE").Ly);
        new DailySalesReportPdfExporter().Export(Path.Combine(review,"Evening-DSR-with-Helios-history.pdf"),historicalDsr);
        Assert.Equal(0,Convert.ToInt32(await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_tenders WHERE is_reporting_eligible=0")));
    }

    [Fact]
    public async Task Brand_entry_prefills_only_yesterday_and_partial_components_do_not_become_zero()
    {
        var counts=new OperationalCompletionRepository(db.ConnectionString);var reports=new OperationalReportRepository(db.ConnectionString);
        await counts.SaveManualStockCountAsync("PREFILL",new(2030,6,1),"BRAND",10,5,1,2,18,"Yesterday","test","Test fixture");
        var proposed=Assert.Single(await reports.LoadBrandStockEntryAsync("PREFILL",new(2030,6,2)));
        Assert.Equal(10m,proposed.Display);Assert.Contains("Yesterday",proposed.Source);
        Assert.Empty(await counts.LoadManualStockCountsAsync("PREFILL",new(2030,6,2)));
        await counts.SaveManualStockCountAsync("PREFILL",new(2030,6,2),"BRAND",11,null,1,2,null,"Incomplete","test","Test fixture");
        Assert.Equal(11m,Assert.Single(await reports.LoadBrandStockEntryAsync("PREFILL",new(2030,6,2))).Display);
        Assert.Null(Assert.Single(await reports.LoadBrandPhysicalStockAsync("PREFILL",new(2030,6,2))).ComponentTotal);
        Assert.Empty(await reports.LoadBrandStockEntryAsync("PREFILL",new(2030,6,4)));
    }

    [Fact]
    public async Task Known_zero_sales_differs_from_missing_source_and_partial_walkins_do_not_inflate_conversion()
    {
        await db.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID();
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date,period_start,period_end,data_truth_version)
            VALUES(@batch,'empty-sales.xlsx',REPLICATE('e',64),1,'ZERO','R025','20310701','20310701','20310731',1);
            """);
        var rows=await new OperationalReportRepository(db.ConnectionString).LoadDsrAsync(new(2031,7,25),["ZERO","MISSING"]);
        var zero=rows.Single(x=>x.Store=="ZERO"&&x.Period=="FTD");
        Assert.Equal(0m,zero.TySales);Assert.Equal(0,zero.TyInvoices);Assert.Null(zero.LySales);Assert.Null(zero.Upt);Assert.Null(zero.ConversionPercent);
        Assert.Null(rows.Single(x=>x.Store=="MISSING"&&x.Period=="FTD").TyInvoices);
        Assert.Null(rows.Single(x=>x.Store=="COMBINED"&&x.Period=="FTD").TySales);
        var service=await new OperationalReportRepository(db.ConnectionString).LoadServiceSalesAsync(new(2031,7,25),["MISSING"]);
        Assert.All(service,x=>Assert.Null(x.Total));
    }

}
