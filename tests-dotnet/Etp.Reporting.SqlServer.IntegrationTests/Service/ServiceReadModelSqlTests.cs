using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// Service interim (S-2, decision 15), lane L4: the 0048 C_SERVICE_READ views over the synthetic Service fixtures
/// (tests-dotnet/fixtures/service-interim, lane L0), imported through the real folder import path. The views choose
/// rows by snapshot date (design section 4): week2 (05 Oct) wins over week1 (28 Sep) whatever the import order, a job
/// leaving a list is an event and never a review row, and a restated S004 day is listed as a money change.
/// Each test builds its own database; expectations are derived from the landing tables, not from fixture job numbers.
/// </summary>
public sealed class ServiceReadModelSqlTests
{
    private static readonly DateOnly Week1 = new(2026, 9, 28);
    private static readonly DateOnly Week2 = new(2026, 10, 5);

    // The rows each view shows, without import_file_id or import time, so two databases can be compared.
    private static readonly (string View, string Columns)[] Comparable =
    [
        ("v_service_readings", "report_code,snapshot_date,window_from,window_to,row_count,source_kind,is_latest"),
        ("v_service_job_readings", "job_order_number,report_code,snapshot_date,first_status_date,status_date,line_count"),
        ("v_service_job_status_current", "job_order_number,status_view,status_label,status_date,job_date,edd,brand,model,product_category,customer_name,spare_value,labour_charge,lines,snapshot_date,other_lists"),
        ("v_service_pending_current", "[list],report_code,job_order_number,job_date,age_days,brand,model,customer_name,pending_store,snapshot_date"),
        ("v_service_job_list_events", "job_order_number,report_code,list_label,event_kind,snapshot_date,previous_snapshot_date,status_date"),
        ("v_service_s004_daily", "business_date,tender,amount,row_count,snapshot_date"),
        ("v_service_money_changes", "report_code,business_date,previous_snapshot_date,previous_amount,current_snapshot_date,current_amount"),
    ];

    // Tables a review, pending change or approval would land in; a job leaving a list must write none of them.
    private static readonly string[] ReviewTables =
        ["approval_requests", "import_restatement_approvals", "controlled_adjustments", "data_quality_issues", "import_conflicts"];

    private static readonly string[] AllViews =
    [
        "v_service_families", "v_service_reading_windows", "v_service_readings", "v_service_datelog_readings",
        "v_service_status_view_rows", "v_service_job_readings", "v_service_job_status_current", "v_service_pending_current",
        "v_service_job_list_events", "v_service_s004_daily", "v_service_money_changes",
    ];

    [Fact]
    public async Task Week2_wins_jobs_leaving_lists_are_events_and_the_restated_S004_day_is_listed()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var week1 = await ImportAsync(database, FixtureFolder("week1"));
            Assert.True(week1.Files.Count(file => file.Status == "Imported") == 35,
                $"week1 should import 35 Service files: {Outcomes(week1)}");
            var reviewsBefore = await ReviewRowsAsync(database);
            var week2 = await ImportAsync(database, FixtureFolder("week2"));
            Assert.True(week2.Files.Count(file => file.Status == "Imported") == 35,
                $"week2 should import 35 Service files beside week1: {Outcomes(week2)}");

            // The refresh log: 70 readings, and the latest of every family is week2.
            Assert.Equal(70, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_readings"));
            Assert.Equal(35, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_readings WHERE is_latest=1"));
            Assert.Equal(0, await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_readings WHERE is_latest=1 AND snapshot_date<>'{Week2:yyyy-MM-dd}'"));
            Assert.Equal(0, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_readings WHERE source_kind<>'CONSOLIDATED'"));

            // Two jobs leave S009 Pending repair between the readings: each gets one LeftList dated week2, keeping week1.
            var leftS009 = await JobsAsync(database, $"""
                SELECT a.job FROM {ReadingJobs("s009", "jonumber", Week1)} a
                WHERE NOT EXISTS(SELECT 1 FROM {ReadingJobs("s009", "jonumber", Week2)} b WHERE b.job=a.job)
                """);
            Assert.Equal(2, leftS009.Count);
            var leftEvents = await JobsAsync(database, $"""
                SELECT job_order_number FROM dbo.v_service_job_list_events
                WHERE report_code='S009' AND event_kind='LeftList' AND snapshot_date='{Week2:yyyy-MM-dd}' AND previous_snapshot_date='{Week1:yyyy-MM-dd}'
                """);
            Assert.Equal(leftS009.Order(StringComparer.Ordinal), leftEvents.Order(StringComparer.Ordinal));
            Assert.Equal(2, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job_list_events WHERE report_code='S009' AND event_kind='LeftList'"));
            // Three new jobs enter S009: FirstSeen at week2.
            Assert.Equal(3, await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_job_list_events WHERE report_code='S009' AND event_kind='FirstSeen' AND snapshot_date='{Week2:yyyy-MM-dd}'"));
            // The jobs that left S009 were delivered (S018 in week2), which is their current status.
            foreach (var job in leftS009)
                Assert.Equal("DELIVERED", await database.ExecuteAsync($"SELECT status_label FROM dbo.v_service_job_status_current WHERE job_order_number=N'{job}'"));

            // The job that moved from S015 (IR) to S017 (RWR) is RWR now.
            var moved = await JobsAsync(database, $"""
                SELECT a.job FROM {ReadingJobs("s015", "jobordernumber", Week1)} a
                WHERE NOT EXISTS(SELECT 1 FROM {ReadingJobs("s015", "jobordernumber", Week2)} b WHERE b.job=a.job)
                  AND EXISTS(SELECT 1 FROM {ReadingJobs("s017", "jobordernumber", Week2)} c WHERE c.job=a.job)
                """);
            var rwr = Assert.Single(moved);
            Assert.Equal("RWR", await database.ExecuteAsync($"SELECT status_label FROM dbo.v_service_job_status_current WHERE job_order_number=N'{rwr}'"));
            Assert.Equal("S017", await database.ExecuteAsync($"SELECT status_view FROM dbo.v_service_job_status_current WHERE job_order_number=N'{rwr}'"));
            // Job-list families: the job no longer in S015 is LeftList there at week2 (keeping week1), never StillListed;
            // StillListed is only for a job the family's latest reading (week2) still holds after an earlier one did.
            Assert.Equal(1, await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_job_list_events WHERE report_code='S015' AND job_order_number=N'{rwr}' AND event_kind='LeftList' AND snapshot_date='{Week2:yyyy-MM-dd}' AND previous_snapshot_date='{Week1:yyyy-MM-dd}'"));
            Assert.Equal(0, await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_job_list_events WHERE report_code='S015' AND job_order_number=N'{rwr}' AND event_kind='StillListed'"));
            Assert.Equal(0, await IntAsync(database, $"""
                SELECT COUNT(*) FROM dbo.v_service_job_list_events e JOIN dbo.v_service_families f ON f.report_code=e.report_code AND f.read_rule='JobList'
                WHERE e.event_kind='StillListed' AND (e.snapshot_date<>'{Week2:yyyy-MM-dd}' OR e.previous_snapshot_date IS NULL OR e.previous_snapshot_date>=e.snapshot_date)
                """));

            // One row per job that any status view of any live reading holds; money summed over the winning reading's lines.
            var statusJobs = string.Join(" UNION ", new[] { "s014", "s015", "s016", "s017", "s018", "s031", "s032", "s033", "s034", "s035" }
                .Select(table => $"SELECT {Job("jobordernumber")} job FROM dbo.etp_landing_{table} t JOIN {Live} l ON l.import_file_id=t.import_file_id"));
            Assert.Equal(await IntAsync(database, $"SELECT COUNT(*) FROM ({statusJobs}) j WHERE job IS NOT NULL"),
                await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job_status_current"));
            Assert.Equal(0, await IntAsync(database, "SELECT COUNT(*) FROM (SELECT job_order_number FROM dbo.v_service_job_status_current GROUP BY job_order_number HAVING COUNT(*)>1) d"));
            Assert.True(await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job_status_current WHERE lines>1") >= 1,
                "A job with several line rows (S018) should count them.");
            Assert.True(await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job_status_current WHERE other_lists>=1") >= 2,
                "At least two jobs appear in two status views.");

            // Pending repair is the week2 state snapshot; age = snapshot date - job date.
            Assert.Equal(await IntAsync(database, $"SELECT COUNT(*) FROM {ReadingJobs("s009", "jonumber", Week2)} j"),
                await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_pending_current WHERE [list]='PENDING_REPAIR'"));
            Assert.Equal(0, await IntAsync(database, $"""
                SELECT COUNT(*) FROM dbo.v_service_pending_current WHERE [list] IN('PENDING_REPAIR','PENDING_DELIVERY')
                  AND (snapshot_date<>'{Week2:yyyy-MM-dd}' OR (job_date IS NOT NULL AND age_days<>DATEDIFF(day,job_date,snapshot_date)))
                """));
            Assert.Equal(0, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_pending_current WHERE [list] NOT IN('PENDING_REPAIR','PENDING_DELIVERY','SRN_STATUS')"));

            // The restated S004 billing date: its week1 and week2 totals differ, and only such dates are listed.
            var restated = await JobsAsync(database, $"""
                SELECT CONVERT(char(10),a.billingdate,23) FROM
                  (SELECT t.billingdate,SUM(t.totalamount) total FROM dbo.etp_landing_s004 t JOIN {Live} l ON l.import_file_id=t.import_file_id AND l.snapshot_date='{Week1:yyyy-MM-dd}' GROUP BY t.billingdate) a
                JOIN (SELECT t.billingdate,SUM(t.totalamount) total FROM dbo.etp_landing_s004 t JOIN {Live} l ON l.import_file_id=t.import_file_id AND l.snapshot_date='{Week2:yyyy-MM-dd}' GROUP BY t.billingdate) b
                  ON b.billingdate=a.billingdate
                WHERE COALESCE(a.total,0)<>COALESCE(b.total,0)
                """);
            Assert.NotEmpty(restated);
            var listed = await JobsAsync(database, $"""
                SELECT CONVERT(char(10),business_date,23) FROM dbo.v_service_money_changes
                WHERE report_code='S004' AND previous_snapshot_date='{Week1:yyyy-MM-dd}' AND current_snapshot_date='{Week2:yyyy-MM-dd}'
                """);
            Assert.Equal(restated.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
            // S004 per day and tender comes from week2 for every date week2 covers.
            Assert.Equal(0, await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_s004_daily WHERE snapshot_date<>'{Week2:yyyy-MM-dd}'"));

            // A job leaving a list wrote no review, pending-change or approval row, and reading the views writes nothing.
            Assert.Equal(reviewsBefore, await ReviewRowsAsync(database));
            foreach (var view in AllViews) _ = await IntAsync(database, $"SELECT COUNT(*) FROM dbo.{view}");
            Assert.Equal(reviewsBefore, await ReviewRowsAsync(database));

            // Importing week1 again after week2 changes no view.
            var before = await SnapshotAsync(database);
            var again = await ImportAsync(database, FixtureFolder("week1"));
            Assert.Equal(0, again.Files.Count(file => file.Status == "Imported"));
            Assert.Equal(before, await SnapshotAsync(database));

            // The query class reads the same views as the Owner.
            var query = new SqlServerServiceReportQuery(database.ConnectionString);
            Assert.Equal(70, (await query.LoadRefreshesAsync()).Count);
            Assert.Equal(Week2, (await query.LoadRefreshesAsync())[0].SnapshotDate);
            Assert.Equal(await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job_status_current"), (await query.LoadJobsByStatusAsync(null)).Count);
            Assert.Equal(rwr, Assert.Single(await query.LoadJobsByStatusAsync("S017"), row => row.JobOrderNumber == rwr).JobOrderNumber);
            var pending = await query.LoadPendingAsync(ServicePendingLists.PendingRepair);
            Assert.All(pending, row => Assert.Equal(Week2, row.SnapshotDate));
            Assert.Equal(pending.OrderByDescending(row => row.AgeDays ?? int.MinValue).Select(row => row.AgeDays), pending.Select(row => row.AgeDays));
            var history = await query.LoadJobHistoryAsync("  " + leftS009[0] + " ");
            Assert.Contains(history, step => step.ReportCode == "S009" && step.EventKind == ServiceJobEventKind.LeftList
                && step.SnapshotDate == Week2 && step.PreviousSnapshotDate == Week1);
            Assert.Contains(await query.LoadMoneyChangesAsync(), change => change.ReportCode == "S004" && change.CurrentSnapshotDate == Week2);
            var money = await query.LoadMoneyCheckAsync(new DateOnly(2024, 1, 1), Week2);
            Assert.NotEmpty(money);
            Assert.All(money, day => Assert.Contains(day.Tender, SqlServerServiceReportQuery.TenderOrder));

            // A viewer can read every view.
            await database.ExecuteAsync("CREATE USER service_views_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER service_views_viewer;");
            foreach (var view in AllViews)
                _ = await database.ExecuteAsync($"EXECUTE AS USER='service_views_viewer'; SELECT COUNT(*) FROM dbo.{view}; REVERT;");
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task The_views_do_not_depend_on_the_import_order()
    {
        var forward = new SqlDatabaseFixture();
        var backward = new SqlDatabaseFixture();
        try
        {
            await forward.InitializeAsync();
            await ImportAsync(forward, FixtureFolder("week1"));
            await ImportAsync(forward, FixtureFolder("week2"));
            var expected = await SnapshotAsync(forward);

            // The older reading imported after the newer one changes nothing the views show.
            await backward.InitializeAsync();
            await ImportAsync(backward, FixtureFolder("week2"));
            await ImportAsync(backward, FixtureFolder("week1"));
            Assert.Equal(expected, await SnapshotAsync(backward));
        }
        finally
        {
            await forward.DisposeAsync();
            await backward.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_raw_window_after_week2_replaces_only_the_dates_and_jobs_it_holds()
    {
        var raw = FixtureFolder("raw");
        Assert.True(Directory.Exists(raw), "The raw Service fixtures (lane L9) are needed for this test.");
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await ImportAsync(database, FixtureFolder("week1"));
            await ImportAsync(database, FixtureFolder("week2"));
            var rawImport = await ImportAsync(database, raw);
            Assert.True(rawImport.Files.Count(file => file.Status == "Imported") >= 1, $"The raw window should import: {Outcomes(rawImport)}");
            Assert.True(await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_readings WHERE source_kind='RAW'") >= 1);

            // DateLog (S004): a date inside the raw window comes from the raw reading, an older date still from week2.
            var window = await JobsAsync(database, """
                SELECT CONVERT(char(10),window_from,23)+'|'+CONVERT(char(10),snapshot_date,23) FROM dbo.v_service_readings
                WHERE report_code='S004' AND source_kind='RAW' AND is_latest=1
                """);
            var bounds = Assert.Single(window).Split('|');
            Assert.Equal(0, await IntAsync(database, $"""
                SELECT COUNT(*) FROM dbo.v_service_s004_daily
                WHERE (business_date>='{bounds[0]}' AND snapshot_date<>'{bounds[1]}')
                   OR (business_date<'{bounds[0]}' AND snapshot_date<>'{Week2:yyyy-MM-dd}')
                """));
            // StateSnapshot (S009): the raw pending list is the latest state.
            Assert.Equal(0, await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_pending_current WHERE [list]='PENDING_REPAIR' AND snapshot_date<>'{bounds[1]}'"));
            // JobList (S018): a job the raw window does not hold keeps its week2 reading.
            Assert.True(await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_job_readings WHERE report_code='S018' AND snapshot_date='{Week2:yyyy-MM-dd}'") >= 1);

            // A second raw import changes nothing.
            var before = await SnapshotAsync(database);
            await ImportAsync(database, raw);
            Assert.Equal(before, await SnapshotAsync(database));
        }
        finally { await database.DisposeAsync(); }
    }

    // Live Service readings (the views' own rule), as a derived table for the expectations above.
    private const string Live = """
        (SELECT f.import_file_id,COALESCE(f.period_end,f.business_date) snapshot_date FROM dbo.import_files f
         JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
         WHERE f.is_superseded=0 AND f.data_truth_version=1 AND b.status='Completed' AND f.report_code LIKE 'S[0-9][0-9][0-9]')
        """;

    private static string Job(string column) => $"NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),t.{column}))),N'')";

    // The distinct jobs of one family's live reading of one snapshot date.
    private static string ReadingJobs(string table, string column, DateOnly snapshot) =>
        $"(SELECT DISTINCT {Job(column)} job FROM dbo.etp_landing_{table} t JOIN {Live} l ON l.import_file_id=t.import_file_id " +
        $"AND l.snapshot_date='{snapshot:yyyy-MM-dd}' WHERE {Job(column)} IS NOT NULL)";

    private static async Task<FolderImportSummary> ImportAsync(SqlDatabaseFixture database, string folder) =>
        await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString)).RunAsync(folder, new("Synthetic Owner"));

    private static string Outcomes(FolderImportSummary summary) =>
        string.Join(", ", summary.Files.GroupBy(file => file.Status).OrderBy(group => group.Key).Select(group => $"{group.Key} {group.Count()}"));

    private static string FixtureFolder(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "tests-dotnet", "fixtures", "service-interim", name);
    }

    private static async Task<int> IntAsync(SqlDatabaseFixture database, string sql) => Convert.ToInt32(await database.ExecuteAsync(sql));

    private static async Task<List<string>> JobsAsync(SqlDatabaseFixture database, string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }

    private static async Task<string> ReviewRowsAsync(SqlDatabaseFixture database)
    {
        var counts = new List<string>();
        foreach (var table in ReviewTables)
            counts.Add($"{table}={await database.ExecuteAsync($"SELECT COUNT_BIG(*) FROM dbo.{table}")}");
        return string.Join(";", counts);
    }

    // Every comparable view as ordered JSON; equal snapshots mean the views show the same rows.
    private static async Task<string> SnapshotAsync(SqlDatabaseFixture database)
    {
        var parts = new List<string>();
        foreach (var (view, columns) in Comparable)
            parts.Add($"{view}:{await database.ExecuteAsync($"SELECT (SELECT {columns} FROM dbo.{view} ORDER BY {columns} FOR JSON PATH, INCLUDE_NULL_VALUES)", 300)}");
        return string.Join("\n", parts);
    }
}
