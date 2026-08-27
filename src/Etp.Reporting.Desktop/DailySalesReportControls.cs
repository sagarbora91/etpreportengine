using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop;

internal static class DsrUi
{
    public static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    public static TextBlock Text(string value, double size = 12, FontWeight? weight = null, string colour = "#162034", TextAlignment align = TextAlignment.Left) =>
        new() { Text = value, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = Brush(colour), TextAlignment = align, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    public static Border Card(UIElement child, Thickness? margin = null) => new() { Child = child, Background = Brush("#FFFFFF"), BorderBrush = Brush("#DCE4EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = margin ?? new Thickness(4) };
}

public sealed class KpiCard : Border
{
    public KpiCard(string label, string value, string secondary, string accent)
    {
        Background = DsrUi.Brush("#FFFFFF"); BorderBrush = DsrUi.Brush("#DCE4EF"); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8); Margin = new Thickness(4);
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) }); grid.RowDefinitions.Add(new RowDefinition());
        var stripe = new Border { Background = DsrUi.Brush(accent), CornerRadius = new CornerRadius(8, 8, 0, 0) }; Grid.SetRow(stripe, 0); grid.Children.Add(stripe);
        var content = new StackPanel { Margin = new Thickness(12, 7, 12, 9) }; content.Children.Add(DsrUi.Text(label, 11, colour: "#687285")); content.Children.Add(DsrUi.Text(value, 23, FontWeights.SemiBold)); content.Children.Add(DsrUi.Text(secondary, 10.5, colour: "#687285")); Grid.SetRow(content, 1); grid.Children.Add(content); Child = grid;
        AutomationProperties.SetName(this, $"{label}: {value}. {secondary}");
    }
}

public sealed class StorePeriodCard : Border
{
    public StorePeriodCard(DsrStoreCard store)
    {
        Background = DsrUi.Brush("#FFFFFF"); BorderBrush = DsrUi.Brush("#DCE4EF"); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8); Margin = new Thickness(4); Padding = new Thickness(10);
        var root = new StackPanel(); root.Children.Add(new Border { Height = 4, Background = DsrUi.Brush(store.Accent), CornerRadius = new CornerRadius(3), Margin = new Thickness(-10, -10, -10, 8) }); root.Children.Add(DsrUi.Text(store.DisplayName, 18, FontWeights.SemiBold));
        var table = new Grid { Margin = new Thickness(0, 5, 0, 0) }; var widths = new[] { 0.7, 1.35, 1.35, 1.0, 0.9 }; foreach (var width in widths) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) }); for (var i = 0; i < 4; i++) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headers = new[] { "Period", "TY Value / Qty", "LY Value / Qty", "Value Growth", "Qty Growth" }; AddRow(table, 0, headers, "#687285", false);
        for (var row = 0; row < store.Periods.Count; row++)
        {
            var p = store.Periods[row]; var missing = p.MissingSourceNote is not null;
            var values = new[] { p.Period, DsrDisplay.ValueQuantity(p.TyValue, p.TyQuantity), DsrDisplay.ValueQuantity(p.LyValue, p.LyQuantity), missing ? "—" : DsrDisplay.Percent(p.ValueGrowth), missing ? "—" : DsrDisplay.Percent(p.QuantityGrowth) };
            var background = row == 1 ? "#FFFFFF" : store.StoreCode.Equals("WLMHW", StringComparison.OrdinalIgnoreCase) ? "#EAF2FF" : "#F2ECFF";
            var band = new Border { Background = DsrUi.Brush(background), Padding = new Thickness(2, 7, 2, missing ? 15 : 7), Margin = new Thickness(0, 2, 0, 0) }; Grid.SetRow(band, row + 1); Grid.SetColumnSpan(band, 5); table.Children.Add(band); AddRow(table, row + 1, values, "#162034", true);
            if (missing) { var note = DsrUi.Text(p.MissingSourceNote!, 9, colour: "#C97800", align: TextAlignment.Center); note.Margin = new Thickness(0, 22, 0, 0); Grid.SetRow(note, row + 1); Grid.SetColumn(note, 2); Grid.SetColumnSpan(note, 3); table.Children.Add(note); }
        }
        root.Children.Add(table); Child = root; AutomationProperties.SetName(this, $"{store.DisplayName} FTD MTD YTD comparison");
    }
    private static void AddRow(Grid grid, int row, IReadOnlyList<string> values, string colour, bool body) { for (var col = 0; col < values.Count; col++) { var text = DsrUi.Text(values[col], body ? 10.5 : 9.5, body && col == 0 ? FontWeights.SemiBold : FontWeights.Normal, col >= 3 && body && values[col] != "—" ? "#07965C" : colour, TextAlignment.Center); text.Margin = new Thickness(3, body ? 7 : 3, 3, body ? 7 : 3); Grid.SetRow(text, row); Grid.SetColumn(text, col); grid.Children.Add(text); } }
}

public sealed class OperationalMetricTable : Border
{
    public OperationalMetricTable(DsrStoreCard store)
    {
        var root = new StackPanel(); root.Children.Add(DsrUi.Text(store.DisplayName, 16, FontWeights.SemiBold)); var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) }; for (var i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition()); for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headers = new[] { "Metric", "FTD", "LY", "Change", "MTD / YTD" }; Add(grid, 0, headers, false);
        for (var i = 0; i < store.OperationalMetrics.Count; i++) { var m = store.OperationalMetrics[i]; Add(grid, i + 1, [m.Name, m.IsCurrency ? DsrDisplay.Currency(m.Ftd) : DsrDisplay.Number(m.Ftd), m.IsCurrency ? DsrDisplay.Currency(m.LastYear) : DsrDisplay.Number(m.LastYear), DsrDisplay.Percent(m.Change), m.Context], true); }
        root.Children.Add(grid); Child = root; Background = DsrUi.Brush("#FFFFFF"); BorderBrush = DsrUi.Brush("#DCE4EF"); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8); Padding = new Thickness(12); Margin = new Thickness(4);
    }
    private static void Add(Grid grid, int row, IReadOnlyList<string> values, bool body) { for (var i = 0; i < values.Count; i++) { var value = DsrUi.Text(values[i], body ? 10.5 : 9.5, body && i is 0 or 1 ? FontWeights.SemiBold : FontWeights.Normal, i == 3 && body ? "#07965C" : body ? "#162034" : "#687285", TextAlignment.Center); value.Background = row == 0 ? DsrUi.Brush("#EDF2F8") : Brushes.Transparent; value.Margin = new Thickness(1); value.Padding = new Thickness(4, 7, 4, 7); Grid.SetRow(value, row); Grid.SetColumn(value, i); grid.Children.Add(value); } }
}

public sealed class ServiceSummaryCard : Border
{
    public ServiceSummaryCard(DsrServiceSummary service)
    {
        Background = DsrUi.Brush("#FFFFFF"); BorderBrush = DsrUi.Brush("#DCE4EF"); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8); Padding = new Thickness(12); Margin = new Thickness(4);
        var root = new StackPanel(); root.Children.Add(new Border { Height = 4, Background = DsrUi.Brush("#08A9DE"), CornerRadius = new CornerRadius(3), Margin = new Thickness(-12, -12, -12, 8) }); root.Children.Add(DsrUi.Text("Service", 16, FontWeights.SemiBold));
        var tender = new UniformGrid { Columns = 5, Margin = new Thickness(0, 9, 0, 9) }; var labels = new[] { "WDC", "Cash", "Card", "UPI", "Total" }; var values = new[] { service.Wdc, service.Cash, service.Card, service.Upi, service.Total }; for (var i = 0; i < labels.Length; i++) { var cell = new StackPanel(); cell.Children.Add(DsrUi.Text(labels[i], 9.5, colour: "#687285", align: TextAlignment.Center)); cell.Children.Add(DsrUi.Text(DsrDisplay.Number(values[i], 0), 13, FontWeights.SemiBold, align: TextAlignment.Center)); tender.Children.Add(cell); } root.Children.Add(tender);
        var periods = new UniformGrid { Columns = 6, Margin = new Thickness(0, 8, 0, 0) }; foreach (var period in new[] { "FTD", "LY FTD", "MTD", "LY MTD", "YTD", "LY YTD" }) { var cell = new StackPanel(); cell.Children.Add(DsrUi.Text(period, 9, colour: "#687285", align: TextAlignment.Center)); cell.Children.Add(DsrUi.Text(DsrDisplay.Number(service.PeriodTotals.GetValueOrDefault(period), 0), 11, period is "FTD" or "MTD" or "YTD" ? FontWeights.SemiBold : FontWeights.Normal, period is "MTD" or "YTD" ? "#2269E8" : "#162034", TextAlignment.Center)); periods.Children.Add(cell); } root.Children.Add(periods); Child = root;
    }
}

public sealed class TargetProgressCard : Border
{
    public TargetProgressCard(IReadOnlyList<DsrTargetProgress> targets)
    {
        Background = DsrUi.Brush("#FFFFFF"); BorderBrush = DsrUi.Brush("#DCE4EF"); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8); Padding = new Thickness(12); Margin = new Thickness(4);
        var root = new StackPanel(); root.Children.Add(DsrUi.Text("Monthly Target Progress", 16, FontWeights.SemiBold)); root.Children.Add(DsrUi.Text("MTD actual vs monthly target", 10, colour: "#687285")); foreach (var target in targets) { var row = new Grid { Margin = new Thickness(0, 8, 0, 2) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) }); var label = DsrUi.Text(target.DisplayName, 10.5, FontWeights.SemiBold); var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = (double)target.FillPercent, Height = 10, Foreground = DsrUi.Brush(target.Accent), Background = DsrUi.Brush("#DCE4EF") }; var value = DsrUi.Text(DsrDisplay.Percent(target.Achievement).TrimStart('+'), 10.5, FontWeights.SemiBold, target.Accent, TextAlignment.Right); Grid.SetColumn(label, 0); Grid.SetColumn(progress, 1); Grid.SetColumn(value, 2); row.Children.Add(label); row.Children.Add(progress); row.Children.Add(value); root.Children.Add(row); } Child = root;
    }
}

public sealed class DailySalesReportView : Grid
{
    public DailySalesReportView(DailySalesReportDocument report)
    {
        Background = DsrUi.Brush("#F4F7FB"); Margin = new Thickness(10, 4, 10, 8); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Margin = new Thickness(4, 2, 4, 8) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) }); var titles = new StackPanel(); titles.Children.Add(DsrUi.Text("ETP REPORTING ENGINE", 10.5, FontWeights.SemiBold, "#687285")); titles.Children.Add(DsrUi.Text(report.Title, 28, FontWeights.SemiBold)); titles.Children.Add(DsrUi.Text(report.Subtitle, 12, colour: "#687285")); var date = new StackPanel(); date.Children.Add(DsrUi.Text(report.BusinessDate.ToString("dd MMM yyyy"), 16, FontWeights.SemiBold, align: TextAlignment.Center)); date.Children.Add(DsrUi.Text(report.Weekday(), 11, colour: "#687285", align: TextAlignment.Center)); var dateCard = DsrUi.Card(date); Grid.SetColumn(dateCard, 1); header.Children.Add(titles); header.Children.Add(dateCard); Add(header, 0);
        var kpis = new UniformGrid { Columns = 6 }; kpis.Children.Add(new KpiCard("COMBINED FTD", DsrDisplay.Currency(report.CombinedFtd), $"{DsrDisplay.Percent(report.CombinedFtdGrowth)} vs LY", "#2269E8")); kpis.Children.Add(new KpiCard("UNITS", DsrDisplay.Number(report.Units, 0), report.Units is null ? "Data not available" : $"Titan {DsrDisplay.Number(report.Stores[0].Periods[0].TyQuantity, 0)} · Helios {DsrDisplay.Number(report.Stores[1].Periods[0].TyQuantity, 0)}", "#08A9DE")); kpis.Children.Add(new KpiCard("WALK-INS", DsrDisplay.Number(report.WalkIns, 0), report.WalkIns is null ? "Data not available" : $"Titan {DsrDisplay.Number(report.Stores[0].FtdWalkIns, 0)} · Helios {DsrDisplay.Number(report.Stores[1].FtdWalkIns, 0)}", "#7137D4")); kpis.Children.Add(new KpiCard("CONVERSION", DsrDisplay.Percent(report.Conversion).TrimStart('+'), report.Conversion.Value is null ? "Data not available" : $"{DsrDisplay.Number(report.CombinedInvoices, 0)} invoices / {DsrDisplay.Number(report.WalkIns, 0)} walk-ins", "#07965C")); kpis.Children.Add(new KpiCard("MTD SALES", DsrDisplay.CompactCurrency(report.MtdSales), $"{DsrDisplay.Percent(report.MtdTargetAchievement).TrimStart('+')} of target", "#2269E8")); kpis.Children.Add(new KpiCard("YTD SALES", DsrDisplay.CompactCurrency(report.YtdSales), $"{DsrDisplay.Percent(report.YtdGrowth)} vs LY YTD", "#07965C")); Add(kpis, 1);
        AddPair(new StorePeriodCard(report.Stores[0]), new StorePeriodCard(report.Stores[1]), 2); AddPair(new OperationalMetricTable(report.Stores[0]), new OperationalMetricTable(report.Stores[1]), 3); AddPair(new ServiceSummaryCard(report.Service), new TargetProgressCard(report.Targets), 4);
        var footer = DsrUi.Text("All amounts in INR · FTD = For the Day · MTD = Month to Date · YTD = Year to Date", 9, colour: "#687285", align: TextAlignment.Center); footer.Margin = new Thickness(0, 5, 0, 0); Add(footer, 5); AutomationProperties.SetName(this, $"Daily Sales Report for {report.BusinessDate:dd MMM yyyy}, {report.Weekday()}");
    }
    private void AddPair(UIElement left, UIElement right, int row) { var pair = new Grid(); pair.ColumnDefinitions.Add(new ColumnDefinition()); pair.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(left, 0); Grid.SetColumn(right, 1); pair.Children.Add(left); pair.Children.Add(right); Add(pair, row); }
    private void Add(UIElement element, int row) { SetRow(element, row); Children.Add(element); }
}
