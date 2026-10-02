using System.Globalization;
using System.Text.RegularExpressions;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Sources;

/// <summary>One sheet row as the Info readers see it: its sheet row number and its cells, the header row included.</summary>
internal sealed record SheetLine(int RowNumber, IReadOnlyList<WorkbookCell> Cells)
{
    public string Text(int column) => column >= 0 && column < Cells.Count ? SheetText.Of(Cells[column]) : "";
    public bool IsBlank => Cells.All(cell => SheetText.Of(cell).Length == 0);
}

/// <summary>Cell text and date readings shared by the Info readers and the snapshot dating.</summary>
internal static partial class SheetText
{
    public static WorkbookSheet? Find(WorkbookSnapshot workbook, string name) =>
        workbook.Sheets.FirstOrDefault(sheet => string.Equals(sheet.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The header row (as text) followed by every non-empty row, in sheet order. Blank rows are gaps in the numbers.</summary>
    public static IReadOnlyList<SheetLine> Lines(WorkbookSheet sheet)
    {
        var lines = new List<SheetLine>();
        if (sheet.Headers.Any(header => !string.IsNullOrWhiteSpace(header)))
            lines.Add(new(sheet.HeaderRowNumber, sheet.Headers.Select(header => new WorkbookCell(header, header)).ToArray()));
        lines.AddRange(sheet.Rows.Where(row => row.RowNumber > sheet.HeaderRowNumber).OrderBy(row => row.RowNumber)
            .Select(row => new SheetLine(row.RowNumber, row.Cells)));
        return lines;
    }

    /// <summary>The trimmed text of a cell. Numbers are written invariantly; a date-typed cell becomes contract date or time text.</summary>
    public static string Of(WorkbookCell? cell) => cell?.Value switch
    {
        null => "",
        string text => text.Trim(),
        decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
        DateTime time when time.TimeOfDay == TimeSpan.Zero => time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime time when time.Second == 0 => time.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
        DateTime time => time.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        var other => Convert.ToString(other, CultureInfo.InvariantCulture)?.Trim() ?? ""
    };

    /// <summary>False for a non-blank cell Excel stored as a number, date or boolean.</summary>
    public static bool IsText(WorkbookCell? cell) => cell?.Value is null or string;

    /// <summary>A whole non-negative number written as digits or stored as a whole number; null otherwise.</summary>
    public static int? Int(WorkbookCell? cell) => cell?.Value switch
    {
        decimal number when number >= 0 && number == decimal.Truncate(number) && number <= int.MaxValue => (int)number,
        _ => ContractCell.Int(Of(cell))
    };

    /// <summary>One date written as a date cell, <c>yyyy-MM-dd</c>, <c>dd.MM.yyyy</c> or <c>d MMM yyyy</c> (any separator); null otherwise.</summary>
    public static DateOnly? Date(WorkbookCell? cell)
    {
        if (cell?.Value is DateTime time) return DateOnly.FromDateTime(time);
        var text = Of(cell);
        var dates = Dates(text);
        return dates.Count == 1 && dates[0].Text.Length == text.Length ? dates[0].Date : null;
    }

    /// <summary>
    /// Every date token in a piece of text: <c>yyyy-MM-dd</c>, <c>dd.MM.yyyy</c>, <c>d MMM yyyy</c> or <c>d-MMM-yy</c> with the month
    /// spelled (e.g. <c>29 Sep 2026</c>, <c>01 JULY 2026</c>, <c>6 sep 26</c>) and, when <paramref name="compactDigits"/> is set, a
    /// <c>yyyyMMdd</c> run that letters and digits do not touch (so a GUID never yields a date). Tokens that are not real dates are skipped.
    /// </summary>
    public static IReadOnlyList<(DateOnly Date, string Text)> Dates(string text, bool compactDigits = false)
    {
        var found = new List<(DateOnly, string)>();
        if (string.IsNullOrWhiteSpace(text)) return found;
        foreach (Match match in IsoDate().Matches(text))
            if (TryDate(match.Groups["y"].Value, match.Groups["m"].Value, match.Groups["d"].Value, out var date)) found.Add((date, match.Value));
        foreach (Match match in DottedDate().Matches(text))
            if (TryDate(match.Groups["y"].Value, match.Groups["m"].Value, match.Groups["d"].Value, out var date)) found.Add((date, match.Value));
        foreach (Match match in NamedMonthDate().Matches(text))
        {
            var month = Array.IndexOf(Months, match.Groups["m"].Value[..3].ToUpperInvariant()) + 1;
            var year = match.Groups["y"].Value;
            if (year.Length == 2) year = "20" + year;
            if (month > 0 && TryDate(year, month.ToString(CultureInfo.InvariantCulture), match.Groups["d"].Value, out var date))
                found.Add((date, match.Value));
        }
        if (compactDigits)
            foreach (Match match in CompactDate().Matches(text))
                if (TryDate(match.Groups["y"].Value, match.Groups["m"].Value, match.Groups["d"].Value, out var date)) found.Add((date, match.Value));
        return found;
    }

    private static readonly string[] Months = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    private static bool TryDate(string year, string month, string day, out DateOnly date)
    {
        date = default;
        if (!int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y) ||
            !int.TryParse(month, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ||
            !int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var d) ||
            y is < 2000 or > 2099 || m is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, m)) return false;
        date = new DateOnly(y, m, d);
        return true;
    }

    [GeneratedRegex(@"(?<![0-9])(?<y>20\d{2})-(?<m>\d{2})-(?<d>\d{2})(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"(?<![0-9])(?<d>\d{2})\.(?<m>\d{2})\.(?<y>20\d{2})(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex DottedDate();

    [GeneratedRegex(@"(?<![0-9A-Za-z])(?<d>\d{1,2})[- ](?<m>(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[A-Za-z]*)[- ](?<y>20\d{2}|\d{2})(?![0-9A-Za-z])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NamedMonthDate();

    [GeneratedRegex(@"(?<![0-9A-Za-z])(?<y>20\d{2})(?<m>\d{2})(?<d>\d{2})(?:\d{4}|\d{6})?(?![0-9A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex CompactDate();
}
