using System.Globalization;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PrivatePhaseOneCorpusAttribute : FactAttribute
{
    public const string Root=@"C:\Codex\Reporting Manger\ETP Source Data";
    public PrivatePhaseOneCorpusAttribute()
    {
        if(!Directory.Exists(Path.Combine(Root,"HEMW","till 6 sep 26")) ||
           !Directory.Exists(Path.Combine(Root,"HEMW","HELIOS ALL REPORT 01 JULY 2026 TO 25 AUG 2026")) ||
           !Directory.Exists(Path.Combine(Root,"WLMHW","TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026")) ||
           !File.Exists(Path.Combine(Root,"HEMW","golden-monthly-HEMW-R025.csv")))
            Skip="Optional private acceptance corpus (both raw stores, consolidated Helios and monthly CSV) is incomplete or absent. Sanitised SQL behavior tests still run.";
    }
}

public sealed class PhaseOneImportSqlTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Financial_year_identity_content_dedupe_superset_and_conflicts_are_atomic()
    {
        await using var db=new TestDatabase();await db.InitializeAsync();
        var original=await Sample();
        var a=Row(original,2,"100000068",new(2025,1,1));
        var b=Row(original,3,"100000068",new(2025,6,21));
        var c=Row(original,4,"100000068",new(2026,7,1));
        var usecase=new SqlServerImportPersistenceUseCase(db.Fixture.ConnectionString);
        var first=await Save(usecase,Workbook(original,[a,b]));Assert.Equal(2,first.PersistedRows);
        var superset=await Save(usecase,Workbook(original,[a,b,c]));Assert.Equal(1,superset.PersistedRows);
        Assert.Equal(2,superset.AlreadyPresentRows);
        Assert.Equal(3,await db.Int("SELECT COUNT(*) FROM dbo.sales_lines"));
        Assert.Equal("2025,2026,2027",await db.Fixture.ExecuteAsync("SELECT STRING_AGG(CONVERT(varchar(4),invoice_year),',') WITHIN GROUP(ORDER BY invoice_year) FROM dbo.sales_invoices"));
        Assert.Equal(1,await db.Int("SELECT COUNT(*) FROM dbo.import_restatements"));
        var reordered=await Save(usecase,Workbook(original,[c with {RowNumber=2},a with {RowNumber=3},b with {RowNumber=4}]));
        Assert.Equal("Duplicate content",reordered.Status);Assert.Equal(0,reordered.PersistedRows);
        Assert.Equal(3,await db.Int("SELECT COUNT(*) FROM dbo.sales_lines"));
        var subset=await Save(usecase,Workbook(original,[b]));Assert.Equal("Duplicate content",subset.Status);
        var changed=Row(original,4,"100000068",new(2026,7,1),236m);
        var error=await Assert.ThrowsAsync<Etp.Reporting.Import.Batch.ImportSourceException>(()=>Save(usecase,Workbook(original,[a,b,changed])));
        Assert.Contains("Use Restate",error.Message);
        Assert.Equal(3,await db.Int("SELECT COUNT(*) FROM dbo.sales_lines"));
        Assert.Equal(0,await db.Int("SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE outcome='CONFLICT'"));
        Assert.Equal(354m,Convert.ToDecimal(await db.Fixture.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines")));
        Assert.Equal(300m,Convert.ToDecimal(await db.Fixture.ExecuteAsync("SELECT SUM(source_net_amount) FROM dbo.sales_lines")));
        Assert.Equal(54m,Convert.ToDecimal(await db.Fixture.ExecuteAsync("SELECT SUM(source_tax_amount) FROM dbo.sales_lines")));
        var day=await new DailyReportingWorkflowRepository(db.Fixture.ConnectionString).LoadAsync("HEMW",new(2025,3,5));
        Assert.Contains("R025",day.ImportedReports);
    }

    [Fact]
    public async Task Customer_only_corrections_require_restatement_and_preserve_full_source_values()
    {
        await using var db=new TestDatabase();await db.InitializeAsync();var sample=await Sample();
        var row=Row(sample,2,"100000001",new(2026,8,25));
        var service=new SqlServerImportPersistenceUseCase(db.Fixture.ConnectionString);
        await Save(service,Workbook(sample,[row]));
        var cells=row.Cells.ToArray();
        cells[sample.Sheets[0].Headers.ToList().IndexOf("CONTACTNO")]=new("9876500123");
        var corrected=Workbook(sample,[row with {Cells=cells}]);
        var error=await Assert.ThrowsAsync<Etp.Reporting.Import.Batch.ImportSourceException>(()=>Save(service,corrected));
        Assert.Contains("Use Restate",error.Message);
        Assert.Equal(1,await db.Int("SELECT COUNT(*) FROM dbo.import_files"));
        var oldId=Convert.ToInt64(await db.Fixture.ExecuteAsync("SELECT import_file_id FROM dbo.import_files"));
        var accepted=new MatchedImportEnvelopeFactory().RequireAccepted(corrected);
        var pending = await Assert.ThrowsAsync<Etp.Reporting.Import.Batch.ImportSourceException>(() => service.PersistAsync(new(accepted,new(2026,8,25),"HEMW","SQL test",new(oldId,"SQL test","Correct synthetic contact"))));
        Assert.Equal("RESTATEMENT_APPROVAL_PENDING", pending.Code);
        Assert.Equal(1,await db.Int("SELECT COUNT(*) FROM dbo.import_files"));
        await db.Fixture.ExecuteAsync("DECLARE @id bigint=(SELECT approval_request_id FROM dbo.approval_requests WHERE approval_type='RESTATEMENT'); EXEC dbo.decide_approval_request @id,1,N'Checked exact replacement';");
        await service.PersistAsync(new(accepted,new(2026,8,25),"HEMW","SQL test",new(oldId,"SQL test","Correct synthetic contact")));
        Assert.Equal(1,await db.Int("SELECT COUNT(*) FROM dbo.sales_lines"));
        Assert.Equal("9876500123",await db.Fixture.ExecuteAsync("SELECT r.customer_phone FROM dbo.etp_r025 r JOIN dbo.import_files f ON f.import_file_id=r.import_file_id WHERE f.is_superseded=0"));
        Assert.Equal(2,await db.Int("SELECT COUNT(*) FROM dbo.etp_r025"));
    }

    [Fact]
    public async Task Reimporting_the_same_Phase_zero_file_upgrades_values_and_content_identity()
    {
        await using var db=new TestDatabase();await db.InitializeAsync();
        var sample=await Sample();var workbook=Workbook(sample,[Row(sample,2,"100000001",new(2026,8,25))]);
        var service=new SqlServerImportPersistenceUseCase(db.Fixture.ConnectionString);
        await Save(service,workbook);
        // Reproduce the Phase 0 persisted shape and retained file hash.
        await db.Fixture.ExecuteAsync("UPDATE dbo.import_files SET data_truth_version=0; DELETE dbo.etp_import_content; DELETE dbo.etp_r025; UPDATE dbo.sales_lines SET source_gross_amount=NULL,source_tax_amount=NULL,line_identifier=N'2'");
        Assert.False(await service.ExistsByHashAsync(workbook.Sha256));
        var upgraded=await Save(service,workbook);
        Assert.Equal(1,upgraded.PersistedRows);
        Assert.Equal(118m,Convert.ToDecimal(await db.Fixture.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines")));
        Assert.Equal(1,await db.Int("SELECT COUNT(*) FROM dbo.sales_lines"));
        Assert.Equal(2,await db.Int("SELECT COUNT(*) FROM dbo.import_files"));
        Assert.Equal(1,await db.Int("SELECT COUNT(*) FROM dbo.import_files WHERE is_superseded=0 AND data_truth_version=1"));
        Assert.Equal(1,await db.Int("SELECT COUNT(*) FROM dbo.import_restatements"));
        Assert.True(await service.ExistsByHashAsync(workbook.Sha256));
    }

    [Fact]
    public async Task A_changed_legacy_file_cannot_auto_supersede_facts_without_a_content_manifest()
    {
        await using var db=new TestDatabase();await db.InitializeAsync();var sample=await Sample();
        var a=Row(sample,2,"100000001",new(2026,8,1));var b=Row(sample,3,"100000002",new(2026,8,25));
        var service=new SqlServerImportPersistenceUseCase(db.Fixture.ConnectionString);
        await Save(service,Workbook(sample,[a,b]));
        await db.Fixture.ExecuteAsync("UPDATE dbo.import_files SET data_truth_version=0; DELETE dbo.etp_import_content; DELETE dbo.etp_r025");
        var error=await Assert.ThrowsAsync<Etp.Reporting.Import.Batch.ImportSourceException>(()=>
            Save(service,Workbook(sample,[a,Row(sample,3,"100000002",new(2026,8,25),236m)])));
        Assert.Contains("Re-import its original workbook",error.Message);
        Assert.Equal(2,await db.Int("SELECT COUNT(*) FROM dbo.sales_lines"));
        Assert.Equal(236m,Convert.ToDecimal(await db.Fixture.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines")));
        Assert.Equal(0,await db.Int("SELECT COUNT(*) FROM dbo.import_restatements"));
    }

    [Fact]
    public async Task A_superset_replaces_two_separate_period_files_in_one_transaction()
    {
        await using var db=new TestDatabase();await db.InitializeAsync();var sample=await Sample();
        var a=Row(sample,2,"100000001",new(2026,7,1));var b=Row(sample,3,"100000002",new(2026,8,1));
        var service=new SqlServerImportPersistenceUseCase(db.Fixture.ConnectionString);
        await Save(service,Workbook(sample,[a]));await Save(service,Workbook(sample,[b]));
        await Save(service,Workbook(sample,[a,b,Row(sample,4,"100000003",new(2026,8,2))]));
        Assert.Equal(3,await db.Int("SELECT COUNT(*) FROM dbo.sales_lines"));
        Assert.Equal(1,await db.Int("SELECT COUNT(*) FROM dbo.import_files WHERE is_superseded=0"));
        Assert.Equal(2,await db.Int("SELECT COUNT(*) FROM dbo.import_restatements"));
    }

    [Fact]
    public async Task A_locked_day_inside_the_range_prevents_the_whole_file()
    {
        await using var db=new TestDatabase();await db.InitializeAsync();var source=await Sample();
        await db.Fixture.ExecuteAsync("INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('HEMW','20260820','LOCKED',N'SQL test',SYSUTCDATETIME())");
        var workbook=Workbook(source,[Row(source,2,"100000001",new(2026,8,1)),Row(source,3,"100000002",new(2026,8,25))]);
        var error=await Assert.ThrowsAsync<SqlException>(()=>Save(new(db.Fixture.ConnectionString),workbook));
        Assert.Contains("finalised",error.Message);
        Assert.Equal(0,await db.Int("SELECT COUNT(*) FROM dbo.sales_lines"));
        Assert.Equal(0,await db.Int("SELECT COUNT(*) FROM dbo.import_files"));
    }

    [PrivatePhaseOneCorpus]
    public async Task Both_raw_store_folders_import_in_one_action_with_golden_sales_tenders_staff_and_headers()
    {
        await using var db=new TestDatabase();await db.InitializeAsync();
        var folders=new[]
        {
            Path.Combine(PrivatePhaseOneCorpusAttribute.Root,"WLMHW","TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026"),
            Path.Combine(PrivatePhaseOneCorpusAttribute.Root,"HEMW","HELIOS ALL REPORT 01 JULY 2026 TO 25 AUG 2026")
        };
        var paths=folders.SelectMany(path=>Directory.GetFiles(path,"*.xlsx")).ToArray();
        var service=new FolderImportService(new SqlServerImportPersistenceUseCase(db.Fixture.ConnectionString));
        var result=await service.RunFilesAsync(paths,new("Both stores SQL golden"));
        foreach(var f in result.Files) output.WriteLine($"{f.StoreCode}/{f.ReportCode}: {f.Status}; {f.Message}");
        Assert.Equal(59,result.Files.Count);
        Assert.All(result.Files,f=>Assert.Contains(f.Status,new[]{"Imported","empty export"}));
        var report=new SqlServerReportingQueryRepository(db.Fixture.ConnectionString);
        foreach(var store in new[]{"WLMHW","HEMW"})
        {
            var day=new ReportingQueryScope(new(2026,8,25),new(2026,8,25),[store]);
            var month=new ReportingQueryScope(new(2026,8,1),new(2026,8,25),[store]);
            var lines=await report.LoadSalesAsync(day);
            Assert.Equal(store=="WLMHW"?34215m:29290m,lines.Sum(x=>x.SourceGrossAmount));
            Assert.Equal(store=="WLMHW"?28995.76m:24822.02m,lines.Sum(x=>x.SourceNetAmount));
            var monthly=await report.LoadSalesAsync(month);
            Assert.Equal(store=="WLMHW"?938197m:774868.60m,monthly.Sum(x=>x.SourceGrossAmount));
            Assert.Equal(store=="WLMHW"?182:38,monthly.Select(x=>x.DocumentNumber).Distinct().Count());
            var tender=await report.LoadTendersAsync(month);
            var modes=tender.GroupBy(x=>x.TenderType).OrderByDescending(g=>g.Sum(x=>x.SourceAmount)).ToArray();
            Assert.Equal("UPI",modes[0].Key);
            foreach(var date in monthly.Select(x=>x.TransactionDate).Distinct())
                Assert.Equal(monthly.Where(x=>x.TransactionDate==date).Sum(x=>x.SourceGrossAmount),
                    (await report.LoadTendersAsync(new(date,date,[store]))).Sum(x=>x.SourceAmount));
            var staff=await new OperationalReportRepository(db.Fixture.ConnectionString).LoadStaffPerformanceAsync(day);
            Assert.Equal(lines.Sum(x=>x.SourceGrossAmount),staff.AttributedSales);
            Assert.Equal(0m,staff.Variance);
            Assert.All(staff.Rows,row=>Assert.False(string.IsNullOrWhiteSpace(row.CroName)));
            output.WriteLine($"{store} 25Aug gross={lines.Sum(x=>x.SourceGrossAmount)} net={lines.Sum(x=>x.SourceNetAmount)}; Aug gross={monthly.Sum(x=>x.SourceGrossAmount)}; UPI={modes[0].Sum(x=>x.SourceAmount)}; staff={staff.AttributedSales}");
        }
        Assert.Equal(0,await db.Int("SELECT COUNT(*) FROM dbo.sales_tenders WHERE is_reporting_eligible=0"));
        foreach(var path in paths)
        {
            var accepted=new MatchedImportEnvelopeFactory().RequireAccepted(await new OpenXmlWorkbookReader().ReadAsync(path));
            if(accepted.ProfileIdentity.ReportCode is not ("R008" or "R009" or "R012" or "R013" or "R020" or "R024" or "R029")) continue;
            var family=EtpReportFamilyRegistry.Resolve(accepted.ProfileIdentity.ReportCode);
            var dateColumn=family.Columns.Single(x=>x.SourceHeader==family.PrimaryDateHeader).CanonicalField;
            var expected=accepted.Staging.Rows.Count(row=>row.Values.GetValueOrDefault(dateColumn) is DateOnly date && date==new DateOnly(2026,8,25));
            var actual=await db.Int($"SELECT COUNT(*) FROM dbo.[{family.TableName}] t JOIN dbo.import_files f ON f.import_file_id=t.import_file_id WHERE f.store_code='{accepted.Scope.StoreCode}' AND t.[{dateColumn}]='20260825' AND f.is_superseded=0");
            Assert.Equal(expected,actual);
        }
        // Moving physical worksheet rows produces no extra canonical facts.
        var titan=paths.Single(path=>path.Contains("WLMHW") && Path.GetFileName(path).Contains("SDB-VariantwiseSales"));
        var original=await new OpenXmlWorkbookReader().ReadAsync(titan);
        var data=original.Sheets[0];
        var moved=data.Rows.TakeLast(3).Concat(data.Rows.SkipLast(3)).Select((row,i)=>row with {RowNumber=i+2}).ToArray();
        var reordered=await Save(new(db.Fixture.ConnectionString),Workbook(original,moved));
        Assert.Equal("Duplicate content",reordered.Status);Assert.Equal(0,reordered.PersistedRows);
    }

    [PrivatePhaseOneCorpus]
    public async Task Whole_Helios_folder_matches_monthly_golden_reimports_and_raw_subset_without_new_sales()
    {
        await using var db=new TestDatabase();await db.InitializeAsync();
        var service=new FolderImportService(new SqlServerImportPersistenceUseCase(db.Fixture.ConnectionString));
        var folder=Path.Combine(PrivatePhaseOneCorpusAttribute.Root,"HEMW","till 6 sep 26");
        var first=await service.RunAsync(folder,new("Phase 1 SQL golden"));
        foreach(var f in first.Files) output.WriteLine($"{f.ReportCode}: {f.Status}; {f.RowsProcessed} rows; {f.Message}");
        Assert.DoesNotContain(first.Files,f=>f.Failed || f.Status=="Unknown layout");
        Assert.Equal(31,first.Files.Count(f=>f.Status is "Imported" or "empty export"));
        Assert.Equal(790,await db.Int("SELECT COUNT(*) FROM dbo.sales_lines"));
        Assert.Equal(759,await db.Int("SELECT COUNT(*) FROM dbo.sales_invoices"));
        Assert.Equal(3955,await db.Int("SELECT COUNT(*) FROM dbo.stock_movements"));
        Assert.Equal(466,await db.Int("SELECT COUNT(*) FROM dbo.stock_snapshots WHERE snapshot_date='20260907'"));
        Assert.Equal(0,await db.Int("SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE outcome='CONFLICT'"));
        Assert.Equal("2025,2026,2027",await db.Fixture.ExecuteAsync("SELECT STRING_AGG(CONVERT(varchar(4),invoice_year),',') WITHIN GROUP(ORDER BY invoice_year) FROM dbo.sales_invoices WHERE document_number='100000068'"));
        foreach(var line in File.ReadLines(Path.Combine(PrivatePhaseOneCorpusAttribute.Root,"HEMW","golden-monthly-HEMW-R025.csv")).Skip(1))
        {
            var cells=line.Split(',');var year=int.Parse(cells[1]);var month=int.Parse(cells[2]);
            await using var connection=new SqlConnection(db.Fixture.ConnectionString);await connection.OpenAsync();
            await using var q=new SqlCommand("SELECT COUNT(DISTINCT i.sales_invoice_id),SUM(l.source_quantity),SUM(l.source_gross_amount),SUM(l.source_net_amount),SUM(l.source_tax_amount),SUM(CASE WHEN l.source_transaction_type='SR' THEN 1 ELSE 0 END),SUM(CASE WHEN l.source_transaction_type='SR' THEN l.source_gross_amount ELSE 0 END) FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id WHERE YEAR(i.transaction_date)=@year AND MONTH(i.transaction_date)=@month",connection);
            q.Parameters.AddWithValue("@year",year);q.Parameters.AddWithValue("@month",month);
            await using var reader=await q.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
            Assert.Equal(int.Parse(cells[3]),reader.GetInt32(0));
            for(var i=1;i<=4;i++) Assert.Equal(decimal.Parse(cells[i+3],CultureInfo.InvariantCulture),reader.GetDecimal(i));
            Assert.Equal(int.Parse(cells[8]),reader.GetInt32(5));
            Assert.Equal(decimal.Parse(cells[9],CultureInfo.InvariantCulture),reader.GetDecimal(6));
        }
        var reporting=new SqlServerReportingQueryRepository(db.Fixture.ConnectionString);
        var august=new ReportingQueryScope(new(2026,8,1),new(2026,8,31),["HEMW"]);
        var augustSales=await reporting.LoadSalesAsync(august);
        var augustTenders=await reporting.LoadTendersAsync(august);
        Assert.Equal("UPI",augustTenders.GroupBy(x=>x.TenderType).OrderByDescending(g=>g.Sum(x=>x.SourceAmount)).First().Key);
        Assert.Equal(1015259.10m,augustSales.Sum(x=>x.SourceGrossAmount));
        foreach(var date in augustSales.Select(x=>x.TransactionDate).Distinct())
            Assert.Equal(augustSales.Where(x=>x.TransactionDate==date).Sum(x=>x.SourceGrossAmount),
                (await reporting.LoadTendersAsync(new(date,date,["HEMW"]))).Sum(x=>x.SourceAmount));
        Assert.Equal(0,await db.Int("SELECT COUNT(*) FROM dbo.sales_tenders WHERE is_reporting_eligible=0"));
        var again=await service.RunAsync(folder,new("Phase 1 SQL golden"));
        Assert.All(again.Files.Where(f=>f.Status!="Not needed"),f=>Assert.Equal("Duplicate",f.Status));
        Assert.All(again.Files,f=>Assert.Equal(0,f.NewRows));
        var raw=Path.Combine(PrivatePhaseOneCorpusAttribute.Root,"HEMW","HELIOS ALL REPORT 01 JULY 2026 TO 25 AUG 2026");
        var subset=await service.RunAsync(raw,new("Phase 1 SQL golden"));
        foreach(var f in subset.Files) output.WriteLine($"Raw {f.ReportCode}: {f.Status}; {f.Message}");
        Assert.Equal("Duplicate content",subset.Files.Single(f=>f.ReportCode=="R025").Status);
        // These older exports genuinely changed after banking and a late 25-Aug sale.
        // Task 9 requires an explicit restatement for changed overlap; sales remain a true subset.
        Assert.Equal(new[]{"R008","R009","R014"},subset.Files.Where(f=>f.Failed).Select(f=>f.ReportCode).Order().ToArray());
        Assert.All(subset.Files.Where(f=>f.Failed),f=>Assert.Contains("Use Restate",f.Message));
        Assert.DoesNotContain(subset.Files,f=>f.Status=="Unknown layout");
        Assert.Equal(790,await db.Int("SELECT COUNT(*) FROM dbo.sales_lines"));
    }

    private static async Task<WorkbookSnapshot> Sample()=>await new OpenXmlWorkbookReader().ReadAsync(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"fixtures","etp-sample"),"R025_*.xlsx").Single());
    private static WorkbookRow Row(WorkbookSnapshot sample,int row,string invoice,DateOnly date,decimal gross=118m)
    {
        var sheet=sample.Sheets[0];var cells=sheet.Rows[0].Cells.ToArray();
        void Set(string header,object value)=>cells[sheet.Headers.ToList().FindIndex(x=>x==header)]=new(value);
        Set("INVNUMBER",invoice);Set("INVDATE",date);Set("NETAMOUNT",gross);
        return new(row,cells);
    }
    private static WorkbookSnapshot Workbook(WorkbookSnapshot sample,WorkbookRow[] rows)=>sample with
    { Sha256=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"),Sheets=[sample.Sheets[0] with {Rows=rows}] };
    private static Task<ImportPersistenceResult> Save(SqlServerImportPersistenceUseCase service,WorkbookSnapshot workbook)
    {
        var accepted=new MatchedImportEnvelopeFactory().RequireAccepted(workbook);
        return service.PersistAsync(new(accepted,accepted.Scope.PeriodEnd!.Value,accepted.Scope.StoreCode!,"SQL behavior test"));
    }
    private sealed class TestDatabase : IAsyncDisposable
    {
        public SqlDatabaseFixture Fixture {get;}=new();
        public Task InitializeAsync()=>Fixture.InitializeAsync();
        public async Task<int> Int(string sql)=>Convert.ToInt32(await Fixture.ExecuteAsync(sql));
        public async ValueTask DisposeAsync()=>await Fixture.DisposeAsync();
    }
}
