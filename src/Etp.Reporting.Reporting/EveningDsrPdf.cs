using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;

namespace Etp.Reporting.Reporting;

/// <summary>
/// Page 2 of the DSR PDF: the evening store matrices (A4 landscape, for print). 1.12 lane U4 (UIUX-OUTPUTS OUT-02/03/04/
/// 06/07): weekday, stores and generation stamp in the header; spelled-out labels instead of "MTD BLA"/"REQ ADS"/"VOL";
/// whole rupees (the row label carries "(₹)"); text never shrinks below 7 pt (a rupee value that still does not fit is
/// written in lakh/crore); one colour rule (green positive growth, red negative, grey "—"); an 8 pt footnote. The figures
/// are the document's own values: only their presentation changed.
/// </summary>
internal static class EveningDsrPdf
{
    internal const double MinimumFontSize = 7, BodyFontSize = 8;
    internal static readonly string[] Headers = ["", "FTD", "LY FTD", "Growth vs LY", "MTD", "YTD", "LY YTD"];

    public static void Draw(XGraphics g, DailySalesReportDocument report, DateTimeOffset generatedAt)
    {
        var stores = report.EveningSheets.Where(x => x.StoreCode != "COMBINED").ToArray();
        Write(g, $"Daily Sales Report · {OutputText.DateWithWeekday(report.BusinessDate)}", 18, 12, 806, 22, 16, true);
        var storeNames = stores.Length == 0 ? report.Subtitle : string.Join(" + ", stores.Select(x => x.StoreName));
        Write(g, $"{storeNames} · All values incl. GST, in ₹ · Generated {OutputText.Generated(generatedAt)}", 18, 34, 806, 14, 9,
            colour: OutputPalette.SecondaryText);
        for (var i = 0; i < stores.Length; i++)
        {
            var s = stores[i]; var width = (806d - 9 * Math.Max(0, stores.Length - 1)) / Math.Max(1, stores.Length); var x = 18 + i * (width + 9);
            Write(g, s.StoreName, x, 52, width, 18, 12, true);
            Write(g, $"Month target {DsrDisplay.Currency(s.StoreTarget)}    ·    Target per day {DsrDisplay.Currency(s.DayTarget)}", x, 71, width, 14, 8.5);
            Write(g, $"Balance to target {DsrDisplay.Currency(s.Balance)}    ·    Needed per day, incl. today {DsrDisplay.Currency(s.RequiredAds)}", x, 86, width, 14, 8.5);
            Table(g, x, 104, width, 328, s.Rows);
        }
        var combined = report.EveningSheets.FirstOrDefault(x => x.StoreCode == "COMBINED");
        if (combined is not null)
        {
            Write(g, combined.StoreName, 18, 437, 806, 15, 10, true);
            Table(g, 18, 453, 806, 86, combined.Rows.Where(x => x.Metric is "VOL" or "VALUE" or "GIFT CARD" or "INVOICE" or "CONVERSION %").ToArray());
        }
        Write(g, $"Service today · WDC {DsrDisplay.Currency(report.Service.Wdc)} · Cash {DsrDisplay.Currency(report.Service.Cash)} · Card {DsrDisplay.Currency(report.Service.Card)} · UPI {DsrDisplay.Currency(report.Service.Upi)} · Total {DsrDisplay.Currency(report.Service.Total)}",
            18, 543, 806, 14, BodyFontSize);
        Paragraph(g, "All values incl. GST. Bills count invoices (INV) only. Sales, units and bills exclude gift cards, which have their own row. "
            + OutputText.MissingLegend + " Totals of manual entries use the entries made. Needed per day includes today.", 18, 562, 806, 26, BodyFontSize);
    }

    /// <summary>The words a reader sees for an evening row code (OUT-03); a brand row keeps its own name.</summary>
    internal static string Label(string metric) => metric switch
    {
        "VOL" => "Units",
        "VALUE" => "Sales (₹)",
        "AUPT" => "Units per bill",
        "AVPT" => "Value per bill (₹)",
        "INVOICE" => "Bills",
        "CONVERSION %" => "Conversion",
        "RETAIL WALKIN" => "Walk-ins (retail)",
        "WCC WALKIN" => "Walk-ins (WCC)",
        "WCC SALES" => "WCC sales (₹)",
        "WDC BILLS" => "WDC bills",
        "GIFT CARD" => "Gift cards (₹)",
        _ => metric
    };

    /// <summary>The seven cells of one matrix row as printed: label, FTD, LY FTD, growth, MTD, YTD, LY YTD.</summary>
    internal static string[] Cells(EveningMetricRow row) =>
        [Label(row.Metric), Value(row.Ftd, row.Format), Value(row.Ly, row.Format), Growth(row.Growth), Value(row.Mtd, row.Format), Value(row.Ytd, row.Format), Value(row.LyYtd, row.Format)];

    internal static string Value(decimal? value, string format) => value is null ? DsrDisplay.Missing : format switch
    {
        "currency" => value.Value.ToString("#,##0;-#,##0;0", OutputText.India),
        "ratio" => value.Value.ToString("0.00", OutputText.India),
        "percent" => value.Value.ToString("0.0", OutputText.India) + "%",
        _ => value.Value.ToString("#,##0.##", OutputText.India)
    };

    internal static string Growth(decimal? value) => value is null ? DsrDisplay.Missing : value.Value.ToString("+0.0;-0.0;0.0", OutputText.India) + "%";

    /// <summary>A rupee value in lakh/crore without the symbol, used only when the whole figure cannot fit at 7 pt.</summary>
    internal static string Compact(decimal? value, string format)
    {
        if (value is null || format != "currency") return Value(value, format);
        var absolute = Math.Abs(value.Value); var sign = value < 0 ? "-" : string.Empty;
        if (absolute >= 10_000_000m) return $"{sign}{(absolute / 10_000_000m).ToString("0.00", OutputText.India)} Cr";
        if (absolute >= 100_000m) return $"{sign}{(absolute / 100_000m).ToString("0.00", OutputText.India)} L";
        return Value(value, format);
    }

    private static void Table(XGraphics g, double x, double y, double width, double height, IReadOnlyList<EveningMetricRow> rows)
    {
        var rowHeight = height / (rows.Count + 1); var labelWidth = width * .23; var numberWidth = (width - labelWidth) / 6;
        var size = Math.Clamp(rowHeight - 3, MinimumFontSize, BodyFontSize);
        g.DrawRectangle(new XSolidBrush(Colour(OutputPalette.Divider)), x, y, width, rowHeight);
        for (var r = 0; r <= rows.Count; r++)
        {
            if (r > 0 && r % 2 == 0) g.DrawRectangle(new XSolidBrush(Colour(OutputPalette.SurfaceSecondary)), x, y + r * rowHeight, width, rowHeight);
            var row = r == 0 ? null : rows[r - 1];
            var cells = row is null ? Headers : Cells(row);
            for (var c = 0; c < 7; c++)
            {
                var left = x + (c == 0 ? 0 : labelWidth + (c - 1) * numberWidth);
                var colour = row is null ? OutputPalette.PrimaryText
                    : cells[c] == DsrDisplay.Missing ? OutputPalette.SecondaryText
                    : c == 3 && row.Growth > 0 ? OutputPalette.Success
                    : c == 3 && row.Growth < 0 ? OutputPalette.Critical
                    : OutputPalette.PrimaryText;
                var fallback = row is null || c is 0 or 3 ? null : Compact(c switch { 1 => row.Ftd, 2 => row.Ly, 4 => row.Mtd, 5 => row.Ytd, _ => row.LyYtd }, row.Format);
                Write(g, cells[c], left, y + r * rowHeight, c == 0 ? labelWidth : numberWidth, rowHeight, size, r == 0 || c == 0, colour,
                    c == 0 ? XStringFormats.CenterLeft : XStringFormats.CenterRight, fallback);
            }
        }
    }

    private static void Write(XGraphics g, string text, double x, double y, double width, double height, double size, bool bold = false,
        string colour = OutputPalette.PrimaryText, XStringFormat? format = null, string? fallback = null)
    {
        var fitted = Fit(g, text, width - 6, size, bold);
        if (fitted is null && fallback is not null && fallback != text) { text = fallback; fitted = Fit(g, text, width - 6, size, bold); }
        var font = Font(fitted ?? MinimumFontSize, bold);
        g.DrawString(text, font, new XSolidBrush(Colour(colour)), new XRect(x + 3, y, width - 6, height), format ?? XStringFormats.CenterLeft);
    }

    /// <summary>The largest size from <paramref name="size"/> down to 7 pt at which the text fits; null when it never does.</summary>
    private static double? Fit(XGraphics g, string text, double width, double size, bool bold)
    {
        for (var current = size; current >= MinimumFontSize - 0.001; current -= .2)
            if (g.MeasureString(text, Font(current, bold)).Width <= width) return current;
        return null;
    }

    private static void Paragraph(XGraphics g, string text, double x, double y, double width, double height, double size) =>
        new XTextFormatter(g).DrawString(text, Font(size, false), new XSolidBrush(Colour(OutputPalette.SecondaryText)), new XRect(x, y, width, height));

    private static XFont Font(double size, bool bold) => new("Segoe UI", size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
    internal static XColor Colour(string hex) => XColor.FromArgb(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
}
