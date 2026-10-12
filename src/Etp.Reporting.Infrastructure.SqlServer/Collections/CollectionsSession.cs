using System.Data;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Collections;

/// <summary>What the collections services need from SQL besides their own statements: the caller's role, the audit and a
/// connection. A seam so the role refusals are tested without a database (CollectionsServiceBoundaryTests).</summary>
internal interface ICollectionsSession
{
    Task<AccessRole> LoadRoleAsync(CancellationToken cancellationToken);
    Task RecordAuditAsync(string eventType, string outcome, string? safeDetail, CancellationToken cancellationToken);
    Task<SqlConnection> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>The SQL session through the 1.12 connection factory (112-WAVE-PLAN A1: new Infrastructure code takes
/// <see cref="IEtpConnectionFactory"/>).</summary>
internal sealed class SqlCollectionsSession(IEtpConnectionFactory connections) : ICollectionsSession
{
    private readonly IEtpConnectionFactory connections = connections ?? throw new ArgumentNullException(nameof(connections));

    public Task<SqlConnection> OpenAsync(CancellationToken cancellationToken) =>
        connections.OpenAsync(EtpConnectionPurpose.Default, cancellationToken);

    // Phase2OperationsRepository.LoadCurrentAccessAsync's query: the caller's own application_users row (0059's row filter
    // lets a Viewer read exactly this row).
    public async Task<AccessRole> LoadRoleAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @identity nvarchar(200)=SUSER_SNAME();
            SELECT COALESCE(u.role_code,'NONE'),CONVERT(bit,COALESCE(u.is_active,0))
            FROM (SELECT 1 anchor) x LEFT JOIN dbo.application_users u ON u.windows_identity=@identity;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(1)) return AccessRole.None;
        return reader.GetString(0) switch
        {
            "OWNER" => AccessRole.Owner,
            "STORE_MANAGER" => AccessRole.StoreManager,
            "VIEWER" => AccessRole.Viewer,
            _ => AccessRole.None
        };
    }

    public async Task RecordAuditAsync(string eventType, string outcome, string? safeDetail, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("EXEC dbo.record_operational_audit @type,@outcome,@detail,N'database';", connection);
        command.Parameters.Add("@type", SqlDbType.VarChar, 40).Value = eventType;
        command.Parameters.Add("@outcome", SqlDbType.VarChar, 20).Value = outcome;
        command.Parameters.Add("@detail", SqlDbType.NVarChar, 200).Value = (object?)safeDetail ?? DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>Role gates, audit names and small SQL helpers shared by the collections services.</summary>
internal static class CollectionsAccess
{
    // 1.12 audit event types, pre-allocated in CK_operational_audit_type by lane A3's migration (112-WAVE-PLAN A3).
    public const string SetupAudit = "CollectionsSetup";
    public const string ImportAudit = "CollectionsImport";
    public const string DepositAudit = "CashDepositConfirmed";

    public const string OwnerRequired = "Owner permission is required.";
    public const string ImportRequired = "Importing bank and settlement files needs the Owner or a Store Manager.";
    public const string ViewRequired = "Your Windows account has no ETP role. Ask the owner to add you in Settings > Users.";

    public static bool CanView(AccessRole role) => role is AccessRole.Viewer or AccessRole.StoreManager or AccessRole.Owner;
    public static bool CanImport(AccessRole role) => role is AccessRole.StoreManager or AccessRole.Owner;
    public static bool CanAdminister(AccessRole role) => role is AccessRole.Owner;

    /// <summary>Loads the caller's role; when it is not allowed, writes the Blocked audit row first, then refuses.</summary>
    public static async Task<AccessRole> RequireAsync(ICollectionsSession session, Func<AccessRole, bool> allowed, string auditType, string message, CancellationToken cancellationToken)
    {
        var role = await session.LoadRoleAsync(cancellationToken).ConfigureAwait(false);
        if (allowed(role)) return role;
        try { await session.RecordAuditAsync(auditType, "Blocked", null, cancellationToken).ConfigureAwait(false); }
        catch (SqlException) { /* the refusal below is what matters; an audit failure must not turn it into another error */ }
        throw new UnauthorizedAccessException(message);
    }

    public static void Add(SqlCommand command, string name, SqlDbType type, object? value, int size = 0)
    {
        var parameter = command.Parameters.Add(name, type);
        if (size != 0) parameter.Size = size;
        parameter.Value = value ?? DBNull.Value;
    }

    public static object? Date(DateOnly? value) => value?.ToDateTime(TimeOnly.MinValue);

    public static string? StoresJson(IReadOnlyList<string>? storeCodes) =>
        storeCodes is null ? null : System.Text.Json.JsonSerializer.Serialize(storeCodes.Select(code => code.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal));

    public static string? OptionalString(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    public static decimal? OptionalDecimal(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    public static int? OptionalInt(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    public static DateOnly? OptionalDate(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(ordinal));
    public static DateOnly RequiredDate(SqlDataReader reader, int ordinal) => DateOnly.FromDateTime(reader.GetDateTime(ordinal));

    /// <summary>Reads a .csv or .xlsx statement with ETP's own readers (zip and size checks included).</summary>
    public static async Task<WorkbookSnapshot> ReadFileAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var info = new FileInfo(filePath);
        if (!info.Exists) throw new FileNotFoundException("The selected file no longer exists.", filePath);
        if (info.Length is <= 0 or > 100L * 1024 * 1024) throw new InvalidOperationException("The file is empty or larger than 100 MB.");
        IWorkbookReader reader = info.Extension.ToLowerInvariant() switch
        {
            ".csv" => new CsvWorkbookReader(),
            ".xlsx" => new OpenXmlWorkbookReader(),
            _ => throw new InvalidOperationException("Choose a .csv or .xlsx statement file.")
        };
        return await reader.ReadAsync(info.FullName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A SQL refusal from the collections objects (THROW 51950-51959) or a constraint, as a plain sentence; null when
    /// the error is something else and must surface as it is.</summary>
    public static string? Plain(SqlException exception)
    {
        if (exception.Number is >= 51950 and <= 51959) return exception.Message;
        var text = exception.Message;
        if (exception.Number is 2601 or 2627)
        {
            if (text.Contains("UQ_bank_accounts_key", StringComparison.Ordinal)) return "Another bank account already uses this key.";
            if (text.Contains("PK_collection_providers", StringComparison.Ordinal)) return "Another provider already uses this key.";
            if (text.Contains("UQ_provider_merchant_ids", StringComparison.Ordinal)) return "This merchant and terminal id is already set up for the provider.";
            if (text.Contains("UQ_statement_layouts_key", StringComparison.Ordinal)) return "Another file layout already uses this key.";
            if (text.Contains("UQ_statement_layouts_signature", StringComparison.Ordinal)) return "This account or provider already has a layout for files with these headers.";
            if (text.Contains("UX_statement_layouts_one_active_per_account", StringComparison.Ordinal))
                return "This bank account already has an active file layout. Switch the other one off first, so one statement is never read two ways.";
            if (text.Contains("UX_cash_deposit_confirmations_active", StringComparison.Ordinal)) return "This deposit is already marked as credited.";
            if (text.Contains("UX_cash_deposit_confirmations_line", StringComparison.Ordinal)) return "This bank line is already used for another deposit.";
            return "This entry already exists.";
        }
        if (exception.Number == 547)
        {
            if (text.Contains("_store", StringComparison.Ordinal)) return "The store is not in Settings > Stores & masters > Stores.";
            if (text.Contains("_account", StringComparison.Ordinal)) return "The bank account is not set up.";
            if (text.Contains("_provider", StringComparison.Ordinal)) return "The provider is not set up.";
            if (text.Contains("CK_", StringComparison.Ordinal)) return "A value is not allowed here. Check the entry and try again.";
            return "This entry is still used elsewhere, so it cannot change this way.";
        }
        return null;
    }
}
