using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// 1.10.1, lane service-sql: migration 0052 against SQL on the synthetic week fixtures (see ServiceJobModelSqlTests for
/// the fixture plan). Each case the fixtures lack is made by changing a few landing rows in the test's own database:
/// an S009 SRN_For_Repair row (R-SQL-04), a reopened SRN beside a later closed one (R-SQL-03), a job held only by an older
/// S009 reading (R-SQL-09), an overlapping S003 reading and an S003 line without a date (R-SQL-10, R-SQL-17 c), an old WRA
/// line carrying a WDC document and a job number (R-SQL-11, R-SQL-17 b). The Service-money shop flag (RA-OPS-21) needs no
/// fixture. Synthetic data only; each test builds its own database.
/// </summary>
public sealed class ServiceUiFixesSqlTests
{
    private static string Job(int n) => $"JOAW330SYN{n:0000}";

    private const string LatestS009 = "(SELECT r.import_file_id FROM dbo.v_service_readings r WHERE r.report_code='S009' AND r.is_latest=1)";

    [SqlFact]
    public async Task S009_SRN_For_Repair_is_on_the_bench_and_an_SRN_out_is_dated_by_its_open_SRN()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await ImportAsync(database, FixtureFolder("week1"));
            await ImportAsync(database, FixtureFolder("week2"));

            // R-SQL-04 (design 4.2 rule 8): SRN_For_Repair in the latest S009 is still at the bench; SRN_Issued is out.
            await database.ExecuteAsync($"""
                UPDATE dbo.etp_landing_s009 SET indentid=NULL,indentdate=NULL,jostatus=N'SRN_For_Repair'
                WHERE import_file_id={LatestS009} AND LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber)))=N'{Job(27)}';
                """);
            Assert.Equal(ServiceStages.OnBench, await TextAsync(database, $"SELECT stage FROM dbo.v_service_job WHERE job_order_number=N'{Job(27)}'"));
            await database.ExecuteAsync($"""
                UPDATE dbo.etp_landing_s009 SET jostatus=N'SRN_Issued'
                WHERE import_file_id={LatestS009} AND LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber)))=N'{Job(27)}';
                """);
            Assert.Equal(ServiceStages.SrnOut, await TextAsync(database, $"SELECT stage FROM dbo.v_service_job WHERE job_order_number=N'{Job(27)}'"));

            // R-SQL-03 tail: job 21's latest S011 reading gets a second, closed SRN dated after its reopened one. The job is
            // out for repair and dated by the open SRN, never by the closed one's later date.
            await database.ExecuteAsync($"""
                DECLARE @file bigint=(SELECT MAX(s.import_file_id) FROM dbo.etp_landing_s011 s
                  JOIN dbo.v_service_readings r ON r.import_file_id=s.import_file_id AND r.report_code='S011'
                  WHERE LTRIM(RTRIM(CONVERT(nvarchar(100),s.joborder_number)))=N'{Job(21)}');
                DECLARE @cols nvarchar(max)=(SELECT STRING_AGG(QUOTENAME(name),',') FROM sys.columns
                  WHERE object_id=OBJECT_ID(N'dbo.etp_landing_s011') AND is_identity=0 AND is_computed=0 AND name<>N'source_lineage_id');
                DECLARE @row bigint=(SELECT MIN(etp_row_id) FROM dbo.etp_landing_s011 WHERE import_file_id=@file
                  AND LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number)))=N'{Job(21)}');
                -- Every landing row has its own source_lineage row (unique), so the copy gets a new one.
                INSERT dbo.source_lineage(import_file_id,source_row_number,source_record_type,sheet_name)
                SELECT l.import_file_id,l.source_row_number+100000,l.source_record_type,l.sheet_name
                FROM dbo.etp_landing_s011 s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id WHERE s.etp_row_id=@row;
                DECLARE @lineage bigint=SCOPE_IDENTITY();
                DECLARE @sql nvarchar(max)=N'INSERT dbo.etp_landing_s011(source_lineage_id,'+@cols+N') SELECT @lineage,'+@cols+N' FROM dbo.etp_landing_s011 WHERE etp_row_id=@row';
                EXEC sp_executesql @sql,N'@lineage bigint,@row bigint',@lineage=@lineage,@row=@row;
                UPDATE dbo.etp_landing_s011 SET srn_date='2026-09-28',srn_received_date='2026-09-30',to_status=N'SRN_after_Repair_Received'
                WHERE etp_row_id=(SELECT MAX(etp_row_id) FROM dbo.etp_landing_s011 WHERE import_file_id=@file);
                UPDATE dbo.etp_landing_s011 SET srn_date='2026-09-10',srn_received_date=NULL,repaired_date=NULL,to_status=N'SRN_For_Repair_DC_Created'
                WHERE etp_row_id=@row;
                """);
            Assert.Equal(ServiceStages.SrnOut, await TextAsync(database, $"SELECT stage FROM dbo.v_service_job WHERE job_order_number=N'{Job(21)}'"));
            Assert.Equal("2026-09-10", await TextAsync(database, $"SELECT CONVERT(char(10),stage_date,23) FROM dbo.v_service_job WHERE job_order_number=N'{Job(21)}'"));
            Assert.Equal("2026-09-10", await TextAsync(database, $"SELECT CONVERT(char(10),srn_date,23) FROM dbo.v_service_job WHERE job_order_number=N'{Job(21)}'"));
        }
        finally { await database.DisposeAsync(); }
    }

    [SqlFact]
    public async Task Readings_dates_claims_and_revenue_follow_the_1_10_1_rules()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await ImportAsync(database, FixtureFolder("week1"));
            await ImportAsync(database, FixtureFolder("week2"));

            // R-SQL-09: a job only an older S009 reading holds has no reading date of its own (aged to as_at).
            await database.ExecuteAsync($"""
                UPDATE dbo.etp_landing_s009 SET jonumber=N'{Job(99)}'
                WHERE etp_row_id=(SELECT MIN(s.etp_row_id) FROM dbo.etp_landing_s009 s
                  WHERE s.import_file_id=(SELECT MIN(r.import_file_id) FROM dbo.v_service_readings r WHERE r.report_code='S009' AND r.is_latest=0));
                """);
            Assert.Equal(1, await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_job WHERE job_order_number=N'{Job(99)}' AND last_reading_date IS NULL"));

            // R-SQL-13: open ages and days in stage run to the job's own as-at.
            Assert.Equal(0, await IntAsync(database, """
                SELECT COUNT(*) FROM dbo.v_service_job WHERE is_open=1 AND stage NOT IN('DELIVERED','RWR')
                  AND (age_days<>DATEDIFF(day,booking_date,COALESCE(last_reading_date,as_at)) OR days_in_stage<>DATEDIFF(day,stage_date,COALESCE(last_reading_date,as_at)))
                """));
            Assert.Equal(0, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job WHERE age_days<0 OR days_in_stage<0"));

            // R-SQL-16 / R-UI-07: a claimed DC job is closed on its first WDC claim date; delivered jobs on the delivery date.
            Assert.Equal(1, await IntAsync(database, $"""
                SELECT COUNT(*) FROM dbo.v_service_job j
                WHERE j.job_order_number=N'{Job(10)}' AND j.is_open=0 AND j.claim_date=j.closed_date
                  AND j.claim_date=(SELECT MIN(c.business_date) FROM dbo.v_service_claims c WHERE c.claim_type='WDC' AND c.job_order_number=j.job_order_number)
                """));
            Assert.Equal(0, await IntAsync(database, """
                SELECT COUNT(*) FROM dbo.v_service_job
                WHERE (is_open=1 AND closed_date IS NOT NULL) OR (is_open=0 AND closed_date IS NULL)
                  OR (stage IN('DELIVERED','RWR') AND closed_date<>stage_date) OR (claim_date IS NOT NULL AND stage NOT IN('DC_ISSUED','RA_ISSUED'))
                """));

            // R-SQL-11 / R-SQL-17 b: an old WRA (S026) line that carries a WDC document is a WDC line only, so it never
            // closes an RA job as "WRA claimed", even with that job's number on it.
            await database.ExecuteAsync($"""
                UPDATE dbo.etp_landing_s026
                SET documentnum=(SELECT TOP (1) CONVERT(nvarchar(100),document_number) FROM dbo.etp_landing_s039 WHERE document_number IS NOT NULL ORDER BY document_number),
                  jonumber=N'{Job(30)}'
                WHERE LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber)))=N'{Job(14)}';
                """);
            Assert.Equal(0, await IntAsync(database, $"SELECT CONVERT(int,claim_raised) FROM dbo.v_service_job WHERE job_order_number=N'{Job(30)}'"));
            Assert.Equal(1, await IntAsync(database, $"SELECT CONVERT(int,is_open) FROM dbo.v_service_job WHERE job_order_number=N'{Job(30)}'"));
            Assert.Equal(1, await IntAsync(database, $"SELECT CONVERT(int,claim_raised) FROM dbo.v_service_job WHERE job_order_number=N'{Job(14)}'"));
            Assert.Equal(0, await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_claims WHERE claim_type='WRA' AND job_order_number=N'{Job(30)}'"));

            // R-SQL-17 c: a second S003 reading that overlaps the latest one (same lines, one day later) wins every date, and
            // the job revenue is unchanged: each line is counted once.
            const string Revenue = "SELECT CONCAT(SUM(revenue_net_incl_tax),'|',SUM(revenue_documents)) FROM dbo.v_service_job";
            var before = await TextAsync(database, Revenue);
            await database.ExecuteAsync("""
                DECLARE @old bigint=(SELECT MAX(r.import_file_id) FROM dbo.v_service_readings r WHERE r.report_code='S003'
                  AND r.snapshot_date=(SELECT MAX(snapshot_date) FROM dbo.v_service_readings WHERE report_code='S003'));
                INSERT dbo.import_files(import_batch_id,import_profile_id,original_file_name,source_sha256,size_bytes,source_row_count,report_code,store_code,
                  business_date,source_report_date,imported_by,is_superseded,period_start,period_end,data_truth_version)
                SELECT import_batch_id,import_profile_id,original_file_name,REPLICATE('e',64),size_bytes,source_row_count,report_code,store_code,
                  DATEADD(day,1,business_date),source_report_date,imported_by,0,period_start,DATEADD(day,1,COALESCE(period_end,business_date)),data_truth_version
                FROM dbo.import_files WHERE import_file_id=@old;
                DECLARE @new bigint=SCOPE_IDENTITY();
                -- Each copied line gets its own source_lineage row in the new file (old id -> new id in #map).
                CREATE TABLE #map(old_id bigint NOT NULL, new_id bigint NOT NULL);
                MERGE dbo.source_lineage AS t
                USING (SELECT l.source_lineage_id old_id,l.source_row_number,l.source_record_type,l.sheet_name
                  FROM dbo.etp_landing_s003 s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id WHERE s.import_file_id=@old) AS src
                ON 1=0
                WHEN NOT MATCHED THEN INSERT(import_file_id,source_row_number,source_record_type,sheet_name)
                  VALUES(@new,src.source_row_number,src.source_record_type,src.sheet_name)
                OUTPUT src.old_id,inserted.source_lineage_id INTO #map(old_id,new_id);
                DECLARE @cols nvarchar(max)=(SELECT STRING_AGG(QUOTENAME(name),',') FROM sys.columns
                  WHERE object_id=OBJECT_ID(N'dbo.etp_landing_s003') AND is_identity=0 AND is_computed=0 AND name NOT IN(N'import_file_id',N'source_lineage_id'));
                DECLARE @source nvarchar(max)=(SELECT STRING_AGG(N's.'+QUOTENAME(name),',') FROM sys.columns
                  WHERE object_id=OBJECT_ID(N'dbo.etp_landing_s003') AND is_identity=0 AND is_computed=0 AND name NOT IN(N'import_file_id',N'source_lineage_id'));
                DECLARE @sql nvarchar(max)=N'INSERT dbo.etp_landing_s003(import_file_id,source_lineage_id,'+@cols+N') SELECT @new,m.new_id,'+@source
                  +N' FROM dbo.etp_landing_s003 s JOIN #map m ON m.old_id=s.source_lineage_id WHERE s.import_file_id=@old';
                EXEC sp_executesql @sql,N'@new bigint,@old bigint',@new=@new,@old=@old;
                """);
            Assert.Equal(before, await TextAsync(database, Revenue));

            // R-SQL-10: an S003 line without a business date is in no DateLog reading and never counts.
            const string Winners = """
                SELECT SUM(s.netamount_incl_tax) FROM dbo.etp_landing_s003 s
                JOIN dbo.v_service_datelog_readings w ON w.report_code='S003' AND w.reading_rank=1 AND w.import_file_id=s.import_file_id AND w.business_date=s.trans_date
                WHERE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.joborder_number))),N'') IS NOT NULL
                """;
            await database.ExecuteAsync("""
                UPDATE dbo.etp_landing_s003 SET trans_date=NULL
                WHERE etp_row_id=(SELECT MIN(etp_row_id) FROM dbo.etp_landing_s003 WHERE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N'') IS NOT NULL);
                """);
            Assert.Equal(Convert.ToDecimal(await database.ExecuteAsync(Winners, 300)),
                Convert.ToDecimal(await database.ExecuteAsync("SELECT SUM(revenue_net_incl_tax) FROM dbo.v_service_job", 300)));

            // R-SQL-15: the query shares one read of the job rows per data revision; a changed reading (here its period) renews it.
            SqlServerServiceReportQuery.ForgetJobRows();
            var query = new SqlServerServiceReportQuery(database.ConnectionString);
            var first = await query.LoadJobSummariesAsync();
            Assert.Same(first, await new SqlServerServiceReportQuery(database.ConnectionString).LoadJobSummariesAsync());
            Assert.Equal(first.Count(job => job.IsOpen), (await query.LoadPendingBoardAsync()).OpenJobs);
            await database.ExecuteAsync("UPDATE dbo.import_files SET period_end=DATEADD(day,1,period_end) WHERE import_file_id=(SELECT MAX(import_file_id) FROM dbo.import_files WHERE report_code='S003')");
            Assert.NotSame(first, await query.LoadJobSummariesAsync());

            // R-SQL-08: the week fixtures' GPRC lines leave no hole, and the Claims screen states this month by the one rule.
            var claims = await query.LoadClaimsAsync(null, null);
            Assert.Null(claims.GprcHoleFrom);
            Assert.NotNull(claims.RaisedThisMonth);
            Assert.Equal(ServiceClaimRules.RaisedInMonth(claims.Lines, claims.AsAt!.Value), claims.RaisedThisMonth);
        }
        finally { await database.DisposeAsync(); }
    }

    [SqlFact]
    public async Task The_Service_money_shop_is_the_store_flagged_in_data_and_only_one_store_holds_the_flag()
    {
        // RA-OPS-21: 0052 seeds the flag on the shop the 0048 view named; moving it in data moves the view; a second
        // flagged store is refused.
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            Assert.Equal("WLMHW", await TextAsync(database, "SELECT store_code FROM dbo.stores WHERE is_service_money_shop=1"));
            await database.ExecuteAsync("""
                INSERT dbo.manual_operational_inputs(store_code,business_date,field_code,numeric_value,entered_by,modified_by,change_reason) VALUES
                ('WLMHW','20260825','SERVICE_CASH',700,'test','test','Synthetic service money'),
                ('HEMW','20260825','SERVICE_CASH',300,'test','test','Synthetic service money');
                """);
            const string Marked = "SELECT STRING_AGG(store_code,',') FROM dbo.v_service_manual_money WHERE is_service_money_shop=1";
            Assert.Equal("WLMHW", await TextAsync(database, Marked));
            Assert.Equal(2, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_manual_money"));

            await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("UPDATE dbo.stores SET is_service_money_shop=1 WHERE store_code='HEMW'"));
            await database.ExecuteAsync("""
                UPDATE dbo.stores SET is_service_money_shop=0 WHERE store_code='WLMHW';
                UPDATE dbo.stores SET is_service_money_shop=1 WHERE store_code='HEMW';
                """);
            Assert.Equal("HEMW", await TextAsync(database, Marked));
            // A store added later is not the Service-money shop unless someone says so.
            Assert.Equal(0, await IntAsync(database, "SELECT CONVERT(int,is_service_money_shop) FROM dbo.stores WHERE store_code='WLMHW'"));
        }
        finally { await database.DisposeAsync(); }
    }

    private static async Task<FolderImportSummary> ImportAsync(SqlDatabaseFixture database, string folder) =>
        await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString)).RunAsync(folder, new("Synthetic Owner"));

    private static string FixtureFolder(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "tests-dotnet", "fixtures", "service-interim", name);
    }

    private static async Task<int> IntAsync(SqlDatabaseFixture database, string sql) => Convert.ToInt32(await database.ExecuteAsync(sql, 300));

    private static async Task<string?> TextAsync(SqlDatabaseFixture database, string sql) => (await database.ExecuteAsync(sql, 300))?.ToString();
}
