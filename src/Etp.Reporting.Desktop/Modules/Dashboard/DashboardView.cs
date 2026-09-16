extern alias EtpApplication;

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Etp.Reporting.Desktop.Modules.Dashboard;
using Etp.Reporting.Reporting;
using Microsoft.Win32;

namespace Etp.Reporting.Desktop;

using DashboardSnapshot = EtpApplication::Etp.Reporting.Application.Dashboard.DashboardSnapshot;

public delegate Task ExportManagementSummaryPdfAsync(string path, ExcelReportMetadata metadata, ExcelReportData data);

public sealed class DashboardView : UserControl
{
    private static readonly Brush PrimaryText = Brush("Success");
    private static readonly Brush SecondaryText = Brush("SecondaryText");
    private static readonly Brush Accent = Brush("Success");
    private static readonly Brush AccentSoft = Brush("SurfaceSecondary");
    private static readonly Brush DarkSurface = Brush("PrimaryText");
    private static readonly Brush Divider = Brush("SecondaryText");
    private static readonly Brush SurfaceSecondary = Brush("SurfaceSecondary");

    private readonly TextBlock importedFilesMetric = MetricValue(21);
    private readonly TextBlock completedBatchesMetric = MetricValue(21, Brushes.White);
    private readonly TextBlock sourceRowsMetric = MetricValue(26, Brushes.White);
    private readonly TextBlock latestImportMetric = MetricValue(14);
    private readonly DataGrid importHistoryGrid = ReadOnlyGrid(260);
    private readonly StackPanel dashboardChartPanel = new();
    private readonly TextBlock databaseHealthMetric = MetricValue(16);
    private readonly TextBlock databaseHealthDetailMetric = MetricValue();
    private readonly TextBlock databaseSizeMetric = MetricValue();
    private readonly TextBlock backupAgeMetric = MetricValue();
    private readonly TextBlock recoveryDrillMetric = MetricValue();
    private readonly TextBlock backupHashMetric = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock recoveryDrillHashMetric = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock backupSpaceMetric = MetricValue();
    private readonly TextBlock failedImportsMetric = MetricValue(21, Brushes.White);
    private readonly ItemsControl healthWarningsList = new();
    private readonly DataGrid operationalAuditGrid = ReadOnlyGrid(230);
    private readonly DataGrid overviewAuditGrid = ReadOnlyGrid(230);
    private readonly ProgressBar readinessProgress = new() { Minimum = 0, Maximum = 100, Height = 8 };
    private readonly TextBlock readinessPercent = new() { FontWeight = FontWeights.SemiBold, Foreground = Accent };
    private readonly TextBlock dailyCloseMessage = new() { Foreground = SecondaryText, TextWrapping = TextWrapping.Wrap };
    private readonly Border errorBanner = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock errorMessage = new() { Foreground = Brush("Critical"), TextWrapping = TextWrapping.Wrap };
    private readonly DashboardPresentationSession presentation;
    private readonly ExportManagementSummaryPdfAsync? exportManagementSummaryPdfAsync;
    private bool exportInProgress;

    public event EventHandler? RefreshRequested;
    public event EventHandler<string>? NotificationRequested;

    public DashboardViewState? CurrentState { get; private set; }

    public DashboardView(
        DashboardPresentationSession? presentation = null,
        ExportManagementSummaryPdfAsync? exportManagementSummaryPdfAsync = null)
    {
        this.presentation = presentation ?? new DashboardPresentationSession();
        this.exportManagementSummaryPdfAsync = exportManagementSummaryPdfAsync;
        Content = BuildContent();
        AutomationProperties.SetName(this, "Operational dashboard");
        AutomationProperties.SetName(importHistoryGrid, "Recent import history");
        AutomationProperties.SetName(dashboardChartPanel, "Imported rows by report chart");
        AutomationProperties.SetName(healthWarningsList, "Database health warnings");
        AutomationProperties.SetName(operationalAuditGrid, "Recent operational activity");
        AutomationProperties.SetName(overviewAuditGrid, "Dashboard operational activity");
        AutomationProperties.SetName(backupAgeMetric, "Last verified backup UTC");
        AutomationProperties.SetName(recoveryDrillMetric, "Last recovery drill UTC");
        AutomationProperties.SetName(backupHashMetric, "Verified backup fingerprint");
        AutomationProperties.SetName(recoveryDrillHashMetric, "Recovery drill backup fingerprint");
    }

    public Func<DateOnly>? ExportDateFrom { get; set; }
    public Func<DateOnly>? ExportDateTo { get; set; }

    public void Show(DashboardSnapshot snapshot) => Show(presentation.Show(snapshot));

    public void Show(DashboardViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Apply(state, preserveHealthColour: false);
    }

    public void ShowError(string message) => Apply(presentation.ShowError(message), preserveHealthColour: true);

    private void Apply(DashboardViewState state, bool preserveHealthColour)
    {
        CurrentState = state;
        importedFilesMetric.Text = state.ImportedFiles;
        completedBatchesMetric.Text = state.CompletedBatches;
        sourceRowsMetric.Text = state.SourceRows;
        latestImportMetric.Text = state.LatestImport;
        importHistoryGrid.ItemsSource = state.RecentImports;
        databaseHealthMetric.Text = state.DatabaseHealth;
        databaseHealthDetailMetric.Text = state.DatabaseHealth;
        if (!preserveHealthColour)
            databaseHealthMetric.Foreground = state.DatabaseHealthTone switch
            {
                DashboardHealthTone.Healthy => Accent,
                DashboardHealthTone.Warning => Brush("Critical"),
                _ => Brush("Critical")
            };
        databaseSizeMetric.Text = state.DatabaseSize;
        backupAgeMetric.Text = state.LatestBackup;
        recoveryDrillMetric.Text = state.LatestRecoveryDrill;
        backupHashMetric.Text = state.LatestBackupSha256;
        recoveryDrillHashMetric.Text = state.LatestRecoveryDrillSha256;
        backupSpaceMetric.Text = state.BackupFreeSpace;
        failedImportsMetric.Text = state.FailedImports;
        healthWarningsList.ItemsSource = state.HealthWarnings;
        operationalAuditGrid.ItemsSource = state.RecentAuditEvents;
        overviewAuditGrid.ItemsSource = state.RecentAuditEvents;
        errorMessage.Text = state.ErrorMessage ?? string.Empty;
        errorBanner.Visibility = string.IsNullOrWhiteSpace(state.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

        var files = ParseMetric(state.ImportedFiles);
        var completed = ParseMetric(state.CompletedBatches);
        var readiness = files <= 0 ? 0 : Math.Clamp(completed * 100d / files, 0, 100);
        readinessProgress.Value = readiness;
        readinessPercent.Text = state.ErrorMessage is not null ? "Unavailable" : files <= 0 ? "No sources" : $"{readiness:N0}% imported";
        dailyCloseMessage.Text = state.ErrorMessage is not null
            ? "Live readiness could not be refreshed. Existing safe context remains visible."
            : files <= 0
                ? "Import the required ETP source files to begin the daily close."
                : state.DatabaseHealthTone == DashboardHealthTone.Critical
                    ? "Resolve the system control warning before finalising the day."
                    : "Review manual inputs and controls, then generate the reporting pack.";
        RenderChart(state.ImportedRowsByReport);
    }

    private UIElement? overviewContent;
    private UIElement? historyContent;
    private UIElement? auditContent;
    private UIElement? chartContent;

    public void SelectTask(string id)
    {
        Content = id switch { "import-history" => historyContent, "audit" or "recent-activity" => auditContent, "trends" => chartContent, _ => overviewContent };
    }

    private UIElement BuildContent()
    {
        historyContent = new ScrollViewer { Content = BuildHistoryContent(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        auditContent = new ScrollViewer { Content = BuildAuditContent(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        chartContent = new ScrollViewer { Content = BuildChartContent(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new DockPanel { Margin = new Thickness(8) };
        var actions = new WrapPanel();
        actions.Children.Add(Button("Refresh", "Refresh dashboard", () => RefreshRequested?.Invoke(this,EventArgs.Empty)));
        actions.Children.Add(Button("Export summary", "Export management summary PDF", ExportPdfAsync));
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);
        errorBanner.Child = errorMessage;
        errorBanner.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(errorBanner, Dock.Top);
        root.Children.Add(errorBanner);
        var content = new StackPanel();
        var status = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        status.Children.Add(MiniMetric("SYSTEM STATUS", databaseHealthDetailMetric));
        status.Children.Add(MiniMetric("LAST VERIFIED BACKUP (UTC)", backupAgeMetric));
        status.Children.Add(MiniMetric("LAST RECOVERY DRILL (UTC)", recoveryDrillMetric));
        content.Children.Add(status);
        var fingerprints = new StackPanel();
        fingerprints.Children.Add(new TextBlock { Text = "Verified backup", Margin = new Thickness(0, 6, 0, 2) });
        fingerprints.Children.Add(backupHashMetric);
        fingerprints.Children.Add(new TextBlock { Text = "Backup used by recovery drill", Margin = new Thickness(0, 6, 0, 2) });
        fingerprints.Children.Add(recoveryDrillHashMetric);
        content.Children.Add(new Expander { Header = "Verification fingerprints", Content = fingerprints, Margin = new Thickness(0, 0, 0, 8) });
        content.Children.Add(Title("Recent privacy-safe activity"));
        overviewAuditGrid.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(overviewAuditGrid);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        overviewContent = root; return root;
    }


    private StackPanel BuildHistoryContent()
    {
        var content = new StackPanel();
        var heading = new DockPanel();
        var refresh = Button("Refresh", "Refresh dashboard", () => RefreshRequested?.Invoke(this, EventArgs.Empty));
        DockPanel.SetDock(refresh, Dock.Right);
        heading.Children.Add(refresh);
        heading.Children.Add(Title("Recent import history"));
        content.Children.Add(heading);
        importHistoryGrid.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(importHistoryGrid);
        return content;
    }

    private StackPanel BuildChartContent()
    {
        var content = new StackPanel();
        var heading = new DockPanel();
        var export = Button("Export PDF…", "Export management summary PDF", ExportPdfAsync);
        DockPanel.SetDock(export, Dock.Right);
        heading.Children.Add(export);
        heading.Children.Add(Title("Rows by report"));
        content.Children.Add(heading);
        dashboardChartPanel.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(dashboardChartPanel);
        return content;
    }

    private StackPanel BuildAuditContent()
    {
        var content = new StackPanel();
        content.Children.Add(Title("Recent privacy-safe activity"));
        operationalAuditGrid.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(operationalAuditGrid);
        return content;
    }

    private async Task ExportPdfAsync()
    {
        if (exportInProgress) return;
        if (!presentation.HasSnapshot)
        {
            NotificationRequested?.Invoke(this, "Refresh the dashboard before exporting a management summary.");
            return;
        }
        if (exportManagementSummaryPdfAsync is null) return;
        var dialog = new SaveFileDialog { Filter = "PDF report (*.pdf)|*.pdf", FileName = $"ETP_Management_Summary_{DateTime.Today:yyyyMMdd}.pdf", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var summary = presentation.BuildManagementSummary(ExportDateFrom?.Invoke() ?? today, ExportDateTo?.Invoke() ?? today, DateTimeOffset.UtcNow);
        exportInProgress = true;
        try
        {
            await exportManagementSummaryPdfAsync(dialog.FileName, summary.Metadata, summary.Data);
            NotificationRequested?.Invoke(this, $"Management summary saved to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            DesktopDiagnostics.Record(ex, "Dashboard.Workspace", "MANAGEMENT_SUMMARY_EXPORT_FAILED");
            NotificationRequested?.Invoke(this, $"Management summary export failed: {DesktopFriendlyError.Describe(ex)}");
        }
        finally { exportInProgress = false; }
    }

    private void RenderChart(IReadOnlyList<DashboardChartItem> items)
    {
        dashboardChartPanel.Children.Clear();
        var maximum = Math.Max(1L, items.Select(item => item.SourceRows).DefaultIfEmpty(1L).Max());
        foreach (var item in items.Take(8))
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(68) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(72) });
            var label = new TextBlock { Text = item.ReportCode, VerticalAlignment = VerticalAlignment.Center, Foreground = SecondaryText };
            var track = new Border { Background = SurfaceSecondary, Height = 11, CornerRadius = new CornerRadius(6), Margin = new Thickness(8, 0, 8, 0) };
            track.Child = new Border { Background = Accent, Height = 11, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left, Width = 210d * item.SourceRows / maximum };
            var value = new TextBlock { Text = item.SourceRows.ToString("N0"), HorizontalAlignment = HorizontalAlignment.Right, FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(label, 0); Grid.SetColumn(track, 1); Grid.SetColumn(value, 2);
            row.Children.Add(label); row.Children.Add(track); row.Children.Add(value);
            dashboardChartPanel.Children.Add(row);
        }
        if (items.Count == 0)
            dashboardChartPanel.Children.Add(new TextBlock { Text = "No imported report activity yet.", Foreground = SecondaryText, Margin = new Thickness(0, 8, 0, 0) });
    }

    private static Border Card(UIElement child) => new()
    {
        Background = Brushes.White,
        BorderBrush = Divider,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(17),
        Child = child
    };



    private static Border MiniMetric(string label, TextBlock value)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = PrimaryText });
        value.Margin = new Thickness(0, 5, 0, 0);
        content.Children.Add(value);
        return new Border { Background = SurfaceSecondary, CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(3), Child = content };
    }





    private static TextBlock Title(string text) => new() { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = PrimaryText };

    private static TextBlock MetricValue(double fontSize = 12, Brush? foreground = null) =>
        new() { Text = "-", FontSize = fontSize, FontWeight = FontWeights.SemiBold, Foreground = foreground ?? PrimaryText };

    private static DataGrid ReadOnlyGrid(double maximumHeight) =>
        new() { AutoGenerateColumns = true, IsReadOnly = true, MaxHeight = maximumHeight };

    private static Button Button(string content, string automationName, Action action, bool primary = false)
    {
        var button = new Button { Content = content, Padding = new Thickness(14, 7, 14, 7) };
        if (primary && Application.Current?.TryFindResource("PrimaryButton") is Style style) button.Style = style;
        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => action();
        return button;
    }

    private static Button Button(string content, string automationName, Func<Task> action)
    {
        var button = new Button { Content = content, Padding = new Thickness(14, 7, 14, 7) };
        AutomationProperties.SetName(button, automationName);
        button.Click += async (_, _) => await action();
        return button;
    }

    private static long ParseMetric(string value) => long.TryParse(value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static SolidColorBrush Brush(string colour)
    {
        return Themes.ThemeBrushes.Resolve(colour);
    }
}
