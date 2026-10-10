using System.Data;
using Etp.Reporting.Application.DailyReadiness;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// 1.9.9 import reminder: reads, for one business date, what the database holds (active Retail stores,
/// received exports, entered manual inputs, the month's targets) in one read-only batch, and lets
/// <see cref="DailyReadinessEvaluator"/> decide what is missing.
/// <para>An export counts as received for date D when a current (not superseded) file of a Completed
/// batch has that report code and store and its period (or its business date when it has no period)
/// covers D. An empty export counts: the file was received.</para>
/// </summary>
public sealed class SqlServerDailyReadinessQuery : IDailyReadinessQuery
{
    internal const string FactsSql = """
        SET NOCOUNT ON;
        SELECT s.store_code
        FROM dbo.stores s
        LEFT JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
        WHERE s.is_active=1 AND (u.business_unit_code IS NULL OR u.business_unit_code<>'SERVICE');

        SELECT DISTINCT f.store_code,f.report_code
        FROM dbo.import_files f
        JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
        WHERE b.status='Completed' AND f.is_superseded=0
          AND f.store_code IS NOT NULL
          AND f.report_code IN (SELECT LTRIM(RTRIM(value)) FROM STRING_SPLIT(@reportCodes,','))
          AND COALESCE(f.period_start,f.business_date)<=@date
          AND COALESCE(f.period_end,f.business_date)>=@date;

        SELECT DISTINCT i.store_code,i.field_code
        FROM dbo.manual_operational_inputs i
        WHERE i.business_date=@date
          AND i.field_code IN (SELECT LTRIM(RTRIM(value)) FROM STRING_SPLIT(@fieldCodes,','))
          AND (i.numeric_value IS NOT NULL OR i.text_value IS NOT NULL);

        SELECT t.store_code FROM dbo.monthly_targets t WHERE t.target_month=@month;

        SELECT DISTINCT t.store_code FROM dbo.staff_sales_targets t WHERE t.target_month=@month;
        """;

    private readonly Func<DateOnly, CancellationToken, Task<DailyReadinessFacts>> loadFacts;

    public SqlServerDailyReadinessQuery(string connectionString)
        : this(CreateLoader(SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString))))
    {
    }

    internal SqlServerDailyReadinessQuery(Func<DateOnly, CancellationToken, Task<DailyReadinessFacts>> loadFacts) =>
        this.loadFacts = loadFacts ?? throw new ArgumentNullException(nameof(loadFacts));

    public async Task<DailyReadinessResult> LoadAsync(DailyReadinessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var facts = await loadFacts(request.BusinessDate, cancellationToken).ConfigureAwait(false);
        return DailyReadinessEvaluator.Evaluate(request, facts);
    }

    private static Func<DateOnly, CancellationToken, Task<DailyReadinessFacts>> CreateLoader(string connectionString) =>
        (date, cancellationToken) => LoadFactsAsync(connectionString, date, cancellationToken);

    internal static async Task<DailyReadinessFacts> LoadFactsAsync(string connectionString, DateOnly date, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(FactsSql, connection);
        command.Parameters.Add("@date", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@month", SqlDbType.Date).Value = new DateOnly(date.Year, date.Month, 1).ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@reportCodes", SqlDbType.VarChar, 4000).Value = string.Join(',', DailyReadinessExpectations.AllStoredReportCodes);
        command.Parameters.Add("@fieldCodes", SqlDbType.VarChar, 4000).Value = string.Join(',', DailyReadinessExpectations.ManualInputs.Select(x => x.FieldCode));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var stores = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) stores.Add(reader.GetString(0));
        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        var exports = new List<(string, string)>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) exports.Add((reader.GetString(0), reader.GetString(1)));
        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        var inputs = new List<(string, string)>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) inputs.Add((reader.GetString(0), reader.GetString(1)));
        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        var monthly = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) monthly.Add(reader.GetString(0));
        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        var staff = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) staff.Add(reader.GetString(0));
        return new(stores, exports, inputs, monthly, staff);
    }
}
