extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Etp.Reporting.Reporting;
using Microsoft.Win32;
using ServiceRefresh = EtpApplication::Etp.Reporting.Application.Service.ServiceRefresh;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>Writes one table to an Excel workbook. Production uses the report exporter (IReportExportCoordinator).</summary>
public delegate Task ServiceExcelExport(string path, ExcelReportMetadata metadata, ExcelReportData data);

/// <summary>Writes several tables to one Excel workbook, one sheet each (the Pending board: one sheet per stage).</summary>
public delegate Task ServiceExcelPackExport(string path, ReportPackDocument document);

/// <summary>One column of a Service grid: its header, its Excel number format and its display format.</summary>
public sealed record ServiceColumn(string Header, string NumberFormat = "General", string? DisplayFormat = null, double Width = 120);

/// <summary>One grid row. The cells are in column order and are what the grid shows and the export writes.</summary>
public sealed record ServiceGridRow(object Source, IReadOnlyList<object?> Cells);

/// <summary>
/// The shared frame of the four read-only Service centre screens (Service interim, decision 15):
/// a title, "Service Centre AW330", the "Service data as at" line, a filter bar, one grid and Export.
/// Every activation re-queries; a slower earlier load never overwrites a newer one (revision counter).
/// Service has no store picker, so the shell's store selection is ignored.
/// </summary>
public abstract class ServiceScreenView : UserControl
{
    public const string ServiceCentreLabel = "Service Centre AW330";
    public const string NoDataText = "No Service data imported yet";

    private readonly Func<ServiceReportQuery> query;
    private readonly ServiceExcelExport export;
    private readonly string title;
    private readonly string diagnosticsSource;
    private readonly string diagnosticsEvent;
    private readonly TextBlock asAt = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly Button exportButton = new() { Content = "Export to Excel", MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
    private IReadOnlyList<ServiceColumn> columns = [];
    private int revision;

    protected ServiceScreenView(string title, string intro, string diagnosticsSource, string diagnosticsEvent,
        Func<ServiceReportQuery> query, ServiceExcelExport export)
    {
        this.title = title;
        this.diagnosticsSource = diagnosticsSource;
        this.diagnosticsEvent = diagnosticsEvent;
        this.query = query ?? throw new ArgumentNullException(nameof(query));
        this.export = export ?? throw new ArgumentNullException(nameof(export));

        var root = new Grid { Margin = new Thickness(4) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = ServiceCentreLabel + " · read only", TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(asAt);
        heading.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
        Summary = new StackPanel();
        heading.Children.Add(Summary);
        FilterBar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        heading.Children.Add(FilterBar);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        var refresh = new Button { Content = "Refresh", MinHeight = 44, Margin = new Thickness(0, 0, 8, 0) };
        refresh.Click += async (_, _) => await ActivateAsync();
        exportButton.Click += async (_, _) => await ExportWithDialogAsync();
        actions.Children.Add(refresh);
        actions.Children.Add(exportButton);
        heading.Children.Add(actions);
        heading.Children.Add(status);
        root.Children.Add(heading);

        TablePresentation.Configure(Table);
        AutomationProperties.SetName(Table, title);
        Grid.SetRow(Table, 1);
        root.Children.Add(Table);

        Footer = new StackPanel();
        Grid.SetRow(Footer, 2);
        root.Children.Add(Footer);
        Content = root;
        AutomationProperties.SetName(this, title);
    }

    protected WrapPanel FilterBar { get; }
    /// <summary>A panel between the intro and the filter bar, for a screen's numbers (KPI cards).</summary>
    protected StackPanel Summary { get; }
    protected StackPanel Footer { get; }
    public DataGrid Table { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
    public IReadOnlyList<ServiceGridRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> ColumnHeaders => columns.Select(column => column.Header).ToArray();
    public string AsAtText => asAt.Text;
    public string StatusText => status.Text;
    public bool IsLoading { get; private set; }
    public bool HasData { get; private set; }
    protected string Title => title;

    protected void SetColumns(IReadOnlyList<ServiceColumn> value)
    {
        columns = value;
        Table.Columns.Clear();
        for (var index = 0; index < value.Count; index++)
            Table.Columns.Add(new DataGridTextColumn
            {
                Header = value[index].Header,
                Binding = new Binding($"Cells[{index}]") { StringFormat = value[index].DisplayFormat },
                Width = value[index].Width
            });
    }

    /// <summary>Re-reads the refresh log and then this screen's rows.</summary>
    public async Task ActivateAsync()
    {
        var current = ++revision;
        IsLoading = true;
        status.Text = "Loading Service data…";
        Rows = []; Table.ItemsSource = null; exportButton.IsEnabled = false;
        try
        {
            var source = query();
            var refreshes = await source.LoadRefreshesAsync();
            if (current != revision) return;
            HasData = refreshes.Count > 0;
            asAt.Text = DescribeRefreshes(refreshes);
            if (!HasData) { status.Text = NoDataText + ". Import the Service Centre files on Import → Import folder."; ClearExtras(); return; }
            var request = PrepareLoad();
            if (request is not null) { status.Text = request; ClearExtras(); return; }
            var loaded = await LoadRowsAsync(source);
            if (current != revision) return;
            Rows = loaded; Table.ItemsSource = loaded; exportButton.IsEnabled = loaded.Count > 0;
            status.Text = loaded.Count == 0 ? EmptyRowsText : Summarise(loaded.Count);
            await LoadExtrasAsync(source, () => current == revision);
        }
        catch (Exception exception)
        {
            if (current != revision) return;
            DesktopDiagnostics.Record(exception, diagnosticsSource, diagnosticsEvent);
            status.Text = $"{title} could not be loaded. " + DesktopFriendlyError.Describe(exception);
        }
        finally { if (current == revision) IsLoading = false; }
    }

    /// <summary>The "Service data as at" line. The latest reading of any Service family sets the date.</summary>
    public static string DescribeRefreshes(IReadOnlyList<ServiceRefresh> refreshes)
    {
        if (refreshes.Count == 0) return NoDataText;
        var latest = refreshes.Max(refresh => refresh.SnapshotDate);
        var imported = refreshes.Max(refresh => AsUtc(refresh.ImportedAtUtc)).ToLocalTime();
        return $"Service data as at {latest:dd MMM yyyy} (refreshed {imported:dd MMM yyyy})";
    }

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>Returns a message instead of loading when the screen still needs input (job history).</summary>
    protected virtual string? PrepareLoad() => null;
    protected abstract Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source);
    protected virtual Task LoadExtrasAsync(ServiceReportQuery source, Func<bool> isCurrent) => Task.CompletedTask;
    protected virtual void ClearExtras() { }
    protected virtual string EmptyRowsText => "No rows for this choice.";
    protected virtual string Summarise(int count) => $"{count:N0} rows.";
    protected virtual string ExportName => title;
    protected virtual (DateOnly From, DateOnly To) ExportPeriod => (DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));

    /// <summary>The visible columns and rows, as the export writes them.</summary>
    public ExcelReportData BuildExportData() =>
        new(columns.Select(column => new ExcelReportColumn(column.Header, column.NumberFormat)).ToArray(),
            Rows.Select(row => row.Cells).ToArray());

    public virtual async Task ExportToPathAsync(string path)
    {
        var (from, to) = ExportPeriod;
        var metadata = new ExcelReportMetadata(ExportName, from, to, "Read only", "service-interim-1",
            asAt.Text, DateTimeOffset.UtcNow, ServiceCentreLabel);
        await export(path, metadata, BuildExportData());
    }

    private async Task ExportWithDialogAsync()
    {
        if (Rows.Count == 0) return;
        var dialog = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"{ExportName.Replace(' ', '_')}_{DateTime.Today:yyyyMMdd}.xlsx",
            AddExtension = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            await ExportToPathAsync(dialog.FileName);
            status.Text = $"Exported {Rows.Count:N0} rows to {System.IO.Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, diagnosticsSource, "SERVICE_EXPORT_FAILED");
            status.Text = "The export could not be saved. " + DesktopFriendlyError.Describe(exception);
        }
    }

    protected static string Joined(IEnumerable<string>? values) =>
        values is null ? "" : string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
}
