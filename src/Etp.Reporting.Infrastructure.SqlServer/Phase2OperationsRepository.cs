using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public enum ApplicationRole { None, Viewer, StoreManager, Owner }

public sealed record ApplicationAccess(string WindowsIdentity, string DisplayName, ApplicationRole Role, bool IsActive)
{
    public bool CanView => IsActive && Role is not ApplicationRole.None;
    public bool CanImport => IsActive && Role is ApplicationRole.Owner or ApplicationRole.StoreManager;
    public bool CanEnterOperations => CanImport;
    public bool CanAdminister => IsActive && Role == ApplicationRole.Owner;
}

public sealed record ApplicationUserRow(int Id, string WindowsIdentity, string DisplayName, string RoleCode, bool IsActive, DateTime ModifiedUtc, string ModifiedBy);
public sealed record ControlledMasterRow(string MasterType, string Code, string DisplayName, string ApprovalStatus, bool IsActive, DateTime? ModifiedUtc, string? ModifiedBy);
public sealed record WatchFolderSettings(string InboundPath, string ProcessedPath, string FailedPath, string ReportOutputPath, int PollMinutes, bool IsEnabled, DateTime ModifiedUtc, string ModifiedBy);
public sealed record ReportPackSchedule(int Id, string Name, TimeOnly LocalRunTime, bool IsEnabled, bool ExportExcel, bool ExportPdf, DateOnly? LastBusinessDate, DateTime? LastRunUtc, string? LastStatus, string? LastMessage);
public sealed record AutomationRunRow(long Id, string RunType, string? SourceFileName, string? StoreCode, DateOnly? BusinessDate, string Outcome, string SafeMessage, DateTime StartedUtc, DateTime CompletedUtc, string RunBy);
public sealed record ArchivedReportGeneration(long Id, string StoreCode, DateOnly BusinessDate, int GenerationNumber, string ControlSha256, string? DocumentSha256, DateTime GeneratedUtc, string GeneratedBy, bool IsFinal, long? SupersedesGenerationId, bool CanReExport);
public sealed record ReportGenerationComparisonRow(string Table, int FirstRows, int SecondRows, string FirstStatus, string SecondStatus, bool Changed);
public sealed record ManagementTrendRow(DateOnly BusinessDate, string StoreCode, decimal NetSales, decimal Units, int Invoices, decimal TenderVariance, int UnmatchedEnrichmentRows);
public sealed record DataQualitySummaryRow(string Severity, string Area, string Code, long Count, DateTime? LatestUtc, string Message);

public sealed class Phase2OperationsRepository(string connectionString)
{
    public async Task<ApplicationAccess> LoadCurrentAccessAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            DECLARE @identity nvarchar(200)=SUSER_SNAME();
            SELECT @identity,COALESCE(u.display_name,@identity),COALESCE(u.role_code,'NONE'),CONVERT(bit,COALESCE(u.is_active,0))
            FROM (SELECT 1 anchor) x LEFT JOIN dbo.application_users u ON u.windows_identity=@identity;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(reader.GetString(0), reader.GetString(1), ParseRole(reader.GetString(2)), reader.GetBoolean(3));
    }

    public async Task<IReadOnlyList<ApplicationUserRow>> LoadUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureOwnerAsync(connection, cancellationToken);
        await using var command = new SqlCommand("SELECT application_user_id,windows_identity,display_name,role_code,is_active,modified_utc,modified_by FROM dbo.application_users ORDER BY role_code,display_name", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ApplicationUserRow>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4), reader.GetDateTime(5), reader.GetString(6)));
        return rows;
    }

    public async Task UpsertUserAsync(string windowsIdentity, string displayName, string roleCode, bool isActive, string reason, CancellationToken cancellationToken = default)
    {
        windowsIdentity = Required(windowsIdentity, nameof(windowsIdentity));
        displayName = Required(displayName, nameof(displayName));
        roleCode = Required(roleCode, nameof(roleCode)).Replace(' ', '_').ToUpperInvariant();
        reason = Required(reason, nameof(reason));
        if (windowsIdentity.Length > 200 || displayName.Length > 200 || reason.Length > 500) throw new ArgumentException("The user identity, display name or reason is too long.");
        if (!Regex.IsMatch(windowsIdentity, """^[^\\/\[\];'"]+\\[^\\/\[\];'"]+$""", RegexOptions.CultureInvariant))
            throw new ArgumentException(@"Enter a Windows identity as DOMAIN\User or COMPUTER\User.", nameof(windowsIdentity));
        if (roleCode is not ("OWNER" or "STORE_MANAGER" or "VIEWER")) throw new ArgumentException("Select Owner, Store Manager or Viewer.", nameof(roleCode));
        const string provisionSql = """
            IF SUSER_ID(@identity) IS NULL
            BEGIN
              DECLARE @createLogin nvarchar(max)=N'CREATE LOGIN '+QUOTENAME(@identity)+N' FROM WINDOWS';
              EXEC(@createLogin);
            END;
            DECLARE @principal sysname=(SELECT TOP(1) name FROM sys.database_principals WHERE sid=SUSER_SID(@identity));
            IF @principal IS NULL
            BEGIN
              DECLARE @createUser nvarchar(max)=N'CREATE USER '+QUOTENAME(@identity)+N' FOR LOGIN '+QUOTENAME(@identity);
              EXEC(@createUser);
              SET @principal=@identity;
            END;
            IF @principal<>N'dbo'
            BEGIN
              DECLARE @membership nvarchar(max)=N'';
              IF IS_ROLEMEMBER(N'db_owner',@principal)=1 SET @membership+=N'ALTER ROLE db_owner DROP MEMBER '+QUOTENAME(@principal)+N';';
              IF IS_ROLEMEMBER(N'db_datawriter',@principal)=1 SET @membership+=N'ALTER ROLE db_datawriter DROP MEMBER '+QUOTENAME(@principal)+N';';
              IF IS_ROLEMEMBER(N'db_datareader',@principal)=1 SET @membership+=N'ALTER ROLE db_datareader DROP MEMBER '+QUOTENAME(@principal)+N';';
              IF IS_ROLEMEMBER(N'db_backupoperator',@principal)=1 SET @membership+=N'ALTER ROLE db_backupoperator DROP MEMBER '+QUOTENAME(@principal)+N';';
              SET @membership+=N'REVOKE DELETE ON SCHEMA::dbo FROM '+QUOTENAME(@principal)+N';';
              SET @membership+=N'REVOKE INSERT,UPDATE,DELETE ON dbo.application_users FROM '+QUOTENAME(@principal)+N';';
              SET @membership+=N'REVOKE INSERT,UPDATE,DELETE ON dbo.application_user_history FROM '+QUOTENAME(@principal)+N';';
              SET @membership+=N'REVOKE INSERT,UPDATE,DELETE ON dbo.controlled_master_values FROM '+QUOTENAME(@principal)+N';';
              SET @membership+=N'REVOKE INSERT,UPDATE,DELETE ON dbo.controlled_master_history FROM '+QUOTENAME(@principal)+N';';
              SET @membership+=N'REVOKE INSERT,UPDATE,DELETE ON dbo.watch_folder_settings FROM '+QUOTENAME(@principal)+N';';
              SET @membership+=N'REVOKE INSERT,UPDATE,DELETE ON dbo.stores FROM '+QUOTENAME(@principal)+N';';
              SET @membership+=N'REVOKE INSERT,UPDATE,DELETE ON dbo.schema_migrations FROM '+QUOTENAME(@principal)+N';';
              SET @membership+=N'REVOKE INSERT,UPDATE,DELETE ON dbo.report_pack_schedules FROM '+QUOTENAME(@principal)+N';';
              SET @membership+=N'REVOKE UPDATE,DELETE ON dbo.operational_audit FROM '+QUOTENAME(@principal)+N';';
              SET @membership+=N'REVOKE UPDATE,DELETE ON dbo.automation_runs FROM '+QUOTENAME(@principal)+N';';
              IF @role='OWNER' AND @active=1 SET @membership+=N'ALTER ROLE db_owner ADD MEMBER '+QUOTENAME(@principal)+N';';
              IF @role='STORE_MANAGER' AND @active=1
              BEGIN
                SET @membership+=N'ALTER ROLE db_datareader ADD MEMBER '+QUOTENAME(@principal)+N';ALTER ROLE db_datawriter ADD MEMBER '+QUOTENAME(@principal)+N';';
                SET @membership+=N'DENY DELETE ON SCHEMA::dbo TO '+QUOTENAME(@principal)+N';';
                SET @membership+=N'DENY INSERT,UPDATE,DELETE ON dbo.application_users TO '+QUOTENAME(@principal)+N';';
                SET @membership+=N'DENY INSERT,UPDATE,DELETE ON dbo.application_user_history TO '+QUOTENAME(@principal)+N';';
                SET @membership+=N'DENY INSERT,UPDATE,DELETE ON dbo.controlled_master_values TO '+QUOTENAME(@principal)+N';';
                SET @membership+=N'DENY INSERT,UPDATE,DELETE ON dbo.controlled_master_history TO '+QUOTENAME(@principal)+N';';
                SET @membership+=N'DENY INSERT,UPDATE,DELETE ON dbo.watch_folder_settings TO '+QUOTENAME(@principal)+N';';
                SET @membership+=N'DENY INSERT,UPDATE,DELETE ON dbo.stores TO '+QUOTENAME(@principal)+N';';
                SET @membership+=N'DENY INSERT,UPDATE,DELETE ON dbo.schema_migrations TO '+QUOTENAME(@principal)+N';';
                IF @identity<>N'NT AUTHORITY\SYSTEM' SET @membership+=N'DENY INSERT,UPDATE,DELETE ON dbo.report_pack_schedules TO '+QUOTENAME(@principal)+N';';
                SET @membership+=N'DENY UPDATE,DELETE ON dbo.operational_audit TO '+QUOTENAME(@principal)+N';';
                SET @membership+=N'DENY UPDATE,DELETE ON dbo.automation_runs TO '+QUOTENAME(@principal)+N';';
              END;
              IF @role='VIEWER' AND @active=1 SET @membership+=N'ALTER ROLE db_datareader ADD MEMBER '+QUOTENAME(@principal)+N';';
              IF @identity=N'NT AUTHORITY\SYSTEM' AND @active=1 SET @membership+=N'ALTER ROLE db_backupoperator ADD MEMBER '+QUOTENAME(@principal)+N';';
              SET @membership+=CASE WHEN @active=1 THEN N'REVOKE CONNECT TO ' ELSE N'DENY CONNECT TO ' END+QUOTENAME(@principal)+N';';
              SET @membership+=N'GRANT INSERT ON dbo.operational_audit TO '+QUOTENAME(@principal)+N';';
              EXEC(@membership);
            END;
            DECLARE @serverPermission nvarchar(max)=N'USE [master]; '+CASE WHEN @role='OWNER' AND @active=1 THEN N'GRANT ALTER ANY LOGIN TO ' ELSE N'REVOKE ALTER ANY LOGIN TO ' END+QUOTENAME(@identity)+N';';
            EXEC(@serverPermission);
            """;
        const string upsertSql = """
            MERGE dbo.application_users WITH(HOLDLOCK) AS target
            USING(SELECT @identity windows_identity) source ON target.windows_identity=source.windows_identity
            WHEN MATCHED THEN UPDATE SET display_name=@name,role_code=@role,is_active=@active,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason
            WHEN NOT MATCHED THEN INSERT(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
              VALUES(@identity,@name,@role,@active,SUSER_SNAME(),@reason);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureOwnerAsync(connection, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var provision = new SqlCommand(provisionSql, connection, transaction))
            {
                provision.Parameters.AddWithValue("@identity", windowsIdentity); provision.Parameters.AddWithValue("@role", roleCode); provision.Parameters.AddWithValue("@active", isActive);
                await provision.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var command = new SqlCommand(upsertSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@identity", windowsIdentity); command.Parameters.AddWithValue("@name", displayName);
                command.Parameters.AddWithValue("@role", roleCode); command.Parameters.AddWithValue("@active", isActive); command.Parameters.AddWithValue("@reason", reason);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<IReadOnlyList<ControlledMasterRow>> LoadMasterValuesAsync(string masterType, CancellationToken cancellationToken = default)
    {
        masterType = NormalizeMasterType(masterType);
        await using var connection = await OpenAsync(cancellationToken);
        var rows = new List<ControlledMasterRow>();
        var sql = masterType == "STORE"
            ? "SELECT 'STORE',store_code,store_name,'APPROVED',is_active,modified_utc,modified_by FROM dbo.stores ORDER BY store_code"
            : "SELECT master_type,master_code,display_name,approval_status,is_active,modified_utc,modified_by FROM dbo.controlled_master_values WHERE master_type=@type ORDER BY master_code";
        await using var command = new SqlCommand(sql, connection);
        if (masterType != "STORE") command.Parameters.AddWithValue("@type", masterType);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        return rows;
    }

    public async Task UpsertMasterValueAsync(string masterType, string code, string displayName, string approvalStatus, bool isActive, string reason, CancellationToken cancellationToken = default)
    {
        masterType = NormalizeMasterType(masterType);
        code = Required(code, nameof(code)).ToUpperInvariant();
        displayName = Required(displayName, nameof(displayName));
        approvalStatus = Required(approvalStatus, nameof(approvalStatus)).ToUpperInvariant();
        reason = Required(reason, nameof(reason));
        var maximumCodeLength = masterType == "STORE" ? 30 : 100;
        if (code.Length > maximumCodeLength || displayName.Length > 200 || reason.Length > 500) throw new ArgumentException("The master code, name or reason is too long.");
        if (approvalStatus is not ("OBSERVED" or "APPROVED" or "QUARANTINED")) throw new ArgumentException("Select Observed, Approved or Quarantined.", nameof(approvalStatus));
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureOwnerAsync(connection, cancellationToken);
        var sql = masterType == "STORE"
            ? """
              MERGE dbo.stores WITH(HOLDLOCK) target USING(SELECT CONVERT(varchar(30),@code) store_code) source ON target.store_code=source.store_code
              WHEN MATCHED THEN UPDATE SET store_name=@name,is_active=@active,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason
              WHEN NOT MATCHED THEN INSERT(store_code,store_name,is_active,modified_by,modified_utc,change_reason) VALUES(@code,@name,@active,SUSER_SNAME(),SYSUTCDATETIME(),@reason);
              INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name) VALUES('MasterDataChange','Succeeded',N'Store master changed',N'database',SUSER_SNAME());
              """
            : """
              MERGE dbo.controlled_master_values WITH(HOLDLOCK) target USING(SELECT @type master_type,@code master_code) source
                ON target.master_type=source.master_type AND target.master_code=source.master_code
              WHEN MATCHED THEN UPDATE SET display_name=@name,approval_status=@approval,is_active=@active,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason
              WHEN NOT MATCHED THEN INSERT(master_type,master_code,display_name,approval_status,is_active,modified_by,change_reason)
                VALUES(@type,@code,@name,@approval,@active,SUSER_SNAME(),@reason);
              """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@type", masterType); command.Parameters.AddWithValue("@code", code); command.Parameters.AddWithValue("@name", displayName);
        command.Parameters.AddWithValue("@approval", approvalStatus); command.Parameters.AddWithValue("@active", isActive); command.Parameters.AddWithValue("@reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WatchFolderSettings> LoadWatchFolderSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT inbound_path,processed_path,failed_path,report_output_path,poll_minutes,is_enabled,modified_utc,modified_by FROM dbo.watch_folder_settings WHERE watch_folder_setting_id=1", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Watch-folder settings are missing.");
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetBoolean(5), reader.GetDateTime(6), reader.GetString(7));
    }

    public async Task SaveWatchFolderSettingsAsync(WatchFolderSettings settings, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var paths = AutomationPathPolicy.Validate(settings.InboundPath, settings.ProcessedPath, settings.FailedPath, settings.ReportOutputPath);
        reason = Required(reason, nameof(reason));
        if (settings.PollMinutes is < 1 or > 60 || reason.Length > 500) throw new ArgumentException("Polling must be 1–60 minutes and the reason at most 500 characters.");
        var sql = """
            UPDATE dbo.watch_folder_settings SET inbound_path=@inbound,processed_path=@processed,failed_path=@failed,report_output_path=@output,
              poll_minutes=@poll,is_enabled=@enabled,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason WHERE watch_folder_setting_id=1;
            INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name) VALUES('ConfigurationChange','Succeeded',N'Watch-folder configuration changed',N'database',SUSER_SNAME());
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureOwnerAsync(connection, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@inbound", paths.InboundPath); command.Parameters.AddWithValue("@processed", paths.ProcessedPath);
        command.Parameters.AddWithValue("@failed", paths.FailedPath); command.Parameters.AddWithValue("@output", paths.ReportOutputPath);
        command.Parameters.AddWithValue("@poll", settings.PollMinutes); command.Parameters.AddWithValue("@enabled", settings.IsEnabled); command.Parameters.AddWithValue("@reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReportPackSchedule>> LoadSchedulesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT report_pack_schedule_id,schedule_name,local_run_time,is_enabled,export_excel,export_pdf,last_business_date,last_run_utc,last_status,last_message FROM dbo.report_pack_schedules ORDER BY local_run_time", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ReportPackSchedule>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetInt32(0), reader.GetString(1), TimeOnly.FromTimeSpan(reader.GetTimeSpan(2)), reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6), reader.IsDBNull(7) ? null : reader.GetDateTime(7), Text(reader, 8), Text(reader, 9)));
        return rows;
    }

    public async Task SaveScheduleAsync(int id, TimeOnly localRunTime, bool enabled, bool excel, bool pdf, string reason, CancellationToken cancellationToken = default)
    {
        if (id <= 0 || (!excel && !pdf)) throw new ArgumentException("A schedule and at least one output format are required.");
        reason = Required(reason, nameof(reason));
        var sql = """
            UPDATE dbo.report_pack_schedules SET local_run_time=@time,is_enabled=@enabled,export_excel=@excel,export_pdf=@pdf,
              modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason WHERE report_pack_schedule_id=@id;
            IF @@ROWCOUNT<>1 THROW 51101,'The report-pack schedule was not found.',1;
            INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name) VALUES('ConfigurationChange','Succeeded',N'Report-pack schedule changed',N'database',SUSER_SNAME());
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureOwnerAsync(connection, cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@time", localRunTime.ToTimeSpan()); command.Parameters.AddWithValue("@enabled", enabled);
        command.Parameters.AddWithValue("@excel", excel); command.Parameters.AddWithValue("@pdf", pdf); command.Parameters.AddWithValue("@reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AutomationRunRow>> LoadAutomationRunsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT TOP(@limit) automation_run_id,run_type,source_file_name,store_code,business_date,outcome,safe_message,started_utc,completed_utc,run_by FROM dbo.automation_runs ORDER BY completed_utc DESC,automation_run_id DESC", connection);
        command.Parameters.AddWithValue("@limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<AutomationRunRow>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetInt64(0), reader.GetString(1), Text(reader, 2), Text(reader, 3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
            reader.GetString(5), reader.GetString(6), reader.GetDateTime(7), reader.GetDateTime(8), reader.GetString(9)));
        return rows;
    }

    internal async Task RecordAutomationRunAsync(string type, string? safeFileName, string? store, DateOnly? date, string outcome, string message, DateTime startedUtc, CancellationToken token)
    {
        if (safeFileName?.Length > 260) safeFileName = safeFileName[..260];
        if (message.Length > 500) message = message[..500];
        const string sql = """
            INSERT dbo.automation_runs(run_type,source_file_name,store_code,business_date,outcome,safe_message,started_utc,completed_utc,run_by)
            VALUES(@type,@file,@store,@date,@outcome,@message,@started,SYSUTCDATETIME(),SUSER_SNAME());
            INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name)
            VALUES('AutomationRun',CASE WHEN @outcome='Succeeded' THEN 'Succeeded' WHEN @outcome='Skipped' THEN 'Blocked' ELSE 'Failed' END,N'Unattended operation completed',N'automation',SUSER_SNAME());
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@type", type); command.Parameters.AddWithValue("@file", (object?)safeFileName ?? DBNull.Value);
        command.Parameters.AddWithValue("@store", (object?)store ?? DBNull.Value); command.Parameters.AddWithValue("@date", (object?)date ?? DBNull.Value);
        command.Parameters.AddWithValue("@outcome", outcome); command.Parameters.AddWithValue("@message", message); command.Parameters.AddWithValue("@started", startedUtc);
        await command.ExecuteNonQueryAsync(token);
    }

    internal async Task<DateOnly?> LoadLatestCombinedBusinessDateAsync(CancellationToken token)
    {
        const string sql = """
            SELECT MAX(business_date) FROM
            (SELECT business_date FROM dbo.import_files WHERE report_code='R025' AND is_superseded=0 AND store_code IN('WLMHW','HEMW')
             GROUP BY business_date HAVING COUNT(DISTINCT store_code)=2) x;
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(token);
        return value is null or DBNull ? null : DateOnly.FromDateTime((DateTime)value);
    }

    internal async Task<IReadOnlyList<ReportPackSchedule>> LoadDueSchedulesAsync(DateOnly businessDate, TimeOnly localTime, CancellationToken token)
    {
        const string sql = """
            SELECT report_pack_schedule_id,schedule_name,local_run_time,is_enabled,export_excel,export_pdf,last_business_date,last_run_utc,last_status,last_message
            FROM dbo.report_pack_schedules WITH(UPDLOCK,READPAST)
            WHERE is_enabled=1 AND local_run_time<=@time AND (last_business_date IS NULL OR last_business_date<@date)
            ORDER BY local_run_time;
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@time", localTime.ToTimeSpan()); command.Parameters.AddWithValue("@date", businessDate);
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<ReportPackSchedule>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetInt32(0), reader.GetString(1), TimeOnly.FromTimeSpan(reader.GetTimeSpan(2)), reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6), reader.IsDBNull(7) ? null : reader.GetDateTime(7), Text(reader, 8), Text(reader, 9)));
        return rows;
    }

    internal async Task CompleteScheduleAsync(int id, DateOnly date, string status, string message, CancellationToken token)
    {
        if (message.Length > 500) message = message[..500];
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand("UPDATE dbo.report_pack_schedules SET last_business_date=@date,last_run_utc=SYSUTCDATETIME(),last_status=@status,last_message=@message WHERE report_pack_schedule_id=@id", connection);
        command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@date", date); command.Parameters.AddWithValue("@status", status); command.Parameters.AddWithValue("@message", message);
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<IReadOnlyList<ArchivedReportGeneration>> LoadReportGenerationsAsync(string? storeCode = null, DateOnly? businessDate = null, int limit = 200, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        const string sql = """
            SELECT TOP(@limit) daily_report_generation_id,store_code,business_date,generation_number,content_sha256,document_sha256,generated_utc,generated_by,is_final,supersedes_generation_id,
              CASE WHEN report_document_json IS NULL THEN CONVERT(bit,0) ELSE CONVERT(bit,1) END
            FROM dbo.daily_report_generations
            WHERE (@store IS NULL OR store_code=@store) AND (@date IS NULL OR business_date=@date)
            ORDER BY business_date DESC,store_code,generation_number DESC;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@limit", limit); command.Parameters.AddWithValue("@store", (object?)BlankToNull(storeCode) ?? DBNull.Value); command.Parameters.AddWithValue("@date", (object?)businessDate ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ArchivedReportGeneration>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetFieldValue<DateOnly>(2), reader.GetInt32(3), reader.GetString(4), Text(reader, 5),
            reader.GetDateTime(6), reader.GetString(7), reader.GetBoolean(8), reader.IsDBNull(9) ? null : reader.GetInt64(9), reader.GetBoolean(10)));
        return rows;
    }

    public async Task<ReportPackDocument> LoadArchivedReportAsync(long generationId, CancellationToken cancellationToken = default)
    {
        if (generationId <= 0) throw new ArgumentOutOfRangeException(nameof(generationId));
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT report_document_json,document_sha256 FROM dbo.daily_report_generations WHERE daily_report_generation_id=@id", connection);
        command.Parameters.AddWithValue("@id", generationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("The report generation was not found.");
        if (reader.IsDBNull(0) || reader.IsDBNull(1)) throw new InvalidOperationException("This generation predates full report archival and cannot be re-exported.");
        var json = reader.GetString(0); var expectedHash = reader.GetString(1);
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal)) throw new InvalidDataException("The archived report document failed its SHA-256 integrity check.");
        return ReportPackArchiveCodec.Deserialize(json);
    }

    public async Task<IReadOnlyList<ReportGenerationComparisonRow>> CompareReportGenerationsAsync(long firstId, long secondId, CancellationToken cancellationToken = default)
    {
        if (firstId == secondId) throw new ArgumentException("Select two different report generations.");
        var first = await LoadArchivedReportAsync(firstId, cancellationToken);
        var second = await LoadArchivedReportAsync(secondId, cancellationToken);
        var names = first.Tables.Select(x => x.Name).Union(second.Tables.Select(x => x.Name), StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        return names.Select(name =>
        {
            var a = first.Tables.SingleOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            var b = second.Tables.SingleOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            var aRows = a?.Data.Rows.Count ?? 0; var bRows = b?.Data.Rows.Count ?? 0;
            var aStatus = a?.Status ?? "MISSING"; var bStatus = b?.Status ?? "MISSING";
            var changed = aRows != bRows || !string.Equals(aStatus, bStatus, StringComparison.Ordinal) || !TotalsEqual(a?.Data.Totals, b?.Data.Totals);
            return new ReportGenerationComparisonRow(name, aRows, bRows, aStatus, bStatus, changed);
        }).ToArray();
    }

    public async Task<IReadOnlyList<ManagementTrendRow>> LoadManagementTrendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366) throw new ArgumentException("Select a valid trend period of at most 366 days.");
        var sql = """
            WITH sales AS
            (
              SELECT i.transaction_date,i.store_code,SUM(l.source_net_amount) net_sales,SUM(l.source_quantity) units,COUNT(DISTINCT i.sales_invoice_id) invoices
              FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
              JOIN dbo.source_lineage sl ON sl.source_lineage_id=l.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=sl.import_file_id AND f.is_superseded=0
              WHERE i.transaction_date BETWEEN @from AND @to GROUP BY i.transaction_date,i.store_code
            ), controls AS
            (
              SELECT i.transaction_date,i.store_code,SUM(c.source_net_value) revenue
              FROM dbo.sales_invoice_controls c JOIN dbo.sales_invoices i ON i.sales_invoice_id=c.sales_invoice_id
              JOIN dbo.source_lineage sl ON sl.source_lineage_id=c.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=sl.import_file_id AND f.is_superseded=0
              WHERE i.transaction_date BETWEEN @from AND @to GROUP BY i.transaction_date,i.store_code
            ), tenders AS
            (
              SELECT i.transaction_date,i.store_code,SUM(CASE WHEN t.is_reporting_eligible=1 THEN t.source_amount ELSE 0 END) tender
              FROM dbo.sales_tenders t JOIN dbo.sales_invoices i ON i.sales_invoice_id=t.sales_invoice_id
              JOIN dbo.source_lineage sl ON sl.source_lineage_id=t.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=sl.import_file_id AND f.is_superseded=0
              WHERE i.transaction_date BETWEEN @from AND @to GROUP BY i.transaction_date,i.store_code
            ), unmatched AS
            (
              SELECT transaction_date,store_code,COUNT_BIG(*) unmatched
              FROM dbo.sales_line_enrichments WHERE transaction_date BETWEEN @from AND @to AND match_status<>'Matched' GROUP BY transaction_date,store_code
            )
            SELECT s.transaction_date,s.store_code,s.net_sales,s.units,s.invoices,COALESCE(t.tender,0)-COALESCE(c.revenue,0),CONVERT(int,COALESCE(u.unmatched,0))
            FROM sales s LEFT JOIN controls c ON c.transaction_date=s.transaction_date AND c.store_code=s.store_code
            LEFT JOIN tenders t ON t.transaction_date=s.transaction_date AND t.store_code=s.store_code
            LEFT JOIN unmatched u ON u.transaction_date=s.transaction_date AND u.store_code=s.store_code
            ORDER BY s.transaction_date,s.store_code;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection); command.Parameters.AddWithValue("@from", from); command.Parameters.AddWithValue("@to", to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ManagementTrendRow>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetFieldValue<DateOnly>(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetInt32(4), reader.GetDecimal(5), reader.GetInt32(6)));
        return rows;
    }

    public async Task<IReadOnlyList<DataQualitySummaryRow>> LoadDataQualitySummaryAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT severity,area,code,item_count,latest_utc,message FROM
            (
              SELECT 'FAIL' severity,'Import' area,'FAILED_IMPORT_BATCH' code,COUNT_BIG(*) item_count,MAX(COALESCE(completed_utc,started_utc)) latest_utc,N'Failed import batches require correction or an approved retry.' message FROM dbo.import_batches WHERE status='Failed'
              UNION ALL SELECT 'WARNING','Tender','QUARANTINED_TENDER',COUNT_BIG(*),MAX(b.completed_utc),N'Unapproved tender values remain excluded from reporting controls.' FROM dbo.sales_tenders t JOIN dbo.source_lineage l ON l.source_lineage_id=t.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=l.import_file_id JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id WHERE t.is_reporting_eligible=0 AND f.is_superseded=0
              UNION ALL SELECT 'FAIL','Staff','UNMATCHED_ENRICHMENT',COUNT_BIG(*),MAX(b.completed_utc),N'R003/R013 enrichment rows could not be matched uniquely to canonical sales.' FROM dbo.sales_line_enrichments e JOIN dbo.source_lineage l ON l.source_lineage_id=e.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=l.import_file_id JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id WHERE e.match_status<>'Matched' AND f.is_superseded=0
              UNION ALL SELECT 'WARNING','Workflow','UNFINALISED_DAY',COUNT_BIG(*),MAX(CONVERT(datetime2,business_date)),N'Business dates have been opened but are not finalised.' FROM dbo.daily_reporting_days WHERE status<>'LOCKED'
              UNION ALL SELECT 'INFORMATION','Restatement','RESTATED_SOURCE',COUNT_BIG(*),MAX(requested_utc),N'Controlled restatements are retained with immutable archived facts.' FROM dbo.import_restatements
            ) q WHERE item_count>0 ORDER BY CASE severity WHEN 'FAIL' THEN 1 WHEN 'WARNING' THEN 2 ELSE 3 END,area;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<DataQualitySummaryRow>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetDateTime(4), reader.GetString(5)));
        return rows;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("A SQL Server connection string is required.");
        var connection = new SqlConnection(connectionString);
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task EnsureOwnerAsync(SqlConnection connection, CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.application_users WHERE windows_identity=SUSER_SNAME() AND role_code='OWNER' AND is_active=1", connection);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 1) throw new UnauthorizedAccessException("Owner permission is required for this change.");
    }

    private static bool TotalsEqual(IReadOnlyList<object?>? first, IReadOnlyList<object?>? second) =>
        string.Equals(first is null ? null : string.Join('\u001f', first.Select(x => Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture))),
            second is null ? null : string.Join('\u001f', second.Select(x => Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture))), StringComparison.Ordinal);
    private static ApplicationRole ParseRole(string value) => value switch { "OWNER" => ApplicationRole.Owner, "STORE_MANAGER" => ApplicationRole.StoreManager, "VIEWER" => ApplicationRole.Viewer, _ => ApplicationRole.None };
    private static string NormalizeMasterType(string value) => Required(value, nameof(value)).Trim().Replace(' ', '_').ToUpperInvariant() switch
    {
        "STORE" => "STORE", "BRAND_SEGMENT" => "BRAND_SEGMENT", "INVENTORY_GROUP" => "INVENTORY_GROUP", "TENDER" => "TENDER",
        _ => throw new ArgumentException("Select Store, Brand Segment, Inventory Group or Tender.", nameof(value))
    };
    private static string? Text(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", name) : value.Trim();
}

public static class AutomationPathPolicy
{
    public static WatchFolderSettings Validate(string inbound, string processed, string failed, string output, int pollMinutes = 5, bool enabled = true)
    {
        var values = new[] { Canonical(inbound), Canonical(processed), Canonical(failed), Canonical(output) };
        if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length) throw new ArgumentException("Automation folders must be different locations.");
        if (values.Skip(1).Any(path => IsWithin(path, values[0]))) throw new ArgumentException("Processed, failed and report folders cannot be inside the inbound folder.");
        return new(values[0], values[1], values[2], values[3], pollMinutes, enabled, DateTime.MinValue, string.Empty);
    }

    private static string Canonical(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)) throw new ArgumentException("Automation folders require absolute local paths.");
        var full = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (new Uri(full + Path.DirectorySeparatorChar).IsUnc) throw new ArgumentException("Automation folders must be on a local drive.");
        var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("A drive root cannot be used as an automation folder.");
        if (Directory.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked folders cannot be used for automation.");
        return full;
    }

    private static bool IsWithin(string candidate, string parent) => candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
