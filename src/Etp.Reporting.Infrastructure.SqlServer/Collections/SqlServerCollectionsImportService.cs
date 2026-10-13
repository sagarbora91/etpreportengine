using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Etp.Reporting.Application.Collections;
using Etp.Reporting.Import.Collections;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;
using static Etp.Reporting.Infrastructure.SqlServer.Collections.CollectionsAccess;

namespace Etp.Reporting.Infrastructure.SqlServer.Collections;

/// <summary>
/// Import > Bank and settlement files (master plan 8a tasks 3-4). The file is read with ETP's CSV / XLSX readers, matched to
/// the Owner's layouts by header signature, read line by line with the chosen layout (identities computed here), kept under
/// <c>&lt;collections evidence root&gt;\BankSources|SettlementSources\&lt;account or provider key&gt;\yyyy\MM\&lt;sha256&gt;.ext</c>
/// and passed to dbo.collections_import_statement, which alone writes the database (idempotent re-import, supersede,
/// "possible duplicate lines"). Owner and Store Manager. Nothing claims a live connector: a provider without an Owner-made
/// layout simply has no layout to match.
/// </summary>
public sealed class SqlServerCollectionsImportService : ICollectionsImportService
{
    private readonly ICollectionsSession session;

    public SqlServerCollectionsImportService(IEtpConnectionFactory connections) : this(new SqlCollectionsSession(connections)) { }

    internal SqlServerCollectionsImportService(ICollectionsSession session) => this.session = session ?? throw new ArgumentNullException(nameof(session));

    private sealed record ActiveLayout(int Id, string Key, string Name, string Kind, string ScopeKey, string ScopeName, string Signature,
        string? SheetName, int HeaderRow, int SkipFooterRows, string MappingJson);

    public async Task<CollectionsFilePreview> PreviewAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await RequireAsync(session, CanImport, ImportAudit, ImportRequired, cancellationToken);
        var snapshot = await ReadFileAsync(filePath, cancellationToken);
        var layouts = await LoadLayoutsAsync(null, cancellationToken);
        var matched = layouts.Where(layout => Matches(snapshot, layout))
            .Select(layout => new MatchedLayout(layout.Id, layout.Key, layout.Name, layout.Kind, layout.ScopeKey, layout.ScopeName))
            .ToList();
        var problem = matched.Count == 0
            ? "This file does not match any file layout. Ask the owner to add one in Settings > Collections setup."
            : null;
        return new(snapshot.FileName, snapshot.Sha256, matched, problem);
    }

    public async Task<CollectionsImportResult> ImportAsync(CollectionsImportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var choice = request.OverlapChoice switch
        {
            CollectionsOverlapChoice.Stop => "STOP",
            CollectionsOverlapChoice.ImportAsNew => "NEW",
            CollectionsOverlapChoice.Restate => "RESTATE",
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unknown choice.")
        };
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (choice != "STOP" && reason is null) throw new ArgumentException("Give a reason for importing lines that look like ones already imported.");
        if (reason is { Length: > 500 }) throw new ArgumentException("Keep the reason to 500 characters.");
        await RequireAsync(session, CanImport, ImportAudit, ImportRequired, cancellationToken);

        var layout = (await LoadLayoutsAsync(request.LayoutId, cancellationToken)).SingleOrDefault()
            ?? throw new InvalidOperationException("This file layout, its bank account or its provider is not set up or is switched off. Ask the owner to check Collections setup.");
        var snapshot = await ReadFileAsync(request.FilePath, cancellationToken);
        if (!Matches(snapshot, layout))
            throw new InvalidOperationException("This file's header row does not match the chosen layout. Choose another layout or ask the owner to check it.");
        StatementLayout map;
        try { map = StatementLayout.Parse(layout.MappingJson, layout.Kind == "BANK" ? StatementLayoutKind.Bank : StatementLayoutKind.Provider); }
        catch (StatementLayoutException exception) { throw new InvalidOperationException("The layout cannot be used: " + exception.Message, exception); }
        var read = StatementLayoutReader.Read(snapshot, map, layout.SheetName, layout.HeaderRow, layout.SkipFooterRows, layout.ScopeKey);
        if (read.Problems.Count > 0) throw new InvalidOperationException(string.Join(" ", read.Problems));
        if (read.ReadableLines == 0)
            throw new InvalidOperationException("No line of this file could be read with the layout. Ask the owner to check it with Test on this file.");

        await KeepFileAsync(request.FilePath, snapshot, layout, cancellationToken);

        await using var connection = await session.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("EXEC dbo.collections_import_statement @layout,@name,@sha,@size,@sheet,@lines,@choice,@reason;", connection)
        {
            CommandTimeout = 300
        };
        Add(command, "@layout", SqlDbType.Int, layout.Id);
        Add(command, "@name", SqlDbType.NVarChar, MaskedFileName(Path.GetFileName(snapshot.FileName)), 260);
        Add(command, "@sha", SqlDbType.Char, snapshot.Sha256, 64);
        Add(command, "@size", SqlDbType.BigInt, snapshot.FileSizeBytes);
        Add(command, "@sheet", SqlDbType.NVarChar, read.Header!.SheetName.Length > 128 ? read.Header.SheetName[..128] : read.Header.SheetName, 128);
        Add(command, "@lines", SqlDbType.NVarChar, LinesJson(read.Lines), -1);
        Add(command, "@choice", SqlDbType.VarChar, choice, 10);
        Add(command, "@reason", SqlDbType.NVarChar, reason, 500);
        CollectionsImportResult result;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("The database gave no import result.");
            var outcome = reader.GetString(0) switch
            {
                "IMPORTED" => CollectionsImportOutcome.Imported,
                "ALREADY_IMPORTED" => CollectionsImportOutcome.AlreadyImported,
                "DUPLICATE_CONTENT" => CollectionsImportOutcome.DuplicateContent,
                "POSSIBLE_DUPLICATES" => CollectionsImportOutcome.PossibleDuplicates,
                var other => throw new InvalidOperationException($"Unknown import result {other}.")
            };
            var fileId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
            int Count(int ordinal) => reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
            result = new(outcome, fileId, Count(2), Count(3), Count(4), Count(5), Count(6), string.Empty);
        }
        catch (SqlException exception) when (Plain(exception) is { } message)
        {
            throw new InvalidOperationException(message, exception);
        }
        if (result.Outcome != CollectionsImportOutcome.Imported)
            await session.RecordAuditAsync(ImportAudit, "Cancelled", "1 files skipped", cancellationToken);
        return result with { Message = Describe(result) };
    }

    /// <summary>The sentence the per-file result list shows.</summary>
    public static string Describe(CollectionsImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string Lines(int count) => count == 1 ? "1 line" : count.ToString(CultureInfo.InvariantCulture) + " lines";
        return result.Outcome switch
        {
            CollectionsImportOutcome.Imported => $"Imported {Lines(result.NewLines)}."
                + (result.AlreadyPresentLines > 0 ? $" {Lines(result.AlreadyPresentLines)} were imported before." : string.Empty)
                + (result.UnreadableLines > 0 ? $" {Lines(result.UnreadableLines)} could not be read and were not imported." : string.Empty)
                + (result.SupersededFiles > 0 ? " It replaces an earlier export of the same statement." : string.Empty),
            CollectionsImportOutcome.AlreadyImported => "Already imported: this exact file was imported before.",
            CollectionsImportOutcome.DuplicateContent => "Duplicate content: every line of this file was imported before. Nothing was added.",
            _ => $"Possible duplicate lines: {Lines(result.PossibleDuplicateLines)} have the date, amount and direction of a line already imported. "
                + "Nothing was added. Import them as new lines, or let this file replace the earlier one for its dates, with a reason."
        };
    }

    private static bool Matches(WorkbookSnapshot snapshot, ActiveLayout layout) =>
        StatementLayoutReader.ReadHeader(snapshot, layout.SheetName, layout.HeaderRow, out _) is { } header
        && string.Equals(header.HeaderSignature, layout.Signature, StringComparison.Ordinal);

    private async Task<IReadOnlyList<ActiveLayout>> LoadLayoutsAsync(int? layoutId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT l.layout_id,l.layout_key,l.display_name,l.layout_kind,COALESCE(a.account_key,p.provider_key),COALESCE(a.display_name,p.display_name),
                   l.header_signature_sha256,l.sheet_name,l.header_row,l.skip_footer_rows,l.mapping_json
            FROM dbo.statement_layouts l
            LEFT JOIN dbo.bank_accounts a ON a.bank_account_id=l.bank_account_id
            LEFT JOIN dbo.collection_providers p ON p.provider_key=l.provider_key
            WHERE l.active=1 AND COALESCE(a.active,p.active)=1 AND (@id IS NULL OR l.layout_id=@id)
            ORDER BY l.layout_key;
            """;
        await using var connection = await session.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@id", SqlDbType.Int, layoutId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var layouts = new List<ActiveLayout>();
        while (await reader.ReadAsync(cancellationToken))
            layouts.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetString(6), OptionalString(reader, 7), reader.GetInt32(8), reader.GetInt32(9), reader.GetString(10)));
        return layouts;
    }

    /// <summary>
    /// Review m-5 (decision 53): some bank exports put the full account number in the file name, and import_files keeps the name.
    /// Every run of five or more digits keeps only its last four ("XXXXXXXXXX5678"), as bank accounts keep only their last four.
    /// </summary>
    internal static string MaskedFileName(string fileName) =>
        System.Text.RegularExpressions.Regex.Replace(fileName ?? "", "[0-9]{5,}", match => new string('X', match.Length - 4) + match.Value[^4..]);

    internal const string NotAllowedToKeep =
        "ETP could not save a copy of this statement file, so nothing was imported. Only an Owner or Store Manager can import statement files, " +
        "and setup must have prepared the statement folder: ask the owner to run ETP setup again.";

    /// <summary>Keeps the file by its SHA-256 under the evidence root. The copy is hashed again; a file that changed after it
    /// was read is refused, so the database never records lines of bytes that were not kept.</summary>
    private async Task KeepFileAsync(string filePath, WorkbookSnapshot snapshot, ActiveLayout layout, CancellationToken cancellationToken)
    {
        string root;
        await using (var connection = await session.OpenAsync(cancellationToken))
        await using (var command = new SqlCommand("SELECT collections_evidence_root FROM dbo.product_settings WHERE product_setting_id=1;", connection))
            root = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("The folder for statement files is not set. Ask the owner to set it in Collections setup.");
        // ScopeKey is an account or provider key (CHECK: A-Z, 0-9 and '_' only), so it is safe as a folder name.
        var folder = Path.Combine(root, layout.Kind == "BANK" ? "BankSources" : "SettlementSources", layout.ScopeKey);
        (string ManagedPath, string Sha256, long Size) kept;
        try
        {
            kept = await ManagedDocumentRepository.StoreAsync(filePath, folder, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            // Review B-2: setup gives the evidence folder Modify for active Owner and Store Manager accounts only. Anyone else
            // (or a folder setup has not prepared) gets this sentence instead of a raw access-denied error.
            throw new InvalidOperationException(NotAllowedToKeep, exception);
        }
        if (!string.Equals(kept.Sha256, snapshot.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The file changed while it was being read. Close it in Excel and import it again.");
    }

    /// <summary>The lines as the procedure's JSON: row, identity, error, the mapped fields (dates yyyy-MM-dd, amounts as
    /// numbers) and raw (every cell by header).</summary>
    internal static string LinesJson(IReadOnlyList<StatementLine> lines)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            foreach (var line in lines)
            {
                json.WriteStartObject();
                json.WriteNumber("row", line.RowNumber);
                json.WriteString("identity", line.LineIdentity);
                if (line.ParseError is not null) json.WriteString("error", line.ParseError);
                Day(json, "value_date", line.ValueDate);
                Day(json, "posting_date", line.PostingDate);
                Text(json, "narration", line.Narration);
                Text(json, "bank_reference", line.Reference ?? line.BankReference);
                Number(json, "debit", line.Debit);
                Number(json, "credit", line.Credit);
                Number(json, "running_balance", line.RunningBalance);
                Text(json, "merchant_id", line.MerchantId);
                Text(json, "terminal_id", line.TerminalId);
                Text(json, "txn_reference", line.TxnReference);
                Text(json, "invoice_reference", line.InvoiceReference);
                Text(json, "approval_code", line.ApprovalCode);
                if (line.TxnDateTime is { } moment) json.WriteString("txn_datetime", moment.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
                Day(json, "txn_business_date", line.TxnBusinessDate);
                Day(json, "settlement_date", line.SettlementDate);
                Text(json, "settlement_id", line.SettlementId);
                Text(json, "status", line.Status);
                if (line.IsFailed) json.WriteBoolean("is_failed", true);
                Text(json, "line_kind", line.LineKind);
                Number(json, "gross", line.Gross);
                Number(json, "fee", line.Fee);
                Number(json, "tax_on_fee", line.TaxOnFee);
                Number(json, "net", line.Net);
                Text(json, "payer_name", line.PayerName);
                Text(json, "payer_vpa", line.PayerVpa);
                json.WritePropertyName("raw");
                json.WriteRawValue(line.RawJson, skipInputValidation: false);
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Day(Utf8JsonWriter json, string name, DateOnly? value)
    {
        if (value is { } day) json.WriteString(name, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static void Text(Utf8JsonWriter json, string name, string? value)
    {
        if (value is not null) json.WriteString(name, value);
    }

    private static void Number(Utf8JsonWriter json, string name, decimal? value)
    {
        if (value is { } number) json.WriteNumber(name, number);
    }
}
