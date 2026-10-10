using Etp.Reporting.Application.Service;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Service Centre UI (1.10.0, decision 25): the 0050 read contract. Every SQL statement here reads a Service view of
/// migrations 0048/0050 (and the manual Service entries through v_service_manual_money); the rules that are not SQL
/// (overdue with the per-stage limits, age bands, TAT medians, the board and Today counts, claim and parts grouping,
/// freshness colours) are the pure classes of ServiceUiRules.cs, so each screen reads its rows once and the unit tests
/// pin the arithmetic. No statement names a phone, e-mail or address column.
/// </summary>
public sealed partial class SqlServerServiceReportQuery
{
    internal const string JobColumns =
        "job_order_number,booking_date,jo_type,exported_status,brand,model,product_category,customer_name,guarantee,customer_type,edd,stage,stage_date," +
        "pending_at,spare_required,indent_date,srn_date,srn_to_store,repair_date,delivery_date,rwr_date,rwr_reason,wdc_date,wdc_number,wra_date," +
        "radc_number,claim_raised,spare_value,labour_charge,revenue_labour_charge,revenue_spare_charge,revenue_net_incl_tax,revenue_documents," +
        "tat_days,tat_repair_days,age_days,days_in_stage,is_overdue,is_open,last_reading_date,as_at";

    internal const string AllJobsSql = "SELECT " + JobColumns + " FROM dbo.v_service_job ORDER BY job_order_number;";

    internal const string OpenJobsSql = "SELECT " + JobColumns + " FROM dbo.v_service_job WHERE is_open=1 ORDER BY job_order_number;";

    internal const string OneJobSql = "SELECT " + JobColumns + " FROM dbo.v_service_job WHERE job_order_number=@job;";

    internal const string StageJobsSql = "SELECT " + JobColumns + " FROM dbo.v_service_job WHERE stage IN(@stage1,@stage2) ORDER BY job_order_number;";

    internal const string TimelineSql = """
        SELECT job_order_number,snapshot_date,report_code,list_label,event_date,status_text,pending_store,document_number,amount,source_kind,import_file_id
        FROM dbo.v_service_job_timeline
        WHERE job_order_number=@job
        ORDER BY snapshot_date DESC,CASE WHEN event_date IS NULL THEN 1 ELSE 0 END,event_date DESC,report_code;
        """;

    internal const string ClaimsSql = """
        SELECT claim_type,business_date,document_number,job_order_number,item_id,quantity,net_amount_inc_tax,ucp_value,account_number,report_code,snapshot_date
        FROM dbo.v_service_claims
        WHERE (@from IS NULL OR business_date>=@from) AND (@to IS NULL OR business_date<=@to)
        ORDER BY business_date DESC,document_number,item_id;
        """;

    internal const string JobClaimsSql = """
        SELECT claim_type,business_date,document_number,job_order_number,item_id,quantity,net_amount_inc_tax,ucp_value,account_number,report_code,snapshot_date
        FROM dbo.v_service_claims
        WHERE job_order_number=@job
        ORDER BY business_date DESC,document_number,item_id;
        """;

    // The GPRC gap warning (design 3.5, Q11): the latest GPRC reading against the latest DC/RA status reading.
    internal const string ClaimReadingsSql = """
        SELECT MAX(CASE WHEN report_code IN('S023','S041') THEN snapshot_date END) latest_gprc,
          MAX(CASE WHEN report_code IN('S014','S016') THEN snapshot_date END) latest_dc_ra,
          MAX(snapshot_date) as_at
        FROM dbo.v_service_readings;
        """;

    internal const string PartsSql = """
        SELECT invoice_number,invoice_date,item_id,shipped_quantity,received_quantity,net_amount,grn_number,grn_date,received_date,status,from_location,days_open,snapshot_date
        FROM dbo.v_service_parts
        WHERE invoice_number IS NOT NULL;
        """;

    internal const string TransitSql = """
        SELECT stm_number,business_date,item_id,quantity_shipped,from_location,to_location,ucp,snapshot_date
        FROM dbo.v_service_parts_transit;
        """;

    internal const string S004LatestSql = """
        SELECT MAX(business_date)
        FROM dbo.v_service_s004_daily
        WHERE business_date <= @to;
        """;

    internal const string S004RangeSql = """
        SELECT business_date,tender,amount
        FROM dbo.v_service_s004_daily
        WHERE business_date BETWEEN @from AND @to;
        """;

    internal const string StockSql = "SELECT snapshot_date,items,quantity,value FROM dbo.v_service_stock_summary;";

    internal const string FreshnessSql = """
        SELECT report_code,snapshot_date,row_count,import_file_id,imported_utc,source_kind,is_latest
        FROM dbo.v_service_readings;
        """;

    /// <summary>Every SQL statement of the 1.10 contract, for the text tests (views only, no privacy column).</summary>
    internal static IReadOnlyList<string> UiStatements { get; } =
        [AllJobsSql, OpenJobsSql, OneJobSql, StageJobsSql, TimelineSql, ClaimsSql, JobClaimsSql, ClaimReadingsSql, PartsSql, TransitSql, S004RangeSql, StockSql, FreshnessSql];

    public async Task<ServiceToday> LoadTodayAsync(DateOnly? businessDate = null, CancellationToken cancellationToken = default)
    {
        var jobs = await ReadJobsAsync(AllJobsSql, _ => { }, cancellationToken);
        var asAt = jobs.Count > 0 ? jobs[0].AsAt : (DateOnly?)null;
        DateOnly? latestS004 = null;
        if (businessDate is null)
        {
            var latest = await ReadAsync(S004LatestSql, command =>
                command.Parameters.Add("@to", System.Data.SqlDbType.Date).Value = (asAt ?? DateOnly.MaxValue).ToDateTime(TimeOnly.MinValue),
                reader => reader.IsDBNull(0) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(0), cancellationToken);
            latestS004 = latest.FirstOrDefault();
        }
        // Q15 / R-SQL-01: no date chosen = the latest day the exports hold data for, not the raw pack's export day.
        var date = businessDate ?? ServiceBoard.LatestDataDate(jobs, latestS004, asAt) ?? DateOnly.FromDateTime(DateTime.Today);
        var month = new DateOnly(date.Year, date.Month, 1);
        void Range(SqlCommand command)
        {
            command.Parameters.Add("@from", System.Data.SqlDbType.Date).Value = month.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@to", System.Data.SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        }
        var s004 = await ReadAsync(S004RangeSql, Range, reader =>
            new ServiceS004TenderAmount(reader.GetFieldValue<DateOnly>(0), reader.GetString(1), Money(reader, 2)), cancellationToken);
        var manual = await ReadAsync(ManualMoneySql, Range, ManualEntry, cancellationToken);
        var claims = await ReadAsync(ClaimsSql, Range, ClaimLine, cancellationToken);
        return ServiceBoard.Today(date, asAt, jobs, s004, manual, claims);
    }

    public async Task<ServicePendingBoard> LoadPendingBoardAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await ReadJobsAsync(OpenJobsSql, _ => { }, cancellationToken);
        return ServiceBoard.Build(jobs, jobs.Count > 0 ? jobs[0].AsAt : null);
    }

    public async Task<ServiceJobDetail?> LoadJobAsync(string jobOrderNumber, CancellationToken cancellationToken = default)
    {
        var job = jobOrderNumber?.Trim() ?? "";
        if (job.Length == 0) return null;
        if (job.Length > 100) throw new ArgumentException("A job number has at most 100 characters.", nameof(jobOrderNumber));
        void Bind(SqlCommand command) => command.Parameters.Add("@job", System.Data.SqlDbType.NVarChar, 100).Value = job;
        var header = (await ReadJobsAsync(OneJobSql, Bind, cancellationToken)).SingleOrDefault();
        if (header is null) return null;
        var timeline = await ReadAsync(TimelineSql, Bind, reader => new ServiceJobTimelineRow(reader.GetString(0), reader.GetFieldValue<DateOnly>(1),
            reader.GetString(2), reader.GetString(3), Date(reader, 4), Text(reader, 5), Text(reader, 6), Text(reader, 7), Money(reader, 8),
            reader.GetString(9), reader.GetInt64(10)), cancellationToken);
        return new ServiceJobDetail(ServiceJobProjection.Header(header), timeline);
    }

    /// <summary>The claim lines of one job (for the Job history screen beside <see cref="LoadJobAsync"/>).</summary>
    public async Task<IReadOnlyList<ServiceClaimLine>> LoadJobClaimsAsync(string jobOrderNumber, CancellationToken cancellationToken = default)
    {
        var job = jobOrderNumber?.Trim() ?? "";
        if (job.Length == 0) return [];
        return await ReadAsync(JobClaimsSql, command => command.Parameters.Add("@job", System.Data.SqlDbType.NVarChar, 100).Value = job, ClaimLine, cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceJobHeader>> LoadJobListAsync(CancellationToken cancellationToken = default) =>
        (await LoadJobSummariesAsync(cancellationToken)).Select(ServiceJobProjection.Header).ToArray();

    /// <summary>Every v_service_job row with the full column set (Today, Jobs list, Claims and Parts compose from it).</summary>
    public Task<IReadOnlyList<ServiceJobSummary>> LoadJobSummariesAsync(CancellationToken cancellationToken = default) =>
        ReadJobsAsync(AllJobsSql, _ => { }, cancellationToken);

    public async Task<ServiceClaims> LoadClaimsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        if (from is { } f && to is { } t && f > t) throw new ArgumentException("Start date must not follow end date.", nameof(from));
        var lines = await ReadAsync(ClaimsSql, command =>
        {
            command.Parameters.Add("@from", System.Data.SqlDbType.Date).Value = from is { } a ? a.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
            command.Parameters.Add("@to", System.Data.SqlDbType.Date).Value = to is { } b ? b.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        }, ClaimLine, cancellationToken);
        var jobs = await ReadJobsAsync(StageJobsSql, command =>
        {
            command.Parameters.Add("@stage1", System.Data.SqlDbType.VarChar, 30).Value = ServiceStages.DcIssued;
            command.Parameters.Add("@stage2", System.Data.SqlDbType.VarChar, 30).Value = ServiceStages.RaIssued;
        }, cancellationToken);
        var readings = (await ReadAsync(ClaimReadingsSql, _ => { }, reader => (Gprc: Date(reader, 0), DcRa: Date(reader, 1), AsAt: Date(reader, 2)), cancellationToken)).Single();
        return new ServiceClaims(ServiceClaimRules.Summarise(lines), lines, ServiceClaimRules.NotYetClaimed(jobs),
            ServiceClaimRules.GprcGap(readings.Gprc, readings.DcRa), readings.Gprc, readings.DcRa, readings.AsAt);
    }

    public async Task<ServiceParts> LoadPartsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await ReadAsync(PartsSql, _ => { }, reader => new ServicePartsRow(reader.GetString(0), Date(reader, 1), Text(reader, 2), Money(reader, 3),
            Money(reader, 4), Money(reader, 5), Text(reader, 6), Date(reader, 7), Date(reader, 8), reader.GetString(9), Text(reader, 10),
            reader.IsDBNull(11) ? null : reader.GetInt32(11), reader.GetFieldValue<DateOnly>(12)), cancellationToken);
        var git = await ReadAsync(TransitSql, _ => { }, reader => new ServiceGitLine(Text(reader, 0), reader.GetFieldValue<DateOnly>(1), Text(reader, 2),
            Money(reader, 3), Text(reader, 4), Text(reader, 5), Money(reader, 6), reader.GetFieldValue<DateOnly>(7)), cancellationToken);
        var stock = (await ReadAsync(StockSql, _ => { }, reader => new ServiceStockSummary(reader.GetFieldValue<DateOnly>(0), reader.GetInt32(1),
            Money(reader, 2), Money(reader, 3)), cancellationToken)).SingleOrDefault();
        var jobs = await ReadJobsAsync(OpenJobsSql, _ => { }, cancellationToken);
        var readings = (await ReadAsync(ClaimReadingsSql, _ => { }, reader => Date(reader, 2), cancellationToken)).Single();
        return ServicePartsRules.Build(rows, git, stock, jobs, readings);
    }

    public async Task<IReadOnlyList<ServiceFreshnessChip>> LoadFreshnessAsync(DateOnly? asOf = null, CancellationToken cancellationToken = default)
    {
        var rows = await ReadAsync(FreshnessSql, _ => { }, reader => (Refresh: new ServiceRefresh(reader.GetString(0), reader.GetFieldValue<DateOnly>(1),
            Convert.ToInt64(reader.GetValue(2)), reader.GetInt64(3), DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)),
            Kind: reader.GetString(5), Latest: reader.GetBoolean(6)), cancellationToken);
        var kinds = rows.Where(row => row.Latest).ToDictionary(row => row.Refresh.ReportCode, row => row.Kind, StringComparer.OrdinalIgnoreCase);
        return ServiceFreshness.Build(rows.Select(row => row.Refresh), asOf ?? DateOnly.FromDateTime(DateTime.Today), kinds);
    }

    private async Task<IReadOnlyList<ServiceJobSummary>> ReadJobsAsync(string sql, Action<SqlCommand> bind, CancellationToken cancellationToken) =>
        await ReadAsync(sql, bind, JobSummary, cancellationToken);

    internal static ServiceJobSummary JobSummary(SqlDataReader reader)
    {
        var joType = ServiceJobTypes.Normalise(Text(reader, 2));
        var stage = reader.GetString(11);
        var edd = Date(reader, 10);
        int? Int(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
        var daysInStage = Int(36);
        var asAt = reader.GetFieldValue<DateOnly>(40);
        return new ServiceJobSummary(reader.GetString(0), Date(reader, 1), joType, joType == ServiceJobTypes.QuickBilling, Text(reader, 3),
            Text(reader, 4), Text(reader, 5), Text(reader, 6), Text(reader, 7), Text(reader, 8), Text(reader, 9), edd, stage, Date(reader, 12),
            Text(reader, 13), Text(reader, 14), Date(reader, 15), Date(reader, 16), Text(reader, 17), Date(reader, 18), Date(reader, 19),
            Date(reader, 20), Text(reader, 21), Date(reader, 22), Text(reader, 23), Date(reader, 24), Text(reader, 25), reader.GetBoolean(26),
            Money(reader, 27), Money(reader, 28), Money(reader, 29), Money(reader, 30), Money(reader, 31), reader.GetInt32(32),
            Int(33), Int(34), Int(35), daysInStage, ServiceAgeing.OverdueBy(stage, edd, daysInStage, asAt, reader.GetBoolean(26)), reader.GetBoolean(38),
            Date(reader, 39), asAt);
    }

    private static ServiceClaimLine ClaimLine(SqlDataReader reader) => new(reader.GetString(0), reader.GetFieldValue<DateOnly>(1), Text(reader, 2),
        Text(reader, 3), Text(reader, 4), Money(reader, 5), Money(reader, 6), Money(reader, 7), Text(reader, 8), reader.GetString(9),
        reader.GetFieldValue<DateOnly>(10));
}
