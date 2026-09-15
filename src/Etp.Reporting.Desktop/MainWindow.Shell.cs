extern alias EtpApplication;

using System.ComponentModel;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Etp.Reporting.Reporting;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;

namespace Etp.Reporting.Desktop;

public partial class MainWindow
{
    private IInputElement? drawerReturnFocus;
    private TaskNavigator? taskNavigator;
    private UiPreferences uiPreferences = UiPreferences.Default;
    private Border? moduleHomePanel;
    private bool sidebarOverlay;
    private bool sidebarExplicitlyCollapsed;


    private string CurrentModuleId => shell.CurrentRoute == WorkspaceRoute.Home
        ? "home"
        : ShellRouteRegistry.Find(shell.CurrentRoute.Destination)?.ModuleId ?? "home";

    internal ShellAccess CurrentShellAccess => new(
        currentAccess.Role != AccessRole.None,
        currentAccess.CanView,
        currentAccess.CanImport,
        currentAccess.CanAdminister);

    private void InitializeShell()
    {
        uiPreferences = UiPreferenceStore.Load();
        ShellBusinessDateSelector.SelectedDate = TaskNavigator.InitialBusinessDate;
        taskNavigator = new TaskNavigator(this);
        taskNavigator.InitializeTaskNavigation();
        sidebarExplicitlyCollapsed = true;
        ApplyDensity(uiPreferences.Density, persist: false);
        InitializeFocusedWorkspaces();
        ShowModuleHome();
    }

    private void CompleteWelcomeState()
    {
        WelcomeIdentityText.Text = currentAccess.DisplayName == "Access not initialized"
            ? WindowsIdentity.GetCurrent().Name
            : currentAccess.DisplayName;
        WelcomeRoleText.Text = currentAccess.Role == AccessRole.None ? "Database setup required" : RoleLabel(currentAccess.Role);
        WelcomeProgress.Visibility = Visibility.Collapsed;
        ContinueButton.IsEnabled = true;
        ContinueButton.Focus();
        WelcomeMessage.Text = currentAccess.Role == AccessRole.None
            ? "Continue to database setup. Existing security rules remain authoritative."
            : "Your Windows identity and application role have been verified.";
        BuildModuleHome();
    }

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (startupFailed) { await InitializeWorkspaceAsync(); return; }
        WelcomeOverlay.Visibility = Visibility.Collapsed;
        if (currentAccess.Role == AccessRole.None) ShowModuleHome(); else NavigateToDestination("Dashboard");
        ApplicationStatus.Text = currentAccess.Role == AccessRole.None
            ? "Database setup is required before operational modules can be used."
            : $"Signed in as {currentAccess.DisplayName} — {RoleLabel(currentAccess.Role)}.";
    }

    private void ShowModuleHome_Click(object sender, RoutedEventArgs e) => ShowModuleHome();

    private void ShowModuleHome()
    {
        taskNavigator!.NavigateSafely(() => shell.Navigate(WorkspaceRoute.Home, CurrentShellAccess));
    }

    private void DisplayModuleHome()
    {
        HideFocusedWorkspace();
        HideAllFeaturePanels();
        EnsureModuleHome();
        if (moduleHomePanel is not null) moduleHomePanel.Visibility = Visibility.Visible;
        ContextSidebar.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
        SidebarToggleButton.Visibility = Visibility.Collapsed;
        BreadcrumbLinks.Children.Clear();
        BreadcrumbText.Text = "Modules";
        PageTitle.Text = currentAccess.Role == AccessRole.Owner ? "Owner Workspace" : "Home";
        PageDescription.Text = "Choose a module. Daily work stays on the surface while governed controls remain underneath.";
        ReadinessSummaryPanel.Visibility = Visibility.Collapsed;
        GettingStartedPanel.Visibility = currentAccess.Role == AccessRole.None ? Visibility.Visible : Visibility.Collapsed;
        LegacyWorkspaceScroll.Visibility = Visibility.Collapsed;
        FocusedWorkspaceLayer.Visibility = Visibility.Visible;
        FocusedWorkspaceHost.Content = moduleHomePanel;
    }

    private void EnsureModuleHome()
    {
        if (moduleHomePanel is not null) { BuildModuleHome(); return; }
        moduleHomePanel = new Border { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 16) };

        BuildModuleHome();
    }

    private void BuildModuleHome()
    {
        if (moduleHomePanel is null) return;
        var root = new System.Windows.Controls.Primitives.UniformGrid { Columns = ActualWidth >= 1000 || ActualHeight < 600 ? 3 : 2, Margin = new Thickness(12) };
        foreach (var module in UiNavigationRegistry.Modules.Where(x => x.IsVisibleTo(currentAccess.Role)).OrderBy(x => uiPreferences.PinnedModuleIds.Contains(x.Id) ? 0 : 1).ThenBy(x => x.Order))
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = module.DisplayName, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (ActualHeight >= 600) content.Children.Add(new TextBlock { Text = module.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,0) });
            var button = new Button { Content = content, Margin = new Thickness(ActualHeight < 600 ? 2 : 6), Padding = new Thickness(ActualHeight < 600 ? 8 : 14), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, module.DisplayName);
            button.Click += (_, _) => taskNavigator!.NavigateOverview(module.DisplayName, module.Destination);
            root.Children.Add(button);
        }
        if (root.Children.Count == 0)
        {
            var setup = new Button { Content = "Configure database connection", Margin = new Thickness(12) };
            setup.Click += (_, _) => NavigateToDestination("Settings"); root.Children.Add(setup);
        }
        moduleHomePanel.Child = root;
    }

    internal void SavePreferences(UiPreferences preferences)
    {
        uiPreferences = preferences; UiPreferenceStore.Save(preferences);
        ApplyDensity(preferences.Density, false); BuildModuleHome();
    }

    private bool NavigateToDestination(string destination) => NavigateToDestinationWithFeature(destination, null);

    private bool NavigateToDestinationWithFeature(string destination, string? featureCode)
    {
        if (destination == "Home") return taskNavigator!.NavigateSafely(() => shell.Navigate(WorkspaceRoute.Home, CurrentShellAccess));
        if (featureCode is not null && TaskNavigation.Find("report-" + featureCode) is { } reportTask) { taskNavigator!.NavigateTask(reportTask); return true; }
        if (destination == "Daily Workflow" || destination == "Manual Entry" || destination == "Dashboard")
        {
            var id = destination == "Manual Entry" ? "walk-ins" : destination == "Daily Workflow" ? "readiness" : "dashboard";
            var task = TaskNavigation.Find(id)!;
            var allowed = task.IsAllowed(CurrentShellAccess);
            taskNavigator!.NavigateTask(task); return allowed;
        }
        if (destination != "Settings" || CurrentShellAccess.HasAssignedRole)
        {
            var module = destination switch { "Sales Reports" or "Stock Reports" => "Reports", "Admin / Settings" or "Masters" or "Settings" => "Settings", "Import ETP" => "Imports", "Report Archive" => "Archive", "Operations Center" => "Exceptions", _ => destination };
            if (module == "Settings" && CurrentShellAccess.CanView && !CurrentShellAccess.CanAdminister) destination = "Home";
            taskNavigator!.NavigateOverview(module, destination); return shell.CurrentRoute.Destination == destination;
        }
        var decision = shell.Navigate(new WorkspaceRoute(destination, featureCode), CurrentShellAccess);
        ApplyNavigationDecision(decision);
        return decision.IsAllowed;
    }

    private void UpdateShellForDestination(ShellRouteDescriptor route)
    {
        var destination = route.Destination;
        if (moduleHomePanel is not null) moduleHomePanel.Visibility = Visibility.Collapsed;
        ReadinessSummaryPanel.Visibility = Visibility.Collapsed;
        GettingStartedPanel.Visibility = Visibility.Collapsed;
        ConfigureSidebar(route.ModuleId);
        BreadcrumbText.Text = destination == "Dashboard" ? "TODAY  /  OVERVIEW" : route.ModuleId == "settings" ? "Administration" : $"Modules / {SidebarModuleTitle.Text}";
        LegacyWorkspaceScroll.ScrollToTop();
    }

    private void ConfigureSidebar(string moduleId)
    {
        if (moduleId == "home") return;
        var module = UiNavigationRegistry.Modules.FirstOrDefault(x => x.Id == moduleId)
            ?? (moduleId == "settings" ? new ModuleDefinition("settings", "Settings", "IconSettings", "Administration", "Admin / Settings", 0, AccessRole.Owner) : null);
        SidebarModuleTitle.Text = module?.DisplayName ?? "Workspace";
        SidebarModuleSubtitle.Text = moduleId switch { "reports" => $"{ProductReportCatalogue.All.Count} live reports", "imports" => "Sources, documents & registers", "settings" => "Administration", _ => "Workspace navigation" };
        SidebarSearchInput.Clear();
        PopulateSidebar(UiNavigationRegistry.ForModule(moduleId), string.Empty);
        HideSidebar();
    }

    private void PopulateSidebar(IEnumerable<NavigationGroupDefinition> groups, string search)
    {
        SidebarItemsPanel.Children.Clear();
        var role = currentAccess.Role;
        foreach (var group in groups.Where(x => x.IsVisibleTo(role)).OrderBy(x => x.Order))
        {
            var items = group.Items.Where(x => x.IsVisibleTo(role) && (string.IsNullOrWhiteSpace(search) || x.Label.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (items.Length == 0) continue;
            var panel = new StackPanel();
            foreach (var item in items)
            {
                var button = new Button { Style = (Style)FindResource("SidebarItemButton"), Tag = item, IsEnabled = item.IsAvailable, ToolTip = item.IsAvailable ? item.Label : item.UnavailableReason };
                var label = new TextBlock { Text = item.IsAvailable ? item.Label : $"{item.Label} — requires source", TextWrapping = TextWrapping.Wrap, Foreground = item.IsAvailable ? (Brush)FindResource("PrimaryText") : (Brush)FindResource("SecondaryText") };
                button.Content = label; button.Click += SidebarItem_Click; AutomationProperties.SetName(button, item.IsAvailable ? item.Label : $"{item.Label}. Unavailable. {item.UnavailableReason}"); panel.Children.Add(button);
            }
            var expander = new Expander { Header = group.Label, Content = panel, IsExpanded = !string.IsNullOrWhiteSpace(search) || group.Order <= 20, Margin = new Thickness(0, 4, 0, 4), Foreground = (Brush)FindResource("SecondaryText"), FontWeight = FontWeights.SemiBold };
            AutomationProperties.SetName(expander, $"{group.Label} navigation group"); SidebarItemsPanel.Children.Add(expander);
        }
        if (SidebarItemsPanel.Children.Count == 0) SidebarItemsPanel.Children.Add(new EmptyState("No navigation matches", "Try another page or report name.") { Margin = new Thickness(6, 10, 6, 0) });
    }

    private void SidebarItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NavigationItemDefinition item }) return;
        if (!item.IsAvailable) { ApplicationStatus.Text = item.UnavailableReason ?? "This capability is not available."; return; }
        if (TaskNavigation.ForItem(item) is { } task) { taskNavigator!.NavigateTask(task); return; }
        if (sidebarOverlay) HideSidebar();
    }

    private void SidebarSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded && SidebarItemsPanel is null) return;
        PopulateSidebar(UiNavigationRegistry.ForModule(CurrentModuleId), SidebarSearchInput.Text.Trim());
    }

    private void ShowSidebar()
    {
        sidebarExplicitlyCollapsed = false;
        ContextSidebar.Visibility = Visibility.Visible;
        if (Math.Max(Width, ActualWidth) < 1100)
        {
            sidebarOverlay = true; SidebarColumn.Width = new GridLength(0); Grid.SetColumn(ContextSidebar, 2); ContextSidebar.Width = 260; ContextSidebar.HorizontalAlignment = HorizontalAlignment.Left; SidebarToggleButton.Visibility = Visibility.Visible;
        }
        else
        {
            sidebarOverlay = false; Grid.SetColumn(ContextSidebar, 1); ContextSidebar.Width = double.NaN; ContextSidebar.HorizontalAlignment = HorizontalAlignment.Stretch; SidebarColumn.Width = new GridLength(260); SidebarToggleButton.Visibility = Visibility.Collapsed;
        }
    }

    internal void HideSidebar()
    {
        ContextSidebar.Visibility = Visibility.Collapsed; SidebarColumn.Width = new GridLength(0); SidebarToggleButton.Visibility = Visibility.Collapsed; sidebarExplicitlyCollapsed = true;
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (ContextSidebar.Visibility == Visibility.Visible) HideSidebar(); else ShowSidebar();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (CurrentModuleId != "home" && !sidebarExplicitlyCollapsed) { if (Math.Max(Width, ActualWidth) < 1100) { ContextSidebar.Visibility = Visibility.Collapsed; SidebarColumn.Width = new GridLength(0); SidebarToggleButton.Visibility = Visibility.Visible; sidebarOverlay = true; } else ShowSidebar(); }
        PageDescription.Visibility = Math.Max(Width, ActualWidth) < 1100 ? Visibility.Collapsed : Visibility.Visible; BuildModuleHome();
    }

    internal void ApplyDensity(UiDensity density, bool persist)
    {
        Resources["ActiveTargetHeight"] = density == UiDensity.Comfortable ? 48d : 34d;
        Resources["ActiveGridRowHeight"] = density == UiDensity.Comfortable ? 46d : 30d;

        uiPreferences = uiPreferences with { Density = density };
        if (persist) UiPreferenceStore.Save(uiPreferences);
    }

    private void ShellBusinessDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ShellBusinessDateSelector.SelectedDate is not { } selected) return;
        taskNavigator?.ApplyBusinessDate(selected);
    }

    private void OpenGlobalSearch_Click(object sender, RoutedEventArgs e) => taskNavigator!.FocusMasterSearch();
    private void OpenHelp_Click(object sender, RoutedEventArgs e) => ShowHelpWorkspace(HelpCentreRegistry.HomeTopicId);
    internal void OpenProfile_Click(object sender, RoutedEventArgs e) => OpenDrawer("Current profile", $"Windows identity: {currentAccess.WindowsIdentity}\nUser: {currentAccess.DisplayName}\nRole: {RoleLabel(currentAccess.Role)}\nPermissions continue to be enforced by the existing application services.");

    private void OpenDrawer(string title, string message, object? detail = null)
    {
        if (DrawerOverlay.Visibility != Visibility.Visible) drawerReturnFocus = Keyboard.FocusedElement;
        var panel = new StackPanel();
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 72 };
        close.Click += (_, _) => CloseDrawer(); panel.Children.Add(close);
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("PrimaryText"), Margin = new Thickness(0, 18, 0, 8) });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("SecondaryText"), Margin = new Thickness(0, 0, 0, 18) });
        if (detail is not null)
        {
            var properties = TypeDescriptor.GetProperties(detail).Cast<PropertyDescriptor>().Where(x => x.IsBrowsable).Take(18);
            foreach (var property in properties)
            {
                panel.Children.Add(new TextBlock { Text = property.DisplayName, FontSize = 12, Foreground = (Brush)FindResource("SecondaryText"), Margin = new Thickness(0, 8, 0, 2) });
                panel.Children.Add(new TextBlock { Text = property.GetValue(detail)?.ToString() ?? "—", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("PrimaryText") });
            }
        }
        DetailDrawerHost.Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DrawerOverlay.Visibility = Visibility.Visible; close.Focus();
    }

    private void CloseDrawer() { DrawerOverlay.Visibility = Visibility.Collapsed; DetailDrawerHost.Child = null; if (drawerReturnFocus is FrameworkElement { IsVisible: true } element) element.Focus(); drawerReturnFocus = null; }
    private void DrawerOverlay_MouseDown(object sender, MouseButtonEventArgs e) => CloseDrawer();
    private void DetailDrawerHost_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (WelcomeOverlay.Visibility == Visibility.Visible && e.Key != Key.System)
        {
            if (ContinueButton.IsEnabled && e.Key == Key.Enter) Continue_Click(ContinueButton, new RoutedEventArgs());
            else if (ContinueButton.IsEnabled) ContinueButton.Focus();
            e.Handled = true; return;
        }
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { taskNavigator!.FocusMasterSearch(); e.Handled = true; return; }
        var command = ShellShortcutRegistry.Resolve(e.Key, e.SystemKey, Keyboard.Modifiers);
        if (command == ShellCommand.None) return;
        if (ExecuteShellCommand(command)) e.Handled = true;
    }

    private bool ExecuteShellCommand(ShellCommand command)
    {
        switch (command)
        {
            case ShellCommand.Back when CloseFocusedHelp(): return true;
            case ShellCommand.Back: return NavigateHistory(back: true);
            case ShellCommand.Forward: return NavigateHistory(back: false);
            case ShellCommand.Home: NavigateToDestination("Dashboard"); return true;
            case ShellCommand.Help: ShowHelpWorkspace(contextual: true); return true;
            case ShellCommand.ShortcutGuide: OpenShortcutGuide(); return true;
            case ShellCommand.CloseOrCancel when DrawerOverlay.Visibility == Visibility.Visible: CloseDrawer(); return true;
            case ShellCommand.CloseOrCancel when CloseFocusedHelp(): return true;
            case ShellCommand.Search:
                taskNavigator!.SearchCurrentPage();
                return true;
            case ShellCommand.Refresh: RefreshCurrentWorkspace(); return true;
            case ShellCommand.Run when shell.CurrentRoute.FeatureCode is not null:
                taskNavigator!.RefreshCurrentReport(); return true;
            case ShellCommand.ExportPdf when CurrentModuleId == "reports": taskNavigator!.ExportCurrentReport(true); return true;
            case ShellCommand.ExportExcel when CurrentModuleId == "reports": taskNavigator!.ExportCurrentReport(false); return true;
            case ShellCommand.GenerateReportPack when CurrentModuleId is "dashboard" or "reports": taskNavigator!.GenerateCurrentPack(); return true;
            case ShellCommand.OpenExportFolder when CurrentModuleId == "reports": OpenExportFolder(); return true;
            case ShellCommand.FocusPeriod: FocusPrimaryPeriod(); return true;
            case ShellCommand.GoToReport:
                NavigateToDestination("Sales Reports");
                taskNavigator!.FocusMasterSearch();

                return true;
            case ShellCommand.Save when shell.CurrentRoute.TaskId == "walk-ins": _ = dailyWorkflowWorkspace.SaveManualInputAsync(); return true;
            case ShellCommand.Save when shell.CurrentRoute.TaskId == "stock-count": _ = dailyWorkflowWorkspace.SaveStockCountAsync(); return true;
            case ShellCommand.Save when shell.CurrentRoute.TaskId == "staff-target": _ = dailyWorkflowWorkspace.SaveStaffTargetAsync(); return true;
            case ShellCommand.ImportFiles when CurrentModuleId == "imports": importWorkspaceView.BrowseWorkbook(); return true;
            case ShellCommand.ImportFolder when CurrentModuleId == "imports": importWorkspaceView.BrowseImportFolder(); return true;
            case ShellCommand.RetryImport when CurrentModuleId == "imports" && focusedWorkspaceKind != "help" && importWorkspaceView.CanRetry:
                _ = importWorkspaceView.RetryFailedBatchAsync(); return true;
            case ShellCommand.CycleRegion: CycleShellRegion(); return true;
            default: return false;
        }
    }

    internal bool NavigateHistory(bool back)
    {
        return taskNavigator!.NavigateSafely(() => back ? shell.GoBack(CurrentShellAccess) : shell.GoForward(CurrentShellAccess));
    }

    private void OpenShortcutGuide()
    {
        ShowHelpWorkspace(HelpCentreRegistry.KeyboardShortcutsTopicId);
    }

    private void RefreshCurrentWorkspace()
    {
        switch (CurrentModuleId)
        {
            case "dashboard" when shell.CurrentRoute.Destination is "Daily Workflow" or "Manual Entry": _ = dailyWorkflowWorkspace.RefreshAsync(); break;
            case "dashboard": _ = RefreshDashboardAsync(); break;
            case "imports": _ = sourceInboxWorkspaceView.RefreshAsync(); break;
            case "registers": _ = registersWorkspaceView.RefreshAsync(); break;
            case "accounting": _ = accountingWorkspaceView.RefreshAsync(); break;
            case "archive": _ = archiveWorkspaceView.RefreshAsync(); break;
            case "exceptions": _ = operationsWorkspaceView.RefreshAsync(); break;
            case "settings": _ = settingsWorkspace.PrepareForDisplayAsync(shell.CurrentRoute.Destination == "Admin / Settings"); _ = administrationWorkspaceView.RefreshAsync(); break;
            case "reports" when shell.CurrentRoute.FeatureCode is not null: taskNavigator!.RefreshCurrentReport(); break;
        }
    }

    private void CycleShellRegion()
    {
        KeyboardRegionNavigation.MoveNext(HeaderSearchHost, ShellStoreSelector,
            FocusedWorkspaceLayer.IsVisible ? FocusedWorkspaceHost : LegacyWorkspaceScroll);
    }

    private void FocusPrimaryPeriod()
    {
        if (focusedWorkspaceKind == "report" && reportWorkspaceSession.FocusPrimaryPeriod(reportsWorkspaceView.CurrentReportCode)) return;
        ShellBusinessDateSelector.Focus();
    }

    private static bool IsDescendant(DependencyObject parent, DependencyObject child)
    {
        for (DependencyObject? current = child; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, parent)) return true;
        return false;
    }

    internal void HideAllFeaturePanels()
    {
        foreach (var panel in new FrameworkElement[] { DashboardPanel, SettingsPanel, DailyWorkflowPanel, ImportPanel, SourceInboxPanel, ReportsPanel, OperationsPanel, InvestigationPanel, ReportArchivePanel, RegistersPanel, AccountingPanel, MastersPanel }) panel.Visibility = Visibility.Collapsed;
    }
}
