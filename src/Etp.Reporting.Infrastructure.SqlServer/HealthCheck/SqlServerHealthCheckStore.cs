using System.Text.Json;
using Etp.Reporting.Application.HealthCheck;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

/// <summary>
/// 1.11.0 health check persistence over the <c>diag.*</c> procedures of migration 0055 (see
/// docs\design\SELF-DIAGNOSTICS-PROCEDURES.md). JSON goes in with camelCase names, result sets come back with
/// snake_case columns. Owner-only rules are enforced by the procedures (THROW 51822 and others surface as
/// <see cref="SqlException"/>).
/// </summary>
public sealed class SqlServerHealthCheckStore : IHealthCheckStore
{
    /// <summary>The procedure rejects more than 80 results (51821).</summary>
    internal const int MaxResults = 80;

    private readonly string connectionString;

    public SqlServerHealthCheckStore(string connectionString) =>
        this.connectionString = DiagStoreSupport.ValidateConnectionString(connectionString);

    public async Task<long> RecordRunAsync(HealthCheckRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.record_health_check_run");
        DiagStoreSupport.AddJson(command, "@run", BuildRunJson(run));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("diag.record_health_check_run returned no run id.");
        return DiagStoreSupport.Long(reader, "health_check_run_id");
    }

    public async Task<IReadOnlyList<HealthCheckRunSummary>> LoadRunsAsync(int top = 30, HealthCheckTrigger? trigger = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.load_health_check_runs");
        command.Parameters.Add("@top", System.Data.SqlDbType.Int).Value = top;
        DiagStoreSupport.AddVarChar(command, "@trigger", 8, trigger?.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var runs = new List<HealthCheckRunSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            runs.Add(new(
                DiagStoreSupport.Long(reader, "health_check_run_id"),
                DiagStoreSupport.EnumValue<HealthCheckTrigger>(reader, "trigger_kind"),
                DiagStoreSupport.Date(reader, "business_date"),
                DiagStoreSupport.Instant(reader, "started_utc"),
                DiagStoreSupport.Instant(reader, "completed_utc"),
                DiagStoreSupport.Int(reader, "duration_ms"),
                DiagStoreSupport.Str(reader, "app_version"),
                DiagStoreSupport.Str(reader, "machine_name"),
                DiagStoreSupport.Str(reader, "run_by_role"),
                DiagStoreSupport.EnumValue<HealthOutcome>(reader, "overall"),
                ReadCounts(reader),
                DiagStoreSupport.StrOrNull(reader, "export_file_name")));
        }
        return runs;
    }

    public async Task<IReadOnlyList<HealthCheckResult>> LoadResultsAsync(long healthCheckRunId, CancellationToken cancellationToken = default)
    {
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.load_health_check_results");
        command.Parameters.Add("@health_check_run_id", System.Data.SqlDbType.BigInt).Value = healthCheckRunId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<HealthCheckResult>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new(
                DiagStoreSupport.Str(reader, "check_id"),
                DiagStoreSupport.EnumValue<HealthCheckArea>(reader, "area"),
                DiagStoreSupport.EnumValue<HealthOutcome>(reader, "outcome"),
                DiagStoreSupport.Str(reader, "finding_code"),
                DiagStoreSupport.Str(reader, "explanation"),
                DiagStoreSupport.StrOrNull(reader, "fix_task_id"),
                TimeSpan.FromMilliseconds(DiagStoreSupport.Int(reader, "duration_ms")),
                DiagStoreSupport.DecimalOrNull(reader, "metric_value"),
                ParseDetail(DiagStoreSupport.StrOrNull(reader, "detail_json"))));
        }
        return results;
    }

    public async Task<HealthCheckNightlySummary?> LoadLatestNightlySummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.load_health_check_summary");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new(
            DiagStoreSupport.Date(reader, "business_date"),
            DiagStoreSupport.Instant(reader, "completed_utc"),
            DiagStoreSupport.EnumValue<HealthOutcome>(reader, "overall"),
            ReadCounts(reader));
    }

    public async Task<IReadOnlyList<HealthCheckKnownEntry>> LoadKnownAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.load_health_check_known");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var known = new List<HealthCheckKnownEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            known.Add(new(
                DiagStoreSupport.Str(reader, "check_id"),
                DiagStoreSupport.Str(reader, "finding_code"),
                DiagStoreSupport.Date(reader, "until_date"),
                DiagStoreSupport.Str(reader, "reason"),
                DiagStoreSupport.Str(reader, "set_by"),
                DiagStoreSupport.Instant(reader, "set_utc")));
        }
        return known;
    }

    public async Task SetKnownAsync(string checkId, string findingCode, DateOnly untilDate, string reason, CancellationToken cancellationToken = default)
    {
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.set_health_check_known");
        DiagStoreSupport.AddVarChar(command, "@check_id", 10, checkId);
        DiagStoreSupport.AddVarChar(command, "@finding_code", 60, findingCode);
        command.Parameters.Add("@until", System.Data.SqlDbType.Date).Value = untilDate.ToDateTime(TimeOnly.MinValue);
        DiagStoreSupport.AddNVarChar(command, "@reason", 200, reason);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearKnownAsync(string checkId, string findingCode, CancellationToken cancellationToken = default)
    {
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.clear_health_check_known");
        DiagStoreSupport.AddVarChar(command, "@check_id", 10, checkId);
        DiagStoreSupport.AddVarChar(command, "@finding_code", 60, findingCode);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RolePermissionSnapshot> LoadRolePermissionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.load_role_permissions");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var permissions = new List<RolePermission>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            permissions.Add(new(
                DiagStoreSupport.Str(reader, "role_name"),
                DiagStoreSupport.Str(reader, "class_desc"),
                DiagStoreSupport.StrOrNull(reader, "schema_name"),
                DiagStoreSupport.StrOrNull(reader, "object_name"),
                DiagStoreSupport.Str(reader, "permission_name"),
                DiagStoreSupport.Str(reader, "state_desc")));
        }

        var memberships = new List<RoleMembership>();
        if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                memberships.Add(new(
                    DiagStoreSupport.Str(reader, "role_name"),
                    DiagStoreSupport.StrOrNull(reader, "member_name"),
                    DiagStoreSupport.Bool(reader, "is_active_application_user"),
                    DiagStoreSupport.StrOrNull(reader, "application_role_code"),
                    DiagStoreSupport.Int(reader, "member_count")));
            }
        }

        var owners = new List<string>();
        var ownersWithoutGrantOption = 0;
        if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = DiagStoreSupport.StrOrNull(reader, "member_name");
                if (name is not null) owners.Add(name);
                ownersWithoutGrantOption = Math.Max(ownersWithoutGrantOption, DiagStoreSupport.Int(reader, "owners_without_grant_option"));
            }
        }
        return new(permissions, memberships, owners, ownersWithoutGrantOption);
    }

    /// <summary>The <c>@run</c> document of <c>diag.record_health_check_run</c> (property names camelCase, design 6.2).</summary>
    internal static string BuildRunJson(HealthCheckRun run)
    {
        if (run.Results.Count > MaxResults)
            throw new ArgumentException($"A health check run holds at most {MaxResults} results.", nameof(run));
        return DiagStoreSupport.WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("trigger", run.Trigger.ToString());
            writer.WriteString("businessDate", DiagStoreSupport.Date(run.BusinessDate));
            writer.WriteString("startedUtc", DiagStoreSupport.Instant(run.StartedUtc));
            writer.WriteString("completedUtc", DiagStoreSupport.Instant(run.CompletedUtc));
            writer.WriteNumber("durationMs", (long)Math.Max(0, (run.CompletedUtc - run.StartedUtc).TotalMilliseconds));
            writer.WriteString("appVersion", run.AppVersion);
            writer.WriteString("machineName", HealthMachineName.ToToken(run.MachineName));
            writer.WriteString("runByRole", run.RunByRole);
            writer.WriteString("overall", run.Overall.ToString());
            writer.WriteStartObject("counts");
            writer.WriteNumber("pass", run.Counts.Pass);
            writer.WriteNumber("warning", run.Counts.Warning);
            writer.WriteNumber("fail", run.Counts.Fail);
            writer.WriteNumber("known", run.Counts.Known);
            writer.WriteNumber("skipped", run.Counts.Skipped);
            writer.WriteEndObject();
            DiagStoreSupport.WriteNullableString(writer, "exportFileName", run.ExportFileName);
            writer.WriteStartArray("results");
            foreach (var result in run.Results) WriteResult(writer, result);
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private static void WriteResult(Utf8JsonWriter writer, HealthCheckResult result)
    {
        writer.WriteStartObject();
        writer.WriteString("checkId", result.CheckId);
        writer.WriteString("area", result.Area.ToString());
        writer.WriteString("outcome", result.Outcome.ToString());
        writer.WriteString("findingCode", result.FindingCode);
        writer.WriteString("explanation", result.Explanation);
        DiagStoreSupport.WriteNullableString(writer, "fixTaskId", result.FixTaskId);
        writer.WriteNumber("durationMs", (long)Math.Max(0, result.Duration.TotalMilliseconds));
        WriteNullableNumber(writer, "metricValue", result.MetricValue);
        if (result.SubResults is { Count: > 0 } subs)
        {
            writer.WriteStartArray("detail");
            foreach (var sub in subs)
            {
                writer.WriteStartObject();
                writer.WriteString("key", sub.Key);
                writer.WriteString("outcome", sub.Outcome.ToString());
                DiagStoreSupport.WriteNullableString(writer, "findingCode", sub.FindingCode);
                WriteNullableNumber(writer, "metricValue", sub.MetricValue);
                if (sub.Fields is { Count: > 0 } fields)
                {
                    writer.WriteStartObject("fields");
                    foreach (var field in fields) writer.WriteString(field.Key, field.Value);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, decimal? value)
    {
        if (value is { } number) writer.WriteNumber(name, number);
        else writer.WriteNull(name);
    }

    /// <summary>Reads <c>detail_json</c> back into sub-results; null, blank or malformed gives null.</summary>
    internal static IReadOnlyList<HealthCheckSubResult>? ParseDetail(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var subs = new List<HealthCheckSubResult>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                var key = element.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : string.Empty;
                var outcome = element.TryGetProperty("outcome", out var o) && o.ValueKind == JsonValueKind.String
                    && Enum.TryParse<HealthOutcome>(o.GetString(), true, out var parsed) ? parsed : HealthOutcome.Skipped;
                var finding = element.TryGetProperty("findingCode", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
                decimal? metric = element.TryGetProperty("metricValue", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetDecimal(out var number) ? number : null;
                Dictionary<string, string>? fields = null;
                if (element.TryGetProperty("fields", out var fieldsElement) && fieldsElement.ValueKind == JsonValueKind.Object)
                {
                    fields = [];
                    foreach (var property in fieldsElement.EnumerateObject())
                        fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString() ?? string.Empty
                            : property.Value.GetRawText();
                }
                subs.Add(new(key, outcome, finding, metric, fields));
            }
            return subs.Count == 0 ? null : subs;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HealthCheckCounts ReadCounts(SqlDataReader reader) => new(
        DiagStoreSupport.Int(reader, "pass_count"),
        DiagStoreSupport.Int(reader, "warning_count"),
        DiagStoreSupport.Int(reader, "fail_count"),
        DiagStoreSupport.Int(reader, "known_count"),
        DiagStoreSupport.Int(reader, "skipped_count"));
}
