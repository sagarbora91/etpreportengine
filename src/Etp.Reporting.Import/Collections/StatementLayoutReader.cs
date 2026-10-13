using System.Text.Json;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Collections;

/// <summary>One line of a statement file, read through a layout. Amounts are positive: a bank line is a credit or a debit, a
/// provider line's direction is its <see cref="LineKind"/> (only ADJUSTMENT keeps the file's sign). <see cref="ParseError"/> is
/// set when the line could not be read; its values are then not trusted and the line is recorded as INVALID, never as zero.</summary>
public sealed record StatementLine(
    int RowNumber,
    string LineIdentity,
    string RawJson,
    string? ParseError,
    DateOnly? ValueDate = null,
    DateOnly? PostingDate = null,
    string? Narration = null,
    string? Reference = null,
    decimal? Credit = null,
    decimal? Debit = null,
    decimal? RunningBalance = null,
    string? MerchantId = null,
    string? TerminalId = null,
    string? TxnReference = null,
    string? InvoiceReference = null,
    string? ApprovalCode = null,
    DateTime? TxnDateTime = null,
    DateOnly? TxnBusinessDate = null,
    DateOnly? SettlementDate = null,
    string? SettlementId = null,
    string? BankReference = null,
    string? Status = null,
    bool IsFailed = false,
    string? LineKind = null,
    decimal? Gross = null,
    decimal? Fee = null,
    decimal? TaxOnFee = null,
    decimal? Net = null,
    string? PayerName = null,
    string? PayerVpa = null)
{
    /// <summary>The date the line counts on: value date (else posting date) for a bank, transaction date (else settlement date)
    /// for a provider.</summary>
    public DateOnly? LineDate => ValueDate ?? PostingDate ?? TxnBusinessDate ?? SettlementDate;
}

/// <summary>The header row a layout sees in a file.</summary>
public sealed record StatementHeader(string SheetName, int HeaderRow, IReadOnlyList<string> Headers, string HeaderSignature, IReadOnlyList<string> SheetNames);

/// <summary>A file read through a layout. <see cref="Problems"/> are file-level (a missing column, an empty header): with any
/// problem there are no lines.</summary>
public sealed record StatementReadResult(StatementHeader? Header, IReadOnlyList<string> Problems, IReadOnlyList<StatementLine> Lines)
{
    public int ReadableLines => Lines.Count(line => line.ParseError is null);
    public int UnreadableLines => Lines.Count(line => line.ParseError is not null);
}

/// <summary>
/// Reads a bank or provider statement (CSV or XLSX, already loaded by <see cref="CsvWorkbookReader"/> or
/// <see cref="OpenXmlWorkbookReader"/>) through an Owner's <see cref="StatementLayout"/> (master plan 8a task 2).
/// The header is the row <c>header_row</c> of the chosen sheet (bank exports often start with account details); the last
/// <c>skip_footer_rows</c> non-blank rows are dropped. The header signature is
/// <see cref="ImportProfileMatcher.CreateHeaderSignature"/> over the header cells (trailing blank cells dropped, an inner blank
/// cell read as "(blank)"), so a layout is found the same way an ETP report profile is.
/// </summary>
public static class StatementLayoutReader
{
    public const string BlankHeader = "(blank)";

    public static StatementHeader? ReadHeader(WorkbookSnapshot snapshot, string? sheetName, int headerRow, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        problem = null;
        var sheets = snapshot.Sheets.Select(sheet => sheet.Name).ToArray();
        var sheet = sheetName is null
            ? snapshot.Sheets.FirstOrDefault()
            : snapshot.Sheets.FirstOrDefault(candidate => string.Equals(candidate.Name, sheetName, StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
        {
            problem = sheetName is null ? "The file has no sheet." : "The file has no sheet with the layout's sheet name.";
            return null;
        }
        var cells = HeaderCells(sheet, headerRow);
        if (cells is null)
        {
            problem = "The header row of the layout is empty in this file.";
            return null;
        }
        return new(sheet.Name, headerRow, cells, ImportProfileMatcher.CreateHeaderSignature(cells), sheets);
    }

    public static StatementReadResult Read(WorkbookSnapshot snapshot, StatementLayout layout, string? sheetName, int headerRow, int skipFooterRows, string scopeKey)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        var header = ReadHeader(snapshot, sheetName, headerRow, out var headerProblem);
        if (header is null) return new(null, [headerProblem!], []);

        var normalised = header.Headers.Select(Etp.Reporting.Domain.Imports.ImportProfile.NormalizeHeader).ToArray();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (key, column) in layout.Columns)
        {
            var position = Array.IndexOf(normalised, Etp.Reporting.Domain.Imports.ImportProfile.NormalizeHeader(column));
            if (position < 0) problems.Add($"The file has no column \"{column}\", which the layout uses for {key.Replace('_', ' ')}.");
            else index[key] = position;
        }
        if (problems.Count > 0) return new(header, problems, []);

        var sheet = snapshot.Sheets.First(candidate => candidate.Name == header.SheetName);
        var rows = sheet.Rows.Where(row => row.RowNumber > headerRow && row.Cells.Any(cell => !StatementValues.IsBlank(cell.Value))).ToList();
        if (skipFooterRows > 0) rows = rows.Take(Math.Max(0, rows.Count - skipFooterRows)).ToList();

        var keys = RawKeys(header.Headers);
        var drafts = new List<(StatementLine Line, string? Payload)>();
        foreach (var row in rows)
        {
            object? Cell(string key) => index.TryGetValue(key, out var position) && position < row.Cells.Count ? row.Cells[position].Value : null;
            var raw = RawJson(keys, row);
            var (line, payload) = layout.Kind == StatementLayoutKind.Bank
                ? ReadBank(layout, row.RowNumber, raw, Cell, scopeKey)
                : ReadProvider(layout, row.RowNumber, raw, Cell, scopeKey);
            drafts.Add((line, payload));
        }

        // Ordinals in file order among identical payloads; an unreadable line gets its own identity.
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = new List<StatementLine>(drafts.Count);
        foreach (var (line, payload) in drafts)
        {
            if (payload is null)
            {
                lines.Add(line with { LineIdentity = LineIdentity.ForUnreadable(scopeKey, snapshot.Sha256, line.RowNumber, line.RawJson) });
                continue;
            }
            var ordinal = ordinals.TryGetValue(payload, out var count) ? count + 1 : 1;
            ordinals[payload] = ordinal;
            lines.Add(line with { LineIdentity = LineIdentity.Compute(payload, ordinal) });
        }
        return new(header, [], lines);
    }

    private static (StatementLine Line, string? Payload) ReadBank(StatementLayout layout, int rowNumber, string raw, Func<string, object?> cell, string scopeKey)
    {
        StatementLine Bad(string message) => new(rowNumber, string.Empty, raw, message);
        if (!StatementValues.TryDate(cell("value_date"), layout.DateFormats, out var valueDate)) return (Bad("The value date could not be read."), null);
        if (!StatementValues.TryDate(cell("posting_date"), layout.DateFormats, out var postingDate)) return (Bad("The posting date could not be read."), null);
        if (valueDate is null && postingDate is null) return (Bad("The line has no date."), null);

        decimal? credit, debit;
        if (layout.Columns.ContainsKey("credit"))
        {
            if (!StatementValues.TryAmount(cell("credit"), layout.DecimalSeparator, layout.ThousandsSeparator, out credit, out _))
                return (Bad("The credit amount could not be read."), null);
            if (!StatementValues.TryAmount(cell("debit"), layout.DecimalSeparator, layout.ThousandsSeparator, out debit, out _))
                return (Bad("The debit amount could not be read."), null);
            if (credit < 0 || debit < 0) return (Bad("A credit or debit amount is negative."), null);
            if (credit == 0) credit = null;
            if (debit == 0) debit = null;
            if (credit is not null && debit is not null) return (Bad("The line has both a credit and a debit."), null);
        }
        else
        {
            if (!StatementValues.TryAmount(cell("signed_amount"), layout.DecimalSeparator, layout.ThousandsSeparator, out var amount, out var marker))
                return (Bad("The amount could not be read."), null);
            int direction;
            if (layout.Columns.ContainsKey("dr_cr_flag"))
            {
                var flag = StatementValues.Text(cell("dr_cr_flag"))?.ToUpperInvariant();
                if (flag is null) return (Bad("The credit/debit marker is missing."), null);
                direction = layout.CreditFlagValues.Contains(flag, StringComparer.Ordinal) ? 1 : -1;
            }
            else direction = marker != 0 ? marker : Math.Sign(amount ?? 0);
            if (amount is not { } value || value == 0m || direction == 0) return (Bad("The line has no amount."), null);
            credit = direction > 0 ? Math.Abs(value) : null;
            debit = direction < 0 ? Math.Abs(value) : null;
        }
        if (credit is null && debit is null) return (Bad("The line has no amount."), null);
        StatementValues.TryAmount(cell("running_balance"), layout.DecimalSeparator, layout.ThousandsSeparator, out var balance, out var balanceMarker);
        if (balance is not null && balanceMarker < 0) balance = -Math.Abs(balance.Value);

        var narration = StatementValues.Clip(StatementValues.Text(cell("narration")), 500);
        var reference = StatementValues.Clip(StatementValues.Text(cell("reference")), 100);
        var line = new StatementLine(rowNumber, string.Empty, raw, null, valueDate, postingDate, narration, reference, credit, debit, balance);
        return (line, LineIdentity.BankPayload(scopeKey, (valueDate ?? postingDate)!.Value, reference, narration, credit, debit));
    }

    private static (StatementLine Line, string? Payload) ReadProvider(StatementLayout layout, int rowNumber, string raw, Func<string, object?> cell, string scopeKey)
    {
        StatementLine Bad(string message) => new(rowNumber, string.Empty, raw, message);
        if (!StatementValues.TryDateTime(cell("txn_datetime"), layout.DateTimeFormats, layout.DateTimeUtc, out var moment))
            return (Bad("The transaction date could not be read."), null);
        if (!StatementValues.TryDate(cell("settlement_date"), layout.DateFormats, out var settlementDate))
            return (Bad("The settlement date could not be read."), null);
        var txnDate = moment is { } at ? DateOnly.FromDateTime(at) : (DateOnly?)null;
        if (txnDate is null && settlementDate is null) return (Bad("The line has no date."), null);

        decimal? AmountOf(string key, out bool ok)
        {
            ok = StatementValues.TryAmount(cell(key), layout.DecimalSeparator, layout.ThousandsSeparator, out var value, out var marker);
            return value is not null && marker < 0 ? -Math.Abs(value.Value) : value;
        }
        var gross = AmountOf("gross", out var grossOk);
        var fee = AmountOf("fee", out var feeOk);
        var tax = AmountOf("tax_on_fee", out var taxOk);
        var net = AmountOf("net", out var netOk);
        if (!grossOk || !feeOk || !taxOk || !netOk) return (Bad("An amount could not be read."), null);
        if (gross is null && net is null) return (Bad("The line has no amount."), null);

        string kind;
        if (layout.Columns.ContainsKey("line_kind_column"))
        {
            var word = StatementValues.Text(cell("line_kind_column"));
            kind = word is not null && layout.LineKindValues.TryGetValue(word, out var mapped) ? mapped : "UNKNOWN";
        }
        else kind = layout.LineKindDefault;
        var signed = gross ?? net ?? 0;
        if (kind == "SALE" && signed < 0)
        {
            if (!layout.LineKindFromSign) return (Bad("A sale line has a negative amount. Ask the owner whether negative lines are refunds."), null);
            kind = "REFUND";
        }
        if (kind != "ADJUSTMENT")
        {
            gross = gross is null ? null : Math.Abs(gross.Value);
            net = net is null ? null : Math.Abs(net.Value);
        }
        fee = layout.FeeSource == "COLUMN" && fee is not null ? Math.Abs(fee.Value) : null;
        tax = tax is null ? null : Math.Abs(tax.Value);

        var status = StatementValues.Clip(StatementValues.Text(cell("status")), 30);
        var failed = status is not null && layout.StatusFailedValues.Contains(status.ToUpperInvariant(), StringComparer.Ordinal);
        var txnReference = StatementValues.Clip(StatementValues.Text(cell("txn_reference")), 100);
        var settlementId = StatementValues.Clip(StatementValues.Text(cell("settlement_id")), 100);
        var invoiceReference = StatementValues.Clip(StatementValues.Text(cell("invoice_reference")), 80);
        var line = new StatementLine(rowNumber, string.Empty, raw, null,
            MerchantId: StatementValues.Clip(StatementValues.Text(cell("merchant_id")), 50),
            TerminalId: StatementValues.Clip(StatementValues.Text(cell("terminal_id")), 50),
            TxnReference: txnReference,
            InvoiceReference: invoiceReference,
            ApprovalCode: StatementValues.Clip(StatementValues.Text(cell("approval_code")), 30),
            TxnDateTime: moment,
            TxnBusinessDate: txnDate,
            SettlementDate: settlementDate,
            SettlementId: settlementId,
            BankReference: StatementValues.Clip(StatementValues.Text(cell("bank_reference")), 100),
            Status: status,
            IsFailed: failed,
            LineKind: kind,
            Gross: gross,
            Fee: fee,
            TaxOnFee: tax,
            Net: net,
            PayerName: StatementValues.Clip(StatementValues.Text(cell("payer_name")), 200),
            PayerVpa: StatementValues.Clip(StatementValues.Text(cell("payer_vpa")), 200));
        return (line, LineIdentity.ProviderPayload(scopeKey, moment, txnDate, settlementDate, txnReference, settlementId, invoiceReference, kind, gross, net));
    }

    private static IReadOnlyList<string>? HeaderCells(WorkbookSheet sheet, int headerRow)
    {
        IReadOnlyList<string> cells;
        if (headerRow == sheet.HeaderRowNumber) cells = sheet.Headers;
        else
        {
            var row = sheet.Rows.FirstOrDefault(candidate => candidate.RowNumber == headerRow);
            if (row is null) return null;
            cells = row.Cells.Select(cell => StatementValues.Text(cell.Value) ?? string.Empty).ToArray();
        }
        var width = cells.Count;
        while (width > 0 && string.IsNullOrWhiteSpace(cells[width - 1])) width--;
        if (width == 0) return null;
        return cells.Take(width).Select(cell => string.IsNullOrWhiteSpace(cell) ? BlankHeader : cell.Trim()).ToArray();
    }

    /// <summary>The raw_json keys: the header text, made unique ("Amount", "Amount (2)").</summary>
    private static IReadOnlyList<string> RawKeys(IReadOnlyList<string> headers)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new List<string>(headers.Count);
        foreach (var header in headers)
        {
            var key = header;
            for (var copy = 2; !used.Add(key); copy++) key = $"{header} ({copy})";
            keys.Add(key);
        }
        return keys;
    }

    private static string RawJson(IReadOnlyList<string> keys, WorkbookRow row)
    {
        var cells = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var position = 0; position < keys.Count; position++)
            cells[keys[position]] = position < row.Cells.Count ? StatementValues.Text(row.Cells[position].Value) : null;
        return JsonSerializer.Serialize(cells);
    }
}
