using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

/// <summary>
/// Shared plumbing of the two <c>diag</c> stores (1.11.0): connection, stored-procedure commands, the wire formats of
/// docs\design\SELF-DIAGNOSTICS-PROCEDURES.md (UTC instants <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>, dates <c>yyyy-MM-dd</c>)
/// and tolerant column readers (a column may be datetime2, datetimeoffset or date; counts may be int or bigint).
/// </summary>
internal static class DiagStoreSupport
{
    internal const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    internal const string DateFormat = "yyyy-MM-dd";

    internal static string ValidateConnectionString(string connectionString) =>
        SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString));

    internal static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static SqlCommand Procedure(SqlConnection connection, string name) =>
        new(name, connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };

    internal static void AddJson(SqlCommand command, string name, string json) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, -1).Value = json;

    internal static void AddVarChar(SqlCommand command, string name, int size, string? value) =>
        command.Parameters.Add(name, SqlDbType.VarChar, size).Value = (object?)value ?? DBNull.Value;

    internal static void AddNVarChar(SqlCommand command, string name, int size, string? value) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = (object?)value ?? DBNull.Value;

    internal static string Instant(DateTimeOffset value) =>
        value.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture);

    internal static string Date(DateOnly value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>Writes UTF-8 JSON through a writer (compact, no escaping surprises for ASCII tokens).</summary>
    internal static string WriteJson(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static bool IsNull(SqlDataReader reader, string column) => reader.IsDBNull(reader.GetOrdinal(column));

    internal static string Str(SqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? string.Empty : Convert.ToString(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture) ?? string.Empty;

    internal static string? StrOrNull(SqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? null : Convert.ToString(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

    internal static int Int(SqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? 0 : Convert.ToInt32(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

    internal static int? IntOrNull(SqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? null : Convert.ToInt32(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

    internal static long Long(SqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? 0 : Convert.ToInt64(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

    internal static decimal? DecimalOrNull(SqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? null : Convert.ToDecimal(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

    internal static bool Bool(SqlDataReader reader, string column) =>
        !reader.IsDBNull(reader.GetOrdinal(column)) && Convert.ToBoolean(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

    internal static DateTimeOffset Instant(SqlDataReader reader, string column) =>
        InstantOrNull(reader, column) ?? default;

    internal static DateTimeOffset? InstantOrNull(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return null;
        return reader.GetValue(ordinal) switch
        {
            DateTimeOffset offset => offset.ToUniversalTime(),
            DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)),
            var other => DateTimeOffset.Parse(Convert.ToString(other, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
        };
    }

    internal static DateOnly Date(SqlDataReader reader, string column) =>
        DateOrNull(reader, column) ?? default;

    internal static DateOnly? DateOrNull(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return null;
        return reader.GetValue(ordinal) switch
        {
            DateTime time => DateOnly.FromDateTime(time),
            DateTimeOffset offset => DateOnly.FromDateTime(offset.UtcDateTime),
            var other => DateOnly.ParseExact(Convert.ToString(other, CultureInfo.InvariantCulture)![..10], DateFormat, CultureInfo.InvariantCulture)
        };
    }

    internal static TEnum EnumValue<TEnum>(SqlDataReader reader, string column) where TEnum : struct, System.Enum =>
        System.Enum.Parse<TEnum>(Str(reader, column), ignoreCase: true);

    /// <summary>A JSON array of strings (the stored stack); null, blank or malformed gives an empty list.</summary>
    internal static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            return document.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }
}
