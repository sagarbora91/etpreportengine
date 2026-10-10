using Etp.Reporting.Application.Service;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Service Centre interim (S-2, decision 15): the read contract over the 0048 C_SERVICE_READ views. Read only; every
/// call requires application access (CanView), as <see cref="SqlServerImportHistoryQuery"/> does. The views choose rows
/// by snapshot date (design section 4), so nothing here depends on the import order. No query selects a phone,
/// e-mail or address column: the views expose none.
/// </summary>
public sealed partial class SqlServerServiceReportQuery(string connectionString) : IServiceReportQuery
{
    internal const string RefreshesSql = """
        SELECT report_code,snapshot_date,row_count,import_file_id,imported_utc
        FROM dbo.v_service_readings
        ORDER BY snapshot_date DESC,report_code,import_file_id DESC;
        """;

    internal const string JobsSql = """
        SELECT job_order_number,status_view,status_label,job_date,edd,brand,model,product_category,customer_name,
          spare_value,labour_charge,lines,snapshot_date,other_lists
        FROM dbo.v_service_job_status_current
        WHERE @view IS NULL OR status_view=@view
        ORDER BY status_view,job_date DESC,job_order_number;
        """;

    internal const string PendingSql = """
        SELECT [list],job_order_number,job_date,age_days,brand,model,customer_name,pending_store,snapshot_date
        FROM dbo.v_service_pending_current
        WHERE [list]=@list
        ORDER BY CASE WHEN age_days IS NULL THEN 1 ELSE 0 END,age_days DESC,job_order_number;
        """;

    internal const string JobHistorySql = """
        SELECT job_order_number,report_code,list_label,event_kind,snapshot_date,previous_snapshot_date
        FROM dbo.v_service_job_list_events
        WHERE job_order_number=@job
        ORDER BY snapshot_date,report_code,
          CASE event_kind WHEN 'FirstSeen' THEN 0 WHEN 'Reappeared' THEN 1 WHEN 'StillListed' THEN 2 WHEN 'LeftList' THEN 3 ELSE 4 END;
        """;

    // Every manual SERVICE_* entry by date, shop and field (dbo.v_service_manual_money). The view marks the Service-money
    // shop (decision 16, Q1) from the store catalogue; ServiceMoneyCheck decides what is compared and what is listed apart.
    internal const string ManualMoneySql = """
        SELECT business_date,store_code,store_name,field_code,amount,is_service_money_shop
        FROM dbo.v_service_manual_money
        WHERE business_date BETWEEN @from AND @to;
        """;

    // Two result sets: S004 per date and tender (DateLog rule), then the manual Service entries (ManualMoneySql).
    internal const string MoneyCheckSql = """
        SELECT business_date,tender,amount
        FROM dbo.v_service_s004_daily
        WHERE business_date BETWEEN @from AND @to;
        """ + "\n" + ManualMoneySql;

    internal const string MoneyChangesSql = """
        SELECT business_date,report_code,previous_snapshot_date,previous_amount,current_snapshot_date,current_amount
        FROM dbo.v_service_money_changes
        ORDER BY business_date DESC,report_code;
        """;

    public async Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) =>
        await ReadAsync(RefreshesSql, _ => { }, reader => new ServiceRefresh(reader.GetString(0), reader.GetFieldValue<DateOnly>(1),
            Convert.ToInt64(reader.GetValue(2)), reader.GetInt64(3), DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)),
            cancellationToken);

    public async Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default)
    {
        var view = string.IsNullOrWhiteSpace(statusView) ? null : statusView.Trim().ToUpperInvariant();
        return await ReadAsync(JobsSql, command => command.Parameters.Add("@view", System.Data.SqlDbType.VarChar, 30).Value = (object?)view ?? DBNull.Value,
            reader => new ServiceJobRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), Date(reader, 3), Date(reader, 4),
                Text(reader, 5), Text(reader, 6), Text(reader, 7), Text(reader, 8), Money(reader, 9), Money(reader, 10),
                reader.GetInt32(11), reader.GetFieldValue<DateOnly>(12), reader.GetInt32(13)), cancellationToken);
    }

    public async Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default)
    {
        var key = list?.Trim().ToUpperInvariant() ?? "";
        if (!ServicePendingLists.All.Contains(key)) throw new ArgumentException("Choose Pending repair, Pending delivery or SRN status.", nameof(list));
        return await ReadAsync(PendingSql, command => command.Parameters.Add("@list", System.Data.SqlDbType.VarChar, 30).Value = key,
            reader => new ServicePendingRow(reader.GetString(0), reader.GetString(1), Date(reader, 2), reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Text(reader, 4), Text(reader, 5), Text(reader, 6), Text(reader, 7), reader.GetFieldValue<DateOnly>(8)), cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default)
    {
        var job = jobOrderNumber?.Trim() ?? "";
        if (job.Length == 0) return [];
        if (job.Length > 100) throw new ArgumentException("A job number has at most 100 characters.", nameof(jobOrderNumber));
        return await ReadAsync(JobHistorySql, command => command.Parameters.Add("@job", System.Data.SqlDbType.NVarChar, 100).Value = job,
            reader => new ServiceJobEvent(reader.GetString(0), reader.GetString(1), reader.GetString(2), EventKind(reader.GetString(3)),
                reader.GetFieldValue<DateOnly>(4), Date(reader, 5)), cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (from > to) throw new ArgumentException("Start date must not follow end date.", nameof(from));
        await RequireViewAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(MoneyCheckSql, connection);
        command.Parameters.Add("@from", System.Data.SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@to", System.Data.SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var s004 = new List<ServiceS004TenderAmount>();
        while (await reader.ReadAsync(cancellationToken))
            s004.Add(new ServiceS004TenderAmount(reader.GetFieldValue<DateOnly>(0), reader.GetString(1), Money(reader, 2)));
        await reader.NextResultAsync(cancellationToken);
        var manual = new List<ServiceManualMoneyEntry>();
        while (await reader.ReadAsync(cancellationToken)) manual.Add(ManualEntry(reader));
        return ServiceMoneyCheck.Compare(s004, manual);
    }

    /// <summary>Manual Service entries made at a shop other than the Service-money shop: listed apart, never added to the
    /// money check (decision 16, Q1).</summary>
    public async Task<IReadOnlyList<ServiceUnmatchedMoneyEntry>> LoadUnmatchedServiceEntriesAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (from > to) throw new ArgumentException("Start date must not follow end date.", nameof(from));
        var manual = await ReadAsync(ManualMoneySql, command =>
        {
            command.Parameters.Add("@from", System.Data.SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@to", System.Data.SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        }, ManualEntry, cancellationToken);
        return ServiceMoneyCheck.Unmatched(manual);
    }

    public async Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default) =>
        await ReadAsync(MoneyChangesSql, _ => { }, reader => new ServiceMoneyChange(reader.GetFieldValue<DateOnly>(0), reader.GetString(1),
            reader.GetFieldValue<DateOnly>(2), reader.GetDecimal(3), reader.GetFieldValue<DateOnly>(4), reader.GetDecimal(5)), cancellationToken);

    /// <summary>The view's event kinds are the contract's: a job-list family emits StillListed only when its latest reading
    /// holds the job, otherwise LeftList (v_service_job_list_events).</summary>
    internal static ServiceJobEventKind EventKind(string code) => code switch
    {
        "FirstSeen" => ServiceJobEventKind.FirstSeen,
        "StillListed" => ServiceJobEventKind.StillListed,
        "LeftList" => ServiceJobEventKind.LeftList,
        "Reappeared" => ServiceJobEventKind.Reappeared,
        _ => throw new InvalidOperationException("Unknown Service job event kind."),
    };

    private static ServiceManualMoneyEntry ManualEntry(SqlDataReader reader) =>
        new(reader.GetFieldValue<DateOnly>(0), reader.GetString(1), Text(reader, 2), reader.GetString(3), reader.GetDecimal(4), reader.GetBoolean(5));

    private async Task<IReadOnlyList<T>> ReadAsync<T>(string sql, Action<SqlCommand> bind, Func<SqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        await RequireViewAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        bind(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(map(reader));
        return rows;
    }

    private async Task RequireViewAsync(CancellationToken cancellationToken)
    {
        var access = await new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync(cancellationToken);
        if (!access.CanView) throw new UnauthorizedAccessException("Application access is required.");
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static DateOnly? Date(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);
    private static string? Text(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static decimal? Money(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}
