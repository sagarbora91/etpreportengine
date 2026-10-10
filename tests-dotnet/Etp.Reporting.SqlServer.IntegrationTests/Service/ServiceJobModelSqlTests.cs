using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// Service UI 1.10.0 (decision 25), lane sql: the 0050 views over the synthetic week fixtures
/// (tests-dotnet/fixtures/service-interim/week1 and week2), imported through the real folder import path.
/// The fixture plan (scripts/service-centre/generate_fixtures.py) puts jobs JOAW330SYN0001-0030 in these lists of the
/// week2 reading (05 Oct 2026): S018 1, 2, 18, 19, 20; S017 12, 16, 17, 20; S014 10, 11, 29; S016 14, 15, 30;
/// S010 6-9 (pending store not AW330); S034 23, 24, 16; S033/S035 21, 22, 25 (S011 rows all received); S015 13, 3, 8;
/// S009 3, 4, 5, 26, 27, 28 (every S009 row carries an indent); S039 claims for 10, 12, 13; S040 for 14, 15, 16, with the
/// same document numbers as S025/S026. ON_BENCH, BOOKED, READY at AW330, an open SRN and an open purchase invoice are not
/// in the fixtures, so the test makes them by changing a few rows of the week2 readings in its own database.
/// Synthetic data only; each test builds its own database.
/// </summary>
public sealed class ServiceJobModelSqlTests
{
    private static readonly DateOnly Week2 = new(2026, 10, 5);

    private static readonly string[] UiViews =
    [
        "v_service_status_view_facts", "v_service_claims", "v_service_job", "v_service_job_timeline", "v_service_parts",
        "v_service_parts_transit", "v_service_stock_summary", "v_service_pending_current",
    ];

    private static string Job(int n) => $"JOAW330SYN{n:0000}";

    [Fact]
    public async Task One_fixture_job_per_stage_with_the_rule_of_design_4_2()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await ImportAsync(database, FixtureFolder("week1"));
            await ImportAsync(database, FixtureFolder("week2"));
            await MakeBenchBookedReadyAndOpenCasesAsync(database);

            var expected = new Dictionary<int, string>
            {
                [1] = ServiceStages.Delivered, [18] = ServiceStages.Delivered, [20] = ServiceStages.Delivered,
                [12] = ServiceStages.Rwr, [16] = ServiceStages.Rwr,
                [10] = ServiceStages.DcIssued, [11] = ServiceStages.DcIssued, [29] = ServiceStages.DcIssued,
                [14] = ServiceStages.RaIssued, [30] = ServiceStages.RaIssued,
                [6] = ServiceStages.InTransitBack, [7] = ServiceStages.InTransitBack,
                [9] = ServiceStages.ReadyForDelivery, [23] = ServiceStages.ReadyForDelivery,
                [21] = ServiceStages.SrnOut, [25] = ServiceStages.SrnOut,
                [3] = ServiceStages.IndentRaised, [13] = ServiceStages.IndentRaised, [26] = ServiceStages.IndentRaised,
                [27] = ServiceStages.OnBench,
                [5] = ServiceStages.Booked,
            };
            foreach (var (job, stage) in expected)
                Assert.True(stage == await TextAsync(database, $"SELECT stage FROM dbo.v_service_job WHERE job_order_number=N'{Job(job)}'"),
                    $"{Job(job)} should be {stage}.");

            // One row per job; every job of every family is there.
            Assert.Equal(0, await IntAsync(database, "SELECT COUNT(*) FROM (SELECT job_order_number FROM dbo.v_service_job GROUP BY job_order_number HAVING COUNT(*)>1) d"));
            Assert.Equal(0, await IntAsync(database, """
                SELECT COUNT(*) FROM (SELECT DISTINCT job_order_number FROM dbo.v_service_job_readings) r
                WHERE NOT EXISTS(SELECT 1 FROM dbo.v_service_job j WHERE j.job_order_number=r.job_order_number)
                """));
            // Closed jobs: the 0048 status view and the job model agree.
            Assert.Equal(0, await IntAsync(database, """
                SELECT COUNT(*) FROM dbo.v_service_job j JOIN dbo.v_service_job_status_current c ON c.job_order_number=j.job_order_number
                WHERE c.status_label IN('DELIVERED','RWR','DC','RA')
                  AND j.stage<>CASE c.status_label WHEN 'DC' THEN 'DC_ISSUED' WHEN 'RA' THEN 'RA_ISSUED' ELSE c.status_label END
                """));
            Assert.Equal(0, await IntAsync(database, """
                SELECT COUNT(*) FROM dbo.v_service_job j JOIN dbo.v_service_job_status_current c ON c.job_order_number=j.job_order_number
                WHERE COALESCE(j.spare_value,-1)<>COALESCE(c.spare_value,-1) OR COALESCE(j.labour_charge,-1)<>COALESCE(c.labour_charge,-1)
                """));

            // Q3: a DC/RA job with a claim document is closed by the claim; without one it stays open.
            Assert.Equal((true, false), await ClaimAndOpenAsync(database, 10));
            Assert.Equal((false, true), await ClaimAndOpenAsync(database, 11));
            Assert.Equal((true, false), await ClaimAndOpenAsync(database, 14));
            Assert.Equal((false, true), await ClaimAndOpenAsync(database, 30));
            // Pending at: in transit at the export's store, ready at AW330, bench at the S009 store.
            Assert.Equal("AW330", await TextAsync(database, $"SELECT pending_at FROM dbo.v_service_job WHERE job_order_number=N'{Job(9)}'"));
            Assert.NotEqual("AW330", await TextAsync(database, $"SELECT pending_at FROM dbo.v_service_job WHERE job_order_number=N'{Job(6)}'"));

            // Ages: closed jobs have a TAT and no age; open jobs an age to the latest snapshot date and no TAT.
            Assert.Equal(0, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job WHERE stage IN('DELIVERED','RWR') AND (tat_days IS NULL OR age_days IS NOT NULL OR tat_days<0)"));
            Assert.Equal(0, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job WHERE stage NOT IN('DELIVERED','RWR') AND (tat_days IS NOT NULL OR age_days IS NULL OR age_days<>DATEDIFF(day,booking_date,as_at))"));
            Assert.Equal(0, await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_job WHERE as_at<>'{Week2:yyyy-MM-dd}'"));
            Assert.Equal(0, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job WHERE stage_date IS NULL OR booking_date IS NULL"));

            // The query class composes the board, the claims and the parts from the same rows.
            var query = new SqlServerServiceReportQuery(database.ConnectionString);
            var board = await query.LoadPendingBoardAsync();
            Assert.DoesNotContain(board.Rows, row => row.Stage is ServiceStages.Delivered or ServiceStages.Rwr);
            Assert.DoesNotContain(board.Rows, row => row.JobOrderNumber == Job(10));
            Assert.Contains(board.Rows, row => row.JobOrderNumber == Job(27) && row.Stage == ServiceStages.OnBench);
            Assert.Equal(board.Rows.OrderBy(row => ServiceStages.Rank(row.Stage)).Select(row => row.Stage), board.Rows.Select(row => row.Stage));
            var claims = await query.LoadClaimsAsync(null, null);
            Assert.Equal([Job(11), Job(29), Job(30)], claims.NotYetClaimed.Select(job => job.JobOrderNumber).Order(StringComparer.Ordinal));
            var detail = await query.LoadJobAsync(" " + Job(1) + " ");
            Assert.NotNull(detail);
            Assert.Equal(ServiceStages.Delivered, detail!.Header.Stage);
            Assert.Contains(detail.Timeline, row => row.ReportCode == "S009");
            Assert.Contains(detail.Timeline, row => row.ReportCode == "S018" && row.SnapshotDate == Week2);
            Assert.Null(await query.LoadJobAsync("JOAW330SYN9999"));
            Assert.Equal(await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job"), (await query.LoadJobListAsync()).Count);
            var today = await query.LoadTodayAsync();
            Assert.Equal(Week2, today.BusinessDate);
            Assert.Equal(9, (await query.LoadFreshnessAsync(Week2)).Count);
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task A_job_keeps_one_row_and_moves_stage_across_readings()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await ImportAsync(database, FixtureFolder("week1"));
            // Week1: job 1 waits in S009 with an indent; job 12 is in S015 (IR).
            Assert.Equal(ServiceStages.IndentRaised, await TextAsync(database, $"SELECT stage FROM dbo.v_service_job WHERE job_order_number=N'{Job(1)}'"));
            Assert.Equal(ServiceStages.IndentRaised, await TextAsync(database, $"SELECT stage FROM dbo.v_service_job WHERE job_order_number=N'{Job(12)}'"));
            Assert.Equal(1, await IntAsync(database, $"SELECT CONVERT(int,is_open) FROM dbo.v_service_job WHERE job_order_number=N'{Job(1)}'"));

            await ImportAsync(database, FixtureFolder("week2"));
            Assert.Equal(1, await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_job WHERE job_order_number=N'{Job(1)}'"));
            Assert.Equal(ServiceStages.Delivered, await TextAsync(database, $"SELECT stage FROM dbo.v_service_job WHERE job_order_number=N'{Job(1)}'"));
            Assert.Equal(ServiceStages.Rwr, await TextAsync(database, $"SELECT stage FROM dbo.v_service_job WHERE job_order_number=N'{Job(12)}'"));
            // The timeline keeps both readings of the job.
            Assert.Equal(2, await IntAsync(database, $"SELECT COUNT(DISTINCT snapshot_date) FROM dbo.v_service_job_timeline WHERE job_order_number=N'{Job(1)}'"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task The_job_model_does_not_depend_on_the_import_order()
    {
        var forward = new SqlDatabaseFixture();
        var backward = new SqlDatabaseFixture();
        try
        {
            await forward.InitializeAsync();
            await ImportAsync(forward, FixtureFolder("week1"));
            await ImportAsync(forward, FixtureFolder("week2"));
            await backward.InitializeAsync();
            await ImportAsync(backward, FixtureFolder("week2"));
            await ImportAsync(backward, FixtureFolder("week1"));
            const string columns = "job_order_number,booking_date,jo_type,stage,stage_date,pending_at,claim_raised,tat_days,age_days,days_in_stage,is_open,as_at";
            const string claims = "claim_type,report_code,business_date,document_number,job_order_number,item_id,net_amount_inc_tax,snapshot_date";
            foreach (var (view, cols) in new[] { ("v_service_job", columns), ("v_service_claims", claims) })
            {
                var sql = $"SELECT (SELECT {cols} FROM dbo.{view} ORDER BY {cols} FOR JSON PATH, INCLUDE_NULL_VALUES)";
                Assert.Equal(await forward.ExecuteAsync(sql, 300), await backward.ExecuteAsync(sql, 300));
            }
        }
        finally
        {
            await forward.DisposeAsync();
            await backward.DisposeAsync();
        }
    }

    [Fact]
    public async Task Claims_union_new_header_over_old_header_by_document_and_SRN_and_parts_rules_hold()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await ImportAsync(database, FixtureFolder("week1"));
            await ImportAsync(database, FixtureFolder("week2"));

            // S025 and S039 (and S026/S040) carry the same document numbers: each WDC/WRA document is read once, from
            // the new-header family; a plain UNION ALL would count it twice.
            foreach (var (type, oldCode, newCode) in new[] { ("WDC", "S025", "S039"), ("WRA", "S026", "S040") })
            {
                Assert.Equal(0, await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_claims WHERE claim_type='{type}' AND report_code='{oldCode}'"));
                Assert.Equal(3, await IntAsync(database, $"SELECT COUNT(DISTINCT document_number) FROM dbo.v_service_claims WHERE claim_type='{type}' AND report_code='{newCode}'"));
            }
            Assert.Equal(0, await IntAsync(database, """
                SELECT COUNT(*) FROM (SELECT claim_type,document_number FROM dbo.v_service_claims WHERE document_number IS NOT NULL
                GROUP BY claim_type,document_number HAVING COUNT(DISTINCT report_code)>1) d
                """));
            Assert.Equal(3, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_claims WHERE claim_type='MB'"));
            Assert.Equal(["GPRC", "MB", "WDC", "WRA"], await TextsAsync(database, "SELECT DISTINCT claim_type FROM dbo.v_service_claims ORDER BY claim_type"));

            // Q5: every fixture SRN has a received date, so none is pending; reopen one and only it is listed.
            Assert.Equal(0, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_pending_current WHERE [list]='SRN_STATUS'"));
            await database.ExecuteAsync($"""
                UPDATE s SET srn_received_date=NULL,repaired_date=NULL,to_status=N'SRN_For_Repair_DC_Created'
                FROM dbo.etp_landing_s011 s JOIN dbo.v_service_readings r ON r.import_file_id=s.import_file_id AND r.report_code='S011' AND r.is_latest=1
                WHERE LTRIM(RTRIM(CONVERT(nvarchar(100),s.joborder_number)))=N'{Job(21)}'
                """);
            Assert.Equal([Job(21)], await TextsAsync(database, "SELECT job_order_number FROM dbo.v_service_pending_current WHERE [list]='SRN_STATUS'"));
            // The amended pending view keeps the 0048 columns first and adds the SD-12 ones.
            Assert.Equal(0, await IntAsync(database, """
                SELECT COUNT(*) FROM dbo.v_service_pending_current WHERE [list]='PENDING_REPAIR' AND (edd IS NULL OR jo_status IS NULL)
                """));

            // Parts: an S007 invoice with no GRN and no S008 line is open, aged to the latest snapshot date.
            await database.ExecuteAsync("""
                UPDATE s SET grn_date=NULL,received_date=NULL,grn_no=NULL,status=N'Open',invoice_number=N'BIAW330SYNOPEN'
                FROM dbo.etp_landing_s007 s
                WHERE s.import_file_id=(SELECT MAX(r.import_file_id) FROM dbo.v_service_readings r WHERE r.report_code='S007' AND r.is_latest=1)
                  AND s.etp_row_id=(SELECT MIN(x.etp_row_id) FROM dbo.etp_landing_s007 x WHERE x.import_file_id=s.import_file_id)
                """);
            Assert.Equal("Open", await TextAsync(database, "SELECT DISTINCT status FROM dbo.v_service_parts WHERE invoice_number=N'BIAW330SYNOPEN'"));
            Assert.Equal(0, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_parts WHERE invoice_number=N'BIAW330SYNOPEN' AND days_open<>DATEDIFF(day,invoice_date,as_at)"));
            Assert.Equal(0, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_parts WHERE status='Received' AND invoice_number=N'BIAW330SYNOPEN'"));
            var parts = await new SqlServerServiceReportQuery(database.ConnectionString).LoadPartsAsync();
            Assert.Equal("BIAW330SYNOPEN", parts.Invoices[0].InvoiceNumber);
            Assert.Equal(1, parts.OpenInvoices);
            Assert.NotNull(parts.Stock);
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task No_0050_view_has_a_contact_column_and_a_viewer_can_read_every_one_but_not_write()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var restricted = await IntAsync(database, $"""
                SELECT COUNT(*) FROM sys.columns c JOIN sys.views v ON v.object_id=c.object_id
                WHERE v.name IN({string.Join(',', UiViews.Select(view => $"'{view}'"))})
                  AND (c.name LIKE '%mobile%' OR (c.name LIKE '%phone%' AND c.name NOT LIKE '%phonepe%') OR c.name LIKE '%landline%'
                    OR c.name LIKE '%mail%' OR c.name LIKE '%address%' OR c.name LIKE '%contact%')
                """);
            Assert.Equal(0, restricted);
            Assert.Equal(UiViews.Length, await IntAsync(database, $"SELECT COUNT(*) FROM sys.views WHERE name IN({string.Join(',', UiViews.Select(view => $"'{view}'"))})"));
            await database.ExecuteAsync("CREATE USER service_ui_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER service_ui_viewer;");
            foreach (var view in UiViews)
            {
                _ = await database.ExecuteAsync($"EXECUTE AS USER='service_ui_viewer'; SELECT COUNT(*) FROM dbo.{view}; REVERT;");
                await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                    $"EXECUTE AS USER='service_ui_viewer'; BEGIN TRY DELETE FROM dbo.{view} WHERE 1=0; END TRY BEGIN CATCH REVERT; THROW; END CATCH; REVERT;"));
            }
        }
        finally { await database.DisposeAsync(); }
    }

    // ON_BENCH, BOOKED and READY at AW330 are not in the fixtures: job 27 loses its indent (bench), job 5's S009 rows
    // move to job 28 (job 5 is then only booked in S002), job 9 is pending delivery at AW330. Week2 readings only.
    private static async Task MakeBenchBookedReadyAndOpenCasesAsync(SqlDatabaseFixture database)
    {
        const string LatestS009 = "(SELECT r.import_file_id FROM dbo.v_service_readings r WHERE r.report_code='S009' AND r.is_latest=1)";
        const string LatestS010 = "(SELECT r.import_file_id FROM dbo.v_service_readings r WHERE r.report_code='S010' AND r.is_latest=1)";
        await database.ExecuteAsync($"""
            UPDATE dbo.etp_landing_s009 SET indentid=NULL,indentdate=NULL,jostatus=N'Pending_Repair'
            WHERE import_file_id={LatestS009} AND LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber)))=N'{Job(27)}';
            UPDATE dbo.etp_landing_s009 SET jonumber=N'{Job(28)}'
            WHERE import_file_id={LatestS009} AND LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber)))=N'{Job(5)}';
            UPDATE dbo.etp_landing_s010 SET pendingstore=N'AW330',jostatus=N'Pending_Delivery'
            WHERE import_file_id={LatestS010} AND LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber)))=N'{Job(9)}';
            """);
    }

    private static async Task<(bool Claimed, bool Open)> ClaimAndOpenAsync(SqlDatabaseFixture database, int job)
    {
        var claimed = await IntAsync(database, $"SELECT CONVERT(int,claim_raised) FROM dbo.v_service_job WHERE job_order_number=N'{Job(job)}'");
        var open = await IntAsync(database, $"SELECT CONVERT(int,is_open) FROM dbo.v_service_job WHERE job_order_number=N'{Job(job)}'");
        return (claimed == 1, open == 1);
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

    private static async Task<List<string>> TextsAsync(SqlDatabaseFixture database, string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }
}
