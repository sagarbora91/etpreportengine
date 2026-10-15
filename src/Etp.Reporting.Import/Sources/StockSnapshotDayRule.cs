namespace Etp.Reporting.Import.Sources;

/// <summary>
/// IF-032 (decision 66, 15 Oct 2026): one dating rule for the two stock snapshot exports, Closing Stock (R011, family
/// <c>CLOSING_STOCK</c>) and BinWise Stock (R010). A raw ETP export whose file name states the minute it was made
/// (<c>yyyyMMddHHmm_</c>, or the Service <c>_yyyyMMddHHmmss</c> suffix) is the closing stock of a business day:
/// <list type="bullet">
/// <item>made at or after <see cref="TradingDayFrom"/> (the evening export, the normal case): the closing of the day it
/// was made;</item>
/// <item>made before it (the next-morning export, before the first bill): the closing of the previous day.</item>
/// </list>
/// The 3 Oct 2026 Titan case: BinWise at 17:45 and Closing Stock at 18:00 are both 3 Oct, although the Closing Stock
/// file's own Date column says 2 Oct. An export with only a date, or no time at all, is not re-dated by this rule.
/// </summary>
public static class StockSnapshotDayRule
{
    /// <summary>
    /// The hour from which an export is that day's closing. Before it, the shop has not traded yet (IF-029 user-guide
    /// rule: export after the last bill, or next morning dated the previous day).
    /// </summary>
    public static readonly TimeOnly TradingDayFrom = new(12, 0);

    /// <summary>The families the rule dates: Closing Stock and BinWise Stock, by any of their codes.</summary>
    public static bool Applies(string? reportCode) =>
        reportCode is "CLOSING_STOCK" or "R011" or "R010";

    /// <summary>
    /// The business day whose closing an export made at <paramref name="export"/> is; null when the export's time of
    /// day is not known (date-only or unknown basis), so the caller keeps its other dating.
    /// </summary>
    public static DateOnly? ClosingDayOf(ExportTime export)
    {
        if (export.Basis is not (ExportBasis.Minute or ExportBasis.Second) || export.Instant is not { } instant) return null;
        var day = DateOnly.FromDateTime(instant);
        return TimeOnly.FromDateTime(instant) >= TradingDayFrom ? day : day.AddDays(-1);
    }

    /// <summary>The business day of an export, read from its file name; null when the name states no time of day.</summary>
    public static DateOnly? ClosingDayOf(string? fileName) => ClosingDayOf(ExportNameParser.Parse(fileName));

    /// <summary>
    /// For a Closing Stock file whose Date column holds one date: the day the rule gives instead, or null to keep the
    /// column. The column is only replaced when it is the export day or the day before (the two readings ETP writes);
    /// a column further away is an older snapshot saved under a new name and keeps its own date.
    /// </summary>
    public static DateOnly? ClosingStockDay(DateOnly columnDate, string? fileName)
    {
        var export = ExportNameParser.Parse(fileName);
        if (ClosingDayOf(export) is not { } day || export.ExportDate is not { } exported) return null;
        if (columnDate != exported && columnDate != exported.AddDays(-1)) return null;
        return day == columnDate ? null : day;
    }
}
