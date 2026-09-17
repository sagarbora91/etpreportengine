extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using HistoryEntry = EtpApplication::Etp.Reporting.Application.Imports.ImportHistoryEntry;
using HistoryScope = EtpApplication::Etp.Reporting.Application.Imports.ImportHistoryScope;

namespace Etp.Reporting.Desktop.Modules.Imports;

/// <summary>Re-queries persisted import outcomes on every activation; never uses session results.</summary>
public sealed class ImportHistoryView : UserControl
{
    private readonly Func<HistoryScope, Task<IReadOnlyList<HistoryEntry>>> load;
    private HistoryScope? scope;
    private int revision;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
    private readonly DataGrid rows = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
    private readonly DataGrid diagnostics = new() { AutoGenerateColumns = false, IsReadOnly = true, MaxHeight = 150, Visibility = Visibility.Collapsed };
    public IReadOnlyList<HistoryEntry> Entries { get; private set; } = [];
    public string StatusText => status.Text;
    public string DetailText => detail.Text;
    public bool IsLoading { get; private set; }

    public ImportHistoryView(Func<HistoryScope, Task<IReadOnlyList<HistoryEntry>>> load)
    {
        this.load = load;
        var root = new Grid { Margin = new Thickness(4) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "Imports — saved outcomes", FontSize = 18, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Imported files and repeat attempts are saved here. Received files is the separate document inbox. Undetected dates use the attempt's UTC date.", TextWrapping = TextWrapping.Wrap });
        var refresh = new Button { Content = "Refresh history", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += async (_, _) => { if (scope is not null) await ActivateAsync(scope); };
        heading.Children.Add(refresh); heading.Children.Add(status); root.Children.Add(heading);
        foreach (var (title, path, width) in new[] {
            ("Recorded (UTC)", "RecordedUtc", 155d), ("File", "Result.FileName", 230d), ("Report", "Result.ReportCode", 80d),
            ("Store", "Result.StoreCode", 80d), ("Period", "Result.Period", 225d), ("Outcome", "Result.Status", 130d),
            ("Rows", "Result.RowsProcessed", 75d), ("New", "Result.NewRows", 75d), ("Present", "Result.AlreadyPresentRows", 75d), ("Conflicts", "Result.ConflictRows", 75d) })
            rows.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path) { StringFormat = path == "RecordedUtc" ? "dd MMM yyyy HH:mm" : null }, Width = width });
        TablePresentation.Configure(rows); AutomationProperties.SetName(rows, "Saved import outcomes");
        rows.SelectionChanged += (_, _) => ShowDetails(rows.SelectedItem as HistoryEntry);
        Grid.SetRow(rows, 1); root.Children.Add(rows);
        foreach (var (title, path) in new[] { ("Issue", "Code"), ("Row", "SourceRow"), ("Column", "SourceColumn"), ("Guidance", "Message") })
            diagnostics.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = path == "Message" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });
        var details = new StackPanel(); details.Children.Add(detail); details.Children.Add(diagnostics);
        Grid.SetRow(details, 2); root.Children.Add(details); Content = root;
        AutomationProperties.SetName(this, "Durable import history");
    }

    public async Task ActivateAsync(HistoryScope value)
    {
        scope = value; var current = ++revision; IsLoading = true;
        status.Text = $"Loading saved imports for {value.From:dd MMM yyyy} – {value.To:dd MMM yyyy} · {value.StoreCode ?? "Both stores"}…";
        Entries = []; rows.ItemsSource = null; ShowDetails(null);
        try
        {
            var loaded = await load(value);
            if (current != revision) return;
            Entries = loaded; rows.ItemsSource = loaded;
            status.Text = $"{loaded.Count:N0} saved outcomes · {value.From:dd MMM yyyy} – {value.To:dd MMM yyyy} · {value.StoreCode ?? "Both stores"}";
            if (loaded.Count > 0) rows.SelectedIndex = 0;
        }
        catch (Exception exception)
        {
            if (current != revision) return;
            DesktopDiagnostics.Record(exception, "Imports.History", "IMPORT_HISTORY_LOAD_FAILED");
            status.Text = "Saved imports could not be loaded. " + DesktopFriendlyError.Describe(exception);
        }
        finally { if (current == revision) IsLoading = false; }
    }

    public void SelectEntry(HistoryEntry entry) => rows.SelectedItem = entry;
    private void ShowDetails(HistoryEntry? entry)
    {
        detail.Text = entry is null ? "Select an import to see its saved diagnostics." : $"{entry.Result.FileName}: {entry.Result.Status}. {entry.Result.Message}";
        diagnostics.ItemsSource = entry?.Result.Diagnostics;
        diagnostics.Visibility = entry?.Result.Diagnostics?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
