using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;

namespace Etp.Reporting.Desktop;

/// <summary>
/// 1.11.0 self-diagnostics in the shell: the one-line "System check" tile on Today &gt; Sales (every role, decision 33;
/// Store Managers and Viewers see only this) and the one-time error-register ingest after a successful start (design 4.3).
/// </summary>
public partial class MainWindow
{
    /// <summary>Set by the composition root: reads this account's diagnostics log into the error register. Never throws.</summary>
    internal Func<CancellationToken, Task>? startupErrorIngest;

    private HealthSummaryTile? healthSummaryTile;
    private bool startupIngestStarted;

    internal HealthSummaryTile HealthSummaryTile =>
        healthSummaryTile ??= new HealthSummaryTile(() => DiagnosticsScreens.Services.HealthStore(), null, () => CurrentShellAccess.CanAdminister, OpenHealthCheckFromTile) { Margin = new Thickness(0, 8, 0, 0) };

    private void OpenHealthCheckFromTile()
    {
        if (TaskNavigation.Find(DiagnosticsScreens.HealthCheckTask) is { } task && task.IsAllowed(CurrentShellAccess)) taskNavigator!.NavigateTask(task);
    }

    private StackPanel? referenceBar;
    private TextBox? referenceBox;
    private Button? openRegisterButton;

    /// <summary>
    /// 1.11.2 (UX audit top 5, accessibility 2 and 3): the status line is plain text, so a "Ref: ..." in it could not be
    /// selected or copied, and the rail's "Needs attention" could not be focused. This adds, from code, a read-only Ref
    /// box with Copy Ref and (Owner) Open in Error register beside the status line, and makes "Needs attention" a focusable
    /// control, brighter than the old dim grey, that opens the Health check (Owner) or the full status.
    /// </summary>
    private void InitializeDiagnosticsShell()
    {
        if (ApplicationStatus.Parent is DockPanel footer)
        {
            referenceBox = new TextBox { IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, MinHeight = 0, Height = 22,
                Padding = new Thickness(2, 0, 2, 0), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 12, MinWidth = 96 };
            AutomationProperties.SetName(referenceBox, "Reference of the last error");
            var copy = new Button { Content = "Copy Ref", MinHeight = 0, Height = 22, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(4, 0, 0, 0), FontSize = 12 };
            AutomationProperties.SetName(copy, "Copy the reference of the last error");
            copy.Click += (_, _) => CopyReference();
            openRegisterButton = new Button { Content = "Open in Error register", MinHeight = 0, Height = 22, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(4, 0, 0, 0), FontSize = 12 };
            AutomationProperties.SetName(openRegisterButton, "Open the last error in the Error register");
            openRegisterButton.Click += (_, _) => OpenReferenceInRegister();
            referenceBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            referenceBar.Children.Add(referenceBox);
            referenceBar.Children.Add(copy);
            referenceBar.Children.Add(openRegisterButton);
            DockPanel.SetDock(referenceBar, Dock.Right);
            footer.Children.Insert(0, referenceBar);
        }
        var watcher = PropertyChangeWatcher.Watch(ApplicationStatus, TextBlock.TextProperty, UpdateReferenceBar);
        Closed += (_, _) => watcher.Dispose();

        // 1.12 U1 (ST4): the badge is a Critical chip drawn in MainWindow.xaml from the theme tokens; here it only becomes a
        // focusable control that opens the Health check.
        AttentionBadge.Focusable = true;
        AttentionBadge.Cursor = Cursors.Hand;
        AutomationProperties.SetName(AttentionBadge, "Needs attention. Open the health check");
        AttentionBadge.MouseLeftButtonUp += (_, _) => OpenNeedsAttention();
        AttentionBadge.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { OpenNeedsAttention(); e.Handled = true; }
        };
    }

    private void UpdateReferenceBar()
    {
        if (referenceBar is null || referenceBox is null || openRegisterButton is null) return;
        if (DesktopDiagnostics.TryFindReference(ApplicationStatus.Text, out var reference))
        {
            referenceBox.Text = reference;
            openRegisterButton.Visibility = CurrentShellAccess.CanAdminister ? Visibility.Visible : Visibility.Collapsed;
            referenceBar.Visibility = Visibility.Visible;
        }
        else referenceBar.Visibility = Visibility.Collapsed;
    }

    private void CopyReference()
    {
        if (referenceBox is null || referenceBox.Text.Length == 0) return;
        try { Clipboard.SetText(referenceBox.Text); ApplicationStatus.Text = "Copied Ref " + referenceBox.Text + "."; }
        catch (System.Runtime.InteropServices.ExternalException) { ApplicationStatus.Text = "Could not copy: another program is using the clipboard. Select the Ref and press Ctrl+C."; }
    }

    private void OpenReferenceInRegister()
    {
        if (referenceBox is null || referenceBox.Text.Length == 0) return;
        if (TaskNavigation.Find(Modules.OperationsAdministration.DiagnosticsScreens.ErrorRegisterTask) is not { } task || !task.IsAllowed(CurrentShellAccess))
        {
            ApplicationStatus.Text = "The Error register is for the Owner. Give the Ref to the Owner.";
            return;
        }
        var reference = referenceBox.Text;
        taskNavigator!.NavigateTaskWithArgument(task, reference);
    }

    private void OpenNeedsAttention()
    {
        if (TaskNavigation.Find(Modules.OperationsAdministration.DiagnosticsScreens.HealthCheckTask) is { } task && task.IsAllowed(CurrentShellAccess))
            taskNavigator!.NavigateTask(task);
        else StatusDetails_Click(AttentionBadge, new RoutedEventArgs());
    }

    /// <summary>The tile above the "What's missing" panel; both go into the DSR's checklist slot.</summary>
    private UIElement TodayStatusPanel(UIElement checklist)
    {
        if (HealthSummaryTile.Parent is Panel previous) previous.Children.Remove(HealthSummaryTile);
        if (checklist is FrameworkElement { Parent: Panel owner }) owner.Children.Remove(checklist);
        var panel = new StackPanel();
        panel.Children.Add(HealthSummaryTile);
        panel.Children.Add(checklist);
        _ = HealthSummaryTile.RefreshAsync();
        return panel;
    }

    /// <summary>Once per process, after the start-up checks passed: ingest in the background, never blocking or failing the start.</summary>
    private void StartErrorRegisterIngestOnce()
    {
        if (startupIngestStarted || startupErrorIngest is not { } ingest) return;
        startupIngestStarted = true;
        _ = Task.Run(async () =>
        {
            try { await ingest(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) { DesktopDiagnostics.Record(exception, "Startup.ErrorRegister", "ERROR_REGISTER_STARTUP_INGEST_FAILED", DesktopDiagnosticSeverity.Warning); }
        });
    }
}
