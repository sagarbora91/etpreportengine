using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record TenderModeRow(string SourceCode, string? AgencyName, string Mode, bool Active);
public sealed record StaffMasterRow(string StoreCode, string Code, string Name, bool Active);
public sealed record ManualInputAggregate(string FieldCode, decimal Value, int AvailableDays, int MissingDays);
public sealed record MonthlyTargetRow(string StoreCode, DateOnly Month, decimal TargetSales);

public sealed class DataTruthMasterRepository(string connectionString)
{
    public static IReadOnlyList<string> Modes { get; } = ["Cash", "Card", "UPI", "CN", "TC", "Gift Card", "Bank", "Service Cash", "Service Card", "Service UPI"];

    public async Task<IReadOnlyList<TenderModeRow>> LoadTenderModesAsync(CancellationToken token = default)
    {
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand("SELECT source_tender_code,agency_name,mode,active FROM dbo.tender_modes ORDER BY source_tender_code", connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new List<TenderModeRow>();
        while (await reader.ReadAsync(token)) result.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        return result;
    }

    public async Task SaveTenderModeAsync(TenderModeRow value, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await RequireOwnerAsync(token);
        if (string.IsNullOrWhiteSpace(value.SourceCode) || value.SourceCode.Length > 80 || value.AgencyName?.Length > 200)
            throw new ArgumentException("Enter a source payment code and an agency name of at most 200 characters.");
        if (!Modes.Contains(value.Mode, StringComparer.Ordinal)) throw new ArgumentException("Select a cash-book mode.");
        const string sql = """
            MERGE dbo.tender_modes WITH(HOLDLOCK) t USING(SELECT @code source_tender_code) s ON t.source_tender_code=s.source_tender_code
            WHEN MATCHED THEN UPDATE SET agency_name=@agency,mode=@mode,active=@active,modified_utc=SYSUTCDATETIME(),modified_by=ORIGINAL_LOGIN()
            WHEN NOT MATCHED THEN INSERT(source_tender_code,agency_name,mode,active) VALUES(@code,@agency,@mode,@active);
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@code", value.SourceCode.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@agency", (object?)value.AgencyName?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("@mode", value.Mode);
        command.Parameters.AddWithValue("@active", value.Active);
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<IReadOnlyList<StaffMasterRow>> LoadStaffAsync(CancellationToken token = default)
    {
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand("SELECT store_code,staff_code,staff_name,active FROM dbo.staff ORDER BY store_code,staff_name,staff_code", connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new List<StaffMasterRow>();
        while (await reader.ReadAsync(token)) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        return result;
    }

    public async Task SaveStaffAsync(StaffMasterRow value, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await RequireOwnerAsync(token);
        if (string.IsNullOrWhiteSpace(value.StoreCode) || value.StoreCode.Length > 30 || string.IsNullOrWhiteSpace(value.Code) || value.Code.Length > 80 || string.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 200)
            throw new ArgumentException("Enter a store, staff code and staff name.");
        const string sql = """
            MERGE dbo.staff WITH(HOLDLOCK) t USING(SELECT @store store_code,@code staff_code) s ON t.store_code=s.store_code AND t.staff_code=s.staff_code
            WHEN MATCHED THEN UPDATE SET staff_name=@name,active=@active,modified_utc=SYSUTCDATETIME(),modified_by=ORIGINAL_LOGIN()
            WHEN NOT MATCHED THEN INSERT(store_code,staff_code,staff_name,active) VALUES(@store,@code,@name,@active);
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", value.StoreCode.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@code", value.Code.Trim());
        command.Parameters.AddWithValue("@name", value.Name.Trim());
        command.Parameters.AddWithValue("@active", value.Active);
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task SaveMonthlyTargetAsync(MonthlyTargetRow value, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await RequireOwnerAsync(token);
        if (string.IsNullOrWhiteSpace(value.StoreCode) || value.StoreCode.Length > 30 || value.TargetSales < 0)
            throw new ArgumentException("Enter a store and a non-negative monthly target.");
        const string sql = """
            MERGE dbo.monthly_targets WITH(HOLDLOCK) t USING(SELECT @store store_code,@month target_month) s ON t.store_code=s.store_code AND t.target_month=s.target_month
            WHEN MATCHED THEN UPDATE SET target_sales=@amount,modified_utc=SYSUTCDATETIME(),modified_by=ORIGINAL_LOGIN()
            WHEN NOT MATCHED THEN INSERT(store_code,target_month,target_sales) VALUES(@store,@month,@amount);
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", value.StoreCode.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@month", new DateOnly(value.Month.Year, value.Month.Month, 1));
        command.Parameters.AddWithValue("@amount", value.TargetSales);
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<ManualInputAggregate> LoadManualAggregateAsync(string storeCode, string fieldCode, DateOnly from, DateOnly to, CancellationToken token = default)
    {
        if (to < from) throw new ArgumentException("Choose an end date on or after the start date.");
        const string sql = "SELECT COALESCE(SUM(numeric_value),0),COUNT(DISTINCT business_date) FROM dbo.manual_operational_inputs WHERE store_code=@store AND field_code=@field AND numeric_value IS NOT NULL AND business_date BETWEEN @from AND @to";
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", storeCode);
        command.Parameters.AddWithValue("@field", fieldCode);
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        var available = reader.GetInt32(1);
        return new(fieldCode, reader.GetDecimal(0), available, to.DayNumber - from.DayNumber + 1 - available);
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
