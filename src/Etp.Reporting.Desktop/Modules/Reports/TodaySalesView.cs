using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Modules.Reports;

/// <summary>A compact reading view over the same accepted DSR document used by exports.</summary>
public sealed class TodaySalesView : Grid
{
    private readonly DailySalesReportDocument document;
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private bool? compact;
    public TodaySalesView(DailySalesReportDocument document)
    {
        this.document = document;
        Children.Add(scroll);
        AutomationProperties.SetName(this, "Today sales summary");
        SizeChanged += (_,_) => ArrangeCards(ActualWidth < 1000);
        ArrangeCards(false);
    }
    private void ArrangeCards(bool narrow)
    {
        if (compact == narrow) return;
        compact = narrow;
        var root = new StackPanel();
        var combined = document.EveningSheets.FirstOrDefault(s => s.StoreCode == "COMBINED");
        var kpis = new System.Windows.Controls.Primitives.UniformGrid { Columns = narrow ? 2 : 4 };
        foreach (var (label, metric, period) in new[] { ("Sales today", "VALUE", "FTD"), ("Sales this month", "VALUE", "MTD"), ("Invoices today", "INVOICE", "FTD"), ("Walk-ins today", "RETAIL WALKIN", "FTD") })
        {
            var row = combined?.Rows.FirstOrDefault(r => r.Metric == metric);
            var panel = new StackPanel { Margin = new Thickness(8,4,8,8) };
            panel.Children.Add(Text(label, 12));
            panel.Children.Add(Text(Format(period == "MTD" ? row?.Mtd : row?.Ftd, row?.Format), 20, true));
            kpis.Children.Add(panel);
        }
        root.Children.Add(kpis);
        var cards = new Grid();
        var sheets = document.EveningSheets.Where(s => s.StoreCode != "COMBINED").ToArray();
        if (narrow)
        {
            for (var i = 0; i <= sheets.Length; i++) cards.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }
        else
        {
            foreach (var _ in sheets) cards.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            cards.ColumnDefinitions.Add(new() { Width = new GridLength(200) });
        }
        for (var i = 0; i < sheets.Length; i++)
        {
            var card = Store(sheets[i]);
            if (narrow) SetRow(card,i); else SetColumn(card,i);
            cards.Children.Add(card);
        }
        var service = new StackPanel { Margin = new Thickness(8) };
        service.Children.Add(Heading("Service & targets",16));
        service.Children.Add(Text("Service today",12));
        service.Children.Add(Text(Format(document.Service.Total,"currency"),18,true));
        foreach (var sheet in sheets)
        {
            service.Children.Add(Heading(sheet.StoreName,14));
            foreach (var (label, value) in new[] { ("Month target",sheet.StoreTarget), ("Day target",sheet.DayTarget), ("Balance",sheet.Balance), ("Required daily sales",sheet.RequiredAds) })
            {
                service.Children.Add(Text(label,11));
                service.Children.Add(Text(Format(value,"currency"),13));
            }
        }
        service.Children.Add(Text("— means data unavailable.",11));
        if (narrow) SetRow(service,sheets.Length); else SetColumn(service,sheets.Length);
        cards.Children.Add(service); root.Children.Add(cards); scroll.Content = root;
    }
    private static FrameworkElement Store(EveningStoreSheet sheet)
    {
        var root = new StackPanel { Margin = new Thickness(6) };
        root.Children.Add(Heading(sheet.StoreName,18));
        var sales = sheet.Rows.FirstOrDefault(r => r.Metric == "VALUE");
        var comparison = Text($"LY today {Format(sales?.Ly,"currency")} · Growth {Format(sales?.Growth,"percent")}",11);
        comparison.SetResourceReference(TextBlock.ForegroundProperty, sales?.Growth < 0 ? "Critical" : "Success");
        root.Children.Add(comparison);
        var table = new Grid();
        table.ColumnDefinitions.Add(new() { Width = new GridLength(1.25,GridUnitType.Star) });
        for (var c=0;c<3;c++) table.ColumnDefinitions.Add(new());
        var lines = new List<string[]> { new[] { "Metric", "FTD", "MTD", "YTD" } };
        lines.AddRange(sheet.Rows.Select(r => new[] { Label(r.Metric), Format(r.Ftd,r.Format), Format(r.Mtd,r.Format), Format(r.Ytd,r.Format) }));
        for (var row=0;row<lines.Count;row++)
        {
            table.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var col=0;col<4;col++)
            {
                var cell = Text(lines[row][col],11,row==0);
                cell.Padding = new Thickness(2,2,2,2); cell.TextAlignment = col==0 ? TextAlignment.Left : TextAlignment.Right;
                if (row%2==0) cell.SetResourceReference(TextBlock.BackgroundProperty,"SurfaceSecondary");
                SetRow(cell,row);SetColumn(cell,col);table.Children.Add(cell);
            }
        }
        root.Children.Add(table);
        return root;
    }
    private static string Label(string metric) => metric switch { "VALUE" => "Sales ₹", "VOL" => "Units", "RETAIL WALKIN" => "Walk-ins", "INVOICE" => "Invoices", "CONVERSION %" => "Conversion %", _ => metric };
    private static TextBlock Text(string value,double size,bool bold=false)
    {
        var text = new TextBlock { Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,0) };
        text.SetResourceReference(TextBlock.ForegroundProperty,"PrimaryText"); return text;
    }
    private static TextBlock Heading(string value,double size)
    {
        var text = Text(value,size,true);
        AutomationProperties.SetHeadingLevel(text,AutomationHeadingLevel.Level2);
        return text;
    }
    internal static string Format(decimal? value,string? format) => value is null ? "—" : value.Value.ToString(format is "currency" or "ratio" ? "N2" : "0.##",CultureInfo.GetCultureInfo("en-IN")) + (format=="percent"?"%":"");
}
