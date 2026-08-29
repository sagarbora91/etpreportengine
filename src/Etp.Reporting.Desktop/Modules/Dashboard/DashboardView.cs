extern alias EtpApplication;

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
    private static readonly Brush PrimaryText = Brush("#172B3E");
    private static readonly Brush SecondaryText = Brush("#5D6873");
    private static readonly Brush ChartBrush = Brush("#176B87");

    private readonly TextBlock importedFilesMetric = MetricValue(24);
    private readonly TextBlock completedBatchesMetric = MetricValue(24);
    private readonly TextBlock sourceRowsMetric = MetricValue(24);
    private readonly TextBlock latestImportMetric = MetricValue(16);
    private readonly DataGrid importHistoryGrid = ReadOnlyGrid(240);
    private readonly StackPanel dashboardChartPanel = new();
    private readonly TextBlock databaseHealthMetric = MetricValue();
    private readonly TextBlock databaseSizeMetric = MetricValue();
    private readonly TextBlock backupAgeMetric = MetricValue();
    private readonly TextBlock backupSpaceMetric = MetricValue();
    private readonly TextBlock failedImportsMetric = MetricValue();
    private readonly ItemsControl healthWarningsList = new();
    private readonly DataGrid operationalAuditGrid = ReadOnlyGrid(220);
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
        if (!preserveHealthColour)
            databaseHealthMetric.Foreground = state.DatabaseHealthTone switch
            {
                DashboardHealthTone.Healthy => Brushes.SeaGreen,
                DashboardHealthTone.Warning => Brushes.DarkOrange,
                _ => Brushes.Firebrick
            };
        databaseSizeMetric.Text = state.DatabaseSize;
        backupAgeMetric.Text = state.LatestBackup;
        backupSpaceMetric.Text = state.BackupFreeSpace;
        failedImportsMetric.Text = state.FailedImports;
        healthWarningsList.ItemsSource = state.HealthWarnings;
        operationalAuditGrid.ItemsSource = state.RecentAuditEvents;
        RenderChart(state.ImportedRowsByReport);
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel();
        var heading = new DockPanel();
        heading.Children.Add(new TextBlock { Text = "Operational summary", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = PrimaryText });
        var refresh = Button("Refresh", "Refresh dashboard", () => RefreshRequested?.Invoke(this, EventArgs.Empty));
        DockPanel.SetDock(refresh, Dock.Right);
        heading.Children.Insert(0, refresh);
        root.Children.Add(heading);

        var metrics = new UniformGrid { Columns = 4, Margin = new Thickness(0, 16, 0, 16) };
        metrics.Children.Add(Metric("Imported files", importedFilesMetric));
        metrics.Children.Add(Metric("Completed batches", completedBatchesMetric));
        metrics.Children.Add(Metric("Source rows", sourceRowsMetric));
        metrics.Children.Add(Metric("Latest import (UTC)", latestImportMetric));
        root.Children.Add(metrics);

        root.Children.Add(Label("Recent import history"));
        importHistoryGrid.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(importHistoryGrid);

        var chartHeading = new DockPanel { Margin = new Thickness(0, 16, 0, 8) };
        chartHeading.Children.Add(Label("Imported rows by report"));
        var export = Button("Export management summary PDF…", "Export management summary PDF", ExportPdfAsync);
        DockPanel.SetDock(export, Dock.Right);
        chartHeading.Children.Insert(0, export);
        root.Children.Add(chartHeading);
        root.Children.Add(dashboardChartPanel);

        var health = new Border { Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(12), Background = Brush("#F3F5F7"), CornerRadius = new CornerRadius(4) };
        var healthContent = new StackPanel();
        healthContent.Children.Add(Label("Database health"));
        var healthMetrics = new UniformGrid { Columns = 5, Margin = new Thickness(0, 8, 0, 6) };
        healthMetrics.Children.Add(Metric("Health", databaseHealthMetric));
        healthMetrics.Children.Add(Metric("Database size", databaseSizeMetric));
        healthMetrics.Children.Add(Metric("Latest backup (UTC)", backupAgeMetric));
        healthMetrics.Children.Add(Metric("Backup free space", backupSpaceMetric));
        healthMetrics.Children.Add(Metric("Failed imports (24h)", failedImportsMetric));
        healthContent.Children.Add(healthMetrics);
        healthContent.Children.Add(healthWarningsList);
        health.Child = healthContent;
        root.Children.Add(health);

        var auditHeading = Label("Recent privacy-safe activity");
        auditHeading.Margin = new Thickness(0, 16, 0, 6);
        root.Children.Add(auditHeading);
        root.Children.Add(operationalAuditGrid);
        return root;
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
        foreach (var item in items)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(80) });
            var label = new TextBlock { Text = item.ReportCode, VerticalAlignment = VerticalAlignment.Center };
            var bar = new Border { Background = ChartBrush, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, Width = 420d * item.SourceRows / maximum };
            var value = new TextBlock { Text = item.SourceRows.ToString("N0"), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(value, 2);
            row.Children.Add(label);
            row.Children.Add(bar);
            row.Children.Add(value);
            dashboardChartPanel.Children.Add(row);
        }
    }

    private static StackPanel Metric(string label, TextBlock value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Foreground = SecondaryText });
        panel.Children.Add(value);
        return panel;
    }

    private static TextBlock MetricValue(double fontSize = 12) =>
        new() { Text = "-", FontSize = fontSize, FontWeight = FontWeights.SemiBold };

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold };

    private static DataGrid ReadOnlyGrid(double maximumHeight) =>
        new() { AutoGenerateColumns = true, IsReadOnly = true, MaxHeight = maximumHeight };

    private static Button Button(string content, string automationName, Action action)
    {
        var button = new Button { Content = content, Padding = new Thickness(14, 6, 14, 6) };
        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => action();
        return button;
    }

    private static Button Button(string content, string automationName, Func<Task> action)
    {
        var button = new Button { Content = content, Padding = new Thickness(14, 6, 14, 6) };
        AutomationProperties.SetName(button, automationName);
        button.Click += async (_, _) => await action();
        return button;
    }

    private static SolidColorBrush Brush(string colour)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colour)!;
        brush.Freeze();
        return brush;
    }
}
