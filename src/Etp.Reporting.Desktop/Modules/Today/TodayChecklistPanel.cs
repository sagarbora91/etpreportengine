extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DailyReadinessQuery = EtpApplication::Etp.Reporting.Application.DailyReadiness.IDailyReadinessQuery;
using DailyReadinessResult = EtpApplication::Etp.Reporting.Application.DailyReadiness.DailyReadinessResult;
using NoSaleDay = EtpApplication::Etp.Reporting.Application.NoSaleDays.NoSaleDay;
using Etp.Reporting.Desktop.Modules.Morning;

namespace Etp.Reporting.Desktop.Modules.Today;

/// <summary>
/// 1.9.9 Today "What's missing" panel. For the shell business date and store it lists each missing
/// export, manual input and target from IDailyReadinessQuery, one line per item, with a button that
/// opens the screen where the item is fixed. It hides when nothing is missing, and also when the
/// build has no readiness query. A failed check is one line in the panel and a diagnostics entry;
/// it never blocks the Today screen.
/// </summary>
public sealed class TodayChecklistPanel : Border
{
    private readonly Func<DailyReadinessQuery> query;
    private readonly Action<string> openTask;
    private readonly Func<ShellAccess> access;
    private readonly Func<string, string?>? shopName;
    private DateTime? lastDate;
    private string? lastStore;
    private readonly TextBlock heading = new() { TextWrapping = TextWrapping.Wrap };

    /// <summary>
    /// ST-08: when set, a line opens its task for the shop it names (task id, store code; empty when the line names no shop)
    /// instead of leaving the header on "All stores". The shell sets it; without it a line opens its task as before.
    /// </summary>
    public Action<string, string>? OpenTaskForStore { get; set; }

    /// <summary>
    /// 1.12 U1 (decision 52): the no-sale marks of the business date (N1's <c>INoSaleDayService.LoadAsync</c>). A marked
    /// store-day's exports and entries leave the list (N1's readiness); the panel then names the mark instead. Null: none.
    /// </summary>
    public Func<DateOnly, CancellationToken, Task<IReadOnlyList<NoSaleDay>>>? LoadNoSaleDays { get; set; }

    /// <summary>N1's Mark / Withdraw buttons (MainWindow.NoSaleDayButtons); without them the panel shows the text only.</summary>
    public Func<NoSaleDayButtons?>? NoSaleButtons { get; set; }

    /// <summary>The no-sale marks on screen.</summary>
    public IReadOnlyList<NoSaleDay> NoSaleDays { get; private set; } = [];

    private readonly StackPanel lines = new();
    private int revision;

    public TodayChecklistPanel(Func<DailyReadinessQuery> query, Action<string> openTask, Func<ShellAccess> access, Func<string, string?>? shopName = null)
    {
        this.shopName = shopName;
        this.query = query ?? throw new ArgumentNullException(nameof(query));
        this.openTask = openTask ?? throw new ArgumentNullException(nameof(openTask));
        this.access = access ?? throw new ArgumentNullException(nameof(access));
        Visibility = Visibility.Collapsed;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(12, 8, 12, 8);
        Margin = new Thickness(0, 8, 0, 0);
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(BorderBrushProperty, "Warning");
        heading.SetResourceReference(TextBlock.StyleProperty, "CardTitleText");
        var root = new DockPanel();
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        // 4 stores x 4 exports plus inputs can be 25+ lines; keep the DSR preview on a 768-pixel panel. 1.11.2: taller (the
        // lines are grouped by shop now) and not a tab stop, so a keyboard user goes straight to the buttons inside it.
        root.Children.Add(new ScrollViewer { Content = lines, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 4, 0, 0), Focusable = false });
        Child = root;
        AutomationProperties.SetName(this, "What's missing for the business date");
    }

    /// <summary>The lines on screen; empty when hidden.</summary>
    public IReadOnlyList<TodayChecklistLine> Lines { get; private set; } = [];

    public string Heading => heading.Text;

    /// <summary>The failure line when the last check failed, otherwise null.</summary>
    public string? FailureMessage { get; private set; }

    public async Task RefreshAsync(DateTime businessDate, string? headerStore, CancellationToken cancellationToken = default)
    {
        var current = ++revision;
        lastDate = businessDate; lastStore = headerStore;
        var request = TodayChecklist.Request(businessDate, headerStore);
        DailyReadinessQuery readiness;
        try { readiness = query(); }
        catch (InvalidOperationException)
        {
            // No readiness query in this build (composition root not wired): nothing to show.
            Show([], request.BusinessDate, null);
            return;
        }
        try
        {
            var result = await readiness.LoadAsync(request, cancellationToken);
            var marks = await LoadMarksAsync(request.BusinessDate, headerStore, cancellationToken);
            if (current != revision) return;
            NoSaleDays = marks;
            Show(result, request.BusinessDate);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (current != revision) return;
            DesktopDiagnostics.Record(exception, "Today.Checklist", "TODAY_CHECKLIST_FAILED");
            Show([], request.BusinessDate, "Could not check what's missing for this date: " + DesktopFriendlyError.Describe(exception));
        }
    }

    /// <summary>
    /// Checks again for the date and store last shown. The shell calls it when the window is activated, so a file that
    /// arrived while the Owner was in another program leaves the list without a screen change.
    /// </summary>
    public Task RefreshLastAsync() =>
        lastDate is { } date ? RefreshAsync(date, lastStore) : Task.CompletedTask;

    /// <summary>The marks for the date (one store, or all); a failure leaves them out and is recorded, never blocking the list.</summary>
    private async Task<IReadOnlyList<NoSaleDay>> LoadMarksAsync(DateOnly businessDate, string? headerStore, CancellationToken cancellationToken)
    {
        if (LoadNoSaleDays is not { } load) return [];
        try
        {
            var marks = await load(businessDate, cancellationToken);
            var store = headerStore?.Trim() ?? string.Empty;
            return store.Length == 0 ? marks
                : marks.Where(mark => string.Equals(mark.StoreCode, store, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DesktopDiagnostics.Record(exception, "Today.Checklist", "TODAY_NO_SALE_DAYS_FAILED", DesktopDiagnosticSeverity.Warning);
            return [];
        }
    }

    private void Show(DailyReadinessResult result, DateOnly businessDate) =>
        Show(TodayChecklist.Lines(result, access(), shopName), businessDate, null);

    private void Show(IReadOnlyList<TodayChecklistLine> items, DateOnly businessDate, string? failure)
    {
        Lines = items;
        FailureMessage = failure;
        lines.Children.Clear();
        if (failure is not null)
        {
            heading.Text = $"What's missing for {businessDate:dd MMM yyyy}";
            lines.Children.Add(Text(failure));
            SetResourceReference(BorderBrushProperty, "Critical");
            Visibility = Visibility.Visible;
            return;
        }
        var marks = NoSaleDays;
        if (items.Count == 0 && marks.Count == 0)
        {
            heading.Text = string.Empty;
            Visibility = Visibility.Collapsed;
            return;
        }
        // DS-03: waiting for files is amber; red is kept for a check that could not run. Only no-sale marks: information.
        // Review M-1: a mark that no longer applies (sales or entries saved since) asks for action: amber.
        SetResourceReference(BorderBrushProperty, items.Count == 0 && !marks.Any(mark => mark.NoLongerApplies) ? "Information" : "Warning");
        heading.Text = items.Count == 0 ? TodayChecklist.NoSaleHeading(businessDate, marks.Count) : TodayChecklist.Heading(businessDate, items.Count);
        var buttons = NoSaleButtons?.Invoke();
        // The day's lines grouped by shop (named), then the month's targets apart from them.
        foreach (var shop in items.Where(x => x.Section != TodayChecklistSection.ThisMonth).GroupBy(x => x.ShopName))
        {
            if (shop.Key.Length > 0) lines.Children.Add(GroupHeader(shop.Key));
            foreach (var item in shop) lines.Children.Add(Row(item));
            // 1.12 U1 (decision 52): a shop with no sales file can be marked as a no-sale day (Owner, Store Manager).
            if (buttons is not null && TodayChecklist.CanMarkNoSale(shop, access()) && shop.First().StoreCode is { Length: > 0 } store)
                lines.Children.Add(ActionRow(buttons.Mark(store, businessDate, () => _ = RefreshLastAsync())));
        }
        foreach (var mark in marks)
        {
            lines.Children.Add(GroupHeader(TodayChecklist.ShopName(mark.StoreCode, shopName)));
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = true };
            if (buttons is not null && access().CanImport)
            {
                var withdraw = buttons.Withdraw(mark.StoreCode, businessDate, () => _ = RefreshLastAsync());
                DockPanel.SetDock(withdraw, Dock.Right);
                row.Children.Add(withdraw);
            }
            row.Children.Add(Text(TodayChecklist.NoSaleLine(mark)));
            lines.Children.Add(row);
        }
        var month = items.Where(x => x.Section == TodayChecklistSection.ThisMonth).ToArray();
        if (month.Length > 0)
        {
            lines.Children.Add(GroupHeader("This month"));
            foreach (var item in month) lines.Children.Add(Row(item));
        }
        Visibility = Visibility.Visible;
    }

    private static TextBlock GroupHeader(string title)
    {
        var header = new TextBlock { Text = title, Margin = new Thickness(0, 6, 0, 2) };
        header.SetResourceReference(TextBlock.StyleProperty, "EyebrowText");
        AutomationProperties.SetHeadingLevel(header, AutomationHeadingLevel.Level3);
        return header;
    }

    private UIElement Row(TodayChecklistLine item)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = true };
        var button = new Button { Content = item.ActionLabel, Padding = new Thickness(10, 4, 10, 4), MinWidth = 120, Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = item.CanOpen, ToolTip = item.Tooltip, Tag = item.TaskId, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetShowOnDisabled(button, true);
        AutomationProperties.SetName(button, item.ActionLabel + ": " + item.PlainMessage + (item.ShopName.Length > 0 ? " (" + item.ShopName + ")" : ""));
        button.Click += (_, _) =>
        {
            if (OpenTaskForStore is { } openForStore) openForStore(item.TaskId, item.StoreCode);
            else openTask(item.TaskId);
        };
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button);
        row.Children.Add(Text(item.PlainMessage.Length > 0 ? item.PlainMessage : item.Message));
        return row;
    }

    private static UIElement ActionRow(UIElement button)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = false };
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button);
        return row;
    }

    private static TextBlock Text(string value)
    {
        var text = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.StyleProperty, "BodyText");
        return text;
    }
}
