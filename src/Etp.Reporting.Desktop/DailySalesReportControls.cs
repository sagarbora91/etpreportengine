using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop;

/// <summary>
/// Text, card and tone helpers of the DSR and the Service screens, over the theme (ETP-DESIGN-SYSTEM.md, decision 45 #1):
/// the default text colour is PrimaryText (green is a status, C2), every brush is a resource reference (high contrast,
/// UXC-19), cards use the Divider border and the two radii (C4, S1), and text uses the Typography.xaml styles (T1).
/// </summary>
internal static class DsrUi
{
    public static System.Windows.Media.SolidColorBrush Brush(string value) => Themes.ThemeBrushes.Resolve(value);

    /// <summary>A text block in a theme colour. Sizes below 12 render at 12 (the floor, CS-16); prefer <see cref="Styled"/>.</summary>
    public static TextBlock Text(string value, double size = 12, FontWeight? weight = null, string colour = "PrimaryText", TextAlignment align = TextAlignment.Left)
    {
        var text = new TextBlock { Text = value, FontSize = Math.Max(12, size), FontWeight = weight ?? FontWeights.Normal, TextAlignment = align, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Paint(text, colour); return text;
    }

    /// <summary>A text block in one of the Typography.xaml styles (BodyText, CaptionText, EyebrowText, CardTitleText,
    /// SectionTitleText, MetricText), optionally recoloured with a theme key.</summary>
    public static TextBlock Styled(string value, string style, string? colour = null, TextAlignment align = TextAlignment.Left, bool semiBold = false)
    {
        var text = new TextBlock { Text = value, TextAlignment = align, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (semiBold) text.FontWeight = FontWeights.SemiBold;
        if (colour is not null) Paint(text, colour);
        return text;
    }

    /// <summary>Theme keys are referenced (they follow high contrast); a hex value from an older report document is mapped once.</summary>
    public static void Paint(TextBlock text, string colour)
    {
        if (colour.StartsWith('#')) text.Foreground = Brush(colour);
        else text.SetResourceReference(TextBlock.ForegroundProperty, colour);
    }

    /// <summary>CS-13: growth is Critical below zero, Success at or above, SecondaryText when not available; the sign stays printed.</summary>
    public static string GrowthColour(string growth)
    {
        var value = growth.Trim();
        if (value.Length == 0 || value == DsrDisplay.Missing || value.StartsWith(DsrDisplay.Missing, StringComparison.Ordinal)) return "SecondaryText";
        return value[0] is '-' or '−' ? "Critical" : "Success";
    }

    /// <summary>A SurfaceCard drawn in code: Surface fill, Divider border, radius 12, padding 12.</summary>
    public static Border Card(UIElement child, Thickness? margin = null)
    {
        var card = new Border { Child = child, Padding = DesignTokens.Gap("Space12"), Margin = margin ?? DesignTokens.Gap("Space4") };
        Frame(card); return card;
    }

    /// <summary>Surface fill, 1 px Divider border, RadiusMedium: the container look of every DSR card.</summary>
    public static void Frame(Border border)
    {
        border.SetResourceReference(Border.BackgroundProperty, "Surface");
        border.SetResourceReference(Border.BorderBrushProperty, "Divider");
        border.BorderThickness = new Thickness(1);
        border.CornerRadius = DesignTokens.Radius("RadiusMedium");
    }

    /// <summary>The 4 px tone stripe across the top of a card (K1): a tone key, or Divider for "no meaning".</summary>
    public static Border Stripe(string toneKey)
    {
        var radius = DesignTokens.Radius("RadiusMedium");
        var stripe = new Border { Height = 4, CornerRadius = new CornerRadius(radius.TopLeft, radius.TopRight, 0, 0) };
        stripe.SetResourceReference(Border.BackgroundProperty, toneKey);
        return stripe;
    }
}

/// <summary>
/// The one metric tile (ETP-DESIGN-SYSTEM.md section 5): 4 px stripe, EyebrowText label, MetricText value in PrimaryText
/// (never green by default), CaptionText under it, and an optional StatusChip. The stripe is a tone (Success, Warning,
/// Critical, Information) or none: any other key a caller passes (PrimaryText, Accent, a hex) is drawn as Divider (K1).
/// Inside a TileButton the card turns its own fill and border off so the button's hover and focus show (K3).
/// </summary>
public sealed class KpiCard : Border
{
    public KpiCard(string label, string value, string secondary, string accent, string? statusWord = null)
    {
        Tone = StatusTones.FromKey(accent);
        DsrUi.Frame(this); Margin = DesignTokens.Gap("Space4");
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
        var stripe = DsrUi.Stripe(StatusTones.StripeKey(Tone)); Grid.SetRow(stripe, 0); grid.Children.Add(stripe);
        var content = new StackPanel { Margin = DesignTokens.Gap("Space12", "Space8", "Space12", "Space12") };
        content.Children.Add(DsrUi.Styled(label, "EyebrowText"));
        content.Children.Add(DsrUi.Styled(value, "MetricText"));
        if (!string.IsNullOrWhiteSpace(secondary)) content.Children.Add(DsrUi.Styled(secondary, "CaptionText"));
        if (!string.IsNullOrWhiteSpace(statusWord))
        {
            var chip = new StatusChip(statusWord, Tone) { Margin = DesignTokens.Gap(null, "Space4", null, null) };
            content.Children.Add(chip);
        }
        Grid.SetRow(content, 1); grid.Children.Add(content); Child = grid;
        var spoken = string.IsNullOrWhiteSpace(statusWord) ? string.Empty : $" {statusWord}, {StatusTones.Describe(Tone)}.";
        AutomationProperties.SetName(this, $"{label}: {value}. {secondary}{spoken}".TrimEnd());
        Loaded += (_, _) => { if (Parent is ButtonBase) { Background = null; BorderThickness = new Thickness(0); Margin = new Thickness(0); } };
    }

    /// <summary>The tone the stripe carries; Neutral means no stripe colour (Divider).</summary>
    public StatusTone Tone { get; }
}

public sealed class StorePeriodCard : Border
{
    public StorePeriodCard(DsrStoreCard store)
    {
        DsrUi.Frame(this); Margin = DesignTokens.Gap("Space4");
        var root = new StackPanel();
        // The store's report-document accent is decoration, not a status: no coloured stripe (K1).
        root.Children.Add(DsrUi.Stripe("Divider"));
        var body = new StackPanel { Margin = DesignTokens.Gap("Space12", "Space8", "Space12", "Space12") };
        body.Children.Add(DsrUi.Styled(store.DisplayName, "CardTitleText"));
        var table = new Grid { Margin = DesignTokens.Gap(null, "Space4", null, null) }; var widths = new[] { 0.7, 1.35, 1.35, 1.0, 0.9 }; foreach (var width in widths) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) }); for (var i = 0; i < 4; i++) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headers = new[] { "Period", "TY Value / Qty", "LY Value / Qty", "Value Growth", "Qty Growth" }; AddRow(table, 0, headers, false);
        for (var row = 0; row < store.Periods.Count; row++)
        {
            var p = store.Periods[row]; var missing = p.MissingSourceNote is not null;
            var values = new[] { p.Period, DsrDisplay.ValueQuantity(p.TyValue, p.TyQuantity), DsrDisplay.ValueQuantity(p.LyValue, p.LyQuantity), missing ? DsrDisplay.Missing : DsrDisplay.Percent(p.ValueGrowth), missing ? DsrDisplay.Missing : DsrDisplay.Percent(p.QuantityGrowth) };
            var band = new Border { Padding = DesignTokens.Gap(null, "Space8", null, missing ? "Space16" : "Space8"), Margin = DesignTokens.Gap(null, "Space4", null, null) };
            band.SetResourceReference(Border.BackgroundProperty, row % 2 == 1 ? "Surface" : "ZebraRow");
            Grid.SetRow(band, row + 1); Grid.SetColumnSpan(band, 5); table.Children.Add(band); AddRow(table, row + 1, values, true);
            if (missing) { var note = DsrUi.Styled(p.MissingSourceNote!, "CaptionText", "Critical", TextAlignment.Center); note.Margin = DesignTokens.Gap(null, "Space24", null, null); Grid.SetRow(note, row + 1); Grid.SetColumn(note, 2); Grid.SetColumnSpan(note, 3); table.Children.Add(note); }
        }
        body.Children.Add(table); root.Children.Add(body); Child = root; AutomationProperties.SetName(this, $"{store.DisplayName} FTD MTD YTD comparison");
    }
    private static void AddRow(Grid grid, int row, IReadOnlyList<string> values, bool body)
    {
        for (var col = 0; col < values.Count; col++)
        {
            var colour = !body ? "SecondaryText" : col >= 3 ? DsrUi.GrowthColour(values[col]) : "PrimaryText";
            var text = DsrUi.Styled(values[col], body ? "BodyText" : "CaptionText", colour, TextAlignment.Center, semiBold: body && col == 0);
            text.Margin = DesignTokens.Gap("Space4", body ? "Space8" : "Space4", "Space4", body ? "Space8" : "Space4"); Grid.SetRow(text, row); Grid.SetColumn(text, col); grid.Children.Add(text);
        }
    }
}

public sealed class OperationalMetricTable : Border
{
    public OperationalMetricTable(DsrStoreCard store)
    {
        var root = new StackPanel(); root.Children.Add(DsrUi.Styled(store.DisplayName, "CardTitleText")); var grid = new Grid { Margin = DesignTokens.Gap(null, "Space8", null, null) }; for (var i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition()); for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headers = new[] { "Metric", "FTD", "LY", "Change", "MTD / YTD" }; Add(grid, 0, headers, false);
        for (var i = 0; i < store.OperationalMetrics.Count; i++) { var m = store.OperationalMetrics[i]; Add(grid, i + 1, [m.Name, m.IsCurrency ? DsrDisplay.Currency(m.Ftd) : DsrDisplay.Number(m.Ftd), m.IsCurrency ? DsrDisplay.Currency(m.LastYear) : DsrDisplay.Number(m.LastYear), DsrDisplay.Percent(m.Change), m.Context], true); }
        root.Children.Add(grid); Child = root; DsrUi.Frame(this); Padding = DesignTokens.Gap("Space12"); Margin = DesignTokens.Gap("Space4");
    }
    private static void Add(Grid grid, int row, IReadOnlyList<string> values, bool body)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var colour = !body ? "SecondaryText" : i == 3 ? DsrUi.GrowthColour(values[i]) : "PrimaryText";
            var value = DsrUi.Styled(values[i], body ? "BodyText" : "CaptionText", colour, TextAlignment.Center, semiBold: body && i is 0 or 1);
            if (row == 0) value.SetResourceReference(TextBlock.BackgroundProperty, "SurfaceSecondary");
            value.Margin = new Thickness(1); value.Padding = DesignTokens.Gap("Space4", "Space8", "Space4", "Space8"); Grid.SetRow(value, row); Grid.SetColumn(value, i); grid.Children.Add(value);
        }
    }
}

public sealed class ServiceSummaryCard : Border
{
    public ServiceSummaryCard(DsrServiceSummary service)
    {
        DsrUi.Frame(this); Margin = DesignTokens.Gap("Space4");
        var root = new StackPanel(); root.Children.Add(DsrUi.Stripe("Divider"));
        var body = new StackPanel { Margin = DesignTokens.Gap("Space12", "Space8", "Space12", "Space12") };
        body.Children.Add(DsrUi.Styled("Service", "CardTitleText"));
        var tender = new UniformGrid { Columns = 5, Margin = DesignTokens.Gap(null, "Space8", null, "Space8") }; var labels = new[] { "WDC", "Cash", "Card", "UPI", "Total" }; var values = new[] { service.Wdc, service.Cash, service.Card, service.Upi, service.Total }; for (var i = 0; i < labels.Length; i++) { var cell = new StackPanel(); cell.Children.Add(DsrUi.Styled(labels[i], "CaptionText", align: TextAlignment.Center)); cell.Children.Add(DsrUi.Styled(DsrDisplay.Number(values[i], 0), "BodyText", align: TextAlignment.Center, semiBold: true)); tender.Children.Add(cell); } body.Children.Add(tender);
        var periods = new UniformGrid { Columns = 6, Margin = DesignTokens.Gap(null, "Space8", null, null) }; foreach (var period in new[] { "FTD", "LY FTD", "MTD", "LY MTD", "YTD", "LY YTD" }) { var cell = new StackPanel(); cell.Children.Add(DsrUi.Styled(period, "CaptionText", align: TextAlignment.Center)); cell.Children.Add(DsrUi.Styled(DsrDisplay.Number(service.PeriodTotals.GetValueOrDefault(period), 0), "BodyText", align: TextAlignment.Center, semiBold: period is "FTD" or "MTD" or "YTD")); periods.Children.Add(cell); } body.Children.Add(periods);
        root.Children.Add(body); Child = root;
    }
}

public sealed class TargetProgressCard : Border
{
    public TargetProgressCard(IReadOnlyList<DsrTargetProgress> targets)
    {
        DsrUi.Frame(this); Padding = DesignTokens.Gap("Space12"); Margin = DesignTokens.Gap("Space4");
        var root = new StackPanel(); root.Children.Add(DsrUi.Styled("Monthly Target Progress", "CardTitleText")); root.Children.Add(DsrUi.Styled("MTD actual vs monthly target", "CaptionText"));
        foreach (var target in targets)
        {
            var row = new Grid { Margin = DesignTokens.Gap(null, "Space8", null, null) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            var label = DsrUi.Styled(target.DisplayName, "BodyText", semiBold: true);
            // The bar fill is Accent and the track ProgressTrack (section 2); the store accent of the report document is decoration.
            var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = (double)target.FillPercent, Margin = DesignTokens.Gap("Space4", null, "Space4", null) };
            var value = DsrUi.Styled(DsrDisplay.Percent(target.Achievement).TrimStart('+'), "BodyText", align: TextAlignment.Right, semiBold: true);
            Grid.SetColumn(label, 0); Grid.SetColumn(progress, 1); Grid.SetColumn(value, 2); row.Children.Add(label); row.Children.Add(progress); row.Children.Add(value); root.Children.Add(row);
        }
        Child = root;
    }
}

public sealed class DailySalesReportView : Grid
{
    public DailySalesReportView(DailySalesReportDocument report)
    {
        if (report.EveningSheets.Count > 0) { Children.Add(new Modules.Reports.EveningDsrView(report)); return; }
        SetResourceReference(BackgroundProperty, "SurfaceSecondary"); Margin = DesignTokens.Gap("Space8", "Space4", "Space8", "Space8"); for (var i = 0; i < 6; i++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Margin = DesignTokens.Gap("Space4", null, "Space4", "Space8") }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
        var titles = new StackPanel(); titles.Children.Add(DsrUi.Styled("ETP REPORTING ENGINE", "EyebrowText")); titles.Children.Add(DsrUi.Styled(report.Title, "SectionTitleText")); titles.Children.Add(DsrUi.Styled(report.Subtitle, "CaptionText"));
        var date = new StackPanel(); date.Children.Add(DsrUi.Styled(report.BusinessDate.ToString("dd MMM yyyy"), "CardTitleText", align: TextAlignment.Center)); date.Children.Add(DsrUi.Styled(report.Weekday(), "CaptionText", align: TextAlignment.Center)); var dateCard = DsrUi.Card(date); Grid.SetColumn(dateCard, 1); header.Children.Add(titles); header.Children.Add(dateCard); Add(header, 0);
        // DS-08 / K1: the six DSR tiles carry no decorative stripe; dark numbers, green only where something is good.
        var kpis = new UniformGrid { Columns = 6 }; foreach (var card in DsrKpiCards.For(report)) kpis.Children.Add(new KpiCard(card.Label, card.Value, card.Secondary, "Divider")); Add(kpis, 1);
        var periods = new UniformGrid { Columns = Math.Max(1, report.Stores.Count) };
        var metrics = new UniformGrid { Columns = Math.Max(1, report.Stores.Count) };
        foreach (var store in report.Stores) { periods.Children.Add(new StorePeriodCard(store)); metrics.Children.Add(new OperationalMetricTable(store)); }
        Add(periods, 2); Add(metrics, 3); AddPair(new ServiceSummaryCard(report.Service), new TargetProgressCard(report.Targets), 4);
        var footer = DsrUi.Styled("All amounts in INR · FTD = For the Day · MTD = Month to Date · YTD = Year to Date", "CaptionText", align: TextAlignment.Center); footer.Margin = DesignTokens.Gap(null, "Space4", null, null); Add(footer, 5); AutomationProperties.SetName(this, $"Daily Sales Report for {report.BusinessDate:dd MMM yyyy}, {report.Weekday()}");
    }
    private void AddPair(UIElement left, UIElement right, int row) { var pair = new Grid(); pair.ColumnDefinitions.Add(new ColumnDefinition()); pair.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(left, 0); Grid.SetColumn(right, 1); pair.Children.Add(left); pair.Children.Add(right); Add(pair, row); }
    private void Add(UIElement element, int row) { SetRow(element, row); Children.Add(element); }
}
