extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using HistoryEntry = EtpApplication::Etp.Reporting.Application.Imports.ImportHistoryEntry;
using HistoryScope = EtpApplication::Etp.Reporting.Application.Imports.ImportHistoryScope;
using ImportHistoryMessages = EtpApplication::Etp.Reporting.Application.Imports.ImportHistoryMessages;

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

    // Service interim (0048). Service rows carry store AW330, an inactive Service Centre store;
    // the Store column names it "Service Centre (AW330)". Retail codes are shown unchanged.
    private Func<string?, string?> storeLabel = code => code;
    public void SetStoreLabels(Func<string?, string?> label) { storeLabel = label ?? (code => code); rows.Items.Refresh(); }
    /// <summary>The text the Store column shows for a stored store code.</summary>
    public string? StoreText(string? code) => storeLabel(code);

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
            ("Rows", "Result.RowsProcessed", 75d), ("New", "Result.NewRows", 75d), ("Present", "Result.AlreadyPresentRows", 75d), ("Conflicts", "Result.ConflictRows", 75d),
            ("Failure", "Result.Failure.Code", 190d), ("Stage", "Result.Failure.Stage", 80d) })
            rows.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path) { StringFormat = path == "RecordedUtc" ? "dd MMM yyyy HH:mm" : null,
                Converter = path == "Result.StoreCode" ? new StoreLabelConverter(this) : null }, Width = width });
        TablePresentation.Configure(rows); AutomationProperties.SetName(rows, "Saved import outcomes");
        rows.SelectionChanged += (_, _) => ShowDetails(rows.SelectedItem as HistoryEntry);
        Grid.SetRow(rows, 1); root.Children.Add(rows);
        foreach (var (title, path) in new[] { ("Severity", "Severity"), ("Issue", "Code"), ("Block", "BlockNo"), ("Row", "SourceRow"),
            ("Column", "SourceColumn"), ("Document", "DocumentRef"), ("Count", "Occurrences"), ("Guidance", "Message") })
            diagnostics.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = path == "Message" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto });
        var details = new StackPanel(); details.Children.Add(detail); details.Children.Add(diagnostics);
        Grid.SetRow(details, 2); root.Children.Add(details); Content = root;
        AutomationProperties.SetName(this, "Durable import history");
    }

    /// <summary>
    /// Reads the same persisted outcomes the grid shows, without disturbing the grid.
    /// Problems needs the database rather than the session's last import, and must not
    /// move the History screen underneath the operator in order to get it.
    /// </summary>
    public Task<IReadOnlyList<HistoryEntry>> FetchAsync(HistoryScope value) => load(value);

    public async Task ActivateAsync(HistoryScope value)
    {
        scope = value; var current = ++revision; IsLoading = true;
        status.Text = $"Loading saved imports for {value.From:dd MMM yyyy} – {value.To:dd MMM yyyy} · {value.StoreCode ?? "All stores"}…";
        Entries = []; rows.ItemsSource = null; ShowDetails(null);
        try
        {
            var loaded = await load(value);
            if (current != revision) return;
            Entries = loaded; rows.ItemsSource = loaded;
            status.Text = $"{loaded.Count:N0} saved outcomes · {value.From:dd MMM yyyy} – {value.To:dd MMM yyyy} · {value.StoreCode ?? "All stores"}";
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

    private sealed class StoreLabelConverter(ImportHistoryView view) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => view.StoreText(value as string);
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
    private void ShowDetails(HistoryEntry? entry)
    {
        detail.Text = entry is null ? "Select an import to see its saved diagnostics." : Describe(entry.Result);
        diagnostics.ItemsSource = entry?.Result.Diagnostics;
        diagnostics.Visibility = entry?.Result.Diagnostics?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Failure code, stage and message as the attempt recorded them (IF-017), then commit and evidence state.
    private static string Describe(EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult result)
    {
        // A repeat file says "already imported" whichever duplicate outcome text it was saved with (FIX-08).
        var message = result.Failure is null && result.ConflictRows == 0 && ImportHistoryMessages.IsDuplicate(result.Status)
            ? ImportHistoryMessages.AlreadyImported : result.Message;
        var text = $"{result.FileName}: {result.Status}. {message}";
        if (result.Failure is { } failure)
            text += $" Failure {failure.Code} at {failure.Stage}" + (failure.SqlNumber is { } number ? $" (SQL {number})." : ".");
        if (result.CommitState is { } commit) text += $" Transaction: {commit}.";
        if (result.Evidence is { } evidence) text += $" Evidence: {evidence}.";
        return text;
    }
}
