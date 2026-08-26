using System.Data;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public enum DailyReadinessStatus { NotReady, Partial, ReadyWithWarnings, Reconciled, Locked }

public sealed record ManualInputValue(
    string FieldCode,
    string DisplayName,
    string ValueKind,
    decimal? NumericValue,
    string? TextValue,
    bool IsRequired,
    DateTime? ModifiedUtc,
    string? ModifiedBy)
{
    public bool IsPresent => NumericValue is not null || TextValue is not null;
}

public sealed record DailyWorkflowSnapshot(
    string StoreCode,
    DateOnly BusinessDate,
    DailyReadinessStatus Status,
    IReadOnlyList<string> ImportedReports,
    IReadOnlyList<string> MissingReports,
    IReadOnlyList<ManualInputValue> ManualInputs,
    IReadOnlyList<string> MissingRequiredInputs,
    bool CanFinalise,
    string StatusMessage);

public sealed class DailyReportingWorkflowRepository(string connectionString)
{
    private static readonly string[] RequiredReports = ["R025", "R022", "R013", "R003", "STOCK_LEDGER", "CLOSING_STOCK"];

    public async Task<DailyWorkflowSnapshot> LoadAsync(
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        storeCode = Required(storeCode, nameof(storeCode));
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureDayAsync(connection, storeCode, businessDate, cancellationToken);

        var storedStatus = await ScalarAsync<string>(connection,
            "SELECT status FROM dbo.daily_reporting_days WHERE store_code=@store AND business_date=@date",
            storeCode, businessDate, cancellationToken);
        var imported = await LoadStringsAsync(connection,
            "SELECT DISTINCT report_code FROM dbo.import_files WHERE store_code=@store AND business_date=@date AND report_code IS NOT NULL ORDER BY report_code",
            storeCode, businessDate, cancellationToken);
        var inputs = await LoadInputsAsync(connection, storeCode, businessDate, cancellationToken);
        var missingReports = RequiredReports.Except(imported, StringComparer.OrdinalIgnoreCase).ToArray();
        var missingInputs = inputs.Where(x => x.IsRequired && !x.IsPresent).Select(x => x.FieldCode).ToArray();
        var status = ResolveStatus(storedStatus, imported.Count, missingReports.Length, missingInputs.Length);
        var canFinalise = status != DailyReadinessStatus.Locked && missingReports.Length == 0 && missingInputs.Length == 0;
        var message = status switch
        {
            DailyReadinessStatus.Locked => "The business date is finalised and protected from silent changes.",
            DailyReadinessStatus.NotReady => "No required ETP source report has been registered for this business date.",
            DailyReadinessStatus.Partial => $"Missing {missingReports.Length:N0} source report(s) and {missingInputs.Length:N0} required manual input(s).",
            DailyReadinessStatus.ReadyWithWarnings => "Required sources and inputs are present; review reconciliation exceptions before finalising.",
            _ => "Required sources, inputs and controls are reconciled."
        };
        return new(storeCode, businessDate, status, imported, missingReports, inputs, missingInputs, canFinalise, message);
    }

    public async Task SaveManualInputAsync(
        string storeCode,
        DateOnly businessDate,
        string fieldCode,
        decimal? numericValue,
        string? textValue,
        string user,
        string reason,
        CancellationToken cancellationToken = default)
    {
        storeCode = Required(storeCode, nameof(storeCode));
        fieldCode = Required(fieldCode, nameof(fieldCode)).ToUpperInvariant();
        user = Required(user, nameof(user));
        reason = Required(reason, nameof(reason));
        if ((numericValue is null) == (textValue is null))
            throw new ArgumentException("Enter exactly one numeric or text value.");
        if (textValue?.Length > 1000 || reason.Length > 500)
            throw new ArgumentException("The manual value or change reason is too long.");

        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.manual_input_definitions WHERE field_code=@field AND is_active=1)
                THROW 51022,'The manual input field is not active.',1;
            MERGE dbo.manual_operational_inputs WITH(HOLDLOCK) AS target
            USING (SELECT @store store_code,@date business_date,@field field_code) AS source
              ON target.store_code=source.store_code AND target.business_date=source.business_date AND target.field_code=source.field_code
            WHEN MATCHED THEN UPDATE SET numeric_value=@numeric,text_value=@text,modified_by=@user,modified_utc=SYSUTCDATETIME(),change_reason=@reason
            WHEN NOT MATCHED THEN INSERT(store_code,business_date,field_code,numeric_value,text_value,entered_by,modified_by,change_reason)
              VALUES(@store,@date,@field,@numeric,@text,@user,@user,@reason);
            INSERT dbo.daily_reporting_events(store_code,business_date,event_type,performed_by,reason)
            VALUES(@store,@date,'ManualInputChanged',@user,@reason);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureDayAsync(connection, storeCode, businessDate, cancellationToken);
        await using var command = ScopeCommand(connection, sql, storeCode, businessDate);
        command.Parameters.AddWithValue("@field", fieldCode);
        command.Parameters.AddWithValue("@numeric", (object?)numericValue ?? DBNull.Value);
        command.Parameters.AddWithValue("@text", (object?)textValue ?? DBNull.Value);
        command.Parameters.AddWithValue("@user", user);
        command.Parameters.AddWithValue("@reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FinaliseAsync(
        string storeCode,
        DateOnly businessDate,
        string user,
        bool hasBlockingReconciliationExceptions,
        CancellationToken cancellationToken = default)
    {
        if (hasBlockingReconciliationExceptions)
            throw new InvalidOperationException("Resolve blocking reconciliation exceptions before finalising the day.");
        var snapshot = await LoadAsync(storeCode, businessDate, cancellationToken);
        if (!snapshot.CanFinalise)
            throw new InvalidOperationException("Required source reports and manual inputs must be complete before finalisation.");
        user = Required(user, nameof(user));
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE dbo.daily_reporting_days WITH(UPDLOCK,HOLDLOCK)
              SET status='LOCKED',finalised_by=@user,finalised_utc=SYSUTCDATETIME()
              WHERE store_code=@store AND business_date=@date AND status<>'LOCKED';
            IF @@ROWCOUNT<>1 THROW 51023,'The day is already locked or unavailable.',1;
            INSERT dbo.daily_reporting_events(store_code,business_date,event_type,performed_by)
              VALUES(@store,@date,'DayFinalised',@user);
            COMMIT TRANSACTION;
            """;
        await ExecuteScopeAsync(sql, storeCode, businessDate, user, null, cancellationToken);
    }

    public async Task ReopenAsync(
        string storeCode,
        DateOnly businessDate,
        string user,
        string reason,
        bool administratorApproved,
        CancellationToken cancellationToken = default)
    {
        if (!administratorApproved) throw new UnauthorizedAccessException("Administrator approval is required to reopen a finalised day.");
        user = Required(user, nameof(user));
        reason = Required(reason, nameof(reason));
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE dbo.daily_reporting_days WITH(UPDLOCK,HOLDLOCK)
              SET status='OPEN',reopened_by=@user,reopened_utc=SYSUTCDATETIME(),reopen_reason=@reason
              WHERE store_code=@store AND business_date=@date AND status='LOCKED';
            IF @@ROWCOUNT<>1 THROW 51024,'Only a finalised day can be reopened.',1;
            INSERT dbo.daily_reporting_events(store_code,business_date,event_type,performed_by,reason)
              VALUES(@store,@date,'DayReopened',@user,@reason);
            COMMIT TRANSACTION;
            """;
        await ExecuteScopeAsync(sql, storeCode, businessDate, user, reason, cancellationToken);
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("A SQL Server connection string is required.");
        var connection = new SqlConnection(connectionString);
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task EnsureDayAsync(SqlConnection connection, string storeCode, DateOnly date, CancellationToken token)
    {
        await using var command = ScopeCommand(connection,
            "IF NOT EXISTS(SELECT 1 FROM dbo.daily_reporting_days WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND business_date=@date) INSERT dbo.daily_reporting_days(store_code,business_date,status) VALUES(@store,@date,'OPEN')",
            storeCode, date);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<IReadOnlyList<ManualInputValue>> LoadInputsAsync(SqlConnection connection, string storeCode, DateOnly date, CancellationToken token)
    {
        const string sql = """
            SELECT d.field_code,d.display_name,d.value_kind,i.numeric_value,i.text_value,d.is_required_for_finalisation,i.modified_utc,i.modified_by
            FROM dbo.manual_input_definitions d
            LEFT JOIN dbo.manual_operational_inputs i ON i.field_code=d.field_code AND i.store_code=@store AND i.business_date=@date
            WHERE d.is_active=1 ORDER BY d.applies_to,d.display_name;
            """;
        await using var command = ScopeCommand(connection, sql, storeCode, date);
        await using var reader = await command.ExecuteReaderAsync(token);
        var values = new List<ManualInputValue>();
        while (await reader.ReadAsync(token))
            values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetBoolean(5), reader.IsDBNull(6) ? null : reader.GetDateTime(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return values;
    }

    private static async Task<IReadOnlyList<string>> LoadStringsAsync(SqlConnection connection, string sql, string storeCode, DateOnly date, CancellationToken token)
    {
        await using var command = ScopeCommand(connection, sql, storeCode, date);
        await using var reader = await command.ExecuteReaderAsync(token);
        var values = new List<string>();
        while (await reader.ReadAsync(token)) values.Add(reader.GetString(0));
        return values;
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql, string storeCode, DateOnly date, CancellationToken token)
    {
        await using var command = ScopeCommand(connection, sql, storeCode, date);
        return (T)(await command.ExecuteScalarAsync(token) ?? throw new DataException("The daily reporting record was not found."));
    }

    private async Task ExecuteScopeAsync(string sql, string storeCode, DateOnly date, string user, string? reason, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        await using var command = ScopeCommand(connection, sql, Required(storeCode, nameof(storeCode)), date);
        command.Parameters.AddWithValue("@user", user);
        command.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token);
    }

    private static SqlCommand ScopeCommand(SqlConnection connection, string sql, string storeCode, DateOnly date)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", storeCode);
        command.Parameters.AddWithValue("@date", date);
        return command;
    }

    private static DailyReadinessStatus ResolveStatus(string storedStatus, int importedCount, int missingReports, int missingInputs)
    {
        if (string.Equals(storedStatus, "LOCKED", StringComparison.Ordinal)) return DailyReadinessStatus.Locked;
        if (importedCount == 0) return DailyReadinessStatus.NotReady;
        if (missingReports > 0 || missingInputs > 0) return DailyReadinessStatus.Partial;
        return string.Equals(storedStatus, "RECONCILED", StringComparison.Ordinal)
            ? DailyReadinessStatus.Reconciled : DailyReadinessStatus.ReadyWithWarnings;
    }

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A value is required.", name) : value.Trim();
}
