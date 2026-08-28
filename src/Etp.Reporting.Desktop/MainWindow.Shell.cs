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
    private UiPreferences uiPreferences = UiPreferences.Default;
    private Border? moduleHomePanel;
    private bool sidebarOverlay;
    private bool sidebarExplicitlyCollapsed;
    private DensitySelector? sidebarDensitySelector;

    private string CurrentModuleId => shell.CurrentRoute == WorkspaceRoute.Home
        ? "home"
        : ShellRouteRegistry.Find(shell.CurrentRoute.Destination)?.ModuleId ?? "home";

    private ShellAccess CurrentShellAccess => new(
        currentAccess.Role != AccessRole.None,
        currentAccess.CanView,
        currentAccess.CanImport,
        currentAccess.CanAdminister);

    private void InitializeShell()
    {
        uiPreferences = UiPreferenceStore.Load();
        ShellBusinessDateSelector.SelectedDate = DateTime.Today.AddDays(-1);
        InstallSidebarDensitySelector();
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
        WelcomeMessage.Text = currentAccess.Role == AccessRole.None
            ? "Continue to database setup. Existing security rules remain authoritative."
            : "Your Windows identity and application role have been verified.";
        BuildModuleHome();
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        WelcomeOverlay.Visibility = Visibility.Collapsed;
        ShowModuleHome();
        ApplicationStatus.Text = currentAccess.Role == AccessRole.None
            ? "Database setup is required before operational modules can be used."
            : $"Signed in as {currentAccess.DisplayName} — {RoleLabel(currentAccess.Role)}.";
    }

    private void ShowModuleHome_Click(object sender, RoutedEventArgs e) => ShowModuleHome();

    private void ShowModuleHome()
    {
        ApplyNavigationDecision(shell.Navigate(WorkspaceRoute.Home, CurrentShellAccess));
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
        BreadcrumbText.Text = "Modules";
        PageTitle.Text = currentAccess.Role == AccessRole.Owner ? "Owner Workspace" : "Home";
        PageDescription.Text = "Choose a module. Daily work stays on the surface while governed controls remain underneath.";
        ReadinessSummaryPanel.Visibility = Visibility.Collapsed;
        GettingStartedPanel.Visibility = currentAccess.Role == AccessRole.None ? Visibility.Visible : Visibility.Collapsed;
        LegacyWorkspaceScroll.ScrollToTop();
    }

    private void EnsureModuleHome()
    {
        if (moduleHomePanel is not null) { BuildModuleHome(); return; }
        moduleHomePanel = new Border { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 16) };
        WorkspaceStack.Children.Insert(0, moduleHomePanel);
        BuildModuleHome();
    }

    private void BuildModuleHome()
    {
        if (moduleHomePanel is null) return;
        var root = new StackPanel();
        var heading = new DockPanel { Margin = new Thickness(4, 0, 4, 16) };
        if (currentAccess.Role == AccessRole.Owner)
        {
            var ownerBadge = new StatusBadge("Owner workspace", "Information"); DockPanel.SetDock(ownerBadge, Dock.Right); heading.Children.Add(ownerBadge);
        }
        var titleBlock = new StackPanel();
        titleBlock.Children.Add(new TextBlock { Text = currentAccess.Role == AccessRole.Owner ? "Your operational workspace" : "Good morning", FontSize = 27, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("PrimaryText") });
        titleBlock.Children.Add(new TextBlock { Text = "Open a module to work with live application data.", Foreground = (Brush)FindResource("SecondaryText"), Margin = new Thickness(0, 4, 0, 0) });
        heading.Children.Add(titleBlock); root.Children.Add(heading);
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var role = currentAccess.Role;
        var selected = UiNavigationRegistry.Modules.Where(x => x.DefaultVisibility && x.IsVisibleTo(role)).ToList();
        if (currentAccess.Role == AccessRole.Owner)
        {
            var pins = uiPreferences.PinnedModuleIds.Count > 0 ? uiPreferences.PinnedModuleIds : ["registers", "approvals", "health"];
            selected.AddRange(UiNavigationRegistry.Modules.Where(x => x.PinAllowed && pins.Contains(x.Id, StringComparer.OrdinalIgnoreCase) && x.IsVisibleTo(role)));
        }
        var availableWidth = Math.Max(720, ActualWidth - (ContextSidebar.Visibility == Visibility.Visible ? 390 : 100) - 60);
        var columns = availableWidth >= 1100 ? 3 : availableWidth >= 700 ? 2 : 1;
        var tileWidth = Math.Max(280, Math.Min(560, availableWidth / columns - 18));
        foreach (var module in selected.DistinctBy(x => x.Id).OrderBy(x => x.Order))
        {
            var tile = new ModuleTile(module) { Width = tileWidth };
            tile.Click += ModuleTile_Click; wrap.Children.Add(tile);
        }
        root.Children.Add(wrap);
        if (currentAccess.Role == AccessRole.None)
        {
            var notice = new EmptyState("Database setup required", "Operational module access is granted through the existing Windows-integrated role model.", "Open Settings to configure or initialise SQL Server.") { Margin = new Thickness(7, 16, 7, 0) };
            root.Children.Add(notice);
        }
        moduleHomePanel.Child = root;
        AutomationProperties.SetName(moduleHomePanel, $"Module home with {selected.Count:N0} available modules");
    }

    private void ModuleTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ModuleTile tile) return;
        NavigateToDestination(tile.Definition.Destination);
    }

    private bool NavigateToDestination(string destination) => NavigateToDestinationWithFeature(destination, null);

    private bool NavigateToDestinationWithFeature(string destination, string? featureCode)
    {
        var decision = shell.Navigate(new WorkspaceRoute(destination, featureCode), CurrentShellAccess);
        ApplyNavigationDecision(decision);
        return decision.IsAllowed;
    }

    private void UpdateShellForDestination(ShellRouteDescriptor route)
    {
        var destination = route.Destination;
        if (moduleHomePanel is not null) moduleHomePanel.Visibility = Visibility.Collapsed;
        ReadinessSummaryPanel.Visibility = destination == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        GettingStartedPanel.Visibility = Visibility.Collapsed;
        ConfigureSidebar(route.ModuleId);
        BreadcrumbText.Text = route.ModuleId == "settings" ? "Administration" : $"Modules / {SidebarModuleTitle.Text}";
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
        ShowSidebar();
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
        var navigated = NavigateToDestinationWithFeature(item.Destination, item.FeatureCode);
        if (navigated && !string.IsNullOrWhiteSpace(item.FeatureCode))
            _ = reportsWorkspaceView.RunReportAsync(item.FeatureCode);
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
        if (ActualWidth < 1100)
        {
            sidebarOverlay = true; SidebarColumn.Width = new GridLength(0); Grid.SetColumn(ContextSidebar, 2); ContextSidebar.Width = 300; ContextSidebar.HorizontalAlignment = HorizontalAlignment.Left; SidebarToggleButton.Visibility = Visibility.Visible;
        }
        else
        {
            sidebarOverlay = false; Grid.SetColumn(ContextSidebar, 1); ContextSidebar.Width = double.NaN; ContextSidebar.HorizontalAlignment = HorizontalAlignment.Stretch; SidebarColumn.Width = new GridLength(300); SidebarToggleButton.Visibility = Visibility.Collapsed;
        }
    }

    private void HideSidebar()
    {
        ContextSidebar.Visibility = Visibility.Collapsed; SidebarColumn.Width = new GridLength(0); SidebarToggleButton.Visibility = Visibility.Visible; sidebarExplicitlyCollapsed = true;
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (ContextSidebar.Visibility == Visibility.Visible) HideSidebar(); else ShowSidebar();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (CurrentModuleId != "home" && !sidebarExplicitlyCollapsed) ShowSidebar();
        BuildModuleHome();
    }

    private void ToggleDensity_Click(object sender, RoutedEventArgs e) => ApplyDensity(uiPreferences.Density == UiDensity.Comfortable ? UiDensity.Compact : UiDensity.Comfortable, persist: true);

    private void InstallSidebarDensitySelector()
    {
        DensityToggleButton.Visibility = Visibility.Collapsed;
        if (ContextSidebar.Child is not Grid sidebarGrid || sidebarDensitySelector is not null) return;
        var existingFooter = sidebarGrid.Children.Cast<UIElement>().FirstOrDefault(x => Grid.GetRow(x) == 2);
        if (existingFooter is not null) sidebarGrid.Children.Remove(existingFooter);
        var footer = new StackPanel();
        var help = new Button { Content = "Help Centre", Style = (Style)FindResource("SidebarItemButton"), Margin = new Thickness(4, 2, 4, 0) };
        help.Click += OpenHelp_Click;
        AutomationProperties.SetName(help, "Open Help Centre");
        footer.Children.Add(help);
        sidebarDensitySelector = new DensitySelector();
        sidebarDensitySelector.DensityChanged += (_, density) => ApplyDensity(density, persist: true);
        footer.Children.Add(sidebarDensitySelector);
        if (existingFooter is not null) footer.Children.Add(existingFooter);
        Grid.SetRow(footer, 2);
        sidebarGrid.Children.Add(footer);
    }

    private void ApplyDensity(UiDensity density, bool persist)
    {
        Resources["ActiveTargetHeight"] = density == UiDensity.Comfortable ? 48d : 34d;
        Resources["ActiveGridRowHeight"] = density == UiDensity.Comfortable ? 46d : 30d;
        sidebarDensitySelector?.SetDensity(density);
        uiPreferences = uiPreferences with { Density = density };
        if (persist) UiPreferenceStore.Save(uiPreferences);
    }

    private void ShellBusinessDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ShellBusinessDateSelector.SelectedDate is not { } selected) return;
        reportsWorkspaceView.SetBusinessDate(selected); dailyWorkflowWorkspace.BusinessDate = selected; importWorkspaceView.BusinessDate = selected; sourceInboxWorkspaceView.BusinessDate = selected; archiveWorkspaceView.BusinessDate = selected; registersWorkspaceView.BusinessDate = selected; accountingWorkspaceView.BusinessDate = selected;
    }

    private void OpenGlobalSearch_Click(object sender, RoutedEventArgs e) { NavigateToDestination("Operations Center"); investigationWorkspaceView.FocusSearch(); }
    private void OpenHelp_Click(object sender, RoutedEventArgs e) => ShowHelpWorkspace(HelpCentreRegistry.HomeTopicId);
    private void OpenProfile_Click(object sender, RoutedEventArgs e) => OpenDrawer("Current profile", $"Windows identity: {currentAccess.WindowsIdentity}\nUser: {currentAccess.DisplayName}\nRole: {RoleLabel(currentAccess.Role)}\nPermissions continue to be enforced by the existing application services.");

    private void OpenDrawer(string title, string message, object? detail = null)
    {
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
                panel.Children.Add(new TextBlock { Text = property.DisplayName, FontSize = 10, Foreground = (Brush)FindResource("SecondaryText"), Margin = new Thickness(0, 8, 0, 2) });
                panel.Children.Add(new TextBlock { Text = property.GetValue(detail)?.ToString() ?? "—", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("PrimaryText") });
            }
        }
        DetailDrawerHost.Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DrawerOverlay.Visibility = Visibility.Visible; close.Focus();
    }

    private void CloseDrawer() { DrawerOverlay.Visibility = Visibility.Collapsed; DetailDrawerHost.Child = null; }
    private void DrawerOverlay_MouseDown(object sender, MouseButtonEventArgs e) => CloseDrawer();
    private void DetailDrawerHost_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
                if (CurrentModuleId is "reports" or "imports") { SidebarSearchInput.Focus(); SidebarSearchInput.SelectAll(); }
                else OpenGlobalSearch_Click(this, new RoutedEventArgs());
                return true;
            case ShellCommand.Refresh: RefreshCurrentWorkspace(); return true;
            case ShellCommand.Run when shell.CurrentRoute.FeatureCode is { } feature:
                _ = reportsWorkspaceView.RunReportAsync(feature); return true;
            case ShellCommand.ExportPdf when CurrentModuleId == "reports": reportsWorkspaceView.ExportPdf(); return true;
            case ShellCommand.ExportExcel when CurrentModuleId == "reports": reportsWorkspaceView.ExportExcel(); return true;
            case ShellCommand.GenerateReportPack when CurrentModuleId is "dashboard" or "reports": _ = dailyWorkflowWorkspace.GenerateDailyPackAsync(); return true;
            case ShellCommand.OpenExportFolder when CurrentModuleId == "reports": OpenExportFolder(); return true;
            case ShellCommand.FocusPeriod: FocusPrimaryPeriod(); return true;
            case ShellCommand.GoToReport:
                NavigateToDestination("Sales Reports");
                SidebarSearchInput.Focus();
                SidebarSearchInput.SelectAll();
                return true;
            case ShellCommand.Save when shell.CurrentRoute.Destination == "Manual Entry": _ = dailyWorkflowWorkspace.SaveManualInputAsync(); return true;
            case ShellCommand.ImportFiles when CurrentModuleId == "imports": importWorkspaceView.BrowseWorkbook(); return true;
            case ShellCommand.ImportFolder when CurrentModuleId == "imports": importWorkspaceView.BrowseImportFolder(); return true;
            case ShellCommand.RetryImport when CurrentModuleId == "imports" && focusedWorkspaceKind != "help" && importWorkspaceView.CanRetry:
                _ = importWorkspaceView.RetryFailedBatchAsync(); return true;
            case ShellCommand.CycleRegion: CycleShellRegion(); return true;
            default: return false;
        }
    }

    private bool NavigateHistory(bool back)
    {
        var decision = back ? shell.GoBack(CurrentShellAccess) : shell.GoForward(CurrentShellAccess);
        ApplyNavigationDecision(decision);
        if (!decision.IsAllowed) return false;
        if (decision.RequestedRoute.FeatureCode is { } feature) _ = reportsWorkspaceView.RunReportAsync(feature);
        return true;
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
            case "reports" when shell.CurrentRoute.FeatureCode is { } feature: _ = reportsWorkspaceView.RunReportAsync(feature); break;
        }
    }

    private void CycleShellRegion()
    {
        var regions = new List<IInputElement>();
        if (ContextSidebar.Visibility == Visibility.Visible) regions.Add(SidebarSearchInput);
        regions.Add(ShellStoreSelector);
        regions.Add(LegacyWorkspaceScroll);
        var focused = Keyboard.FocusedElement;
        var current = regions.FindIndex(x => ReferenceEquals(x, focused) || x is DependencyObject parent && focused is DependencyObject child && IsDescendant(parent, child));
        Keyboard.Focus(regions[(current + 1) % regions.Count]);
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

    private void HideAllFeaturePanels()
    {
        foreach (var panel in new FrameworkElement[] { DashboardPanel, SettingsPanel, DailyWorkflowPanel, ImportPanel, SourceInboxPanel, ReportsPanel, OperationsPanel, InvestigationPanel, ReportArchivePanel, RegistersPanel, AccountingPanel, MastersPanel }) panel.Visibility = Visibility.Collapsed;
    }
}
