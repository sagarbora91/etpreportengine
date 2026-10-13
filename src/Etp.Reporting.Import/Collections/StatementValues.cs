using System.Globalization;

namespace Etp.Reporting.Import.Collections;

/// <summary>
/// Reads one statement cell as a date, a time or an amount. A blank cell is "no value" (null), never zero; a cell that cannot be
/// read returns false, so the line is kept with a parse error instead of a guessed value. Nothing here ever puts a cell value
/// into an exception or a message.
/// </summary>
public static class StatementValues
{
    /// <summary>Tried in order when the layout gives no date_format. Day before month: Indian exports.</summary>
    public static IReadOnlyList<string> DefaultDateFormats { get; } =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "dd-MM-yyyy", "d-M-yyyy", "dd-MM-yy", "dd-MMM-yy", "dd-MMM-yyyy", "d-MMM-yy",
        "d-MMM-yyyy", "dd MMM yyyy", "d MMM yyyy", "dd MMM yy", "dd.MM.yyyy", "dd.MM.yy", "yyyy-MM-dd", "dd/MMM/yyyy", "dd/MMM/yy"
    ];

    public static IReadOnlyList<string> DefaultDateTimeFormats { get; } =
    [
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd-MM-yyyy HH:mm:ss", "dd-MM-yyyy HH:mm", "dd-MMM-yyyy HH:mm:ss", "dd-MMM-yy HH:mm:ss",
        "dd/MM/yyyy hh:mm:ss tt", "dd/MM/yyyy h:mm:ss tt", "dd-MM-yyyy hh:mm:ss tt", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss.fff", "dd/MM/yy HH:mm:ss", "dd/MM/yy HH:mm"
    ];

    private static readonly string[] CurrencyMarks = ["₹", "INR", "RS.", "RS"];

    public static bool IsBlank(object? value) =>
        value is null || value is string text && (string.IsNullOrWhiteSpace(text) || text.Trim() is "-" or "--");

    /// <summary>Text of a cell, trimmed; null when blank. Dates and numbers are written invariantly.</summary>
    public static string? Text(object? value) => value switch
    {
        null => null,
        string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
        DateTime date => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        bool flag => flag ? "TRUE" : "FALSE",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim()
    };

    public static bool TryDate(object? value, IReadOnlyList<string> formats, out DateOnly? date)
    {
        date = null;
        if (IsBlank(value)) return true;
        switch (value)
        {
            case DateTime moment:
                date = DateOnly.FromDateTime(moment);
                return true;
            case decimal serial when serial == decimal.Truncate(serial) && serial is > 20000m and < 80000m:
                // An Excel date serial in a cell that is not styled as a date.
                date = DateOnly.FromDateTime(DateTime.FromOADate((double)serial));
                return true;
        }
        var text = Text(value);
        if (text is null) return true;
        var candidates = formats.Count > 0 ? formats : DefaultDateFormats;
        if (DateTime.TryParseExact(text, candidates.ToArray(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }
        // A date column that also carries a time ("01/08/2026 10:22").
        if (TryDateTime(text, [], utc: false, out var withTime) && withTime is { } found)
        {
            date = DateOnly.FromDateTime(found);
            return true;
        }
        return false;
    }

    /// <summary>A transaction time, as India Standard Time wall-clock. With <paramref name="utc"/> the file's time is UTC and is
    /// converted to IST (fixed +05:30; India has no daylight saving).</summary>
    public static bool TryDateTime(object? value, IReadOnlyList<string> formats, bool utc, out DateTime? moment)
    {
        moment = null;
        if (IsBlank(value)) return true;
        DateTime parsed;
        if (value is DateTime cell) parsed = cell;
        else
        {
            var text = Text(value);
            if (text is null) return true;
            var candidates = (formats.Count > 0 ? formats : DefaultDateTimeFormats).Concat(DefaultDateFormats).ToArray();
            if (!DateTime.TryParseExact(text, candidates, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed)) return false;
        }
        moment = DateTime.SpecifyKind(utc ? parsed.AddHours(5).AddMinutes(30) : parsed, DateTimeKind.Unspecified);
        moment = moment.Value.AddTicks(-(moment.Value.Ticks % TimeSpan.TicksPerSecond));
        return true;
    }

    /// <summary>
    /// Reads an amount: currency marks, spaces and the thousands separator are dropped (so Indian grouping "1,23,456.78" reads as
    /// 123456.78), the decimal separator becomes '.', "(12.50)" and a leading or trailing '-' are negative, and a trailing
    /// "Cr"/"Dr" is returned in <paramref name="marker"/> (+1 credit, -1 debit) without changing the sign.
    /// </summary>
    public static bool TryAmount(object? value, char decimalSeparator, char? thousandsSeparator, out decimal? amount, out int marker)
    {
        amount = null;
        marker = 0;
        if (IsBlank(value)) return true;
        switch (value)
        {
            case decimal number: amount = number; return true;
            case double number when double.IsFinite(number): amount = Math.Round((decimal)number, 4, MidpointRounding.AwayFromZero); return true;
            case int number: amount = number; return true;
            case long number: amount = number; return true;
        }
        var text = Text(value);
        if (text is null) return true;
        text = text.Replace(' ', ' ').Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (text.EndsWith("CR", StringComparison.Ordinal)) { marker = 1; text = text[..^2]; }
        else if (text.EndsWith("DR", StringComparison.Ordinal)) { marker = -1; text = text[..^2]; }
        foreach (var mark in CurrencyMarks)
            if (text.StartsWith(mark, StringComparison.Ordinal)) { text = text[mark.Length..]; break; }
        var negative = false;
        if (text.StartsWith('(') && text.EndsWith(')')) { negative = true; text = text[1..^1]; }
        if (text.StartsWith('-')) { negative = !negative; text = text[1..]; }
        else if (text.EndsWith('-')) { negative = !negative; text = text[..^1]; }
        else if (text.StartsWith('+')) text = text[1..];
        if (thousandsSeparator is { } thousands) text = text.Replace(thousands.ToString(), string.Empty, StringComparison.Ordinal);
        if (decimalSeparator != '.') text = text.Replace(decimalSeparator, '.');
        if (text.Length == 0 || text.Any(character => character is not (>= '0' and <= '9') and not '.')) return false;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)) return false;
        if (decimal.Round(parsed, 4) != parsed) return false;
        amount = negative ? -parsed : parsed;
        return true;
    }

    /// <summary>Trims and caps text at the column width of its SQL column (the cut is part of the stored value and of the identity).</summary>
    public static string? Clip(string? text, int length) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim() is var trimmed && trimmed.Length > length ? trimmed[..length] : text.Trim();
}
