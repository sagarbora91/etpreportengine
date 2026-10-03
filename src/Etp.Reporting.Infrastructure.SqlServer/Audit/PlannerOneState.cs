using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Audit;

/// <summary>A current import file of a (store, report), with its content keys (planner 1's previous file).</summary>
public sealed record StoredFile(long Id, string Sha256, DateOnly? Start, DateOnly? End, DateTime Imported, IReadOnlyCollection<string> Keys, int Version);

public sealed record StoredInvoice(long Id, int Year, string Document, DateOnly Date);

/// <summary>A stored sales line; <see cref="Protected"/> is the text <c>persist_sales_line</c> hashes, less the date.</summary>
public sealed record StoredSalesLine(long InvoiceId, string LineIdentifier, string Protected, long Id);

public sealed record StoredControl(long InvoiceId, string Protected, long Id);

public sealed record StoredTender(long InvoiceId, string TenderType, string Protected, long Id);

public sealed record StoredMovement(int Year, string Document, DateOnly Date, string Product, string Type, string From, string To,
    int LineSeq, decimal Opening, decimal Transaction, decimal Closing, long Id);

public sealed record StoredSnapshot(DateOnly Date, string Source, string Product, string Item, int LineSeq, string Protected, long Id);

/// <summary>What the database tells the planner-1 prediction (design 5.3, 6.2): read-only, by lookup.</summary>
public interface IPlannerOneState
{
    /// <summary>Whether identities compare case-sensitively (a CS or BIN database collation).</summary>
    bool CaseSensitive { get; }
    Task<long?> ExactDuplicateAsync(string sha256, string reportCode, string? store, DateOnly? start, DateOnly? end, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredFile>> CurrentFilesAsync(string store, string reportCode, DateOnly start, DateOnly end, CancellationToken cancellationToken);
    Task<IReadOnlyList<DateOnly>> LockedDaysAsync(string store, DateOnly start, DateOnly end, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredInvoice>> InvoicesAsync(string store, IReadOnlyCollection<(int Year, string Document)> keys, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredSalesLine>> SalesLinesAsync(IReadOnlyCollection<long> invoiceIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredControl>> ControlsAsync(IReadOnlyCollection<long> invoiceIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredTender>> TendersAsync(IReadOnlyCollection<long> invoiceIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredMovement>> MovementsAsync(string store, IReadOnlyCollection<(int Year, string Document)> documents, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredSnapshot>> SnapshotsAsync(string store, IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<string>> EnrichmentKeysAsync(string type, string store, IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}

/// <summary>
/// The texts the <c>persist_*</c> procedures hash to tell ALREADY_PRESENT from CONFLICT, built the same way for a stored
/// row and an incoming one: decimals as <c>decimal(19,4)</c> text, a missing text as empty, a missing amount as 0 where
/// the procedure says <c>ISNULL(x,0)</c>. Compared ordinally, as <c>HASHBYTES</c> compares them.
/// </summary>
public static class ProtectedValues
{
    public static string Number(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.0000", CultureInfo.InvariantCulture);

    public static string Number(decimal? value) => Number(value ?? 0m);

    private static string Join(params string?[] parts) => string.Join("|", parts.Select(part => part ?? ""));

    /// <summary><c>persist_sales_line</c>: product|type|qty|gross|net|brand code|brand name|segment|currency (the date is the incoming one on both sides).</summary>
    public static string SalesLine(string product, string? type, decimal quantity, decimal? gross, decimal? net, string? brandCode,
        string? brandName, string? segment, string currency) =>
        Join(product, type, Number(quantity), Number(gross), Number(net), brandCode, brandName, segment, currency);

    /// <summary><c>persist_sales_invoice_control</c>: type|quantity|net value|currency.</summary>
    public static string Control(string? type, decimal quantity, decimal net, string currency) => Join(type, Number(quantity), Number(net), currency);

    /// <summary><c>persist_sales_tender</c>: UPPER(type)|amount|currency|eligible|reason.</summary>
    public static string Tender(string type, decimal amount, string currency, bool eligible, string? reason) =>
        Join(type.ToUpperInvariant(), Number(amount), currency, eligible ? "1" : "0", reason);

    /// <summary><c>persist_stock_movement</c>: opening|transaction|closing.</summary>
    public static string Movement(decimal opening, decimal transaction, decimal closing) => Join(Number(opening), Number(transaction), Number(closing));

    /// <summary><c>persist_stock_snapshot</c>: ean|brand|brand name|cluster|gender|batch|uid|quantity|unit cost|total cost.</summary>
    public static string Snapshot(string? ean, string? brand, string? brandName, string? cluster, string? gender, string? batch, string? uid,
        decimal quantity, decimal? unit, decimal? total) =>
        Join(ean, brand, brandName, cluster, gender, batch, uid, Number(quantity), Number(unit), Number(total));
}

/// <summary>
/// <see cref="IPlannerOneState"/> over a live or scratch database, SELECT-only (design 6.2, 6.4): the constants of
/// <see cref="AuditQueries"/>, identities batched 5,000 to a JSON parameter. On a database before 0041 it reads the
/// stock identities as 0041 will make them (<c>line_seq</c> numbered, snapshot source derived).
/// </summary>
public sealed class SqlPlannerOneState(ReadOnlyAuditConnection connection, SqlSchemaFacts schema) : IPlannerOneState
{
    private const int Chunk = 5000;

    public bool CaseSensitive => schema.CaseSensitive;

    public async Task<long?> ExactDuplicateAsync(string sha256, string reportCode, string? store, DateOnly? start, DateOnly? end, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync(AuditQueries.ExactDuplicate, parameters =>
        {
            parameters.Add("@hash", SqlDbType.Char, 64).Value = sha256.Trim().ToLowerInvariant();
            parameters.Add("@report", SqlDbType.VarChar, 30).Value = reportCode;
            parameters.Add("@store", SqlDbType.VarChar, 30).Value = (object?)store ?? DBNull.Value;
            parameters.Add("@start", SqlDbType.Date).Value = (object?)start ?? DBNull.Value;
            parameters.Add("@end", SqlDbType.Date).Value = (object?)end ?? DBNull.Value;
        }, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : Convert.ToInt64(rows[0][0], CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<StoredFile>> CurrentFilesAsync(string store, string reportCode, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync(AuditQueries.CurrentFiles, parameters =>
        {
            parameters.Add("@store", SqlDbType.VarChar, 30).Value = store;
            parameters.Add("@report", SqlDbType.VarChar, 30).Value = reportCode;
            parameters.Add("@start", SqlDbType.Date).Value = start;
            parameters.Add("@end", SqlDbType.Date).Value = end;
        }, cancellationToken).ConfigureAwait(false);
        var files = new List<(StoredFile File, HashSet<string> Keys)>();
        foreach (var row in rows)
        {
            var id = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
            if (files.Count == 0 || files[^1].File.Id != id)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                files.Add((new StoredFile(id, (string)row[1]!, Date(row[2]), Date(row[3]), (DateTime)row[4]!, keys,
                    Convert.ToInt32(row[6], CultureInfo.InvariantCulture)), keys));
            }
            if (row[5] is string key) files[^1].Keys.Add(key);
        }
        return files.Select(file => file.File).ToArray();
    }

    public async Task<IReadOnlyList<DateOnly>> LockedDaysAsync(string store, DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
        (await connection.QueryAsync(AuditQueries.LockedDays, parameters =>
        {
            parameters.Add("@store", SqlDbType.VarChar, 30).Value = store;
            parameters.Add("@start", SqlDbType.Date).Value = start;
            parameters.Add("@end", SqlDbType.Date).Value = end;
        }, cancellationToken).ConfigureAwait(false)).Select(row => Date(row[0])!.Value).ToArray();

    public async Task<IReadOnlyList<StoredInvoice>> InvoicesAsync(string store, IReadOnlyCollection<(int Year, string Document)> keys, CancellationToken cancellationToken)
    {
        var result = new List<StoredInvoice>();
        foreach (var chunk in keys.Distinct().Chunk(Chunk))
            foreach (var row in await connection.QueryAsync(AuditQueries.Invoices, parameters =>
                     {
                         parameters.Add("@store", SqlDbType.VarChar, 30).Value = store;
                         parameters.Add("@keys", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(chunk.Select(key => new { y = key.Year, d = key.Document }));
                     }, cancellationToken).ConfigureAwait(false))
                result.Add(new(Convert.ToInt64(row[0], CultureInfo.InvariantCulture), Convert.ToInt32(row[1], CultureInfo.InvariantCulture), (string)row[2]!, Date(row[3])!.Value));
        return result;
    }

    public Task<IReadOnlyList<StoredSalesLine>> SalesLinesAsync(IReadOnlyCollection<long> invoiceIds, CancellationToken cancellationToken) =>
        ByInvoiceAsync(AuditQueries.SalesLines, invoiceIds, row => new StoredSalesLine(Convert.ToInt64(row[0], CultureInfo.InvariantCulture), (string)row[1]!,
            ProtectedValues.SalesLine((string)row[2]!, row[3] as string, (decimal)row[4]!, row[5] as decimal?, row[6] as decimal?, row[7] as string,
                row[8] as string, row[9] as string, Convert.ToString(row[10], CultureInfo.InvariantCulture)!), Convert.ToInt64(row[11], CultureInfo.InvariantCulture)), cancellationToken);

    public Task<IReadOnlyList<StoredControl>> ControlsAsync(IReadOnlyCollection<long> invoiceIds, CancellationToken cancellationToken) =>
        ByInvoiceAsync(AuditQueries.InvoiceControls, invoiceIds, row => new StoredControl(Convert.ToInt64(row[0], CultureInfo.InvariantCulture),
            ProtectedValues.Control(row[1] as string, (decimal)row[2]!, (decimal)row[3]!, Convert.ToString(row[4], CultureInfo.InvariantCulture)!),
            Convert.ToInt64(row[5], CultureInfo.InvariantCulture)), cancellationToken);

    public Task<IReadOnlyList<StoredTender>> TendersAsync(IReadOnlyCollection<long> invoiceIds, CancellationToken cancellationToken) =>
        ByInvoiceAsync(AuditQueries.Tenders, invoiceIds, row => new StoredTender(Convert.ToInt64(row[0], CultureInfo.InvariantCulture), (string)row[1]!,
            ProtectedValues.Tender((string)row[1]!, (decimal)row[2]!, Convert.ToString(row[3], CultureInfo.InvariantCulture)!, Convert.ToBoolean(row[4], CultureInfo.InvariantCulture), row[5] as string),
            Convert.ToInt64(row[6], CultureInfo.InvariantCulture)), cancellationToken);

    public async Task<IReadOnlyList<StoredMovement>> MovementsAsync(string store, IReadOnlyCollection<(int Year, string Document)> documents, CancellationToken cancellationToken)
    {
        var sql = schema.MovementLine ? AuditQueries.Movements : AuditQueries.MovementsBefore0041;
        var result = new List<StoredMovement>();
        foreach (var chunk in documents.Distinct().Chunk(Chunk))
            foreach (var row in await connection.QueryAsync(sql, parameters =>
                     {
                         parameters.Add("@store", SqlDbType.VarChar, 30).Value = store;
                         parameters.Add("@keys", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(chunk.Select(key => new { y = key.Year, d = key.Document }));
                     }, cancellationToken).ConfigureAwait(false))
                result.Add(new(Convert.ToInt32(row[0], CultureInfo.InvariantCulture), (string)row[1]!, Date(row[2])!.Value, (string)row[3]!, (string)row[4]!,
                    (string)row[5]!, (string)row[6]!, Convert.ToInt32(row[7], CultureInfo.InvariantCulture), (decimal)row[8]!, (decimal)row[9]!, (decimal)row[10]!,
                    Convert.ToInt64(row[11], CultureInfo.InvariantCulture)));
        return result;
    }

    public async Task<IReadOnlyList<StoredSnapshot>> SnapshotsAsync(string store, IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken)
    {
        var sql = schema.SnapshotLine ? AuditQueries.Snapshots : AuditQueries.SnapshotsBefore0041;
        var result = new List<StoredSnapshot>();
        foreach (var chunk in dates.Distinct().Chunk(Chunk))
            foreach (var row in await connection.QueryAsync(sql, parameters =>
                     {
                         parameters.Add("@store", SqlDbType.VarChar, 30).Value = store;
                         parameters.Add("@dates", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(chunk.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                     }, cancellationToken).ConfigureAwait(false))
                result.Add(new(Date(row[0])!.Value, (string)row[1]!, (string)row[2]!, (string)row[3]!, Convert.ToInt32(row[4], CultureInfo.InvariantCulture),
                    ProtectedValues.Snapshot(row[5] as string, row[6] as string, row[7] as string, row[8] as string, row[9] as string, row[10] as string,
                        row[11] as string, (decimal)row[12]!, row[13] as decimal?, row[14] as decimal?), Convert.ToInt64(row[15], CultureInfo.InvariantCulture)));
        return result;
    }

    public async Task<IReadOnlyCollection<string>> EnrichmentKeysAsync(string type, string store, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in keys.Distinct(StringComparer.Ordinal).Chunk(Chunk))
            foreach (var row in await connection.QueryAsync(AuditQueries.EnrichmentKeys, parameters =>
                     {
                         parameters.Add("@type", SqlDbType.VarChar, 10).Value = type;
                         parameters.Add("@store", SqlDbType.VarChar, 30).Value = store;
                         parameters.Add("@keys", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(chunk);
                     }, cancellationToken).ConfigureAwait(false))
                result.Add((string)row[0]!);
        return result;
    }

    private async Task<IReadOnlyList<T>> ByInvoiceAsync<T>(string sql, IReadOnlyCollection<long> invoiceIds, Func<object?[], T> map, CancellationToken cancellationToken)
    {
        var result = new List<T>();
        foreach (var chunk in invoiceIds.Where(id => id > 0).Distinct().Chunk(Chunk))
            foreach (var row in await connection.QueryAsync(sql, parameters =>
                         parameters.Add("@ids", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(chunk), cancellationToken).ConfigureAwait(false))
                result.Add(map(row));
        return result;
    }

    private static DateOnly? Date(object? value) => value switch
    {
        DateOnly date => date,
        DateTime time => DateOnly.FromDateTime(time),
        _ => null
    };
}

/// <summary>What the schema probe found (design 5.2, 6.2).</summary>
public sealed record SqlSchemaFacts(string? LatestMigration, bool Supported, bool MovementLine, bool SnapshotLine, bool CaseSensitive, bool LoginCouldWrite)
{
    /// <summary>The database predates 0041's stock identities, so they are emulated.</summary>
    public bool Emulated0041 => !MovementLine || !SnapshotLine;

    public static async Task<SqlSchemaFacts> ReadAsync(ReadOnlyAuditConnection connection, CancellationToken cancellationToken)
    {
        var row = (await connection.QueryAsync(AuditQueries.Schema, cancellationToken: cancellationToken).ConfigureAwait(false)).Single();
        static bool Flag(object? value) => value is not null && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
        return new(row[0] as string, Flag(row[1]), Flag(row[2]), Flag(row[3]), Flag(row[4]), Flag(row[5]));
    }
}

/// <summary>The read-consistency mark (design 6.4.3).</summary>
public sealed record SqlStateMark(long Batches, long OpenBatches, long? FileHighWater)
{
    public static async Task<SqlStateMark> ReadAsync(ReadOnlyAuditConnection connection, CancellationToken cancellationToken)
    {
        var row = (await connection.QueryAsync(AuditQueries.StateMark, cancellationToken: cancellationToken).ConfigureAwait(false)).Single();
        return new(Convert.ToInt64(row[0], CultureInfo.InvariantCulture), Convert.ToInt64(row[1], CultureInfo.InvariantCulture),
            row[2] is null ? null : Convert.ToInt64(row[2], CultureInfo.InvariantCulture));
    }
}
