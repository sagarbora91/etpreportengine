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
    private static readonly Brush PrimaryText = Brush("#10252D");
    private static readonly Brush SecondaryText = Brush("#65757A");
    private static readonly Brush Accent = Brush("#008D78");
    private static readonly Brush AccentSoft = Brush("#E3F3F0");
    private static readonly Brush DarkSurface = Brush("#082E3A");
    private static readonly Brush Divider = Brush("#D9E3E3");
    private static readonly Brush SurfaceSecondary = Brush("#F0F5F5");

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
    private readonly TextBlock backupSpaceMetric = MetricValue();
    private readonly TextBlock failedImportsMetric = MetricValue(21, Brushes.White);
    private readonly ItemsControl healthWarningsList = new();
    private readonly DataGrid operationalAuditGrid = ReadOnlyGrid(230);
    private readonly ProgressBar readinessProgress = new() { Minimum = 0, Maximum = 100, Height = 8 };
    private readonly TextBlock readinessPercent = new() { FontWeight = FontWeights.SemiBold, Foreground = Accent };
    private readonly TextBlock dailyCloseMessage = new() { Foreground = SecondaryText, TextWrapping = TextWrapping.Wrap };
    private readonly Border errorBanner = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock errorMessage = new() { Foreground = Brush("#C33D49"), TextWrapping = TextWrapping.Wrap };
    private readonly DashboardPresentationSession presentation;
    private readonly ExportManagementSummaryPdfAsync? exportManagementSummaryPdfAsync;
    private bool exportInProgress;

    public event EventHandler? RefreshRequested;
    public event EventHandler<string>? NavigationRequested;
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
                DashboardHealthTone.Warning => Brush("#A76500"),
                _ => Brush("#C33D49")
            };
        databaseSizeMetric.Text = state.DatabaseSize;
        backupAgeMetric.Text = state.LatestBackup;
        backupSpaceMetric.Text = state.BackupFreeSpace;
        failedImportsMetric.Text = state.FailedImports;
        healthWarningsList.ItemsSource = state.HealthWarnings;
        operationalAuditGrid.ItemsSource = state.RecentAuditEvents;
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
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        errorBanner.Child = errorMessage; errorBanner.Padding = new Thickness(8);
        var top = new DockPanel();
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Button("Refresh", "Refresh dashboard", () => RefreshRequested?.Invoke(this, EventArgs.Empty)));
        actions.Children.Add(Button("Export summary", "Export management summary PDF", ExportPdfAsync));
        DockPanel.SetDock(actions, Dock.Right); top.Children.Add(actions); top.Children.Add(errorBanner);
        root.Children.Add(top);
        var groups = new Grid();
        groups.ColumnDefinitions.Add(new ColumnDefinition()); groups.ColumnDefinitions.Add(new ColumnDefinition());
        var groupScroll = new ScrollViewer { Content = groups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(groupScroll, 1); root.Children.Add(groupScroll);
        Border Group(string title, params UIElement[] items)
        {
            var panel = new StackPanel(); panel.Children.Add(Title(title));
            foreach (var item in items) panel.Children.Add(item);
            var card = Card(panel); card.Margin = new Thickness(6); card.Padding = new Thickness(12); return card;
        }
        var closeTasks = new UniformGrid { Columns = 2, Margin = new Thickness(0,8,0,0) };
        foreach (var item in new[] { ("Manual inputs", "Manual Entry"), ("Readiness", "Daily Workflow"), ("Reports", "Sales Reports"), ("Archive", "Report Archive") })
        {
            var button = Button(item.Item1, item.Item1, () => NavigationRequested?.Invoke(this, item.Item2)); button.Margin = new Thickness(3); closeTasks.Children.Add(button);
        }
        groups.Children.Add(Group("Daily close", closeTasks));
        sourceRowsMetric.Foreground = PrimaryText; completedBatchesMetric.Foreground = PrimaryText; failedImportsMetric.Foreground = PrimaryText;
        var metrics = new UniformGrid { Columns = 2, Margin = new Thickness(0,8,0,0) };
        metrics.Children.Add(MiniMetric("SOURCE FILES", importedFilesMetric)); metrics.Children.Add(MiniMetric("SOURCE ROWS", sourceRowsMetric));
        groups.Children.Add(Group("Today at a glance", metrics, new TextBlock { Text = "Operational activity across the loaded database.", Foreground = SecondaryText, FontSize = 12, TextWrapping = TextWrapping.Wrap }));
        groups.Children.Add(Group("Import completion", readinessPercent, readinessProgress,
            new TextBlock { Text = "Import completion is not daily-close readiness. Review manual inputs and financial controls before finalising.", Foreground = SecondaryText, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) },
            Button("Review readiness", "Continue daily workflow", () => NavigationRequested?.Invoke(this, "Daily Workflow"))));
        groups.Children.Add(Group("System status", databaseHealthDetailMetric, backupAgeMetric,
            Button("Health and recovery", "Open health and recovery", () => NavigationRequested?.Invoke(this, "Admin / Settings"))));
        // Cards keep their natural height and reflow to one column in narrow windows; nothing is hidden or clipped, the overview scrolls instead.
        void FitGroups()
        {
            var columns = root.ActualWidth < 700 ? 1 : 2;
            groups.RowDefinitions.Clear();
            for (var i = 0; i < groups.Children.Count; i++)
            {
                if (i % columns == 0) groups.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(groups.Children[i], i / columns); Grid.SetColumn(groups.Children[i], i % columns); Grid.SetColumnSpan(groups.Children[i], columns == 1 ? 2 : 1);
            }
        }
        FitGroups(); root.SizeChanged += (_, _) => FitGroups();
        overviewContent = root; return root;
    }

    private Border BuildDailyCloseCard()
    {
        var content = new StackPanel();
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var continueButton = Button("Continue daily close  →", "Continue daily workflow", () => NavigationRequested?.Invoke(this, "Daily Workflow"), primary: true);
        var titles = new StackPanel();
        titles.Children.Add(Title("Daily close"));
        dailyCloseMessage.Margin = new Thickness(0, 4, 18, 0);
        titles.Children.Add(dailyCloseMessage);
        heading.Children.Add(titles);
        Grid.SetColumn(continueButton, 1);
        heading.Children.Add(continueButton);
        content.Children.Add(heading);

        var tasks = new UniformGrid { Columns = 2, Margin = new Thickness(0, 15, 0, 0) };
        tasks.Children.Add(TaskCard("Sales files", importedFilesMetric, "Source files received", AccentSoft));
        tasks.Children.Add(TaskCard("Control totals", databaseHealthMetric, "Database and control state", AccentSoft));
        tasks.Children.Add(TaskCard("Manual inputs", new TextBlock { Text = "Verify", FontWeight = FontWeights.SemiBold, Foreground = PrimaryText }, "Walk-ins, stock counts and targets", Brush("#FAF1E3")));
        tasks.Children.Add(TaskCard("Daily pack", new TextBlock { Text = "Next", FontWeight = FontWeights.SemiBold, Foreground = SecondaryText }, "Available after readiness checks", SurfaceSecondary));
        content.Children.Add(tasks);
        return Card(content);
    }

    private Border BuildReadinessCard()
    {
        var content = new StackPanel();
        var heading = new DockPanel();
        DockPanel.SetDock(readinessPercent, Dock.Right);
        heading.Children.Add(readinessPercent);
        heading.Children.Add(Title("Reporting readiness"));
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = "Completed import batches compared with received source files.", Foreground = SecondaryText, Margin = new Thickness(0, 4, 0, 12) });
        content.Children.Add(readinessProgress);
        var facts = new UniformGrid { Columns = 3, Margin = new Thickness(0, 13, 0, 0) };
        facts.Children.Add(MiniMetric("LATEST IMPORT", latestImportMetric));
        facts.Children.Add(MiniMetric("DATABASE SIZE", databaseSizeMetric));
        facts.Children.Add(MiniMetric("BACKUP SPACE", backupSpaceMetric));
        content.Children.Add(facts);
        var card = Card(content);
        card.Margin = new Thickness(0, 14, 0, 0);
        return card;
    }

    private Border BuildGlanceCard()
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Today at a glance", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        content.Children.Add(new TextBlock { Text = "Live operational source activity", Foreground = Brush("#AFC4C9"), Margin = new Thickness(0, 4, 0, 17) });
        content.Children.Add(new TextBlock { Text = "SOURCE ROWS", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("#AFC4C9") });
        content.Children.Add(sourceRowsMetric);
        var measures = new UniformGrid { Columns = 2, Margin = new Thickness(0, 18, 0, 16) };
        measures.Children.Add(DarkMetric("Completed batches", completedBatchesMetric));
        measures.Children.Add(DarkMetric("Failed imports", failedImportsMetric));
        content.Children.Add(measures);
        var reports = Button("Open reports  →", "Open reports", () => NavigationRequested?.Invoke(this, "Sales Reports"));
        reports.Background = Brush("#1B4651");
        reports.BorderBrush = Brush("#1B4651");
        reports.Foreground = Brushes.White;
        reports.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        content.Children.Add(reports);
        return new Border { Background = DarkSurface, CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Child = content };
    }

    private Border BuildSystemCard()
    {
        var content = new StackPanel();
        content.Children.Add(Title("System status"));
        content.Children.Add(new TextBlock { Text = "Operational services and recovery controls.", Foreground = SecondaryText, Margin = new Thickness(0, 4, 0, 12) });
        content.Children.Add(StatusRow("SQL database", databaseHealthDetailMetric));
        content.Children.Add(StatusRow("Last backup", backupAgeMetric));
        healthWarningsList.Margin = new Thickness(0, 8, 0, 0);
        content.Children.Add(healthWarningsList);
        var card = Card(content);
        card.Margin = new Thickness(0, 14, 0, 0);
        return card;
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

    private static Border TaskCard(string title, FrameworkElement value, string detail, Brush tint)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = PrimaryText });
        value.Margin = new Thickness(0, 7, 0, 2);
        content.Children.Add(value);
        content.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = PrimaryText, TextWrapping = TextWrapping.Wrap });
        return new Border { Background = tint, BorderBrush = Divider, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(4), Child = content };
    }

    private static Border MiniMetric(string label, TextBlock value)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = PrimaryText });
        value.Margin = new Thickness(0, 5, 0, 0);
        content.Children.Add(value);
        return new Border { Background = SurfaceSecondary, CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(3), Child = content };
    }

    private static StackPanel DarkMetric(string label, TextBlock value)
    {
        var content = new StackPanel();
        content.Children.Add(value);
        content.Children.Add(new TextBlock { Text = label, Foreground = Brush("#AFC4C9"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
        return content;
    }

    private static Grid StatusRow(string label, TextBlock value)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(9), Background = AccentSoft, Child = new TextBlock { Text = "✓", Foreground = Accent, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        value.FontSize = 12;
        value.FontWeight = FontWeights.Normal;
        value.Foreground = SecondaryText;
        content.Children.Add(value);
        Grid.SetColumn(content, 1);
        row.Children.Add(icon); row.Children.Add(content);
        return row;
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
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colour)!;
        brush.Freeze();
        return brush;
    }
}
