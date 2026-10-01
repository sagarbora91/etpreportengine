namespace Etp.Reporting.Import.Sources;

/// <summary>
/// Orders two exports by export time (spec 6.3). Never by import order, block number or file date.
/// </summary>
public static class ExportOrder
{
    /// <summary>
    /// How <paramref name="a"/> relates to <paramref name="b"/>:
    /// <list type="bullet">
    /// <item>either unknown: <see cref="ExportOrderResult.Unknown"/>;</item>
    /// <item>either date-only: the dates decide, and an equal date is <see cref="ExportOrderResult.Unknown"/>;</item>
    /// <item>both to the second: the seconds decide, and an equal second is <see cref="ExportOrderResult.Same"/>;</item>
    /// <item>otherwise the minutes decide, and an equal minute is <see cref="ExportOrderResult.Same"/>.</item>
    /// </list>
    /// </summary>
    public static ExportOrderResult Compare(ExportTime a, ExportTime b)
    {
        if (!a.IsKnown || !b.IsKnown) return ExportOrderResult.Unknown;
        if (a.Basis == ExportBasis.Date || b.Basis == ExportBasis.Date)
        {
            var dates = a.ExportDate!.Value.CompareTo(b.ExportDate!.Value);
            return dates == 0 ? ExportOrderResult.Unknown : Result(dates);
        }
        var left = a.Instant!.Value;
        var right = b.Instant!.Value;
        if (a.Basis != ExportBasis.Second || b.Basis != ExportBasis.Second)
        {
            left = Minute(left);
            right = Minute(right);
        }
        var order = left.CompareTo(right);
        return order == 0 ? ExportOrderResult.Same : Result(order);
    }

    /// <summary>True when <paramref name="a"/> is known to be newer than <paramref name="b"/>.</summary>
    public static bool IsNewer(ExportTime a, ExportTime b) => Compare(a, b) == ExportOrderResult.Newer;

    private static ExportOrderResult Result(int comparison) => comparison > 0 ? ExportOrderResult.Newer : ExportOrderResult.Older;

    private static DateTime Minute(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0);
}
