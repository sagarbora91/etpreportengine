using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Desktop.Modules.Today;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.9.9 Today "What's missing" checklist, built against IDailyReadinessQuery with a fake.</summary>
[Collection(WpfViewCollection.Name)]
public sealed class TodayChecklistTests
{
    private static readonly DateOnly Day = new(2026, 10, 9);

    [Theory]
    [InlineData(DailyReadinessDestination.ImportIntake, "import-files", "Import → Import → Import folder")]
    [InlineData(DailyReadinessDestination.TodayWalkIns, "walk-ins", "Today → Walk-ins → Walk-ins")]
    [InlineData(DailyReadinessDestination.TodayCashEntries, "cash-input", "Today → Cash → Cash and service entries")]
    [InlineData(DailyReadinessDestination.SettingsMonthlyTargets, "masters", "Settings → Stores & masters → Brands and targets")]
    [InlineData(DailyReadinessDestination.SettingsStaffTargets, "staff-target", "Settings → Stores & masters → Staff targets")]
    public void Every_destination_opens_its_entry_or_import_screen(DailyReadinessDestination destination, string taskId, string path)
    {
        Assert.Equal(taskId, TodayChecklist.TaskFor(destination));
        Assert.Equal(path, TaskNavigation.Find(taskId)!.Path);
        Assert.False(string.IsNullOrWhiteSpace(TodayChecklist.ActionLabel(destination)));
    }

    [Fact]
    public void Every_contract_destination_has_a_screen()
    {
        foreach (var destination in Enum.GetValues<DailyReadinessDestination>())
            Assert.NotNull(TaskNavigation.Find(TodayChecklist.TaskFor(destination)));
    }

    [Fact]
    public void Request_follows_the_shell_business_date_and_store()
    {
        var all = TodayChecklist.Request(new DateTime(2026, 10, 9), "");
        Assert.Equal(Day, all.BusinessDate);
        Assert.Empty(all.StoreCodes);
        Assert.True(all.IncludeServiceCentre);

        var one = TodayChecklist.Request(new DateTime(2026, 10, 9), "AX123");
        Assert.Equal(["AX123"], one.StoreCodes);
        Assert.False(one.IncludeServiceCentre);
    }

    [Fact]
    public void Lines_keep_the_query_order_and_disable_screens_the_role_cannot_open()
    {
        var result = Result(
            Item(DailyReadinessItemKind.MissingExport, "R025", DailyReadinessDestination.ImportIntake),
            Item(DailyReadinessItemKind.MissingManualInput, "WALK_INS", DailyReadinessDestination.TodayWalkIns),
            Item(DailyReadinessItemKind.MissingMonthlyTarget, "MONTHLY_TARGET", DailyReadinessDestination.SettingsMonthlyTargets),
            Item(DailyReadinessItemKind.MissingStaffTargets, "STAFF_TARGETS", DailyReadinessDestination.SettingsStaffTargets));

        var manager = TodayChecklist.Lines(result, ShellAccess.StoreManager);
        Assert.Equal(["import-files", "walk-ins", "masters", "staff-target"], manager.Select(x => x.TaskId));
        Assert.Equal(result.Missing.Select(x => x.Message), manager.Select(x => x.Message));
        Assert.Equal([true, true, true, false], manager.Select(x => x.CanOpen));
        Assert.Contains("needs a different role", manager[3].Tooltip);
        Assert.EndsWith("Brands and targets → Monthly targets", manager[2].Tooltip);

        Assert.All(TodayChecklist.Lines(result, ShellAccess.Owner), line => Assert.True(line.CanOpen));
        Assert.All(TodayChecklist.Lines(result, ShellAccess.Viewer), line => Assert.False(line.CanOpen));
    }

    [Fact]
    public void Panel_lists_each_missing_item_and_its_button_opens_the_screen()
    {
        RunSta(() =>
        {
            var query = new FakeReadinessQuery(Result(
                Item(DailyReadinessItemKind.MissingExport, "R022", DailyReadinessDestination.ImportIntake),
                Item(DailyReadinessItemKind.MissingManualInput, "OPENING_CASH", DailyReadinessDestination.TodayCashEntries)));
            var opened = new List<string>();
            var panel = new TodayChecklistPanel(() => query, opened.Add, () => ShellAccess.StoreManager);

            panel.RefreshAsync(new DateTime(2026, 10, 9), "AX123").GetAwaiter().GetResult();

            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.Equal("What's missing for 09 Oct 2026: 2 items", panel.Heading);
            Assert.Equal(Day, query.Requests.Single().BusinessDate);
            Assert.Equal(["AX123"], query.Requests.Single().StoreCodes);
            var buttons = Buttons(panel);
            Assert.Equal(2, buttons.Length);
            Assert.All(buttons, button => Assert.True(button.IsEnabled));
            foreach (var button in buttons) button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(["import-files", "cash-input"], opened);
            Assert.Contains(Texts(panel), text => text == "R022 message for AX123.");
        });
    }

    [Fact]
    public void Panel_hides_when_nothing_is_missing()
    {
        RunSta(() =>
        {
            var query = new FakeReadinessQuery(Result(Item(DailyReadinessItemKind.MissingExport, "R025", DailyReadinessDestination.ImportIntake)));
            var panel = new TodayChecklistPanel(() => query, _ => { }, () => ShellAccess.Owner);
            panel.RefreshAsync(new DateTime(2026, 10, 9), "").GetAwaiter().GetResult();
            Assert.Equal(Visibility.Visible, panel.Visibility);

            query.Next = Result();
            panel.RefreshAsync(new DateTime(2026, 10, 9), "").GetAwaiter().GetResult();

            Assert.Equal(Visibility.Collapsed, panel.Visibility);
            Assert.Empty(panel.Lines);
            Assert.Empty(Buttons(panel));
        });
    }

    [Fact]
    public void Panel_stays_hidden_when_the_build_has_no_readiness_query()
    {
        RunSta(() =>
        {
            var panel = new TodayChecklistPanel(TodayChecklist.Unavailable, _ => { }, () => ShellAccess.Owner);
            panel.RefreshAsync(new DateTime(2026, 10, 9), "").GetAwaiter().GetResult();
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
            Assert.Null(panel.FailureMessage);
        });
    }

    [Fact]
    public void A_failed_check_is_one_plain_line_and_a_diagnostics_entry()
    {
        RunSta(() =>
        {
            var query = new FakeReadinessQuery(Result()) { Failure = new TimeoutException("raw driver text") };
            var panel = new TodayChecklistPanel(() => query, _ => { }, () => ShellAccess.Owner);

            panel.RefreshAsync(new DateTime(2026, 10, 9), "").GetAwaiter().GetResult();

            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.StartsWith("Could not check what's missing for this date: ", panel.FailureMessage);
            Assert.Empty(panel.Lines);
            Assert.Empty(Buttons(panel));
            Assert.Contains(Directory.GetFiles(DiagnosticsIsolation.LogDirectory, "diagnostics-*.jsonl"),
                file => File.ReadAllText(file).Contains("TODAY_CHECKLIST_FAILED", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void A_slower_earlier_answer_does_not_replace_the_current_date()
    {
        RunSta(() =>
        {
            var slow = new TaskCompletionSource<DailyReadinessResult>();
            var query = new FakeReadinessQuery(Result()) { Pending = slow.Task };
            var panel = new TodayChecklistPanel(() => query, _ => { }, () => ShellAccess.Owner);

            var first = panel.RefreshAsync(new DateTime(2026, 10, 8), "");
            query.Pending = null;
            query.Next = Result();
            panel.RefreshAsync(new DateTime(2026, 10, 9), "").GetAwaiter().GetResult();
            slow.SetResult(Result(Item(DailyReadinessItemKind.MissingExport, "R025", DailyReadinessDestination.ImportIntake)));
            first.GetAwaiter().GetResult();

            Assert.Equal(Visibility.Collapsed, panel.Visibility);
            Assert.Empty(panel.Lines);
        });
    }

    [Fact]
    public void Monthly_targets_tab_is_selected_after_brands_and_targets_opens()
    {
        RunSta(() =>
        {
            var tabs = new TabControl();
            var brands = new TabItem { Header = "Brand rows" };
            var targets = new TabItem { Header = "Monthly targets" };
            tabs.Items.Add(brands); tabs.Items.Add(targets);
            brands.IsSelected = true;
            var host = new UserControl { Content = new ScrollViewer { Content = new StackPanel { Children = { tabs } } } };

            Assert.True(TodayChecklist.SelectTab(host, TodayChecklist.MonthlyTargetsTab));
            Assert.True(targets.IsSelected);
            Assert.False(TodayChecklist.SelectTab(new UserControl(), TodayChecklist.MonthlyTargetsTab));
        });
    }

    [Fact]
    public void Daily_sales_workspace_hosts_the_panel_between_toolbar_and_preview()
    {
        RunSta(() =>
        {
            var dsr = new DailySalesReportWorkspace();
            var panel = new TodayChecklistPanel(TodayChecklist.Unavailable, _ => { }, () => ShellAccess.Owner);
            dsr.Checklist = panel;

            Assert.Same(panel, dsr.Checklist);
            var host = dsr.Children.OfType<ContentControl>().Single(x => ReferenceEquals(x.Content, panel));
            Assert.Equal(2, Grid.GetRow(host));
            Assert.Equal(GridUnitType.Auto, dsr.RowDefinitions[2].Height.GridUnitType);
            Assert.Equal(GridUnitType.Star, dsr.RowDefinitions[3].Height.GridUnitType);
            var preview = dsr.Children.OfType<ContentControl>().Single(x => x != host);
            Assert.Equal(3, Grid.GetRow(preview));
        });
    }

    private static DailyReadinessItem Item(DailyReadinessItemKind kind, string code, DailyReadinessDestination destination) =>
        new(kind, "AX123", code, $"{code} message for AX123.", destination);

    private static DailyReadinessResult Result(params DailyReadinessItem[] items) => new(Day, ["AX123"], items);

    private static Button[] Buttons(DependencyObject root) => Descendants(root).OfType<Button>().ToArray();

    private static string[] Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(x => x.Text).ToArray();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class FakeReadinessQuery(DailyReadinessResult next) : IDailyReadinessQuery
    {
        public DailyReadinessResult Next { get; set; } = next;
        public Exception? Failure { get; set; }
        public Task<DailyReadinessResult>? Pending { get; set; }
        public List<DailyReadinessRequest> Requests { get; } = [];

        public Task<DailyReadinessResult> LoadAsync(DailyReadinessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Failure is not null) return Task.FromException<DailyReadinessResult>(Failure);
            return Pending ?? Task.FromResult(Next);
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
