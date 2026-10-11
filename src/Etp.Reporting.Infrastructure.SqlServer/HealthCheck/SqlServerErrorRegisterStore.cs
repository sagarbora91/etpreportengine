using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.ErrorRegister;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

/// <summary>
/// 1.11.0 error register over the <c>diag.*</c> procedures of migration 0054 (see
/// docs\design\SELF-DIAGNOSTICS-PROCEDURES.md). JSON goes in with camelCase names, result sets come back with
/// snake_case columns. Role rules (Owner only, ...) are enforced by the procedures; their THROWs surface as
/// <see cref="SqlException"/> for the desktop's friendly-error mapping.
/// </summary>
public sealed class SqlServerErrorRegisterStore : IErrorRegisterStore
{
    /// <summary>The procedure accepts at most 500 items per call (51800).</summary>
    internal const int MaxIngestBatch = 500;

    private readonly string connectionString;

    public SqlServerErrorRegisterStore(string connectionString) =>
        this.connectionString = DiagStoreSupport.ValidateConnectionString(connectionString);

    public async Task<ErrorIngestResult> IngestAsync(IReadOnlyList<ErrorOccurrence> items, string appVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        if (items.Count == 0) return new(0, 0, 0, 0, 0);

        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        int accepted = 0, duplicates = 0, rejected = 0, newEntries = 0, recurred = 0;
        for (var offset = 0; offset < items.Count; offset += MaxIngestBatch)
        {
            var batch = items.Skip(offset).Take(MaxIngestBatch).ToList();
            await using var command = DiagStoreSupport.Procedure(connection, "diag.ingest_error_occurrences");
            DiagStoreSupport.AddJson(command, "@items", BuildIngestJson(batch));
            DiagStoreSupport.AddVarChar(command, "@app_version", 20, appVersion);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) continue;
            accepted += DiagStoreSupport.Int(reader, "accepted");
            duplicates += DiagStoreSupport.Int(reader, "duplicates");
            rejected += DiagStoreSupport.Int(reader, "rejected");
            newEntries += DiagStoreSupport.Int(reader, "new_entries");
            recurred += DiagStoreSupport.Int(reader, "recurred_entries");
        }
        return new(accepted, duplicates, rejected, newEntries, recurred);
    }

    public async Task<IReadOnlyList<ErrorRegisterEntry>> LoadRegisterAsync(ErrorStatus? status = null, string? reference = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.load_error_register");
        DiagStoreSupport.AddVarChar(command, "@status", 10, status?.ToString());
        command.Parameters.Add("@reference", System.Data.SqlDbType.Char, 12).Value =
            string.IsNullOrWhiteSpace(reference) ? DBNull.Value : reference.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<ErrorRegisterEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) entries.Add(ReadEntry(reader));
        return entries;
    }

    public async Task<ErrorRegisterEntryDetail?> LoadEntryAsync(int errorRegisterId, CancellationToken cancellationToken = default)
    {
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.load_error_entry");
        command.Parameters.Add("@error_register_id", System.Data.SqlDbType.Int).Value = errorRegisterId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        ErrorRegisterEntry? entry = null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) entry = ReadEntry(reader);
        if (entry is null) return null;

        var versions = new List<ErrorRegisterVersionCount>();
        if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                versions.Add(new(
                    DiagStoreSupport.Str(reader, "app_version"),
                    DiagStoreSupport.Long(reader, "occurrence_count"),
                    DiagStoreSupport.Instant(reader, "first_seen_utc"),
                    DiagStoreSupport.Instant(reader, "last_seen_utc")));
            }
        }

        var occurrences = new List<ErrorOccurrenceRecord>();
        if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                occurrences.Add(new(
                    DiagStoreSupport.Long(reader, "error_occurrence_id"),
                    DiagStoreSupport.Instant(reader, "occurred_utc"),
                    DiagStoreSupport.Str(reader, "reference"),
                    DiagStoreSupport.Str(reader, "app_version"),
                    DiagStoreSupport.EnumValue<ErrorRegisterSeverity>(reader, "severity"),
                    DiagStoreSupport.StrOrNull(reader, "report_code"),
                    DiagStoreSupport.StrOrNull(reader, "store_code"),
                    DiagStoreSupport.DateOrNull(reader, "business_date"),
                    DiagStoreSupport.DateOrNull(reader, "date_from"),
                    DiagStoreSupport.StrOrNull(reader, "operation"),
                    DiagStoreSupport.Str(reader, "reporter_role"),
                    DiagStoreSupport.Instant(reader, "ingested_utc")));
            }
        }
        return new(entry, versions, occurrences);
    }

    public async Task SetStatusAsync(int errorRegisterId, ErrorStatus status, string? fixedInVersion, string? ticket, string? reason, CancellationToken cancellationToken = default)
    {
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.set_error_status");
        command.Parameters.Add("@error_register_id", System.Data.SqlDbType.Int).Value = errorRegisterId;
        DiagStoreSupport.AddVarChar(command, "@status", 10, status.ToString());
        DiagStoreSupport.AddVarChar(command, "@fixed_in_version", 20, Blank(fixedInVersion));
        DiagStoreSupport.AddVarChar(command, "@ticket", 12, Blank(ticket));
        DiagStoreSupport.AddNVarChar(command, "@reason", 200, Blank(reason));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MergeAsync(int fromId, int intoId, string reason, CancellationToken cancellationToken = default)
    {
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.merge_error_entries");
        command.Parameters.Add("@from_id", System.Data.SqlDbType.Int).Value = fromId;
        command.Parameters.Add("@into_id", System.Data.SqlDbType.Int).Value = intoId;
        DiagStoreSupport.AddNVarChar(command, "@reason", 200, reason);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ErrorFixManifestResult> ApplyFixManifestAsync(string manifestJson, string runningVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(runningVersion);
        await using var connection = await DiagStoreSupport.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
        await using var command = DiagStoreSupport.Procedure(connection, "diag.apply_error_fix_manifest");
        DiagStoreSupport.AddJson(command, "@manifest", manifestJson);
        DiagStoreSupport.AddVarChar(command, "@running_version", 20, runningVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new(0, 0, 0, 0, 0);
        return new(
            DiagStoreSupport.Int(reader, "lines"),
            DiagStoreSupport.Int(reader, "entries_marked_fixed"),
            DiagStoreSupport.Int(reader, "already_fixed"),
            DiagStoreSupport.Int(reader, "not_found"),
            DiagStoreSupport.Int(reader, "skipped"));
    }

    /// <summary>The <c>@items</c> array of <c>diag.ingest_error_occurrences</c> (property names camelCase, design 6.1).</summary>
    internal static string BuildIngestJson(IReadOnlyList<ErrorOccurrence> items) =>
        DiagStoreSupport.WriteJson(writer =>
        {
            writer.WriteStartArray();
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("fingerprint", item.Fingerprint);
                writer.WriteNumber("fingerprintVersion", item.FingerprintVersion);
                writer.WriteString("eventBase", item.Parts.EventBase);
                writer.WriteString("subCode", item.Parts.SubCode ?? string.Empty);
                writer.WriteString("source", item.Parts.Source);
                writer.WriteString("site", item.Parts.Site ?? string.Empty);
                writer.WriteString("exceptionType", item.Parts.ExceptionType);
                writer.WriteNumber("sqlNumber", item.Parts.SqlNumber);
                writer.WriteString("timestampUtc", DiagStoreSupport.Instant(item.TimestampUtc));
                writer.WriteString("severity", item.Severity.ToString());
                writer.WriteString("reference", item.Reference);
                writer.WriteString("applicationVersion", item.ApplicationVersion);
                DiagStoreSupport.WriteNullableString(writer, "reportCode", item.ReportCode);
                DiagStoreSupport.WriteNullableString(writer, "storeCode", item.StoreCode);
                DiagStoreSupport.WriteNullableString(writer, "businessDate", item.BusinessDate is { } d ? DiagStoreSupport.Date(d) : null);
                DiagStoreSupport.WriteNullableString(writer, "dateFrom", item.DateFrom is { } f ? DiagStoreSupport.Date(f) : null);
                DiagStoreSupport.WriteNullableString(writer, "operation", item.Operation);
                writer.WriteStartArray("stack");
                foreach (var frame in item.Stack) writer.WriteStringValue(frame);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        });

    internal static ErrorRegisterEntry ReadEntry(SqlDataReader reader) => new(
        DiagStoreSupport.Int(reader, "error_register_id"),
        DiagStoreSupport.Str(reader, "fingerprint"),
        DiagStoreSupport.Int(reader, "fingerprint_version"),
        DiagStoreSupport.Str(reader, "event_code"),
        DiagStoreSupport.StrOrNull(reader, "sub_code"),
        DiagStoreSupport.Str(reader, "source"),
        DiagStoreSupport.StrOrNull(reader, "site"),
        DiagStoreSupport.Str(reader, "exception_type"),
        DiagStoreSupport.Int(reader, "sql_number"),
        DiagStoreSupport.EnumValue<ErrorRegisterSeverity>(reader, "max_severity"),
        DiagStoreSupport.EnumValue<ErrorStatus>(reader, "status"),
        DiagStoreSupport.Instant(reader, "first_seen_utc"),
        DiagStoreSupport.Instant(reader, "last_seen_utc"),
        DiagStoreSupport.Long(reader, "occurrence_count"),
        DiagStoreSupport.Str(reader, "first_version"),
        DiagStoreSupport.Str(reader, "last_version"),
        DiagStoreSupport.StrOrNull(reader, "fixed_in_version"),
        DiagStoreSupport.StrOrNull(reader, "fixed_ticket"),
        DiagStoreSupport.InstantOrNull(reader, "fixed_utc"),
        DiagStoreSupport.InstantOrNull(reader, "recurred_utc"),
        DiagStoreSupport.StrOrNull(reader, "recurred_version"),
        DiagStoreSupport.ParseStringArray(DiagStoreSupport.StrOrNull(reader, "latest_stack")),
        DiagStoreSupport.StrOrNull(reader, "status_reason"),
        DiagStoreSupport.Instant(reader, "status_changed_utc"),
        DiagStoreSupport.Str(reader, "status_changed_by"),
        DiagStoreSupport.IntOrNull(reader, "merged_into_id"));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
