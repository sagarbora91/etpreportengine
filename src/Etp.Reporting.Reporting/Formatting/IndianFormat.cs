using System.Globalization;

namespace Etp.Reporting.Reporting;

/// <summary>
/// The one place that turns a value into text for a person (UI/UX audit FW, rulebook R1-R9, decision 45 #5).
/// Indian grouping (12,34,567.89), "₹" directly before the digits, "dd MMM yyyy" dates with "Sept", 24-hour local
/// times, one decimal for percentages, "—" for a value that is missing. No call site writes a format string or picks a
/// culture. <see cref="CultureInfo.InvariantCulture"/> stays only for files meant for machines (Tally XML, JSON, file names,
/// logs, route arguments).
/// </summary>
public static class IndianFormat
{
    public const string Missing = "—";
    public const string NotApplicable = "N/A";
    private const string Minus = "−"; // U+2212, display-only headline money. Percentages use the plain "-" so they paste into Excel and WhatsApp.

    /// <summary>en-IN with the application's date pattern and the app-wide "Sept" spelling, whatever the ICU data says.</summary>
    public static CultureInfo Culture { get; } = Create();

    private static CultureInfo Create()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-IN").Clone();
        var format = culture.DateTimeFormat;
        format.ShortDatePattern = "dd MMM yyyy";
        format.AbbreviatedMonthNames = Sept(format.AbbreviatedMonthNames);
        format.AbbreviatedMonthGenitiveNames = Sept(format.AbbreviatedMonthGenitiveNames);
        return culture;
    }

    private static string[] Sept(string[] names)
    {
        var copy = (string[])names.Clone();
        if (copy.Length > 8 && copy[8].StartsWith("Sep", StringComparison.OrdinalIgnoreCase)) copy[8] = "Sept";
        return copy;
    }

    // ---- money ----

    /// <summary>Headline figure: "₹12,34,567", negative "−₹1,234", zero "₹0".</summary>
    public static string Money(decimal? value) => value is null ? Missing : value.Value.ToString("₹#,##0;−₹#,##0;₹0", Culture);

    /// <summary>Headline figure with paise: "₹12,34,567.50" (screens that reconcile to the paisa).</summary>
    public static string MoneyExact(decimal? value) => value is null ? Missing : value.Value.ToString("₹#,##0.00;−₹#,##0.00;₹0.00", Culture);

    /// <summary>Table cell under a "(₹)" header: "12,34,567.00". A real "-" so the cell can be copied into Excel.</summary>
    public static string MoneyCell(decimal? value) => value is null ? Missing : value.Value.ToString("#,##0.00", Culture);

    /// <summary>Above one lakh: "₹12.34 L", above one crore "₹1.25 Cr"; below that the whole-rupee form.</summary>
    public static string MoneyCompact(decimal? value)
    {
        if (value is null) return Missing;
        var absolute = Math.Abs(value.Value);
        var sign = value < 0 ? Minus : string.Empty;
        if (absolute >= 10_000_000m) return $"{sign}₹{(absolute / 10_000_000m).ToString("0.00", Culture)} Cr";
        if (absolute >= 100_000m) return $"{sign}₹{(absolute / 100_000m).ToString("0.00", Culture)} L";
        return Money(value);
    }

    /// <summary>The marker every money column header carries: "Value incl. GST (₹)".</summary>
    public static string MoneyHeader(string name) => name.EndsWith("(₹)", StringComparison.Ordinal) ? name : name + " (₹)";

    // ---- quantities and counts ----

    /// <summary>A count (invoices, documents, days, rows): whole number with Indian grouping.</summary>
    public static string Count(long value) => value.ToString("#,##0;-#,##0;0", Culture);

    /// <summary>Units of stock: whole by default, or a fixed number of decimals for a column that holds fractions. Never carries ₹.</summary>
    public static string Quantity(decimal? value, int decimals = 0) =>
        value is null ? Missing : value.Value.ToString(decimals <= 0 ? "#,##0;-#,##0;0" : "#,##0." + new string('0', decimals), Culture);

    // ---- percentages ----

    /// <summary>One decimal, "%" attached: "12.5%". With <paramref name="signed"/> (change/growth) "+12.5%" or "-3.2%" (FW-07: the plain hyphen, copyable).</summary>
    public static string Percent(decimal? value, bool signed = false)
    {
        if (value is null) return Missing;
        var rounded = Math.Round(value.Value, 1, MidpointRounding.AwayFromZero);
        var text = Math.Abs(rounded).ToString("0.0", Culture);
        if (!signed) return (rounded < 0 ? "-" : string.Empty) + text + "%";
        return (rounded < 0 ? "-" : "+") + text + "%";
    }

    // ---- dates and times ----

    /// <summary>"07 Sept 2026".</summary>
    public static string Date(DateOnly value) => value.ToString("dd MMM yyyy", Culture);
    public static string Date(DateOnly? value) => value is null ? Missing : Date(value.Value);
    /// <summary>A business date has no time zone: it is never run through ToLocalTime.</summary>
    public static string Date(DateTime value) => value.ToString("dd MMM yyyy", Culture);
    /// <summary>"Mon 07 Sept 2026".</summary>
    public static string DateWithDay(DateOnly value) => value.ToString("ddd dd MMM yyyy", Culture);
    /// <summary>"Sept 2026".</summary>
    public static string Month(DateOnly value) => value.ToString("MMM yyyy", Culture);
    public static string Month(DateTime value) => value.ToString("MMM yyyy", Culture);
    /// <summary>"01 Sept 2026 – 07 Sept 2026" (en dash, no same-month shortening).</summary>
    public static string DateRange(DateOnly from, DateOnly to) => $"{Date(from)} – {Date(to)}";
    /// <summary>"18:05", 24-hour, local time of this PC.</summary>
    public static string Time(DateTime local) => local.ToString("HH:mm", Culture);
    /// <summary>"07 Sept 2026 18:05" from a local time.</summary>
    public static string DateAndTime(DateTime local) => local.ToString("dd MMM yyyy HH:mm", Culture);
    /// <summary>A stored UTC time shown as the local clock time of this PC: "07 Sept 2026 18:05".</summary>
    public static string LocalDateTime(DateTime utc) =>
        DateAndTime(System.DateTime.SpecifyKind(utc, utc.Kind == DateTimeKind.Local ? DateTimeKind.Local : DateTimeKind.Utc).ToLocalTime());

    /// <summary>A report metric by the kind of figure it is: "currency" (cell under a (₹) label), "percent", "ratio" (2 decimals), otherwise a count that shows decimals only when it has some.</summary>
    public static string Metric(decimal? value, string? kind) => value is null ? Missing : kind switch
    {
        "currency" => MoneyCell(value),
        "percent" => Percent(value),
        "ratio" => Quantity(value, 2),
        _ => Quantity(value, value == decimal.Truncate(value.Value) ? 0 : 2)
    };

    // ---- sentences ----

    /// <summary>"1 invoice", "3 invoices". The plural adds "s" unless one is given.</summary>
    public static string Plural(long count, string singular, string? plural = null) =>
        Count(count) + " " + (count == 1 ? singular : plural ?? singular + "s");

    /// <summary>Formats an interpolated sentence with the Indian culture instead of the thread's.</summary>
    public static string Text(FormattableString text) => text.ToString(Culture);
}
