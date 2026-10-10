using System.Text.RegularExpressions;
using Etp.Reporting.Application.Accounting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <summary>Plan task 13: the ledger names Tally vouchers use. Lists every version of the mappings for Tally business events,
/// names the events a store still needs, and approves a new version through the existing approval path
/// (<see cref="SqlServerAccountingService.ApproveMappingAsync"/>), so the history and audit stay in one place. Owner only.</summary>
public sealed class SqlServerTallyLedgerMappingService(string connectionString) : ITallyLedgerMappingService
{
    private static readonly Regex EventPattern = new("^(TENDER_[A-Z_]{1,40}|ROUND_OFF|SALES_REVENUE|OUTPUT_(CGST|SGST|IGST)_[0-9]{1,2}(\\.[0-9]{1,2})?)$", RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<TallyLedgerMappingRow>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        const string sql = """
            SELECT business_event,store_code,debit_ledger,credit_ledger,effective_from,effective_to,version,is_active,modified_by,modified_utc
            FROM dbo.accounting_mappings
            WHERE business_event LIKE 'TENDER[_]%' OR business_event IN('ROUND_OFF','SALES_REVENUE') OR business_event LIKE 'OUTPUT[_]%'
            ORDER BY business_event,store_code,version DESC;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<TallyLedgerMappingRow>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.GetString(3),
                DateOnly.FromDateTime(reader.GetDateTime(4)), reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetInt32(6), reader.GetBoolean(7), reader.GetString(8), reader.GetDateTime(9)));
        return rows;
    }

    public async Task<IReadOnlyList<string>> LoadNeededEventsAsync(string storeCode, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        const string sql = """
            SELECT DISTINCT mode FROM dbo.tender_modes WHERE active=1;
            WITH gst AS (
              SELECT r.cgst_rate,r.cgst_amount,r.sgst_utgst_rate,r.sgst_utgst_amount,r.igst_rate,r.igst_amount
              FROM dbo.[etp_r018] r JOIN dbo.import_files f ON f.import_file_id=r.import_file_id AND f.is_superseded=0
              WHERE CAST(r.store_code AS nvarchar(80)) COLLATE DATABASE_DEFAULT=@store)
            SELECT DISTINCT 'CGST',cgst_rate FROM gst WHERE cgst_amount<>0 AND cgst_rate IS NOT NULL
            UNION SELECT DISTINCT 'SGST',sgst_utgst_rate FROM gst WHERE sgst_utgst_amount<>0 AND sgst_utgst_rate IS NOT NULL
            UNION SELECT DISTINCT 'IGST',igst_rate FROM gst WHERE igst_amount<>0 AND igst_rate IS NOT NULL;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", Store(storeCode));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new SortedSet<string>(StringComparer.Ordinal) { TallySalesVoucherComposer.RoundOffCode, "SALES_REVENUE" };
        while (await reader.ReadAsync(cancellationToken)) events.Add(TallySalesVoucherComposer.TenderEvent(reader.GetString(0)));
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) events.Add(TallySalesVoucherComposer.TaxEvent(reader.GetString(0), reader.GetDecimal(1)));
        return events.ToArray();
    }

    public async Task SaveAsync(string storeCode, string businessEvent, string ledgerName, DateOnly effectiveFrom, string reason, CancellationToken cancellationToken = default)
    {
        var code = (businessEvent ?? "").Trim().ToUpperInvariant();
        if (!EventPattern.IsMatch(code))
            throw new ArgumentException("Choose a business event from the list, such as TENDER_CASH, SALES_REVENUE or OUTPUT_CGST_9.");
        var ledger = (ledgerName ?? "").Trim();
        if (ledger.Length is 0 or > 200 || ledger.Any(char.IsControl))
            throw new ArgumentException("Enter the Tally ledger name exactly as Tally shows it (at most 200 characters).");
        if (effectiveFrom == default) throw new ArgumentException("Choose the first date this ledger applies to.");
        await new SqlServerAccountingService(connectionString).ApproveMappingAsync(
            new ApproveAccountingMapping(new(Store(storeCode), effectiveFrom), code, ledger, ledger, "{reference}", reason), cancellationToken);
    }

    private static string Store(string? storeCode)
    {
        var store = (storeCode ?? "").Trim().ToUpperInvariant();
        if (store.Length == 0) throw new ArgumentException("Choose the store this ledger is for.");
        return store;
    }

    private async Task RequireOwnerAsync(CancellationToken token)
    {
        if (!(await new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync(token)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqlConnection(SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString)));
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
}
