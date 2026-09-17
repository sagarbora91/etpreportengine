using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Application.DatabaseLifecycle;
using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed partial class ExtractedWorkspaceUiSmokeTests
{
    [PhaseFourCaptureFact]
    public void Phase_four_changed_workspaces_render_at_supported_sizes_with_synthetic_data()
    {
        RunSta(() =>
        {
            // Base Application supplies theme lookup only. Never create Desktop.App or run its startup.
            var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var name in new[] { "Colors", "Spacing", "Typography", "Icons", "Controls" })
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Etp.Reporting.Desktop;component/Themes/{name}.xaml", UriKind.Absolute) });
            var testRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpPhaseFourUi", Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                foreach (var size in new[] { (Width: 1366, Height: 768), (Width: 816, Height: 480) })
                foreach (var workspace in CreatePhaseFourWorkspaces(testRoot))
                {
                    var host = CreateCaptureHost(workspace.Name, workspace.View, out var scroll);
                    foreach (var expander in Descendants(workspace.View).OfType<Expander>()) expander.IsExpanded = true;
                    LayoutCapture(host, size.Width, size.Height);
                    Assert.Contains(Descendants(workspace.View).OfType<Button>(), button => !string.IsNullOrWhiteSpace(AccessibleName(button)));
                    SaveCapture(host, workspace.Name + "-overview", size.Width, size.Height);

                    if (workspace.Name == "Settings")
                    {
                        var recovery = Descendants(workspace.View).OfType<Expander>().Single();
                        scroll.ScrollToVerticalOffset(recovery.TransformToAncestor(workspace.View).Transform(new Point()).Y);
                        LayoutCapture(host, size.Width, size.Height);
                        SaveCapture(host, "Settings-recovery-keys", size.Width, size.Height);
                        Assert.True(recovery.ActualWidth <= scroll.ViewportWidth, "Recovery controls require horizontal scrolling.");
                    }
                    else if (workspace.Name == "Daily-Workflow")
                    {
                        scroll.ScrollToBottom();
                        LayoutCapture(host, size.Width, size.Height);
                        SaveCapture(host, "Daily-Workflow-owner-reopen", size.Width, size.Height);
                    }
                    else
                    {
                        foreach (var nested in Descendants(workspace.View).OfType<ScrollViewer>()) nested.ScrollToBottom();
                        scroll.ScrollToBottom();
                        LayoutCapture(host, size.Width, size.Height);
                        SaveCapture(host, "Dashboard-recovery-status", size.Width, size.Height);
                    }
                    scroll.Content = null;
                }
            }
            finally { Directory.Delete(testRoot, recursive: true); application.Shutdown(); }
        });
    }

    private static IReadOnlyList<WorkspaceCase> CreatePhaseFourWorkspaces(string root)
    {
        // These factories never create SQL adapters. The synthetic connection is display-only.
        const string connection = @"Server=(localdb)\MSSQLLocalDB;Database=EtpPhaseFourUiEvidence;Integrated Security=True;Encrypt=Optional";
        var settings = new SettingsWorkspaceView(
            new DesktopSettingsPresentationSession(new DesktopSettingsStore(Path.Combine(root, "settings")), new DesktopConnectionState(connection)),
            _ => Proxy<IDatabaseLifecycleService>(), _ => Proxy<IAdministrationService>(), Path.Combine(root, "migrations"));
        settings.Initialize();
        settings.UpdateAccess(new(true, true));
        var daily = new DailyWorkflowWorkspaceView(new DailyWorkflowPresentationSession(), () => connection,
            _ => new CaptureDailyQuery(), _ => Proxy<IDailyWorkflowCommands>(), _ => Proxy<IDailyReportPackGenerator<ReportPackDocument>>(),
            () => new(true, true, true), (_, _, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask)
        { BusinessDate = new DateTime(2026, 9, 14), StoreCode = "WLMHW" };
        daily.RefreshAsync().GetAwaiter().GetResult();
        ((TextBox)daily.FindName("ReopenReasonInput")).Text = "Correct the synthetic daily count";
        var dashboard = new DashboardView();
        dashboard.Show(DashboardViewState.Create(6, 6, 120, new DateTime(2026, 9, 15, 7, 45, 0), [],
            new DashboardHealthSnapshot("Healthy", 128m, new DateTime(2026, 9, 15, 2, 0, 0), 0, 40m, [])
            {
                LastSuccessfulBackupSha256 = string.Concat(Enumerable.Repeat("0123456789ABCDEF", 4)),
                LastSuccessfulRecoveryDrillUtc = new DateTime(2026, 9, 1, 3, 0, 0),
                LastSuccessfulRecoveryDrillSha256 = string.Concat(Enumerable.Repeat("FEDCBA9876543210", 4))
            }, []));
        return [new("Settings", settings), new("Daily-Workflow", daily), new("Dashboard", dashboard)];
    }

    private static Grid CreateCaptureHost(string title, FrameworkElement workspace, out ScrollViewer scroll)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(244, 247, 247)) };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition());
        host.Children.Add(new TextBlock
        {
            Text = title.Replace('-', ' ') + "  |  Synthetic data  |  WPF workspace rendering",
            Margin = new Thickness(16, 10, 16, 10), FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DarkSlateGray
        });
        scroll = new ScrollViewer
        {
            Content = workspace, Margin = new Thickness(16, 0, 16, 12),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        host.Children.Add(scroll);
        return host;
    }

    private static void LayoutCapture(FrameworkElement host, int width, int height)
    {
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        Assert.Equal(width, host.ActualWidth);
        Assert.Equal(height, host.ActualHeight);
    }

    private static void SaveCapture(FrameworkElement host, string name, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        Assert.Equal(width, bitmap.PixelWidth);
        Assert.Equal(height, bitmap.PixelHeight);
        var output = Environment.GetEnvironmentVariable("ETP_PHASE4_UI_EVIDENCE");
        if (string.IsNullOrWhiteSpace(output)) return;
        Assert.True(Path.IsPathFullyQualified(output), "UI evidence output must be an absolute folder.");
        Directory.CreateDirectory(output);
        using var file = File.Create(Path.Combine(output, $"{name}-{width}x{height}-wpf.png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(file);
    }

    private sealed class CaptureDailyQuery : IDailyWorkflowQuery
    {
        public Task<DailyWorkflowState> LoadAsync(DailyWorkflowScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DailyWorkflowState(scope.StoreCode, scope.BusinessDate, DailyWorkflowStatus.Locked,
                ["R025", "R022", "R013", "R003", "STOCK_LEDGER", "CLOSING_STOCK"], [],
                [new("WALK_INS", "Walk-ins", "NUMBER", 12m, null, true, new DateTime(2026, 9, 14), "Synthetic owner")], [], false,
                "This synthetic day is finalised. An Owner can reopen it with a recorded reason."));
        public Task<IReadOnlyList<DailyManualStockCount>> LoadStockCountsAsync(DailyWorkflowScope scope, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DailyManualStockCount>>([]);
        public Task<IReadOnlyList<DailyStaffSalesTarget>> LoadStaffTargetsAsync(DailyStaffTargetSearch search, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DailyStaffSalesTarget>>([]);
    }
}

public sealed class PhaseFourCaptureFactAttribute : FactAttribute
{
    public PhaseFourCaptureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ETP_PHASE4_UI_EVIDENCE")))
            Skip = "Opt-in WPF evidence capture: set ETP_PHASE4_UI_EVIDENCE and run this test alone.";
    }
}
