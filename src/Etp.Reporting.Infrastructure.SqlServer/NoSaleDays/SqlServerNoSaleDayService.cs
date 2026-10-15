using System.Data;
using Etp.Reporting.Application.NoSaleDays;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.NoSaleDays;

/// <summary>
/// 1.12 lane N1 (decision 52): Mark as no-sale day over SQL. Marking and withdrawing run dbo.mark_no_sale_day /
/// dbo.withdraw_no_sale_day, which check the role, the date, the lock, the sales and the daily entries in one transaction and
/// refuse with THROW 51980-51989 (plain sentences, passed on as <see cref="NoSaleDayRefusedException"/>). Reading uses
/// dbo.v_no_sale_days_current; the reason column is read only when the caller may read it (the Viewer may not).
/// </summary>
public sealed class SqlServerNoSaleDayService : INoSaleDayService
{
    /// <summary>The lane's THROW block. Every number in it carries a sentence written for the person at the screen.</summary>
    public const int FirstRefusal = 51980;
    public const int LastRefusal = 51989;
    /// <summary>1.12.1 P4 (review m-2): an inactive Retail store (P4 block 52010-52019).</summary>
    public const int InactiveStoreRefusal = 52010;

    /// <summary>The plain sentence for a permission refusal (SQL 229 / 230, or 51980).</summary>
    public const string PermissionMessage = "Only the Owner or a Store Manager can mark or withdraw a no-sale day.";

    /// <summary>
    /// Guarded read: a database before 0068 has no view and returns no rows. The reason is selected only when the caller has
    /// SELECT on that column (a statement that is not executed is not permission-checked).
    /// </summary>
    internal const string LoadSql = """
        SET NOCOUNT ON;
        IF OBJECT_ID(N'dbo.v_no_sale_days_current',N'V') IS NULL
          SELECT CAST(NULL AS varchar(30)) store_code,CAST(NULL AS date) business_date,CAST(NULL AS varchar(30)) reason_code,
                 CAST(NULL AS nvarchar(500)) reason,CAST(NULL AS nvarchar(100)) marked_by,CAST(NULL AS datetime2(3)) marked_utc,
                 CAST(0 AS bit) has_day_data WHERE 1=0;
        ELSE IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.v_no_sale_days_current',N'OBJECT',N'SELECT',N'reason',N'COLUMN'),0)=1
          SELECT n.store_code,n.business_date,n.reason_code,n.reason,n.marked_by,n.marked_utc,n.has_day_data
          FROM dbo.v_no_sale_days_current n WHERE n.business_date=@date ORDER BY n.store_code;
        ELSE
          SELECT n.store_code,n.business_date,n.reason_code,CAST(NULL AS nvarchar(500)) reason,n.marked_by,n.marked_utc,n.has_day_data
          FROM dbo.v_no_sale_days_current n WHERE n.business_date=@date ORDER BY n.store_code;
        """;

    private readonly IEtpConnectionFactory connections;

    public SqlServerNoSaleDayService(IEtpConnectionFactory connections) =>
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));

    public async Task<IReadOnlyList<NoSaleDay>> LoadAsync(DateOnly businessDate, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(EtpConnectionPurpose.Default, ct).ConfigureAwait(false);
        await using var command = new SqlCommand(LoadSql, connection);
        command.Parameters.Add("@date", SqlDbType.Date).Value = businessDate.ToDateTime(TimeOnly.MinValue);
        var rows = new List<NoSaleDay>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                rows.Add(new(
                    reader.GetString(0),
                    DateOnly.FromDateTime(reader.GetDateTime(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    reader.GetString(4),
                    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)))
                {
                    NoLongerApplies = reader.GetBoolean(6)
                });
        }
        catch (SqlException exception) when (exception.Number is 229 or 230)
        {
            throw new UnauthorizedAccessException("This Windows account cannot read no-sale days.", exception);
        }
        return rows;
    }

    public Task MarkAsync(string storeCode, DateOnly businessDate, string reasonCode, string reason, CancellationToken ct = default)
    {
        var store = RequireStore(storeCode);
        if (NoSaleDayText.ValidateMark(reasonCode, reason) is { } problem) throw new NoSaleDayRefusedException(problem);
        return ExecuteAsync("dbo.mark_no_sale_day", command =>
        {
            command.Parameters.Add("@store", SqlDbType.VarChar, 30).Value = store;
            command.Parameters.Add("@date", SqlDbType.Date).Value = businessDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@reason_code", SqlDbType.VarChar, 30).Value = reasonCode;
            command.Parameters.Add("@reason", SqlDbType.NVarChar, NoSaleDayText.ReasonMaxLength).Value = reason.Trim();
        }, ct);
    }

    public Task WithdrawAsync(string storeCode, DateOnly businessDate, string reason, CancellationToken ct = default)
    {
        var store = RequireStore(storeCode);
        if (NoSaleDayText.ValidateWithdraw(reason) is { } problem) throw new NoSaleDayRefusedException(problem);
        return ExecuteAsync("dbo.withdraw_no_sale_day", command =>
        {
            command.Parameters.Add("@store", SqlDbType.VarChar, 30).Value = store;
            command.Parameters.Add("@date", SqlDbType.Date).Value = businessDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@reason", SqlDbType.NVarChar, NoSaleDayText.ReasonMaxLength).Value = reason.Trim();
        }, ct);
    }

    private async Task ExecuteAsync(string procedure, Action<SqlCommand> parameters, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(EtpConnectionPurpose.Default, ct).ConfigureAwait(false);
        await using var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
        parameters(command);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException exception) when (Translate(exception) is { } refusal)
        {
            throw refusal;
        }
    }

    /// <summary>
    /// The refusal for a SQL error the person can act on: the lane's own sentence (51980-51989), or the permission sentence
    /// for 229/230 (EXECUTE or column denied). Anything else is not translated (the caller's error handling shows it).
    /// </summary>
    internal static Exception? Translate(SqlException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Number switch
        {
            >= FirstRefusal and <= LastRefusal or InactiveStoreRefusal => new NoSaleDayRefusedException(exception.Message, exception),
            229 or 230 => new UnauthorizedAccessException(PermissionMessage, exception),
            _ => null
        };
    }

    /// <summary>
    /// The DSR's marks for a business date (OperationalReportRepository): store and the printed line only, so it needs no
    /// access to the reason. Guarded: a database before 0068 returns none. A mark whose store-day has sales or a non-zero
    /// daily entry since (has_day_data, review M-1) is left out: the DSR never prints "No sales" over real sales.
    /// </summary>
    internal const string DsrMarksSql = """
        SET NOCOUNT ON;
        IF OBJECT_ID(N'dbo.v_no_sale_days_current',N'V') IS NOT NULL
          SELECT n.store_code,n.marked_by,n.marked_utc FROM dbo.v_no_sale_days_current n WHERE n.business_date=@date AND n.has_day_data=0 ORDER BY n.store_code;
        """;

    internal static async Task<IReadOnlyList<Etp.Reporting.Reporting.DsrNoSaleDay>> LoadDsrMarksAsync(string connectionString, DateOnly businessDate, CancellationToken ct)
    {
        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new SqlCommand(DsrMarksSql, connection);
        command.Parameters.Add("@date", SqlDbType.Date).Value = businessDate.ToDateTime(TimeOnly.MinValue);
        var marks = new List<Etp.Reporting.Reporting.DsrNoSaleDay>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        // No result set at all when the view is missing (older database): the reader simply has no rows.
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            marks.Add(new(reader.GetString(0), NoSaleDayText.NoSalesLine(reader.GetString(1),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)))));
        return marks;
    }

    private static string RequireStore(string storeCode) =>
        string.IsNullOrWhiteSpace(storeCode) ? throw new NoSaleDayRefusedException("Choose a shop that ETP knows and a business date.") : storeCode.Trim();
}
