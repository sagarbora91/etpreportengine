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
    internal ShellAccess CurrentShellAccess => new(currentAccess.Role != AccessRole.None,
        currentAccess.CanView, currentAccess.CanImport, currentAccess.CanAdminister);
    private string CurrentModuleId => ShellRouteRegistry.Find(shell.CurrentRoute.Destination)?.ModuleId ?? "home";
    private string databaseHealthLine = "Database health has not been refreshed.";
    private string activeSection = "Today";

    private void InitializeShell()
    {
        uiPreferences = UiPreferenceStore.Load();
        ShellBusinessDateSelector.SelectedDate = TaskNavigator.InitialBusinessDate;
        taskNavigator = new TaskNavigator(this);
        taskNavigator.InitializeTaskNavigation();
        ApplyDensity(uiPreferences.Density, false);
        InitializeFocusedWorkspaces();
        var toastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        toastTimer.Tick += (_,_) => { toastTimer.Stop(); SuccessToast.Visibility = Visibility.Collapsed; };
        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));
        EventHandler changed = (_,_) =>
        {
            var text = ApplicationStatus.Text;
            if (System.Text.RegularExpressions.Regex.IsMatch(text,"saved|completed|succeeded",System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && !System.Text.RegularExpressions.Regex.IsMatch(text,"failed|not saved|cancel|could not",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            { ToastMessage.Text = text; SuccessToast.Visibility = Visibility.Visible; toastTimer.Stop(); toastTimer.Start(); }
        };
        descriptor.AddValueChanged(ApplicationStatus,changed);
        Closed += (_,_) => { toastTimer.Stop(); descriptor.RemoveValueChanged(ApplicationStatus,changed); };
    }

    private void CompleteWelcomeState()
    {
        WelcomeIdentityText.Text = currentAccess.DisplayName;
        WelcomeRoleText.Text = RoleLabel(currentAccess.Role);
        WelcomeProgress.Visibility = Visibility.Collapsed;
        ContinueButton.IsEnabled = true;
        if (currentAccess.Role != AccessRole.None)
        {
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            OpenSection("Today");
        }
        else WelcomeMessage.Text = "Open database setup to configure access.";
    }

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (startupFailed) { await InitializeWorkspaceAsync(); return; }
        WelcomeOverlay.Visibility = Visibility.Collapsed;
        NavigateToDestination("Settings");
    }

    private void Section_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string section }) OpenSection(section);
    }

    internal void OpenSection(string section, string? tab = null)
    {
        var tasks = TaskNavigation.InSection(section, CurrentShellAccess);
        var target = tab is null ? tasks.FirstOrDefault() : tasks.FirstOrDefault(t => t.Tab == tab);
        if (target is null) { ApplicationStatus.Text = "This section requires a different role."; return; }
        taskNavigator!.NavigateTask(target);
    }

    internal void UpdateSection(TaskDestination task)
    {
        activeSection = task.Rail;
        PageTitle.Text = task.Rail;
        SectionTabs.Children.Clear();
        foreach (var group in TaskNavigation.InSection(task.Rail, CurrentShellAccess).GroupBy(t => t.Tab))
        {
            var button = new Button { Content = group.Key, Tag = group.Key, Margin = new Thickness(0,0,4,0), Padding = new Thickness(8,4,8,4), MinWidth = 44 };
            if (group.Key == task.Tab) button.SetResourceReference(StyleProperty, "PrimaryButton");
            AutomationProperties.SetName(button, group.Key + " tab");
            button.Click += (_,_) => OpenSection(task.Rail, group.Key);
            SectionTabs.Children.Add(button);
        }
        if (task.Rail == "Settings" && task.Tab == "Database")
            SectionTabs.Children.Add(new TextBlock { Text = databaseHealthLine, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,0,0), FontSize = 12 });
        var choices = TaskNavigation.InSection(task.Rail, CurrentShellAccess).Where(t => t.Tab == task.Tab).ToArray();
        if (choices.Length > 1)
        {
            var selector = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Title", SelectedItem = task, MinWidth = 180, MaxWidth = 290, Margin = new Thickness(4,0,0,0) };
            AutomationProperties.SetName(selector, "Choose " + task.Tab.ToLowerInvariant() + " task");
            selector.SelectionChanged += (_,_) => { if (selector.SelectedItem is TaskDestination next) taskNavigator!.NavigateTask(next); };
            SectionTabs.Children.Add(selector);
        }
        foreach (var button in RailPanel.Children.OfType<Button>().Where(b => b.Tag is string))
        {
            button.SetResourceReference(StyleProperty, (string)button.Tag == activeSection ? "PrimaryButton" : typeof(Button));
            AutomationProperties.SetItemStatus(button, (string)button.Tag == activeSection ? "Selected" : "");
        }
    }

    internal void SavePreferences(UiPreferences preferences)
    {
        uiPreferences = preferences; UiPreferenceStore.Save(preferences); ApplyDensity(preferences.Density, false);
    }

    private bool NavigateToDestination(string destination) => NavigateToDestinationWithFeature(destination, null);
    private bool NavigateToDestinationWithFeature(string destination, string? featureCode)
    {
        if (destination == "Settings" && !CurrentShellAccess.HasAssignedRole)
        {
            FocusedWorkspaceHost.Content = settingsWorkspace;
            _ = settingsWorkspace.PrepareForDisplayAsync(false);
            PageTitle.Text = "Database setup";
            return true;
        }
        var id = featureCode is not null ? "report-" + featureCode : destination switch
        {
            "Home" or "Dashboard" => "report-dsr", "Manual Entry" => "walk-ins", "Daily Workflow" => "readiness",
            "Import ETP" => "import-files", "Sales Reports" => "report-sales-combined", "Accounting" => "prepare-batch",
            "Report Archive" => "generations", "Operations Center" => "open-items", "Registers" => "register-inward", _ => "settings"
        };
        if (TaskNavigation.Find(id) is not { } task) return false;
        taskNavigator!.NavigateTask(task); return task.IsAllowed(CurrentShellAccess);
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (RailColumn is null) return;
        RailColumn.Width = new GridLength(ActualWidth < 1000 ? 100 : 112);
    }

    private void StatusDetails_Click(object sender, RoutedEventArgs e) => new StatusDetailsDialog(this, taskNavigator?.CurrentStatusDetails() ?? ApplicationStatus.Text).ShowDialog();
    private void Status_Click(object sender, MouseButtonEventArgs e) => new StatusDetailsDialog(this, taskNavigator?.CurrentStatusDetails() ?? ApplicationStatus.Text).ShowDialog();

    internal void ApplyDensity(UiDensity density, bool persist)
    {
        Resources["ActiveTargetHeight"] = density == UiDensity.Comfortable ? 44d : 36d;
        Resources["ActiveGridRowHeight"] = density == UiDensity.Comfortable ? 44d : 36d;

        uiPreferences = uiPreferences with { Density = density };
        if (persist) UiPreferenceStore.Save(uiPreferences);
    }

    private void ShellBusinessDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ShellBusinessDateSelector.SelectedDate is not { } selected) return;
        taskNavigator?.RequestScopeChange();
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
        KeyboardRegionNavigation.MoveNext(RailPanel, ShellStoreSelector, FocusedWorkspaceHost, StatusFooter);
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

}
