using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Etp.Reporting.Desktop;

public sealed partial class TaskNavigator(MainWindow window)
{
    private TextBox? masterSearch;
    private ListBox? masterResults;
    private Popup? searchPopup;
    private IInputElement? searchReturnFocus;
    private readonly HashSet<UserControl> prepared = new();
    private readonly HashSet<UserControl> visited = new();
    private WorkspaceRoute? displayedRoute;
    private bool databaseContextStarted;
    private bool businessDateDefaulted;
    public static DateTime InitialBusinessDate => DateTime.Today.AddDays(-1);
    private readonly Dictionary<WorkspaceRoute, (double Offset, IInputElement? Focus)> contexts = new();

    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var descendant in LogicalChildren(child)) yield return descendant; }
    }

    private void RememberContext(WorkspaceRoute next)
    {
        if (displayedRoute is not null && window.FocusedWorkspaceHost.Content is DependencyObject previous)
            contexts[displayedRoute] = (LogicalChildren(previous).OfType<ScrollViewer>().FirstOrDefault()?.VerticalOffset ?? 0, Keyboard.FocusedElement);
        displayedRoute = next;
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
        {
            if (window.shell.CurrentRoute != next || window.FocusedWorkspaceHost.Content is not DependencyObject content) return;
            if (contexts.TryGetValue(next, out var context))
            {
                LogicalChildren(content).OfType<ScrollViewer>().FirstOrDefault()?.ScrollToVerticalOffset(context.Offset);
                if (context.Focus is FrameworkElement { IsVisible: true } element) { element.Focus(); return; }
            }
            var buttons = LogicalChildren(content).OfType<Button>().Where(x => x.IsVisible && x.IsEnabled).ToArray();
            (buttons.FirstOrDefault(x => x.Content?.ToString() == "Refresh Preview") ?? buttons.FirstOrDefault())?.Focus();
        });
    }

    public void ShellStore_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (window.reportsWorkspaceView is null || window.ShellBusinessDateSelector is null) return;
        var selected = (window.ShellStoreSelector.SelectedItem as ComboBoxItem)?.Content?.ToString();
        window.reportsWorkspaceView.ApplyScope(window.reportsWorkspaceView.DateFrom, window.reportsWorkspaceView.DateTo, selected == "Titan World" ? "Titan" : selected == "All authorised stores" ? "Combined" : selected);
        var store = selected == "Titan World" ? "WLMHW" : selected == "Helios" ? "HEMW" : null;
        var scope = store == "WLMHW" ? "Titan" : store == "HEMW" ? "Helios" : "Combined (Titan + Helios)";
        if (window.FocusedWorkspaceHost.Content is ReportWorkspaceControl report && report.ScopeSelector.IsEnabled) report.SetStoreScope(scope);
        if (store is not null && !visited.Contains(window.dailyWorkflowWorkspace)) window.dailyWorkflowWorkspace.StoreCode = store;
        window.ApplicationStatus.Text = store is null ? "All authorised stores. Select an explicit task store for imports and edits." : $"Current scope: {store}";
    }

    /// <summary>Until the user picks a date, the shell business date follows the newest imported ETP day so reports open with data.</summary>
    public void DefaultBusinessDateToLatestData(DateOnly? latest)
    {
        if (businessDateDefaulted || latest is not { } date) return;
        businessDateDefaulted = true;
        var selected = window.ShellBusinessDateSelector.SelectedDate;
        if (selected is null || selected.Value.Date != InitialBusinessDate) return;
        var target = date.ToDateTime(TimeOnly.MinValue);
        if (target != selected.Value.Date) window.ShellBusinessDateSelector.SelectedDate = target;
    }

    public void ApplyBusinessDate(DateTime selected)
    {
        window.reportsWorkspaceView.SetBusinessDate(selected);
        if (window.FocusedWorkspaceHost.Content is ReportWorkspaceControl report) report.DateToPicker.SelectedDate = selected;
        if (window.FocusedWorkspaceHost.Content is DailySalesReportWorkspace dsr) dsr.BusinessDatePicker.SelectedDate = selected;
        // Open editing workspaces retain explicit context so global changes cannot retarget drafts or jobs.
        if (!visited.Contains(window.dailyWorkflowWorkspace)) window.dailyWorkflowWorkspace.BusinessDate = selected;
        if (!visited.Contains(window.importWorkspaceView)) window.importWorkspaceView.BusinessDate = selected;
        if (!visited.Contains(window.registersWorkspaceView)) window.registersWorkspaceView.BusinessDate = selected;
        if (!visited.Contains(window.accountingWorkspaceView)) window.accountingWorkspaceView.BusinessDate = selected;
        if (!visited.Contains(window.sourceInboxWorkspaceView)) window.sourceInboxWorkspaceView.BusinessDate = selected;
        if (!visited.Contains(window.archiveWorkspaceView)) window.archiveWorkspaceView.BusinessDate = selected;
        window.ApplicationStatus.Text = "Report date updated. Open tasks retain their displayed task date and store; change those fields explicitly when needed.";
    }

    public void InitializeTaskNavigation()
    {
        var statusDetails = new Button { Content = "Status details", Padding = new Thickness(8,0,8,0), Margin = new Thickness(0,0,8,0) };
        statusDetails.Click += (_,_) => new StatusDetailsDialog(window,CurrentStatusDetails()).ShowDialog();
        AutomationProperties.SetName(statusDetails,"Read full application status"); DockPanel.SetDock(statusDetails,Dock.Right);
        ((DockPanel)window.ApplicationStatus.Parent).Children.Insert(0,statusDetails);
        window.settingsWorkspace.CanChangeDatabase = () => !databaseContextStarted && !HasUnsavedDrafts && !RetainedDrafts.Any();
        window.ShellStoreSelector.SelectionChanged += ShellStore_Changed;
        masterSearch = new TextBox { MinWidth = 180, MaxWidth = 320, Margin = new Thickness(8, 0, 8, 0), ToolTip = "Search every task (Ctrl+K)" };
        AutomationProperties.SetName(masterSearch, "Search tasks and reports");
        masterResults = new ListBox { MaxHeight = 380, MinWidth = 460 };
        masterResults.SetResourceReference(Control.BackgroundProperty, "Surface");
        searchPopup = new Popup { PlacementTarget = masterSearch, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Child = masterResults, BorderThickness = new Thickness(1), BorderBrush = (Brush)window.FindResource("Divider"), Padding = new Thickness(8), Background = (Brush)window.FindResource("Surface") } };
        masterSearch.TextChanged += (_, _) => UpdateMasterSearch();
        masterSearch.PreviewMouseLeftButtonDown += (_, _) => window.Dispatcher.BeginInvoke(UpdateMasterSearch);
        masterSearch.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down) { masterResults.SelectedIndex = Math.Min(masterResults.Items.Count - 1, masterResults.SelectedIndex + 1); e.Handled = true; }
            if (e.Key == Key.Up) { masterResults.SelectedIndex = Math.Max(0, masterResults.SelectedIndex - 1); e.Handled = true; }
            if (e.Key == Key.Enter) { if (searchPopup.IsOpen) OpenSearchSelection(); else UpdateMasterSearch(); e.Handled = true; }
            if (e.Key == Key.Escape) { CloseMasterSearch(); e.Handled = true; }
        };
        masterResults.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { OpenSearchSelection(); e.Handled = true; } if (e.Key == Key.Escape) { CloseMasterSearch(); e.Handled = true; } };
        masterResults.PreviewMouseLeftButtonUp += (_, _) => OpenSearchSelection();
        var searchField = new StackPanel();
        searchField.Children.Add(new TextBlock { Text = "Search tasks · Ctrl+K", FontSize = 12, Margin = new Thickness(8,0,0,2) });
        searchField.Children.Add(masterSearch); window.HeaderSearchHost.Content = searchField;
        if (window.ShellBusinessDateSelector.SelectedDate is { } date) ApplyBusinessDate(date);
        window.Closing += (_, e) =>
        {
            if (window.reportsWorkspaceView.IsExportInProgress)
            {
                e.Cancel = true;
                window.ApplicationStatus.Text = "Wait for the report file to finish saving before closing ETP.";
                return;
            }
            if (!allowClose && BlockWhileBusy()) { e.Cancel = true; return; }
            if (allowClose || (!HasUnsavedDrafts && !RetainedDrafts.Any())) return;
            e.Cancel = true;
            if (!navigationPending) _ = ConfirmCloseAsync();
        };
    }

    private string CurrentStatusDetails() => string.Join("\n\n", new[] { window.ApplicationStatus.Text }.Concat(
        window.FocusedWorkspaceHost.Content is DependencyObject content ? LogicalChildren(content).OfType<TextBlock>()
            .Where(text => text.Name.Contains("Status",StringComparison.Ordinal) || text.Name.Contains("Result",StringComparison.Ordinal))
            .Select(text => text.Text) : []).Where(text => !string.IsNullOrWhiteSpace(text)).Distinct());

    private void UpdateMasterSearch()
    {
        if (masterResults is null || masterSearch is null || searchPopup is null) return;
        masterResults.Items.Clear();
        foreach (var task in TaskNavigation.Search(masterSearch.Text, window.CurrentShellAccess))
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = task.Title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            text.Children.Add(new TextBlock { Text = task.Path, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = task.Purpose, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            var item = new ListBoxItem { Content = text, Tag = task, MinHeight = 48, Padding = new Thickness(8) };
            AutomationProperties.SetName(item, task.Path); masterResults.Items.Add(item);
        }
        if (masterResults.Items.Count == 0) masterResults.Items.Add(new ListBoxItem { Content = "No authorised tasks match. Try a report name or task.", IsEnabled = false });
        else masterResults.SelectedIndex = 0;
        searchPopup.IsOpen = masterSearch.IsKeyboardFocusWithin;
    }

    private void OpenSearchSelection()
    {
        if (masterResults?.SelectedItem is not ListBoxItem { Tag: TaskDestination task }) return;
        CloseMasterSearch(); NavigateTask(task);
    }

    private void CloseMasterSearch()
    {
        if (searchPopup is not null) searchPopup.IsOpen = false;
        if (searchReturnFocus is not null) Keyboard.Focus(searchReturnFocus);
    }

    public void FocusMasterSearch()
    {
        searchReturnFocus = Keyboard.FocusedElement;
        masterSearch?.Focus(); masterSearch?.SelectAll(); UpdateMasterSearch();
    }

    private bool navigationPending;
    private bool allowClose;
    private bool HasUnsavedDrafts => window.registersWorkspaceView.HasUnsavedChanges || window.dailyWorkflowWorkspace.UnsavedDrafts.Count > 0 || window.settingsWorkspace.HasProductDraft
        || window.operationsWorkspaceView.HasWatchDraft || window.operationsWorkspaceView.UnsavedSchedules.Count > 0 || window.administrationWorkspaceView.UnsavedDrafts.Count > 0 || window.archiveWorkspaceView.UnsavedContacts.Count > 0;
    private IEnumerable<(string Name, Action Discard)> RetainedDrafts
    {
        get
        {
            if (window.accountingWorkspaceView.HasRetainedDraft) yield return ("Accounting review", window.accountingWorkspaceView.DiscardRetainedDraft);
            if (window.investigationWorkspaceView.HasRetainedDraft) yield return ("Adjustments and approvals", window.investigationWorkspaceView.DiscardRetainedDraft);
            if (window.operationsWorkspaceView.HasRetainedDraft) yield return ("Data-quality review", window.operationsWorkspaceView.DiscardRetainedDraft);
        }
    }
    private bool BlockWhileBusy()
    {
        if (!window.settingsWorkspace.IsBusy && !window.operationsWorkspaceView.IsBusy && !window.registersWorkspaceView.IsBusy
            && !window.dailyWorkflowWorkspace.IsBusy && !window.administrationWorkspaceView.IsBusy && !window.importWorkspaceView.IsBusy
            && !window.accountingWorkspaceView.IsBusy && !window.archiveWorkspaceView.IsBusy && !window.investigationWorkspaceView.IsBusy && !window.sourceInboxWorkspaceView.IsBusy) return false;
        window.ApplicationStatus.Text = "Wait for the current operation to finish before leaving this task or closing ETP.";
        return true;
    }
    public bool NavigateSafely(Func<NavigationDecision> navigate)
    {
        if (navigationPending || BlockWhileBusy()) return false;
        if (!HasUnsavedDrafts)
        {
            var decision = navigate(); window.ApplyNavigationDecision(decision); return decision.IsAllowed;
        }
        _ = ContinueNavigationAsync(navigate); return false;
    }

    public void ExportCurrentReport(bool pdf)
    {
        var current = window.FocusedWorkspaceHost.Content switch { ReportWorkspaceControl report => report.HasCurrentPreview, DailySalesReportWorkspace dsr => dsr.HasCurrentPreview, _ => false };
        if (!current) { window.ApplicationStatus.Text = "Refresh the report for the displayed date and store before exporting."; return; }
        if (pdf) window.reportsWorkspaceView.ExportPdf(); else window.reportsWorkspaceView.ExportExcel();
    }

    private async Task ContinueNavigationAsync(Func<NavigationDecision> navigate)
    {
        navigationPending = true;
        try
        {
            if (!await ResolveDraftsAsync()) return;
            window.ApplyNavigationDecision(navigate());
        }
        finally { navigationPending = false; }
    }

    private async Task<bool> ResolveDraftsAsync()
    {
        foreach (var id in window.archiveWorkspaceView.UnsavedContacts)
        {
            var dialog = new DraftNavigationDialog(window, id == 0 ? "New sharing contact" : $"Sharing contact {id}"); dialog.ShowDialog();
            if (dialog.Choice == DraftNavigationChoice.Stay) return false;
            if (dialog.Choice == DraftNavigationChoice.Save && !await window.archiveWorkspaceView.SaveContactDraftAsync(id)) return false;
            if (dialog.Choice == DraftNavigationChoice.Discard) window.archiveWorkspaceView.DiscardContactDraft(id);
        }
        foreach (var task in window.administrationWorkspaceView.UnsavedDrafts)
        {
            var dialog = new DraftNavigationDialog(window, task); dialog.ShowDialog();
            if (dialog.Choice == DraftNavigationChoice.Stay) return false;
            if (dialog.Choice == DraftNavigationChoice.Save && !await window.administrationWorkspaceView.SaveDraftAsync(task)) return false;
            if (dialog.Choice == DraftNavigationChoice.Discard) window.administrationWorkspaceView.DiscardDraft(task);
        }
        if (window.operationsWorkspaceView.HasWatchDraft)
        {
            var dialog = new DraftNavigationDialog(window, "Watch-folder settings"); dialog.ShowDialog();
            if (dialog.Choice == DraftNavigationChoice.Stay) return false;
            if (dialog.Choice == DraftNavigationChoice.Save && !await window.operationsWorkspaceView.SaveWatchDraftAsync()) return false;
            if (dialog.Choice == DraftNavigationChoice.Discard) window.operationsWorkspaceView.DiscardWatchDraft();
        }
        foreach (var id in window.operationsWorkspaceView.UnsavedSchedules)
        {
            var dialog = new DraftNavigationDialog(window, $"Report schedule {id}"); dialog.ShowDialog();
            if (dialog.Choice == DraftNavigationChoice.Stay) return false;
            if (dialog.Choice == DraftNavigationChoice.Save && !await window.operationsWorkspaceView.SaveScheduleDraftAsync(id)) return false;
            if (dialog.Choice == DraftNavigationChoice.Discard) window.operationsWorkspaceView.DiscardScheduleDraft(id);
        }
        if (window.settingsWorkspace.HasProductDraft)
        {
            var dialog = new DraftNavigationDialog(window, "Integration settings"); dialog.ShowDialog();
            if (dialog.Choice == DraftNavigationChoice.Stay) return false;
            if (dialog.Choice == DraftNavigationChoice.Save && !await window.settingsWorkspace.SaveProductConfigurationAsync()) return false;
            if (dialog.Choice == DraftNavigationChoice.Discard) window.settingsWorkspace.DiscardProductDraft();
        }
        foreach (var task in window.dailyWorkflowWorkspace.UnsavedDrafts)
        {
            var daily = new DraftNavigationDialog(window, task); daily.ShowDialog();
            if (daily.Choice == DraftNavigationChoice.Stay) return false;
            if (daily.Choice == DraftNavigationChoice.Save && !await window.dailyWorkflowWorkspace.SaveDraftAsync(task)) return false;
            if (daily.Choice == DraftNavigationChoice.Discard) window.dailyWorkflowWorkspace.DiscardDraft(task);
        }
        if (window.registersWorkspaceView.HasUnsavedChanges)
        {
            var dialog = new DraftNavigationDialog(window, "Register entry"); dialog.ShowDialog();
            if (dialog.Choice == DraftNavigationChoice.Stay) return false;
            if (dialog.Choice == DraftNavigationChoice.Save) return await window.registersWorkspaceView.SaveDraftAsync();
            window.registersWorkspaceView.DiscardDraft();
        }
        return true;
    }

    private async Task ConfirmCloseAsync()
    {
        navigationPending = true;
        try
        {
            if (!await ResolveDraftsAsync()) return;
            foreach (var draft in RetainedDrafts.ToArray())
            {
                var dialog = new DraftNavigationDialog(window, draft.Name, canSave: false); dialog.ShowDialog();
                if (dialog.Choice != DraftNavigationChoice.Discard) return;
                draft.Discard();
            }
            allowClose = true; _ = window.Dispatcher.BeginInvoke(window.Close);
        }
        finally { navigationPending = false; }
    }

    public void NavigateTask(TaskDestination task)
    {
        if (task.Section == "profile") { window.OpenProfile_Click(window, new RoutedEventArgs()); return; }
        NavigateSafely(() => window.shell.Navigate(task.Route, window.CurrentShellAccess));
    }

    public void NavigateOverview(string module, string destination, string? category = null)
    {
        if (module == "System Health") { module = "Settings"; category = "Database & Recovery"; }
        NavigateSafely(() => window.shell.Navigate(new(destination, TaskId: category is null ? "overview:" + module : "category:" + module + ":" + category), window.CurrentShellAccess));
    }

    private void ShowTaskOverview(string module, string destination, string? category)
    {
        window.HideAllFeaturePanels(); window.HideSidebar();
        window.focusedWorkspaceKind = "overview";
        window.LegacyWorkspaceScroll.Visibility = Visibility.Collapsed;
        window.FocusedWorkspaceLayer.Visibility = Visibility.Visible;
        var panel = new UniformGrid { Columns = window.ActualWidth >= 1280 ? 3 : 2, Margin = new Thickness(12) };
        var tasks = TaskNavigation.All.Where(x => x.Module == module && x.Section != "overview" && x.IsAllowed(window.CurrentShellAccess)).ToArray();
        if (module == "Reports" && category == "Favourites") { ShowFavouriteReports(); return; }
        if (module == "Reports" && category is null) { ShowReportCategories(tasks, destination); return; }
        var entries = category is null ? tasks.Select(x => x.Category).Distinct().Order().ToArray() : Array.Empty<string>();
        foreach (var name in entries)
        {
            var count = tasks.Count(x => x.Category == name);
            var button = MakeTaskTile(name, count == 1 ? "1 task" : $"{count} tasks", () => NavigateOverview(module, destination, name)); panel.Children.Add(button);
        }
        if (category is not null)
        foreach (var task in tasks.Where(x => x.Category == category)) panel.Children.Add(MakeTaskTile(task.Title, task.Purpose, () => NavigateTask(task)));
        if (panel.Children.Count > 6) panel.Columns = 3;
        panel.MinHeight = Math.Ceiling(panel.Children.Count / (double)panel.Columns) * 104;
        window.PageTitle.Text = category ?? module;
        window.BreadcrumbText.Text = category is null ? $"Modules → {module}" : $"{module} → {category}";
        window.PageDescription.Text = "Choose a focused task.";
        window.FocusedWorkspaceHost.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetBreadcrumb(module, destination, category);
    }

    private Button MakeTaskTile(string title, string detail, Action open)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MaxHeight = 30 });
        var button = new Button { Content = body, Margin = new Thickness(6), Padding = new Thickness(10), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, title); button.Click += (_, _) => open(); return button;
    }

    private void SetBreadcrumb(string module, string destination, string? category)
    {
        window.BreadcrumbLinks.Children.Clear();
        void Link(string name, Action click)
        {
            var button = new Button { Content = name, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0) };
            button.Click += (_, _) => click(); window.BreadcrumbLinks.Children.Add(button);
        }
        Link("← Back", () => window.NavigateHistory(true));
        Link(module, () => NavigateOverview(module, destination));
        if (category is not null) Link(category, () => NavigateOverview(module, destination, category));
    }
    private void ShowFavouriteReports()
    {
        window.HideAllFeaturePanels(); window.HideSidebar(); window.LegacyWorkspaceScroll.Visibility = Visibility.Collapsed;
        window.FocusedWorkspaceLayer.Visibility = Visibility.Visible; window.focusedWorkspaceKind = "task";
        window.PageTitle.Text = "Favourite Reports"; window.PageDescription.Text = "Your selected reports, grouped by category.";
        window.BreadcrumbText.Text = "Reports → Favourites → Favourite Reports";
        SetBreadcrumb("Reports", "Sales Reports", "Favourites");
        window.FocusedWorkspaceHost.Content = new Modules.Reports.FavouriteReportsView(UiPreferenceStore.Load(), window.CurrentShellAccess, NavigateTask);
    }
    private void ShowReportCategories(TaskDestination[] tasks, string destination)
    {
        var body = new StackPanel { Margin = new Thickness(16) };
        var secondary = new[] { "Favourites", "Filters", "Report Packs" };
        foreach (var tools in new[] { false, true })
        {
            body.Children.Add(new TextBlock { Text = tools ? "Favourites, filters and packs" : "Report categories", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, tools ? 12 : 0, 0, 6) });
            var group = new UniformGrid { Columns = tools ? 3 : 2 };
            foreach (var category in tasks.Select(task => task.Category).Distinct().Where(category => secondary.Contains(category) == tools).Order())
            {
                var count = tasks.Count(task => task.Category == category);
                var button = new Button { Content = new TextBlock { Text = $"{category} · {count}", FontSize = 14, TextWrapping = TextWrapping.Wrap }, MinHeight = 48, Margin = new Thickness(4), Padding = new Thickness(8) };
                AutomationProperties.SetName(button, category); button.Click += (_, _) => NavigateOverview("Reports", destination, category); group.Children.Add(button);
            }
            body.Children.Add(group);
        }
        window.PageTitle.Text = "Reports"; window.PageDescription.Text = "Choose a report category or a reporting tool."; window.BreadcrumbText.Text = "Modules → Reports";
        SetBreadcrumb("Reports", destination, null);
        window.FocusedWorkspaceHost.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    public void RestoreBreadcrumbs()
    {
        var route = window.shell.CurrentRoute;
        if (TaskNavigation.Find(route.TaskId) is { } task) SetBreadcrumb(task.Module, task.Destination, task.Category);
        else if (route.TaskId?.StartsWith("overview:") == true) SetBreadcrumb(route.TaskId[9..], route.Destination, null);
        else if (route.TaskId?.StartsWith("category:") == true) { var bits = route.TaskId.Split(':',3); SetBreadcrumb(bits[1], route.Destination, bits[2]); }
        else window.BreadcrumbLinks.Children.Clear();
    }

    public bool DisplayTaskRoute(WorkspaceRoute route)
    {
        pageSearch?.Close();
        RememberContext(route);
        if (route.TaskId?.StartsWith("overview:") == true) { ShowTaskOverview(route.TaskId[9..], route.Destination, null); return true; }
        if (route.TaskId?.StartsWith("category:") == true)
        {
            var bits = route.TaskId.Split(':', 3); ShowTaskOverview(bits[1], route.Destination, bits[2]); return true;
        }
        if (TaskNavigation.Find(route.TaskId) is not { } task) return false;
        if (task.Section == "favourite-reports") { ShowFavouriteReports(); return true; }
        if (task.Section == "help") { window.ShowHelpWorkspace(task.Id[5..]); return true; }
        if (task.Section == "profile") { window.OpenProfile_Click(window, new RoutedEventArgs()); return true; }
        if (task.Section == "overview") { ShowTaskOverview(task.Module, task.Destination, null); return true; }
        if (task.Id != "settings" && task.Destination is not "Settings" and not "Dashboard") databaseContextStarted = true;
        if (task.ReportCode is { } code) { _ = window.reportsWorkspaceView.RunReportAsync(code); SetBreadcrumb(task.Module, task.Destination, task.Category); return true; }
        window.HideAllFeaturePanels(); window.HideSidebar();
        window.PageTitle.Text = task.Title; window.BreadcrumbText.Text = task.Path; window.PageDescription.Text = task.Purpose;
        SetBreadcrumb(task.Module, task.Destination, task.Category);
        UserControl view;
        if (task.Id == "profiles") view = new Modules.Settings.ApprovedProfilesView();
        else if (task.Id == "settings") view = new Modules.Settings.GeneralPreferencesView(UiPreferenceStore.Load(), window.SavePreferences, window.CurrentShellAccess);
        else if (task.Destination == "Dashboard") { view = window.dashboardView; window.dashboardView.SelectTask(task.Id); _ = window.RefreshDashboardAsync(); }
        else view = ResolveTaskView(task);
        if (view.Parent is ContentControl host) host.Content = null;
        window.LegacyWorkspaceScroll.Visibility = Visibility.Collapsed;
        window.FocusedWorkspaceLayer.Visibility = Visibility.Visible;
        window.FocusedWorkspaceHost.Content = view; window.focusedWorkspaceKind = "task";
        return true;
    }

    private UserControl ResolveTaskView(TaskDestination task)
    {
        // Each layout selects the existing module's controls; no business operation is invoked here.
        UserControl view;
        int[] body; int[] actions;
        var id = task.Id;
        if (task.Section == "import-results" || task.Destination == "Import ETP" && task.Section != "inbox")
        {
            window.importWorkspaceView.SelectTask(id);
            visited.Add(window.importWorkspaceView);
            return window.importWorkspaceView;
        }
        if (task.Section == "inbox")
        {
            window.sourceInboxWorkspaceView.SelectTask(id);
            visited.Add(window.sourceInboxWorkspaceView);
            return window.sourceInboxWorkspaceView;
        }
        if (id is "masters" or "tender-rules") return new UserControl { Content = new ScrollViewer { Content = window.settingsWorkspace.CreateDataTruthMastersView(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        if (task.Section == "report-filters") { view = window.reportsWorkspaceView; body = new int[] {0,1}; actions = new int[] {}; }
        else if (task.Destination is "Daily Workflow" or "Manual Entry")
        {
            view = window.dailyWorkflowWorkspace;
            window.dailyWorkflowWorkspace.SelectPackTask(id);
            (body, actions) = task.Section switch
            {
                "manual" => (new int[] {2,7,3,6}, new int[] {7}), "stock-count" => (new int[] {2,11,3,10}, new int[] {11}),
                "staff-target" => (new int[] {2,13,3}, new int[] {13}), "finalisation" => (new int[] {2,14,3}, new int[] {14}),
                "readiness" => (new int[] {2,3}, new int[] {}), _ => (new int[] {2,3,16}, new int[] {15})
            };
        }
        else if (task.Destination == "Registers") { view = window.registersWorkspaceView; body = new int[] {4,5,6,1,2}; actions = new int[] {0,1,5}; window.registersWorkspaceView.SelectTask(id); }
        else if (task.Destination == "Accounting")
        {
            view = window.accountingWorkspaceView; window.accountingWorkspaceView.SelectTask(id);
            (body, actions) = id == "accounting-approval" ? (new int[] {3,5,6,9}, new int[] {2}) : id is "ledger-mapping" or "mapping-review" ? (new int[] {3,7}, new int[] {8}) : id is "export-history" or "tally-export" ? (new int[] {3,5,6}, new int[] {2}) : (new int[] {3,4,5,6}, new int[] {2});
        }
        else if (task.Destination == "Report Archive") { view = window.archiveWorkspaceView; window.archiveWorkspaceView.SelectTask(id); body = id == "sharing-contacts" ? new int[] {6,7,9} : id == "shared" ? new int[] {2,3,5,9,10} : new int[] {2,3,9,10}; actions = id == "sharing-contacts" ? new int[] {8} : new int[] {4}; }
        else if (id is "connection" or "settings" or "sharing")
        { view = window.settingsWorkspace; if (id == "sharing") window.settingsWorkspace.SelectIntegrationTask(id); body = id is "connection" or "settings" ? new int[] {0,1,3} : new int[] {3,5}; actions = id is "connection" or "settings" ? new int[] {2} : new int[] {}; }
        else if (task.Destination is "Masters" or "Admin / Settings")
        {
            view = window.administrationWorkspaceView; window.administrationWorkspaceView.SelectTask(id);
            (body, actions) = id switch { "users" => (new int[] {5,6,8,14}, new int[] {7}), "kpi" or "profiles" => (new int[] {10,11,14}, new int[] {}), "health" => (new int[] {12,13,14}, new int[] {}), _ => (new int[] {0,1,3,14}, new int[] {2}) };
        }
        else if (id is "approval-centre" or "adjustment" or "investigation")
        { view = window.investigationWorkspaceView; (body, actions) = id switch { "approval-centre" => (new int[] {3,7,8}, new int[] {9}), "adjustment" => (new int[] {3,5,6}, new int[] {}), _ => (new int[] {0,1,3,4}, new int[] {2}) }; }
        else if (id is "backups" or "support-package" or "recovery")
        { view = window.operationsWorkspaceView; body = new int[] {29}; actions = new int[] {28}; window.operationsWorkspaceView.SelectMaintenanceTask(id); }
        else if (id is "watch-folder" or "scheduler")
        { view = window.operationsWorkspaceView; (body, actions) = id == "watch-folder" ? (new int[] {2,12,13,14,15,16,17,18,19}, new int[] {20}) : (new int[] {2,21,22,24,25}, new int[] {23}); }
        else { view = window.operationsWorkspaceView; window.operationsWorkspaceView.SelectIssueTask(id); body = id is "trends" ? new int[] {1,2,3,4,5} : new int[] {1,2,8,7}; actions = id is "trends" ? new int[] {0} : new int[] {0,8}; }
        FocusedTaskLayout.Show(view, task.Title, body, actions);
        visited.Add(view);
        if (prepared.Add(view))
        {
            if (view == window.dailyWorkflowWorkspace) { window.dailyWorkflowWorkspace.RefreshAccessState(); _ = window.dailyWorkflowWorkspace.RefreshAsync(); }
            if (view == window.accountingWorkspaceView) _ = window.accountingWorkspaceView.RefreshAsync();
            if (view == window.archiveWorkspaceView) _ = window.archiveWorkspaceView.RefreshAsync();
            if (view == window.operationsWorkspaceView) _ = window.operationsWorkspaceView.RefreshAsync();
            if (view == window.investigationWorkspaceView) _ = window.investigationWorkspaceView.RefreshApprovalsAsync();
        }
        return view;
    }
}
