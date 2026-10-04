using System.Security.Cryptography;
using System.Text;

namespace Etp.Reporting.Import.Workbooks;

/// <summary>
/// Reads a raw ETP export saved as CSV (the daily Service pack, design section 2) into the same sheet model
/// <see cref="OpenXmlWorkbookReader"/> produces, so matching, staging and persistence need no CSV branch.
/// <list type="bullet">
/// <item>One sheet, named after the file without its extension.</item>
/// <item>RFC 4180: comma delimiter, double-quote quoting with <c>""</c> as an escaped quote, line breaks inside quotes kept;
/// CRLF, LF and CR all end a record.</item>
/// <item>UTF-8 with or without a BOM (UTF-16 with a BOM is also read); bytes that are not valid UTF-8 are read as
/// Windows-1252, which is what Excel writes for "CSV (Comma delimited)".</item>
/// <item>Every cell stays text; <see cref="Conversion.TypedCellConverter"/> converts it per catalogue type. An empty cell is
/// a missing value, as an absent XLSX cell is.</item>
/// <item>The header is the first record with a non-blank cell; empty trailing columns are trimmed from every record.</item>
/// <item>Row numbers are record numbers (1-based), the CSV equivalent of sheet row numbers.</item>
/// </list>
/// The bytes are read once through a shared-read handle, hashed and kept as evidence, exactly as for a workbook.
/// No exception message ever carries a cell value.
/// </summary>
public sealed class CsvWorkbookReader : IWorkbookReader
{
    public async Task<WorkbookSnapshot> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(filePath);
        if (!info.Exists) throw new FileNotFoundException("CSV file not found.", filePath);

        await using var source = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var snapshot = new MemoryStream(checked((int)Math.Min(source.Length, int.MaxValue)));
        await source.CopyToAsync(snapshot, cancellationToken).ConfigureAwait(false);
        var bytes = snapshot.ToArray();
        return await Task.Run(() => Materialize(info.Name, bytes, cancellationToken) with { SourcePath = info.FullName },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds the snapshot of CSV bytes; <paramref name="fileName"/> names the sheet.</summary>
    public static WorkbookSnapshot Materialize(string fileName, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(bytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var records = Parse(Decode(bytes), cancellationToken);
        var sheetName = Path.GetFileNameWithoutExtension(fileName).Trim();

        var rows = new List<WorkbookRow>(records.Count);
        for (var index = 0; index < records.Count; index++)
            rows.Add(new WorkbookRow(index + 1, Trim(records[index])));
        var header = rows.FirstOrDefault(row => row.Cells.Any(cell => !string.IsNullOrWhiteSpace(cell.DisplayText)));
        var sheet = header is null
            ? new WorkbookSheet(sheetName, 1, [], [])
            : new WorkbookSheet(sheetName, header.RowNumber,
                header.Cells.Select(cell => cell.DisplayText?.Trim() ?? string.Empty).ToArray(),
                rows.Where(row => row.RowNumber > header.RowNumber && row.Cells.Any(cell => cell.Value is not null)).ToArray());
        return new WorkbookSnapshot(fileName, bytes.LongLength, hash, [sheet]) { Content = new(bytes, hash) };
    }

    /// <summary>The text of CSV bytes: a BOM decides; otherwise strict UTF-8, and Windows-1252 when that fails.</summary>
    internal static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Strict(new UTF8Encoding(false, true), bytes, 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Strict(new UnicodeEncoding(false, false, true), bytes, 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Strict(new UnicodeEncoding(true, false, true), bytes, 2);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Windows1252(bytes); }
    }

    private static string Strict(Encoding encoding, byte[] bytes, int offset)
    {
        try { return encoding.GetString(bytes, offset, bytes.Length - offset); }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The CSV file is not valid text in the encoding its byte-order mark declares.", exception);
        }
    }

    // Windows-1252 differs from ISO-8859-1 only in 0x80-0x9F. Mapping it here keeps the reader free of the code-page
    // provider, which would otherwise have to be registered process-wide. The five unassigned bytes stay as C1 controls.
    private static readonly char[] Windows1252High =
    [
        '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡',
        'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
        '\u0090', '‘', '’', '“', '”', '•', '–', '—',
        '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ'
    ];

    internal static string Windows1252(byte[] bytes) => string.Create(bytes.Length, bytes, static (chars, source) =>
    {
        for (var index = 0; index < source.Length; index++)
        {
            var value = source[index];
            chars[index] = value is >= 0x80 and <= 0x9F ? Windows1252High[value - 0x80] : (char)value;
        }
    });

    /// <summary>The records of CSV text (RFC 4180). A text that ends inside a quoted field is refused.</summary>
    internal static IReadOnlyList<IReadOnlyList<string>> Parse(string text, CancellationToken cancellationToken = default)
    {
        var records = new List<IReadOnlyList<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;
        var index = 0;
        while (index < text.Length)
        {
            if ((index & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var c = text[index];
            if (quoted)
            {
                if (c == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index += 2; continue; }
                    quoted = false; index++; continue;
                }
                field.Append(c); index++; continue;
            }
            switch (c)
            {
                case '"' when field.Length == 0 && !fieldStarted:
                    quoted = true; fieldStarted = true; index++; break;
                case ',':
                    record.Add(field.ToString()); field.Clear(); fieldStarted = false; index++; break;
                case '\r' or '\n':
                    record.Add(field.ToString()); field.Clear(); fieldStarted = false;
                    records.Add(record.ToArray()); record.Clear();
                    index += c == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                    break;
                default:
                    // A quote inside an unquoted field, or text after a closing quote, is kept as written (lenient, as Excel is).
                    field.Append(c); fieldStarted = true; index++; break;
            }
        }
        if (quoted) throw new InvalidDataException("The CSV file ends inside a quoted field.");
        if (field.Length > 0 || fieldStarted || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record.ToArray());
        }
        return records;
    }

    private static WorkbookCell[] Trim(IReadOnlyList<string> record)
    {
        var width = record.Count;
        while (width > 0 && record[width - 1].Length == 0) width--;
        var cells = new WorkbookCell[width];
        for (var index = 0; index < width; index++)
            cells[index] = record[index].Length == 0 ? new WorkbookCell(null) : new WorkbookCell(record[index], record[index]);
        return cells;
    }
}

/// <summary>
/// Chooses the reader by file extension: <c>.csv</c> is read by <see cref="CsvWorkbookReader"/>, anything else by
/// <see cref="OpenXmlWorkbookReader"/>. This is the reader every import path uses unless a test injects another.
/// </summary>
public sealed class SourceFileReader(IWorkbookReader? workbookReader = null, IWorkbookReader? csvReader = null) : IWorkbookReader
{
    private readonly IWorkbookReader workbooks = workbookReader ?? new OpenXmlWorkbookReader();
    private readonly IWorkbookReader csv = csvReader ?? new CsvWorkbookReader();

    public static bool IsCsv(string? path) =>
        Path.GetExtension(path ?? string.Empty).Equals(".csv", StringComparison.OrdinalIgnoreCase);

    public Task<WorkbookSnapshot> ReadAsync(string filePath, CancellationToken cancellationToken = default) =>
        IsCsv(filePath) ? csv.ReadAsync(filePath, cancellationToken) : workbooks.ReadAsync(filePath, cancellationToken);
}
