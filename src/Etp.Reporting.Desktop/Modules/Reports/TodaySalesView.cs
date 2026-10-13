using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.NoSaleDays;
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
            panel.Children.Add(Styled(label, "EyebrowText"));
            panel.Children.Add(Styled(Format(period == "MTD" ? row?.Mtd : row?.Ftd, row?.Format), "MetricText"));
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
            var card = Store(sheets[i], document.BusinessDate, document.NoSaleLine(sheets[i].StoreCode), NoSaleDayActions.Current);
            if (narrow) SetRow(card,i); else SetColumn(card,i);
            cards.Children.Add(card);
        }
        var service = new StackPanel { Margin = new Thickness(8) };
        service.Children.Add(Heading("Service & targets",15));
        service.Children.Add(Styled("Service today", "EyebrowText"));
        service.Children.Add(Styled(Format(document.Service.Total,"currency"), "SectionTitleText"));
        foreach (var sheet in sheets)
        {
            service.Children.Add(Heading(sheet.StoreName,15));
            foreach (var (label, value) in new[] { ("Month target",sheet.StoreTarget), ("Day target",sheet.DayTarget), ("Balance",sheet.Balance), ("Required daily sales",sheet.RequiredAds) })
            {
                service.Children.Add(Styled(label, "CaptionText"));
                service.Children.Add(Styled(Format(value,"currency"), "BodyText"));
            }
        }
        if (narrow) SetRow(service,sheets.Length); else SetColumn(service,sheets.Length);
        cards.Children.Add(service); root.Children.Add(cards); scroll.Content = root;
    }
    /// <summary>DS-06: a shop whose sales file is not in has no value in any period; it gets one sentence instead of a wall of dashes.</summary>
    internal static bool HasNoSalesData(EveningStoreSheet sheet) =>
        sheet.Rows.Count == 0 || sheet.Rows.All(r => r.Ftd is null && r.Mtd is null && r.Ytd is null);

    internal static string WaitingText(DateOnly businessDate) => $"No sales file for {businessDate.ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("en-IN"))} yet - waiting for the shop.";

    /// <summary>1.12 N1: shown in place of the waiting line once the shop's day is marked from this card.</summary>
    internal const string MarkedText = "Marked as a no-sale day. Refresh to see the report with it.";

    /// <summary>1.12 N1: shown once the mark is withdrawn from this card.</summary>
    internal const string WithdrawnText = "No-sale day withdrawn. Refresh to see the report without it.";

    private static FrameworkElement Store(EveningStoreSheet sheet, DateOnly businessDate, string? noSaleLine = null, NoSaleDayActions? actions = null)
    {
        var root = new StackPanel { Margin = new Thickness(6) };
        root.Children.Add(Heading(sheet.StoreName,18));
        // 1.12 N1 (decision 52): a marked day says so in one line; a day with no sales file says it is waiting and, for the
        // Owner and a Store Manager, offers "Mark as no-sale day" (a Viewer sees the sentence only).
        if (noSaleLine is not null && !HasSalesToday(sheet))
        {
            root.Children.Add(NoSaleNotice(noSaleLine, done => actions?.CreateWithdrawButton(sheet.StoreCode, businessDate, done), WithdrawnText));
            return root;
        }
        if (HasNoSalesData(sheet))
        {
            root.Children.Add(NoSaleNotice(WaitingText(businessDate), done => actions?.CreateMarkButton(sheet.StoreCode, businessDate, done), MarkedText));
            return root;
        }
        // Earlier days of the month are in, but not this day's sales file: the same notice above the matrix.
        if (!sheet.Rows.Any(r => r.Metric == "VALUE" && r.Ftd is not null))
            root.Children.Add(NoSaleNotice(WaitingText(businessDate), done => actions?.CreateMarkButton(sheet.StoreCode, businessDate, done), MarkedText));
        var sales = sheet.Rows.FirstOrDefault(r => r.Metric == "VALUE");
        var comparison = Text($"LY today {Format(sales?.Ly,"currency")} · Growth {Format(sales?.Growth,"percent")}",12);
        // RA-SALES-14 (1.10.1): no growth figure (LY or Growth shown as "—") is neutral, not green.
        comparison.SetResourceReference(TextBlock.ForegroundProperty, sales?.Growth is not { } growth ? "PrimaryText" : growth < 0 ? "Critical" : "Success");
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
                var cell = Text(lines[row][col],12,row==0);
                if (row > 0 && lines[row][col] == "—") cell.ToolTip = "Not imported yet. A real zero shows as 0.";
                cell.Padding = new Thickness(2,2,2,2); cell.TextAlignment = col==0 ? TextAlignment.Left : TextAlignment.Right;
                if (row%2==0) cell.SetResourceReference(TextBlock.BackgroundProperty,"SurfaceSecondary");
                SetRow(cell,row);SetColumn(cell,col);table.Children.Add(cell);
            }
        }
        root.Children.Add(table);
        return root;
    }
    /// <summary>True when the sheet holds a sales value for the business date itself.</summary>
    internal static bool HasSalesToday(EveningStoreSheet sheet) => sheet.Rows.Any(r => r.Metric == "VALUE" && r.Ftd is > 0 or < 0);

    /// <summary>
    /// The store card's one notice: a sentence and, when the role may use it (the factory returns a button), the mark or
    /// withdraw button. After a saved change the sentence becomes <paramref name="doneText"/> and the button goes.
    /// </summary>
    internal static Border NoSaleNotice(string text, Func<Action, Button?> createAction, string doneText)
    {
        var panel = new StackPanel();
        var sentence = Styled(text, "BodyText");
        sentence.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(sentence);
        Button? action = null;
        action = createAction(() =>
        {
            sentence.Text = doneText;
            if (action is not null) action.Visibility = Visibility.Collapsed;
        });
        if (action is not null)
        {
            action.HorizontalAlignment = HorizontalAlignment.Left;
            action.Margin = DesignTokens.Gap(null, "Space8", null, null);
            panel.Children.Add(action);
        }
        var notice = new Border { Child = panel };
        notice.SetResourceReference(FrameworkElement.StyleProperty, "Notice");
        AutomationProperties.SetName(notice, text);
        return notice;
    }

    private static string Label(string metric) => metric switch { "VALUE" => "Sales (₹)", "VOL" => "Units", "RETAIL WALKIN" => "Walk-ins", "INVOICE" => "Invoices", "CONVERSION %" => "Conversion %", _ => metric };
    private static TextBlock Text(string value,double size,bool bold=false)
    {
        var text = new TextBlock { Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,0) };
        text.SetResourceReference(TextBlock.ForegroundProperty,"PrimaryText"); return text;
    }
    private static TextBlock Styled(string value, string style)
    {
        var text = new TextBlock { Text = value };
        text.SetResourceReference(FrameworkElement.StyleProperty, style);
        return text;
    }
    private static TextBlock Heading(string value,double size)
    {
        var text = Text(value,size,true);
        AutomationProperties.SetHeadingLevel(text,AutomationHeadingLevel.Level2);
        return text;
    }
    internal static string Format(decimal? value,string? format) => Etp.Reporting.Reporting.IndianFormat.Metric(value,format);
}
