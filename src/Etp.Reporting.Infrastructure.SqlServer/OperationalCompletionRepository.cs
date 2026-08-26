using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record CurrentImportFile(long ImportFileId, string ReportCode, string StoreCode, DateOnly BusinessDate, string SourceSha256);

public sealed record ManualStockCount(
    string StoreCode,
    DateOnly BusinessDate,
    string InventoryGroupCode,
    decimal? DisplayQuantity,
    decimal? BackstockQuantity,
    decimal? DefectiveQuantity,
    decimal? YLocationQuantity,
    decimal? CountedPhysicalQuantity,
    string? Remarks,
    DateTime ModifiedUtc,
    string ModifiedBy)
{
    public decimal? ComponentTotal => new[] { DisplayQuantity, BackstockQuantity, DefectiveQuantity, YLocationQuantity }.All(x => x is null)
        ? null : (DisplayQuantity ?? 0m) + (BackstockQuantity ?? 0m) + (DefectiveQuantity ?? 0m) + (YLocationQuantity ?? 0m);
    public decimal? CompositionVariance => CountedPhysicalQuantity is null || ComponentTotal is null ? null : CountedPhysicalQuantity - ComponentTotal;
}

public sealed record StaffSalesTarget(
    string StoreCode,
    string CroNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TargetSales,
    DateTime ModifiedUtc,
    string ModifiedBy);

public sealed record DailyReportGeneration(
    long GenerationId,
    string StoreCode,
    DateOnly BusinessDate,
    int GenerationNumber,
    string ContentSha256,
    DateTime GeneratedUtc,
    string GeneratedBy,
    bool IsFinal,
    long? SupersedesGenerationId);

public sealed class OperationalCompletionRepository(string connectionString)
{
    public async Task<CurrentImportFile?> FindCurrentImportAsync(
        string reportCode,
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        reportCode = Required(reportCode, nameof(reportCode)).ToUpperInvariant();
        storeCode = Required(storeCode, nameof(storeCode));
        const string sql = """
            SELECT import_file_id,report_code,store_code,business_date,source_sha256
            FROM dbo.import_files
            WHERE report_code=@report AND store_code=@store AND business_date=@date AND is_superseded=0
            ORDER BY import_file_id DESC;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = ScopeCommand(connection, sql, storeCode, businessDate);
        command.Parameters.AddWithValue("@report", reportCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<CurrentImportFile>();
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateOnly>(3), reader.GetString(4)));
        return values.Count switch
        {
            0 => null,
            1 => values[0],
            _ => throw new InvalidOperationException("More than one current source file exists for this report scope. Review import history before restating it.")
        };
    }

    public async Task SaveManualStockCountAsync(
        string storeCode,
        DateOnly businessDate,
        string inventoryGroupCode,
        decimal? display,
        decimal? backstock,
        decimal? defective,
        decimal? yLocation,
        decimal? countedPhysical,
        string? remarks,
        string user,
        string reason,
        CancellationToken cancellationToken = default)
    {
        storeCode = Required(storeCode, nameof(storeCode));
        inventoryGroupCode = Required(inventoryGroupCode, nameof(inventoryGroupCode));
        user = Required(user, nameof(user));
        reason = Required(reason, nameof(reason));
        if (new[] { display, backstock, defective, yLocation, countedPhysical }.All(x => x is null))
            throw new ArgumentException("Enter at least one physical-stock quantity.");
        if (inventoryGroupCode.Length > 100 || remarks?.Length > 500 || reason.Length > 500)
            throw new ArgumentException("The stock group, remarks or change reason is too long.");

        const string sql = """
            MERGE dbo.manual_stock_counts WITH(HOLDLOCK) AS target
            USING (SELECT @store store_code,@date business_date,@group inventory_group_code) AS source
              ON target.store_code=source.store_code AND target.business_date=source.business_date AND target.inventory_group_code=source.inventory_group_code
            WHEN MATCHED THEN UPDATE SET display_quantity=@display,backstock_quantity=@backstock,defective_quantity=@defective,
              y_location_quantity=@y,counted_physical_quantity=@physical,remarks=@remarks,modified_by=@user,modified_utc=SYSUTCDATETIME(),change_reason=@reason
            WHEN NOT MATCHED THEN INSERT(store_code,business_date,inventory_group_code,display_quantity,backstock_quantity,defective_quantity,
              y_location_quantity,counted_physical_quantity,remarks,entered_by,modified_by,change_reason)
              VALUES(@store,@date,@group,@display,@backstock,@defective,@y,@physical,@remarks,@user,@user,@reason);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = ScopeCommand(connection, sql, storeCode, businessDate);
        command.Parameters.AddWithValue("@group", inventoryGroupCode);
        Add(command, "@display", display); Add(command, "@backstock", backstock); Add(command, "@defective", defective);
        Add(command, "@y", yLocation); Add(command, "@physical", countedPhysical); Add(command, "@remarks", remarks);
        command.Parameters.AddWithValue("@user", user); command.Parameters.AddWithValue("@reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ManualStockCount>> LoadManualStockCountsAsync(
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT store_code,business_date,inventory_group_code,display_quantity,backstock_quantity,defective_quantity,
                   y_location_quantity,counted_physical_quantity,remarks,modified_utc,modified_by
            FROM dbo.manual_stock_counts WHERE store_code=@store AND business_date=@date ORDER BY inventory_group_code;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = ScopeCommand(connection, sql, Required(storeCode, nameof(storeCode)), businessDate);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ManualStockCount>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2), Decimal(reader, 3), Decimal(reader, 4),
                Decimal(reader, 5), Decimal(reader, 6), Decimal(reader, 7), Text(reader, 8), reader.GetDateTime(9), reader.GetString(10)));
        return rows;
    }

    public async Task SaveStaffTargetAsync(
        string storeCode,
        string croNumber,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal targetSales,
        string user,
        string reason,
        CancellationToken cancellationToken = default)
    {
        storeCode = Required(storeCode, nameof(storeCode));
        croNumber = Required(croNumber, nameof(croNumber));
        user = Required(user, nameof(user));
        reason = Required(reason, nameof(reason));
        if (periodEnd < periodStart) throw new ArgumentException("The target end date cannot precede its start date.");
        if (croNumber.Length > 80 || reason.Length > 500) throw new ArgumentException("The CRO number or change reason is too long.");
        const string sql = """
            MERGE dbo.staff_sales_targets WITH(HOLDLOCK) AS target
            USING (SELECT @store store_code,@cro cro_number,@from period_start,@to period_end) AS source
              ON target.store_code=source.store_code AND target.cro_number=source.cro_number AND target.period_start=source.period_start AND target.period_end=source.period_end
            WHEN MATCHED THEN UPDATE SET target_sales=@target,modified_by=@user,modified_utc=SYSUTCDATETIME(),change_reason=@reason
            WHEN NOT MATCHED THEN INSERT(store_code,cro_number,period_start,period_end,target_sales,entered_by,modified_by,change_reason)
              VALUES(@store,@cro,@from,@to,@target,@user,@user,@reason);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", storeCode); command.Parameters.AddWithValue("@cro", croNumber);
        command.Parameters.AddWithValue("@from", periodStart); command.Parameters.AddWithValue("@to", periodEnd);
        command.Parameters.AddWithValue("@target", targetSales); command.Parameters.AddWithValue("@user", user); command.Parameters.AddWithValue("@reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffSalesTarget>> LoadStaffTargetsAsync(
        ReportingQueryScope scope,
        CancellationToken cancellationToken = default)
    {
        scope.Validate();
        const string sql = """
            SELECT store_code,cro_number,period_start,period_end,target_sales,modified_utc,modified_by
            FROM dbo.staff_sales_targets
            WHERE period_start=@from AND period_end=@to
              AND (@stores IS NULL OR store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)))
            ORDER BY store_code,cro_number;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@from", scope.DateFrom); command.Parameters.AddWithValue("@to", scope.DateTo);
        command.Parameters.AddWithValue("@stores", scope.StoreCodes is { Count: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(scope.StoreCodes.Distinct(StringComparer.OrdinalIgnoreCase)) : DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<StaffSalesTarget>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateOnly>(2), reader.GetFieldValue<DateOnly>(3), reader.GetDecimal(4), reader.GetDateTime(5), reader.GetString(6)));
        return rows;
    }

    public async Task<DailyReportGeneration> SaveReportGenerationAsync(
        string storeCode,
        DateOnly businessDate,
        string generatedBy,
        string controlJson,
        CancellationToken cancellationToken = default)
    {
        storeCode = Required(storeCode, nameof(storeCode));
        generatedBy = Required(generatedBy, nameof(generatedBy));
        if (string.IsNullOrWhiteSpace(controlJson)) throw new ArgumentException("A report-generation control snapshot is required.", nameof(controlJson));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(controlJson)));
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string sql = """
                DECLARE @number int=ISNULL((SELECT MAX(generation_number) FROM dbo.daily_report_generations WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND business_date=@date),0)+1;
                DECLARE @previous bigint=(SELECT TOP(1) daily_report_generation_id FROM dbo.daily_report_generations WHERE store_code=@store AND business_date=@date ORDER BY generation_number DESC);
                INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,supersedes_generation_id)
                OUTPUT INSERTED.daily_report_generation_id,INSERTED.generation_number,INSERTED.generated_utc,INSERTED.is_final,INSERTED.supersedes_generation_id
                VALUES(@store,@date,@number,@hash,@json,@user,@previous);
                """;
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@store", storeCode); command.Parameters.AddWithValue("@date", businessDate);
            command.Parameters.AddWithValue("@hash", hash); command.Parameters.AddWithValue("@json", controlJson); command.Parameters.AddWithValue("@user", generatedBy);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("The report generation was not recorded.");
            var result = new DailyReportGeneration(reader.GetInt64(0), storeCode, businessDate, reader.GetInt32(1), hash,
                reader.GetDateTime(2), generatedBy, reader.GetBoolean(3), reader.IsDBNull(4) ? null : reader.GetInt64(4));
            await reader.DisposeAsync();
            await using (var audit = new SqlCommand("INSERT dbo.daily_reporting_events(store_code,business_date,event_type,performed_by,reason) VALUES(@store,@date,'ReportPackGenerated',@user,@reason)", connection, transaction))
            {
                audit.Parameters.AddWithValue("@store", storeCode); audit.Parameters.AddWithValue("@date", businessDate);
                audit.Parameters.AddWithValue("@user", generatedBy); audit.Parameters.AddWithValue("@reason", $"Generation {result.GenerationNumber}; control {hash[..12]}");
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("A SQL Server connection string is required.");
        var connection = new SqlConnection(connectionString);
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static SqlCommand ScopeCommand(SqlConnection connection, string sql, string storeCode, DateOnly businessDate)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", storeCode); command.Parameters.AddWithValue("@date", businessDate);
        return command;
    }

    private static void Add(SqlCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static decimal? Decimal(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    private static string? Text(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A value is required.", name) : value.Trim();
}
