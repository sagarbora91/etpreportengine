using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Etp.Reporting.Reporting;

public sealed class DailySalesReportPdfExporter
{
    public const double PageWidth = 841.89;
    public const double PageHeight = 595.28;

    public void Export(string path, DailySalesReportDocument report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(report);
        DsrPdfFontResolver.EnsureRegistered();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var document = new PdfDocument();
        document.Info.Title = report.Title;
        document.Info.Subject = "ETP governed Daily Sales Report";
        var page = document.AddPage(); page.Width = XUnit.FromPoint(PageWidth); page.Height = XUnit.FromPoint(PageHeight);
        using (var gfx = XGraphics.FromPdfPage(page)) Draw(gfx, report);
        document.Save(path);
        ValidateSingleA4LandscapePage(path);
    }

    public static void ValidateSingleA4LandscapePage(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        if (document.PageCount != 1) throw new InvalidDataException($"DSR PDF must contain exactly one page; found {document.PageCount}.");
        var page = document.Pages[0];
        if (Math.Abs(page.Width.Point - PageWidth) > 0.75 || Math.Abs(page.Height.Point - PageHeight) > 0.75)
            throw new InvalidDataException($"DSR PDF must be A4 landscape; found {page.Width.Point:0.##} × {page.Height.Point:0.##} pt.");
    }

    private static void Draw(XGraphics g, DailySalesReportDocument r)
    {
        var navy = Colour("#162034"); var muted = Colour("#687285"); var line = Colour("#DCE4EF"); var page = Colour("#F4F7FB");
        g.DrawRectangle(new XSolidBrush(page), 0, 0, PageWidth, PageHeight);
        Text(g, "ETP REPORTING ENGINE", 6.5, true, muted, 18, 14, 300, 10);
        Text(g, r.Title, 22, true, navy, 18, 30, 520, 28);
        Text(g, r.Subtitle, 8, false, muted, 18, 59, 520, 12);
        Card(g, 694.9, 22, 129, 43, "#FFFFFF", "#DCE4EF", 8);
        Text(g, r.BusinessDate.ToString("dd MMM yyyy"), 12, true, navy, 697, 29, 125, 18, XParagraphAlignment.Center);
        Text(g, r.Weekday(), 8, false, muted, 697, 49, 125, 12, XParagraphAlignment.Center);

        var cards = new[]
        {
            ("COMBINED FTD", DsrDisplay.Currency(r.CombinedFtd), $"{DsrDisplay.Percent(r.CombinedFtdGrowth)} vs LY", "#2269E8"),
            ("UNITS", DsrDisplay.Number(r.Units, 0), r.Units is null ? "Data not available" : StoreSplit(r, x => x.Periods[0].TyQuantity), "#08A9DE"),
            ("WALK-INS", DsrDisplay.Number(r.WalkIns, 0), r.WalkIns is null ? "Data not available" : StoreWalkInSplit(r), "#7137D4"),
            ("CONVERSION", DsrDisplay.Percent(r.Conversion).TrimStart('+'), r.Conversion.Value is null ? "Data not available" : $"{DsrDisplay.Number(r.CombinedInvoices, 0)} invoices / {DsrDisplay.Number(r.WalkIns, 0)} walk-ins", "#07965C"),
            ("MTD SALES", DsrDisplay.CompactCurrency(r.MtdSales), $"{DsrDisplay.Percent(r.MtdTargetAchievement).TrimStart('+')} of target", "#2269E8"),
            ("YTD SALES", DsrDisplay.CompactCurrency(r.YtdSales), $"{DsrDisplay.Percent(r.YtdGrowth)} vs LY YTD", "#07965C")
        };
        const double kpiY = 83, gap = 9, cardWidth = 126.815;
        for (var i = 0; i < cards.Length; i++) Kpi(g, 18 + i * (cardWidth + gap), kpiY, cardWidth, 60, cards[i]);
        StoreCard(g, 18, 159, 398.445, 118, r.Stores[0], "#EAF2FF"); StoreCard(g, 425.445, 159, 398.445, 118, r.Stores[1], "#F2ECFF");
        OperationalCard(g, 18, 289.276, 398.445, 126, r.Stores[0]); OperationalCard(g, 425.445, 289.276, 398.445, 126, r.Stores[1]);
        ServiceCard(g, 18, 427.276, 398.445, 150, r.Service); TargetCard(g, 425.445, 427.276, 398.445, 150, r.Targets);
        Text(g, "All amounts in INR · FTD = For the Day · MTD = Month to Date · YTD = Year to Date", 5.3, false, muted, 200, 582, 442, 9, XParagraphAlignment.Center);
    }

    private static void Kpi(XGraphics g, double x, double y, double w, double h, (string Label, string Value, string Secondary, string Accent) card)
    {
        Card(g, x, y, w, h, "#FFFFFF", "#DCE4EF", 8); FillRounded(g, card.Accent, x, y, w, 3, 1.5);
        Text(g, card.Label, 7.5, false, Colour("#687285"), x + 10, y + 12, w - 20, 10);
        Text(g, card.Value, 17, true, Colour("#162034"), x + 10, y + 23, w - 20, 22);
        Text(g, card.Secondary, 7, false, Colour("#687285"), x + 10, y + 46, w - 20, 10);
    }

    private static void StoreCard(XGraphics g, double x, double y, double w, double h, DsrStoreCard store, string tint)
    {
        Card(g, x, y, w, h, "#FFFFFF", "#DCE4EF", 8); FillRounded(g, store.Accent, x, y, w, 4, 2);
        Text(g, store.DisplayName, 12, true, Colour("#162034"), x + 10, y + 10, w - 20, 16);
        var widths = new[] { 62d, 106d, 106d, 67d, 57d }; var labels = new[] { "Period", "TY Value / Qty", "LY Value / Qty", "Value Growth", "Qty Growth" }; var left = x;
        for (var i = 0; i < labels.Length; i++) { Text(g, labels[i], 6.5, false, Colour("#687285"), left, y + 31, widths[i], 10, XParagraphAlignment.Center); left += widths[i]; }
        for (var row = 0; row < 3; row++)
        {
            var p = store.Periods[row]; var top = y + 45 + row * 25;
            if (row != 1) g.DrawRectangle(new XSolidBrush(Colour(tint)), x + 7, top, w - 14, 25);
            var valueGrowth = p.MissingSourceNote is not null ? "—" : DsrDisplay.Percent(p.ValueGrowth);
            var quantityGrowth = p.MissingSourceNote is not null ? "—" : DsrDisplay.Percent(p.QuantityGrowth);
            var values = new[] { p.Period, DsrDisplay.ValueQuantity(p.TyValue, p.TyQuantity), DsrDisplay.ValueQuantity(p.LyValue, p.LyQuantity), valueGrowth, quantityGrowth };
            left = x; for (var i = 0; i < values.Length; i++) { Text(g, values[i], i == 0 ? 7.3 : 6.6, i == 0, i >= 3 && p.MissingSourceNote is null ? Colour("#07965C") : Colour("#162034"), left, top + 7, widths[i], 10, XParagraphAlignment.Center); left += widths[i]; }
            if (p.MissingSourceNote is not null) Text(g, p.MissingSourceNote, 5.5, false, Colour("#C97800"), x + 205, top + 17, 165, 8, XParagraphAlignment.Center);
        }
    }

    private static void OperationalCard(XGraphics g, double x, double y, double w, double h, DsrStoreCard store)
    {
        Card(g, x, y, w, h, "#FFFFFF", "#DCE4EF", 8); Text(g, store.DisplayName, 10, true, Colour("#162034"), x + 10, y + 9, w - 20, 13);
        FillRounded(g, "#EDF2F8", x + 7, y + 20, w - 14, 18, 6);
        var widths = new[] { 70d, 90d, 90d, 70d, 78d }; var labels = new[] { "Metric", "FTD", "LY", "Change", "MTD / YTD" }; var left = x;
        for (var i = 0; i < labels.Length; i++) { Text(g, labels[i], 6.5, false, Colour("#687285"), left, y + 25, widths[i], 9, XParagraphAlignment.Center); left += widths[i]; }
        for (var row = 0; row < store.OperationalMetrics.Count; row++)
        {
            var m = store.OperationalMetrics[row]; var top = y + 44 + row * 41; g.DrawLine(new XPen(Colour("#DCE4EF"), .5), x + 8, top + 32, x + w - 8, top + 32);
            var values = new[] { m.Name, m.IsCurrency ? DsrDisplay.Currency(m.Ftd) : DsrDisplay.Number(m.Ftd), m.IsCurrency ? DsrDisplay.Currency(m.LastYear) : DsrDisplay.Number(m.LastYear), DsrDisplay.Percent(m.Change), m.Context };
            left = x; for (var i = 0; i < values.Length; i++) { Text(g, values[i], 6.7, i is 0 or 1, i == 3 ? Colour("#07965C") : Colour("#162034"), left, top + 12, widths[i], 10, XParagraphAlignment.Center); left += widths[i]; }
        }
    }

    private static void ServiceCard(XGraphics g, double x, double y, double w, double h, DsrServiceSummary service)
    {
        Card(g, x, y, w, h, "#FFFFFF", "#DCE4EF", 8); FillRounded(g, "#08A9DE", x, y, w, 3, 1.5); Text(g, "Service", 10, true, Colour("#162034"), x + 10, y + 10, w - 20, 13);
        var tenderLabels = new[] { "WDC", "Cash", "Card", "UPI", "Total" }; var tenderValues = new[] { service.Wdc, service.Cash, service.Card, service.Upi, service.Total }; var cell = (w - 20) / 5;
        for (var i = 0; i < 5; i++) { Text(g, tenderLabels[i], 6.3, false, Colour("#687285"), x + 10 + i * cell, y + 34, cell, 9, XParagraphAlignment.Center); Text(g, DsrDisplay.Number(tenderValues[i], 0), 8, true, Colour("#162034"), x + 10 + i * cell, y + 47, cell, 10, XParagraphAlignment.Center); }
        g.DrawLine(new XPen(Colour("#DCE4EF"), .7), x + 8, y + 106, x + w - 8, y + 106);
        var periods = new[] { "FTD", "LY FTD", "MTD", "LY MTD", "YTD", "LY YTD" }; cell = (w - 16) / 6;
        for (var i = 0; i < periods.Length; i++) { Text(g, periods[i], 5.8, false, Colour("#687285"), x + 8 + i * cell, y + 113, cell, 8, XParagraphAlignment.Center); Text(g, DsrDisplay.Number(service.PeriodTotals.GetValueOrDefault(periods[i]), 0), 7.5, i is 0 or 2 or 4, i is 2 or 4 ? Colour("#2269E8") : Colour("#162034"), x + 8 + i * cell, y + 128, cell, 10, XParagraphAlignment.Center); }
    }

    private static void TargetCard(XGraphics g, double x, double y, double w, double h, IReadOnlyList<DsrTargetProgress> targets)
    {
        Card(g, x, y, w, h, "#FFFFFF", "#DCE4EF", 8); Text(g, "Monthly Target Progress", 10, true, Colour("#162034"), x + 10, y + 10, w - 20, 13); Text(g, "MTD actual vs monthly target", 6.5, false, Colour("#687285"), x + 10, y + 26, w - 20, 10);
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i]; var top = y + 48 + i * 31; Text(g, target.DisplayName, 6.8, true, Colour("#162034"), x + 10, top, 44, 10);
            var trackX = x + 56; var trackW = w - 68; FillRounded(g, "#DCE4EF", trackX, top + 2, trackW, 7, 3.5); FillRounded(g, target.Accent, trackX, top + 2, trackW * (double)(target.FillPercent / 100m), 7, 3.5);
            Text(g, DsrDisplay.Percent(target.Achievement).TrimStart('+'), 6.5, true, Colour(target.Accent), trackX, top, trackW, 11, XParagraphAlignment.Right);
        }
    }

    private static string StoreSplit(DailySalesReportDocument report, Func<DsrStoreCard, decimal?> selector) => $"Titan {DsrDisplay.Number(selector(report.Stores[0]), 0)} · Helios {DsrDisplay.Number(selector(report.Stores[1]), 0)}";
    private static string StoreWalkInSplit(DailySalesReportDocument report) => $"Titan {DsrDisplay.Number(report.Stores[0].FtdWalkIns, 0)} · Helios {DsrDisplay.Number(report.Stores[1].FtdWalkIns, 0)}";

    private static void Card(XGraphics g, double x, double y, double w, double h, string fill, string stroke, double radius) => g.DrawRoundedRectangle(new XPen(Colour(stroke), .8), new XSolidBrush(Colour(fill)), x, y, w, h, radius, radius);
    private static void FillRounded(XGraphics g, string fill, double x, double y, double w, double h, double radius) => g.DrawRoundedRectangle(XPens.Transparent, new XSolidBrush(Colour(fill)), x, y, w, h, radius, radius);
    private static void Text(XGraphics g, string value, double size, bool bold, XColor colour, double x, double y, double w, double h, XParagraphAlignment alignment = XParagraphAlignment.Left)
    {
        var formatter = new XTextFormatter(g) { Alignment = alignment }; formatter.DrawString(value ?? string.Empty, new XFont("Segoe UI", size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular), new XSolidBrush(colour), new XRect(x, y, w, h));
    }
    private static XColor Colour(string hex) => XColor.FromArgb(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
}

internal sealed class DsrPdfFontResolver : IFontResolver
{
    private static readonly object Gate = new(); private static bool registered;
    public static void EnsureRegistered() { lock (Gate) { if (registered) return; GlobalFontSettings.FontResolver = new DsrPdfFontResolver(); registered = true; } }
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(isBold ? "SegoeUI-Bold" : "SegoeUI-Regular");
    public byte[] GetFont(string faceName)
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var file = faceName == "SegoeUI-Bold" ? "segoeuib.ttf" : "segoeui.ttf";
        var path = Path.Combine(fonts, file); if (!File.Exists(path)) throw new FileNotFoundException("Segoe UI is required for DSR PDF export.", path); return File.ReadAllBytes(path);
    }
}
