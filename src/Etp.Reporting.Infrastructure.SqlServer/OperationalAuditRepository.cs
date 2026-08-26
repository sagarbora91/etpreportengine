using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record OperationalAuditEvent(DateTime EventUtc, string EventType, string Outcome, string? SafeDetail, string ApplicationVersion);

public sealed class OperationalAuditRepository(string connectionString)
{
    private static readonly HashSet<string> EventTypes = new(StringComparer.Ordinal)
        { "ApplicationStart", "ConnectionTest", "ImportBatch", "ReportRun", "ExportExcel", "ExportPdf", "DatabaseSetup", "SupportPackage" };
    private static readonly HashSet<string> Outcomes = new(StringComparer.Ordinal)
        { "Succeeded", "Failed", "Blocked", "Cancelled" };

    public async Task RecordAsync(string eventType, string outcome, string? safeDetail = null, CancellationToken cancellationToken = default)
    {
        if (!EventTypes.Contains(eventType)) throw new ArgumentException("Unknown operational event type.", nameof(eventType));
        if (!Outcomes.Contains(outcome)) throw new ArgumentException("Unknown operational outcome.", nameof(outcome));
        if (safeDetail is { } detail && (detail.Length > 200 || ContainsPathOrIdentifier(detail)))
            throw new ArgumentException("Audit details must be aggregate-only and cannot contain paths or identifiers.", nameof(safeDetail));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version) VALUES(@type,@outcome,@detail,@version)", connection);
        command.Parameters.AddWithValue("@type", eventType); command.Parameters.AddWithValue("@outcome", outcome);
        command.Parameters.AddWithValue("@detail", (object?)safeDetail ?? DBNull.Value);
        command.Parameters.AddWithValue("@version", typeof(OperationalAuditRepository).Assembly.GetName().Version?.ToString(3) ?? "unknown");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OperationalAuditEvent>> LoadRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT TOP(@limit) event_utc,event_type,outcome,safe_detail,application_version FROM dbo.operational_audit ORDER BY event_utc DESC,operational_audit_id DESC", connection);
        command.Parameters.AddWithValue("@limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var rows = new List<OperationalAuditEvent>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetDateTime(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    private static bool ContainsPathOrIdentifier(string value) => value.Contains('\\') || value.Contains('/') || value.Contains(':') || value.Any(char.IsDigit);
}
