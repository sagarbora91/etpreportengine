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
    private StackPanel? searchWindowContent;
    private Window? searchWindow;
    private ListBox? masterResults;
    private Popup? searchPopup;
    private IInputElement? searchReturnFocus;
    private readonly HashSet<UserControl> prepared = new();
    private readonly HashSet<UserControl> visited = new();
    private WorkspaceRoute? displayedRoute;
    private bool databaseContextStarted;
    public static DateTime InitialBusinessDate => DateTime.Today;
    private readonly Dictionary<WorkspaceRoute, (double Offset, IInputElement? Focus)> contexts = new();

    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var descendant in LogicalChildren(child)) yield return descendant; }
    }

    /// <summary>
    /// Which of the administration view's children each Settings task shows, by position.
    /// R4 appends the Database and recovery section at the end of that view, so every index
    /// here is the one the screen always had. The recovery grid has to be a direct child of
    /// the panel: the focused layout only gives a named tab to a direct child, and nested in
    /// a Border it landed in the unnamed catch-all tab instead.
    /// 13 ProductHealthGrid, 14 AdministrationStatus, 16 DatabaseRecoveryStatus,
    /// 17 DatabaseRecoveryGrid, 18 the Support package button.
    /// <para>
    /// 14 is the only place any of these screens says why a save or a refresh failed, and
    /// until 25 September 2026 every layout but "health" left it out, so Settings &gt; Users
    /// refused a save in silence. Where it sits in the array does not matter: TaskBodyLayout
    /// lifts every <c>*Status</c> and <c>*Result</c> text block out of the body and docks it
    /// above the tabs. What matters is that it is listed at all.
    /// Internal so the tests can assert these layouts without building a window.
    /// </para>
    /// </summary>
    internal static (int[] Body, int[] Actions) AdministrationTaskLayout(string id) => id switch
    {
        "users" => (new int[] {5,6,8,13,14}, new int[] {7}),
        "kpi" or "profiles" => (new int[] {10,11,13,14}, new int[] {}),
        "health" => (new int[] {16,17,13,14}, new int[] {18}),
        _ => (new int[] {0,1,3,13,14}, new int[] {2})
    };

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
            if (next.TaskId == "walk-ins") { ((TextBox)window.dailyWorkflowWorkspace.FindName("ManualValueInput")).Focus(); return; }
            var buttons = LogicalChildren(content).OfType<Button>().Where(x => x.IsVisible && x.IsEnabled).ToArray();
            (buttons.FirstOrDefault(x => x.Content?.ToString() == "Refresh Preview") ?? buttons.FirstOrDefault())?.Focus();
        });
    }

    private bool updatingScope;
    private DateTime appliedDate = InitialBusinessDate;
    private int appliedStore;
    private string HeaderStore => appliedStore >= 0 && appliedStore < window.ShellStoreSelector.Items.Count
        ? (window.ShellStoreSelector.Items[appliedStore] as ComboBoxItem)?.Tag?.ToString() ?? "" : "";

    internal void SetStores(StoreScopeCatalog catalog)
    {
        var previous = HeaderStore;
        updatingScope = true;
        try
        {
            window.ShellStoreSelector.Items.Clear();
            var labelFactory = new FrameworkElementFactory(typeof(TextBlock));
            labelFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            labelFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            labelFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            var labelTemplate = new DataTemplate { VisualTree = labelFactory };
            foreach(var store in catalog.Stores) window.ShellStoreSelector.Items.Add(new ComboBoxItem { Content=StoreScopeCatalog.Label(store), Tag=store.Code, ToolTip=StoreScopeCatalog.Label(store), ContentTemplate=labelTemplate });
            window.ShellStoreSelector.Items.Add(new ComboBoxItem { Content=StoreScopeCatalog.AllStores, Tag="" });
            window.ShellStoreSelector.SelectedItem=window.ShellStoreSelector.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>x.Tag?.ToString()==previous)
                ?? window.ShellStoreSelector.Items[window.ShellStoreSelector.Items.Count-1];
            appliedStore=window.ShellStoreSelector.SelectedIndex;
        }
        finally { updatingScope=false; }
    }

    public void ShellStore_Changed(object sender, SelectionChangedEventArgs e) => RequestScopeChange();

    public async void RequestScopeChange()
    {
        if (updatingScope || window.ShellBusinessDateSelector.SelectedDate is not { } nextDate) return;
        var nextStore = window.ShellStoreSelector.SelectedIndex;
        if (nextDate == appliedDate && nextStore == appliedStore) return;
        RestoreHeader(appliedDate, appliedStore);
        if (navigationPending || BlockWhileBusy()) return;
        navigationPending = true;
        try
        {
            // Resolve drafts while every editor and the header still show the OLD scope.
            if (!await ResolveDraftsAsync()) return;
            foreach (var draft in RetainedDrafts.ToArray())
            {
                var dialog = new DraftNavigationDialog(window, draft.Name, canSave: false); dialog.ShowDialog();
                if (dialog.Choice != DraftNavigationChoice.Discard) return;
                draft.Discard();
            }
            appliedStore = nextStore;
            ApplyBusinessDate(nextDate);
            RestoreHeader(nextDate, nextStore);
            window.reportsWorkspaceView.ApplyScope(window.reportsWorkspaceView.DateFrom, nextDate,
                HeaderStore);
            window.dailyWorkflowWorkspace.StoreCode = HeaderStore;
            ApplyHiddenScope(window.registersWorkspaceView);
            ApplyHiddenScope(window.accountingWorkspaceView);
            ApplyHiddenScope(window.sourceInboxWorkspaceView);
            ApplyHiddenScope(window.archiveWorkspaceView);
            if (TaskNavigation.Find(window.shell.CurrentRoute.TaskId) is { } task) DisplayTaskRoute(task.Route);
            window.ApplicationStatus.Text = $"Scope updated: {nextDate:dd MMM yyyy}.";
        }
        finally { navigationPending = false; }
    }

    private void RestoreHeader(DateTime date, int store)
    {
        updatingScope = true;
        try { window.ShellBusinessDateSelector.SelectedDate = date; window.ShellStoreSelector.SelectedIndex = store; }
        finally { updatingScope = false; }
    }

    private void ApplyHiddenScope(UserControl view) => ShellScopeApplier.Apply(view, appliedDate, HeaderStore);

    // The user-selected header date is authoritative; imports never silently retarget it.
    public void DefaultBusinessDateToLatestData(DateOnly? latest) { }

    public void ApplyBusinessDate(DateTime selected)
    {
        appliedDate = selected;
        window.reportsWorkspaceView.SetBusinessDate(selected);
        if (window.FocusedWorkspaceHost.Content is ReportWorkspaceControl report) report.DateToPicker.SelectedDate = selected;
        if (window.FocusedWorkspaceHost.Content is DailySalesReportWorkspace dsr) dsr.BusinessDatePicker.SelectedDate = selected;
        window.dailyWorkflowWorkspace.BusinessDate = selected;
        window.importWorkspaceView.BusinessDate = selected;
        window.registersWorkspaceView.ApplyHeaderScope(selected, HeaderStore);
        window.accountingWorkspaceView.BusinessDate = selected;
        window.sourceInboxWorkspaceView.BusinessDate = selected;
        window.archiveWorkspaceView.BusinessDate = selected;
    }

    public void InitializeTaskNavigation()
    {
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
        searchField.Children.Add(masterSearch);
        searchWindowContent = searchField;
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

    internal string CurrentStatusDetails() => string.Join("\n\n", new[] { window.ApplicationStatus.Text }.Concat(
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
        searchWindow?.Close();
        if (searchReturnFocus is not null) Keyboard.Focus(searchReturnFocus);
    }

    public void FocusMasterSearch()
    {
        searchReturnFocus = Keyboard.FocusedElement;
        if (searchWindow is null)
        {
            searchWindow = new Window { Title = "Find a screen", Owner = window, Width = 520, Height = 180, Content = searchWindowContent, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            searchWindow.Closed += (_,_) => { if (searchWindow is not null) searchWindow.Content = null; searchWindow = null; };
            searchWindow.Show();
        }
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
        if (!window.reportsWorkspaceView.IsExportInProgress && !window.settingsWorkspace.IsBusy && !window.operationsWorkspaceView.IsBusy && !window.registersWorkspaceView.IsBusy
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

    public void NavigateOverview(string module, string destination, string? category = null) =>
        window.OpenSection(module switch { "Dashboard" => "Today", "Imports" => "Import", "Reports" => "Reports", _ => "Settings" });
    public void RestoreBreadcrumbs() { if (TaskNavigation.Find(window.shell.CurrentRoute.TaskId) is { } task) window.UpdateSection(task); }

    public bool DisplayTaskRoute(WorkspaceRoute route)
    {
        pageSearch?.Close();
        RememberContext(route);
        if (TaskNavigation.Find(route.TaskId) is not { } task) return false;

        window.UpdateSection(task);
        window.ShellStoreSelector.IsEnabled = true;
        if (task.Section == "report-list") { window.FocusedWorkspaceHost.Content = new Modules.Reports.ReportListView(window.CurrentShellAccess, NavigateTask); window.focusedWorkspaceKind = "task"; return true; }
        if (task.Section == "favourite-reports") { window.FocusedWorkspaceHost.Content = new Modules.Reports.FavouriteReportsView(UiPreferenceStore.Load(), window.CurrentShellAccess, NavigateTask); return true; }
        if (task.Section == "help") { window.ShowHelpWorkspace(task.Id[5..]); return true; }
        if (task.Section == "profile") { window.OpenProfile_Click(window, new RoutedEventArgs()); return true; }

        if (task.Id != "settings" && task.Destination is not "Settings" and not "Dashboard") databaseContextStarted = true;
        window.UpdateSection(task);
        window.ShellStoreSelector.IsEnabled = task.ReportCode is not ("dsr" or "sales-combined");
        if (task.ReportCode is "dsr" or "sales-combined")
        {
            appliedStore = window.ShellStoreSelector.Items.Count - 1;
            RestoreHeader(appliedDate,appliedStore);
        }
        var reportStore = Modules.Reports.ReportTaskScope.StoreIndexForReport(task.ReportCode, appliedStore, window.StoreScopes.Stores.Count);
        if (reportStore != appliedStore)
        {
            appliedStore = reportStore;
            RestoreHeader(appliedDate, appliedStore);
        }
        window.reportsWorkspaceView.ApplyScope(window.reportsWorkspaceView.DateFrom,appliedDate,HeaderStore);
        if (task.ReportCode is { } code)
        {
            if (pendingInvestigation is { } reportHit) { pendingInvestigation = null; _ = OpenInvestigationReportAsync(code, reportHit.PrimaryReference); }
            else _ = window.reportsWorkspaceView.RunReportAsync(code);
            return true;
        }



        UserControl view;
        if (task.Id == "profiles") view = new Modules.Settings.ApprovedProfilesView();
        else if (task.Id == "settings") view = new Modules.Settings.GeneralPreferencesView(UiPreferenceStore.Load(), window.SavePreferences, window.CurrentShellAccess);
        else if (task.Destination == "Dashboard") { view = window.dashboardView; window.dashboardView.SelectTask(task.Id); _ = window.RefreshDashboardAsync(); }
        else view = ResolveTaskView(task);
        ApplyHiddenScope(view);
        if (view.Parent is ContentControl host) host.Content = null;

        window.FocusedWorkspaceLayer.Visibility = Visibility.Visible;
        window.FocusedWorkspaceHost.Content = view; window.focusedWorkspaceKind = "task";
        if (pendingInvestigation is { } hit) { pendingInvestigation = null; _ = SelectInvestigationAsync(hit); }

        return true;
    }

    private UserControl ResolveTaskView(TaskDestination task)
    {
        // Each layout selects the existing module's controls; no business operation is invoked here.
        UserControl view;
        int[] body; int[] actions;
        var id = task.Id;
        if (id == "import-history")
        {
            var history = window.importHistoryView ?? throw new InvalidOperationException("Import history is not configured.");
            _ = history.ActivateAsync(new(DateOnly.FromDateTime(appliedDate), DateOnly.FromDateTime(appliedDate), string.IsNullOrEmpty(HeaderStore) ? null : HeaderStore));
            return history;
        }
        if (task.Id == "conflicts") return new Modules.Imports.ImportProblemsView(window.importWorkspaceView, async () =>
        {
            // Persisted outcomes for the header scope, never the session's last import:
            // the problems list has to still be there after the application is closed
            // and reopened. appliedDate and HeaderStore are read on every load.
            var persisted = window.importHistoryView is { } history
                ? Modules.Imports.ImportProblems.FromHistory(
                    (await history.FetchAsync(new(DateOnly.FromDateTime(appliedDate), DateOnly.FromDateTime(appliedDate),
                        string.IsNullOrEmpty(HeaderStore) ? null : HeaderStore)))
                    .Select(entry => (entry.RecordedUtc, entry.Result)))
                : [];
            return persisted.Concat(await window.sourceInboxWorkspaceView.LoadProblemsAsync()).Distinct().ToArray();
        });
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
        if (id == "masters") return new UserControl { Content = new ScrollViewer { Content = window.settingsWorkspace.CreateEveningMastersView(() => window.CurrentShellAccess.CanImport), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        if (id == "tender-rules") return new UserControl { Content = new ScrollViewer { Content = window.settingsWorkspace.CreateDataTruthMastersView(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        if (task.Destination is "Daily Workflow" or "Manual Entry")
        {
            view = window.dailyWorkflowWorkspace;
            window.dailyWorkflowWorkspace.SelectPackTask(id);
            window.dailyWorkflowWorkspace.StoreCode = HeaderStore;
            (body, actions) = task.Section switch
            {
                // Indices are positions in DailyWorkflowWorkspaceView's root StackPanel.
                // CashQuickFields was inserted at 7, so every child from 7 onward moved
                // down by one and each entry below was corrected to match.
                "manual" => (new int[] {8,3,6}, new int[] {8}), "stock-count" => (new int[] {2,12,3,11}, new int[] {12}),
                "staff-target" => (new int[] {2,14,3}, new int[] {14}), "finalisation" => (new int[] {2,15,3}, new int[] {15}),
                "readiness" => (new int[] {3,15,17,18}, new int[] {15,16}), _ => (new int[] {2,3,17}, new int[] {16})
            };
        }
        else if (task.Destination == "Registers") { view = window.registersWorkspaceView; body = new int[] {4,5,6,1,2}; actions = new int[] {0,1,5}; window.registersWorkspaceView.SelectTask(id); }
        else if (task.Destination == "Accounting")
        {
            ApplyHiddenScope(window.accountingWorkspaceView);
            window.accountingWorkspaceView.SelectTask("prepare-batch");
            visited.Add(window.accountingWorkspaceView);
            _ = window.accountingWorkspaceView.RefreshAsync();
            return window.accountingWorkspaceView;
        }
        else if (task.Destination == "Report Archive")
        {
            ApplyHiddenScope(window.archiveWorkspaceView);
            window.archiveWorkspaceView.SelectTask(id);
            view = window.archiveWorkspaceView;
            body = id == "sharing-contacts" ? new int[] {6,7,8,9} : new int[] {0,1,2,3,5,9,10,11,12,13};
            actions = id == "sharing-contacts" ? [] : [4];
        }
        else if (id is "connection" or "settings" or "sharing")
        { view = window.settingsWorkspace; if (id == "sharing") window.settingsWorkspace.SelectIntegrationTask(id); body = id is "connection" or "settings" ? new int[] {0,1,3,7} : new int[] {3,5}; actions = id is "connection" or "settings" ? new int[] {2} : new int[] {}; }
        else if (task.Destination == "Admin / Settings")
        {
            view = window.administrationWorkspaceView; window.administrationWorkspaceView.SelectTask(id);
            (body, actions) = AdministrationTaskLayout(id);
        }
        else if (id is "approval-centre" or "adjustment" or "investigation")
        { view = window.investigationWorkspaceView; (body, actions) = id switch { "approval-centre" => (new int[] {3,7,8}, new int[] {9}), "adjustment" => (new int[] {3,5,6}, new int[] {}), _ => (new int[] {0,1,3,4}, new int[] {2}) }; }
        else if (id is "backups" or "support-package" or "recovery")
        { view = window.operationsWorkspaceView; body = new int[] {29}; actions = new int[] {28}; window.operationsWorkspaceView.SelectMaintenanceTask(id); }
        else if (id == "watch-folder")
        { view = window.operationsWorkspaceView; body = new int[] {2,11,12,13,14,15,16,17,18,19,21,22,24,25}; actions = [20,23]; }
        else { view = window.operationsWorkspaceView; window.operationsWorkspaceView.SelectIssueTask(id); body = id is "trends" ? new int[] {1,2,3,4,5} : new int[] {1,2,8,7}; actions = id is "trends" ? new int[] {0} : new int[] {0,8}; }
        FocusedTaskLayout.Show(view, task.Title, body, actions);
        ApplyHiddenScope(view);
        if (view == window.dailyWorkflowWorkspace) window.dailyWorkflowWorkspace.PrepareTouchTask(id);
        visited.Add(view);
        if (prepared.Add(view) || view == window.dailyWorkflowWorkspace || view == window.archiveWorkspaceView || view == window.investigationWorkspaceView)
        {
            if (view == window.dailyWorkflowWorkspace) { window.dailyWorkflowWorkspace.RefreshAccessState(); _ = window.dailyWorkflowWorkspace.RefreshAsync(); }
            if (view == window.accountingWorkspaceView) _ = window.accountingWorkspaceView.RefreshAsync();
            if (view == window.archiveWorkspaceView) _ = window.archiveWorkspaceView.RefreshAsync();
            if (view == window.operationsWorkspaceView) _ = window.operationsWorkspaceView.RefreshAsync();
            if (view == window.investigationWorkspaceView && id == "approval-centre") _ = window.investigationWorkspaceView.RefreshApprovalsAsync();
        }
        return view;
    }
}
