using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Etp.Reporting.Reporting;

/// <summary>
/// The DSR PDF. Decision 45 #4 (12 Oct 2026, UIUX-OUTPUTS OUT-01/05): page 1 is the designed one-page summary (KPI row,
/// store cards, service and targets: the August mock-up, before this only reachable when no evening sheets existed);
/// page 2 is the evening store matrix (A4 landscape, for print) when the document carries evening sheets; the last page
/// is a portrait phone page (400 pt wide, 13 pt and larger text) that a phone viewer shows full width without zooming.
/// Every figure comes from the <see cref="DailySalesReportDocument"/> unchanged; this class only lays it out.
/// </summary>
public sealed class DailySalesReportPdfExporter
{
    public const double PageWidth = 841.89;
    public const double PageHeight = 595.28;
    public const double PhonePageWidth = 400;
    public const double PhonePageMinimumHeight = 700;

    public void Export(string path, DailySalesReportDocument report) => Export(path, report, DateTimeOffset.Now);

    public void Export(string path, DailySalesReportDocument report, DateTimeOffset generatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(report);
        DsrPdfFontResolver.EnsureRegistered();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var document = new PdfDocument();
        document.Info.Title = report.Title;
        document.Info.Subject = "ETP governed Daily Sales Report";
        var summary = document.AddPage(); summary.Width = XUnit.FromPoint(PageWidth); summary.Height = XUnit.FromPoint(PageHeight);
        using (var gfx = XGraphics.FromPdfPage(summary)) Draw(gfx, report, generatedAt);
        if (report.EveningSheets.Count > 0)
        {
            var matrix = document.AddPage(); matrix.Width = XUnit.FromPoint(PageWidth); matrix.Height = XUnit.FromPoint(PageHeight);
            using var gfx = XGraphics.FromPdfPage(matrix); EveningDsrPdf.Draw(gfx, report, generatedAt);
        }
        var lines = DsrPhonePage.Lines(report, generatedAt);
        var phone = document.AddPage(); phone.Width = XUnit.FromPoint(PhonePageWidth); phone.Height = XUnit.FromPoint(DsrPhonePage.Height(lines));
        using (var gfx = XGraphics.FromPdfPage(phone)) DsrPhonePage.Draw(gfx, lines);
        document.Save(path);
        ValidateLayout(path);
    }

    /// <summary>
    /// Page 1 (and page 2 when there are three pages) A4 landscape; the last page the portrait phone page. Two or three
    /// pages: summary, optional evening matrix, phone page.
    /// </summary>
    public static void ValidateLayout(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        if (document.PageCount is < 2 or > 3) throw new InvalidDataException($"DSR PDF must contain two or three pages; found {document.PageCount}.");
        for (var i = 0; i < document.PageCount - 1; i++)
        {
            var page = document.Pages[i];
            if (Math.Abs(page.Width.Point - PageWidth) > 0.75 || Math.Abs(page.Height.Point - PageHeight) > 0.75)
                throw new InvalidDataException($"DSR PDF page {i + 1} must be A4 landscape; found {page.Width.Point:0.##} × {page.Height.Point:0.##} pt.");
        }
        var last = document.Pages[document.PageCount - 1];
        if (Math.Abs(last.Width.Point - PhonePageWidth) > 0.75 || last.Height.Point < PhonePageMinimumHeight - 0.75)
            throw new InvalidDataException($"DSR PDF last page must be the portrait phone page; found {last.Width.Point:0.##} × {last.Height.Point:0.##} pt.");
    }

    private static void Draw(XGraphics g, DailySalesReportDocument r, DateTimeOffset generatedAt)
    {
        var primary = Colour(OutputPalette.PrimaryText); var muted = Colour(OutputPalette.SecondaryText);
        g.DrawRectangle(new XSolidBrush(Colour(OutputPalette.AppBackground)), 0, 0, PageWidth, PageHeight);
        Text(g, $"ETP REPORTING ENGINE · GENERATED {OutputText.Generated(generatedAt).ToUpperInvariant()}", 6.5, true, muted, 18, 14, 400, 10);
        Text(g, r.Title, 22, true, primary, 18, 30, 520, 28);
        Text(g, r.Subtitle, 8, false, muted, 18, 59, 520, 12);
        Card(g, 694.9, 22, 129, 43, OutputPalette.Surface, OutputPalette.Divider, 8);
        Text(g, OutputText.Date(r.BusinessDate), 12, true, primary, 697, 29, 125, 18, XParagraphAlignment.Center);
        Text(g, r.Weekday(), 8, false, muted, 697, 49, 125, 12, XParagraphAlignment.Center);

        var cards = DsrKpiCards.For(r);
        const double kpiY = 83, gap = 9, cardWidth = 126.815;
        for (var i = 0; i < cards.Count; i++) Kpi(g, 18 + i * (cardWidth + gap), kpiY, cardWidth, 60, cards[i]);
        var width=(PageWidth-36-9*Math.Max(0,r.Stores.Count-1))/Math.Max(1,r.Stores.Count);
        for(var index=0;index<r.Stores.Count;index++) { StoreCard(g,18+index*(width+9),159,width,118,r.Stores[index]); OperationalCard(g,18+index*(width+9),289.276,width,126,r.Stores[index]); }
        ServiceCard(g, 18, 427.276, 398.445, 150, r.Service); TargetCard(g, 425.445, 427.276, 398.445, 150, r.Targets);
        Text(g, $"All amounts in ₹, incl. GST · FTD = for the day · MTD = month to date · YTD = year to date · Gift cards (FTD {DsrDisplay.Currency(r.CombinedFtdGiftCards)}) are not in value, units or invoices · {OutputText.MissingLegend}", 6.5, false, muted, 60, 581, 722, 10, XParagraphAlignment.Center);
    }

    private static void Kpi(XGraphics g, double x, double y, double w, double h, DsrKpiCard card)
    {
        // Decision 45 #1 (design rule Q2): no decorative stripe on DSR tiles; numbers are dark.
        Card(g, x, y, w, h, OutputPalette.Surface, OutputPalette.Divider, 8);
        Text(g, card.Label, 7.5, false, Colour(OutputPalette.SecondaryText), x + 10, y + 10, w - 20, 10);
        Text(g, card.Value, 17, true, Colour(OutputPalette.PrimaryText), x + 10, y + 21, w - 20, 22);
        Text(g, card.Secondary, 7, false, Colour(OutputPalette.SecondaryText), x + 10, y + 44, w - 20, 10);
    }

    private static void StoreCard(XGraphics g, double x, double y, double w, double h, DsrStoreCard store)
    {
        Card(g, x, y, w, h, OutputPalette.Surface, OutputPalette.Divider, 8);
        Text(g, store.DisplayName, 12, true, Colour(OutputPalette.PrimaryText), x + 10, y + 10, w - 20, 16);
        var widths = new[] { 62d, 106d, 106d, 67d, 57d }; var labels = new[] { "Period", "This year value / qty", "Last year value / qty", "Value growth", "Qty growth" }; var left = x;
        for (var i = 0; i < labels.Length; i++) { Text(g, labels[i], 6.5, false, Colour(OutputPalette.SecondaryText), left, y + 31, widths[i], 10, XParagraphAlignment.Center); left += widths[i]; }
        for (var row = 0; row < 3; row++)
        {
            var p = store.Periods[row]; var top = y + 45 + row * 25;
            if (row != 1) g.DrawRectangle(new XSolidBrush(Colour(OutputPalette.SurfaceSecondary)), x + 7, top, w - 14, 25);
            var valueGrowth = p.MissingSourceNote is not null ? DsrDisplay.Missing : DsrDisplay.Percent(p.ValueGrowth);
            var quantityGrowth = p.MissingSourceNote is not null ? DsrDisplay.Missing : DsrDisplay.Percent(p.QuantityGrowth);
            var values = new[] { p.Period, DsrDisplay.ValueQuantity(p.TyValue, p.TyQuantity), DsrDisplay.ValueQuantity(p.LyValue, p.LyQuantity), valueGrowth, quantityGrowth };
            var growthColours = new[] { GrowthColour(p.MissingSourceNote is null ? p.ValueGrowth.Value : null), GrowthColour(p.MissingSourceNote is null ? p.QuantityGrowth.Value : null) };
            left = x; for (var i = 0; i < values.Length; i++) { Text(g, values[i], i == 0 ? 7.3 : 6.6, i == 0, i >= 3 ? growthColours[i - 3] : Colour(OutputPalette.PrimaryText), left, top + 7, widths[i], 10, XParagraphAlignment.Center); left += widths[i]; }
            if (p.MissingSourceNote is not null) Text(g, p.MissingSourceNote, 5.5, false, Colour(OutputPalette.Warning), x + 205, top + 17, 165, 8, XParagraphAlignment.Center);
        }
    }

    private static void OperationalCard(XGraphics g, double x, double y, double w, double h, DsrStoreCard store)
    {
        Card(g, x, y, w, h, OutputPalette.Surface, OutputPalette.Divider, 8); Text(g, store.DisplayName, 10, true, Colour(OutputPalette.PrimaryText), x + 10, y + 9, w - 20, 13);
        FillRounded(g, OutputPalette.SurfaceSecondary, x + 7, y + 20, w - 14, 18, 6);
        var widths = new[] { 70d, 90d, 90d, 70d, 78d }; var labels = new[] { "Metric", "FTD", "LY", "Change", "MTD / YTD" }; var left = x;
        for (var i = 0; i < labels.Length; i++) { Text(g, labels[i], 6.5, false, Colour(OutputPalette.SecondaryText), left, y + 25, widths[i], 9, XParagraphAlignment.Center); left += widths[i]; }
        for (var row = 0; row < store.OperationalMetrics.Count; row++)
        {
            var m = store.OperationalMetrics[row]; var top = y + 44 + row * 41; g.DrawLine(new XPen(Colour(OutputPalette.Divider), .5), x + 8, top + 32, x + w - 8, top + 32);
            var values = new[] { MetricName(m.Name), m.IsCurrency ? DsrDisplay.Currency(m.Ftd) : DsrDisplay.Number(m.Ftd), m.IsCurrency ? DsrDisplay.Currency(m.LastYear) : DsrDisplay.Number(m.LastYear), DsrDisplay.Percent(m.Change), m.Context };
            left = x; for (var i = 0; i < values.Length; i++) { Text(g, values[i], 6.7, i is 0 or 1, i == 3 ? GrowthColour(m.Change.Value) : Colour(OutputPalette.PrimaryText), left, top + 12, widths[i], 10, XParagraphAlignment.Center); left += widths[i]; }
        }
    }

    private static void ServiceCard(XGraphics g, double x, double y, double w, double h, DsrServiceSummary service)
    {
        Card(g, x, y, w, h, OutputPalette.Surface, OutputPalette.Divider, 8); Text(g, "Service (₹)", 10, true, Colour(OutputPalette.PrimaryText), x + 10, y + 10, w - 20, 13);
        var tenderLabels = new[] { "WDC", "Cash", "Card", "UPI", "Total" }; var tenderValues = new[] { service.Wdc, service.Cash, service.Card, service.Upi, service.Total }; var cell = (w - 20) / 5;
        for (var i = 0; i < 5; i++) { Text(g, tenderLabels[i], 6.5, false, Colour(OutputPalette.SecondaryText), x + 10 + i * cell, y + 34, cell, 9, XParagraphAlignment.Center); Text(g, DsrDisplay.Number(tenderValues[i], 0), 8, true, Colour(OutputPalette.PrimaryText), x + 10 + i * cell, y + 47, cell, 10, XParagraphAlignment.Center); }
        g.DrawLine(new XPen(Colour(OutputPalette.Divider), .7), x + 8, y + 106, x + w - 8, y + 106);
        var periods = new[] { "FTD", "LY FTD", "MTD", "LY MTD", "YTD", "LY YTD" }; cell = (w - 16) / 6;
        for (var i = 0; i < periods.Length; i++) { Text(g, periods[i], 6.5, false, Colour(OutputPalette.SecondaryText), x + 8 + i * cell, y + 113, cell, 8, XParagraphAlignment.Center); Text(g, DsrDisplay.Number(service.PeriodTotals.GetValueOrDefault(periods[i]), 0), 7.5, i is 0 or 2 or 4, Colour(OutputPalette.PrimaryText), x + 8 + i * cell, y + 128, cell, 10, XParagraphAlignment.Center); }
    }

    private static void TargetCard(XGraphics g, double x, double y, double w, double h, IReadOnlyList<DsrTargetProgress> targets)
    {
        Card(g, x, y, w, h, OutputPalette.Surface, OutputPalette.Divider, 8); Text(g, "Monthly target progress", 10, true, Colour(OutputPalette.PrimaryText), x + 10, y + 10, w - 20, 13); Text(g, "Month-to-date sales against the monthly target", 6.5, false, Colour(OutputPalette.SecondaryText), x + 10, y + 26, w - 20, 10);
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i]; var top = y + 48 + i * 31; Text(g, target.DisplayName, 6.8, true, Colour(OutputPalette.PrimaryText), x + 10, top, 44, 10);
            var trackX = x + 56; var trackW = w - 68; FillRounded(g, OutputPalette.Divider, trackX, top + 2, trackW, 7, 3.5); FillRounded(g, OutputPalette.Accent, trackX, top + 2, trackW * (double)(target.FillPercent / 100m), 7, 3.5);
            Text(g, DsrDisplay.Percent(target.Achievement).TrimStart('+'), 6.5, true, Colour(OutputPalette.PrimaryText), trackX, top, trackW, 11, XParagraphAlignment.Right);
        }
    }

    private static string MetricName(string name) => name switch { "AUPT" => "Units per bill", "AVPT" => "Value per bill", _ => name };
    private static XColor GrowthColour(decimal? value) => Colour(value is null ? OutputPalette.SecondaryText : value > 0 ? OutputPalette.Success : value < 0 ? OutputPalette.Critical : OutputPalette.PrimaryText);
    private static void Card(XGraphics g, double x, double y, double w, double h, string fill, string stroke, double radius) => g.DrawRoundedRectangle(new XPen(Colour(stroke), .8), new XSolidBrush(Colour(fill)), x, y, w, h, radius, radius);
    private static void FillRounded(XGraphics g, string fill, double x, double y, double w, double h, double radius) => g.DrawRoundedRectangle(XPens.Transparent, new XSolidBrush(Colour(fill)), x, y, w, h, radius, radius);
    private static void Text(XGraphics g, string value, double size, bool bold, XColor colour, double x, double y, double w, double h, XParagraphAlignment alignment = XParagraphAlignment.Left)
    {
        var formatter = new XTextFormatter(g) { Alignment = alignment }; formatter.DrawString(value ?? string.Empty, new XFont("Segoe UI", size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular), new XSolidBrush(colour), new XRect(x, y, w, h));
    }
    private static XColor Colour(string hex) => EveningDsrPdf.Colour(hex);
}

/// <summary>One line of the DSR phone page: text, size, weight and colour (a blank text is a divider).</summary>
internal sealed record DsrPhoneLine(string Text, double Size, bool Bold = false, string Colour = OutputPalette.PrimaryText, double SpaceBefore = 0)
{
    public bool IsDivider => Text.Length == 0;
}

/// <summary>
/// The DSR phone page (decision 45 #4, OUT-05, MOCK-OUT-05-after): portrait, 400 pt wide, 13 pt body, in the order the
/// reader asks: how much today, against last year, bills and walk-ins, month and year, each store with its target, the
/// service money. Built from the document's figures with the same <see cref="DsrDisplay"/> formats as the summary page.
/// </summary>
internal static class DsrPhonePage
{
    private const double Margin = 22, ContentWidth = DailySalesReportPdfExporter.PhonePageWidth - 2 * Margin;

    public static IReadOnlyList<DsrPhoneLine> Lines(DailySalesReportDocument r, DateTimeOffset generatedAt)
    {
        var muted = OutputPalette.SecondaryText;
        var storeNames = r.Stores.Count == 0 ? r.Subtitle : string.Join(" + ", r.Stores.Select(x => x.DisplayName));
        var lines = new List<DsrPhoneLine>
        {
            new("ETP · DAILY SALES REPORT", 10, true, muted),
            new(OutputText.DateWithWeekday(r.BusinessDate), 20, true, SpaceBefore: 2),
            new($"{storeNames} · generated {OutputText.Generated(generatedAt)}", 11, false, muted),
            new("SALES TODAY (incl. GST, excl. gift cards)", 10, true, muted, 14),
            new(DsrDisplay.Currency(r.CombinedFtd), 30, true),
            new(r.CombinedFtdGrowth.Value is null ? "Last year: — (not available)" : $"{DsrDisplay.Percent(r.CombinedFtdGrowth)} vs last year", 13, true, GrowthColour(r.CombinedFtdGrowth.Value)),
            new($"Bills {DsrDisplay.Number(r.CombinedInvoices, 0)} · Walk-ins {DsrDisplay.Number(r.WalkIns, 0)} · Conversion {DsrDisplay.Percent(r.Conversion).TrimStart('+')}", 13, SpaceBefore: 10)
        };
        var combinedTarget = r.Targets.FirstOrDefault(x => x.StoreCode == "COMBINED")?.MonthlyTarget;
        lines.Add(new($"Month so far {DsrDisplay.CompactCurrency(r.MtdSales)} · " + (combinedTarget is null
            ? "target not set" : $"{DsrDisplay.Percent(r.MtdTargetAchievement).TrimStart('+')} of {DsrDisplay.CompactCurrency(combinedTarget)} target"), 13));
        lines.Add(new($"Year so far {DsrDisplay.CompactCurrency(r.YtdSales)} · {DsrDisplay.Percent(r.YtdGrowth)} vs last year", 13));
        foreach (var store in r.Stores)
        {
            lines.Add(new(string.Empty, 1, SpaceBefore: 10));
            lines.Add(new(store.DisplayName, 15, true, SpaceBefore: 4));
            var today = store.Periods.FirstOrDefault(x => x.Period == "FTD"); var month = store.Periods.FirstOrDefault(x => x.Period == "MTD");
            lines.Add(new($"Today {DsrDisplay.Currency(today?.TyValue)} · {(today is null ? DsrDisplay.Missing : DsrDisplay.Percent(today.ValueGrowth))} vs last year", 13));
            var target = r.Targets.FirstOrDefault(x => string.Equals(x.StoreCode, store.StoreCode, StringComparison.OrdinalIgnoreCase));
            lines.Add(new($"Month {DsrDisplay.Currency(month?.TyValue)} · " + (target?.MonthlyTarget is null ? "target not set" : $"{DsrDisplay.Percent(target.Achievement).TrimStart('+')} of target"), 13));
            var evening = r.EveningSheets.FirstOrDefault(x => string.Equals(x.StoreCode, store.StoreCode, StringComparison.OrdinalIgnoreCase));
            if (evening?.RequiredAds is not null) lines.Add(new($"Needed per day {DsrDisplay.Currency(evening.RequiredAds)} (incl. today)", 13));
        }
        if (r.Service.Total is not null || r.Service.PeriodTotals.GetValueOrDefault("MTD") is not null)
        {
            lines.Add(new(string.Empty, 1, SpaceBefore: 10));
            lines.Add(new("Service", 15, true, SpaceBefore: 4));
            lines.Add(new($"Today {DsrDisplay.Currency(r.Service.Total)} · Cash {DsrDisplay.Number(r.Service.Cash, 0)} · Card {DsrDisplay.Number(r.Service.Card, 0)} · UPI {DsrDisplay.Number(r.Service.Upi, 0)}", 13));
            lines.Add(new($"Month {DsrDisplay.Currency(r.Service.PeriodTotals.GetValueOrDefault("MTD"))}", 13));
        }
        lines.Add(new(string.Empty, 1, SpaceBefore: 10));
        lines.Add(new($"All values incl. GST. Bills = invoices only. {OutputText.MissingLegend} The pages before this one are the full DSR for print.", 10, false, muted, 4));
        return lines;
    }

    public static double Height(IReadOnlyList<DsrPhoneLine> lines) =>
        Math.Max(DailySalesReportPdfExporter.PhonePageMinimumHeight, 2 * Margin + lines.Sum(line => line.SpaceBefore + LineCount(line) * LineHeight(line)));

    public static void Draw(XGraphics g, IReadOnlyList<DsrPhoneLine> lines)
    {
        g.DrawRectangle(new XSolidBrush(EveningDsrPdf.Colour(OutputPalette.Surface)), 0, 0, DailySalesReportPdfExporter.PhonePageWidth, Height(lines));
        var y = Margin;
        foreach (var line in lines)
        {
            y += line.SpaceBefore;
            if (line.IsDivider) { g.DrawLine(new XPen(EveningDsrPdf.Colour(OutputPalette.Divider), .8), Margin, y, Margin + ContentWidth, y); y += LineHeight(line); continue; }
            var height = LineCount(line) * LineHeight(line);
            new XTextFormatter(g).DrawString(line.Text, Font(line), new XSolidBrush(EveningDsrPdf.Colour(line.Colour)), new XRect(Margin, y, ContentWidth, height));
            y += height;
        }
    }

    private static double LineHeight(DsrPhoneLine line) => line.IsDivider ? 1 : line.Size * 1.35;

    // Without a graphics context the wrap is estimated from Segoe UI's average glyph width (about 0.5 em, rounded up); the page is
    // only ever made taller than needed, never shorter.
    private static int LineCount(DsrPhoneLine line) => line.IsDivider ? 1
        : Math.Max(1, (int)Math.Ceiling(line.Text.Length * line.Size * (line.Bold ? .64 : .58) / ContentWidth));

    private static XFont Font(DsrPhoneLine line) => new("Segoe UI", line.Size, line.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
    private static string GrowthColour(decimal? value) => value is null ? OutputPalette.SecondaryText : value > 0 ? OutputPalette.Success : value < 0 ? OutputPalette.Critical : OutputPalette.PrimaryText;
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
