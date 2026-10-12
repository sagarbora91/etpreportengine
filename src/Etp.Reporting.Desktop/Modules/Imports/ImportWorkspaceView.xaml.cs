extern alias EtpApplication;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Etp.Reporting.Desktop.Themes;
using Microsoft.Win32;
using FolderImportOptions = EtpApplication::Etp.Reporting.Application.Imports.FolderImportOptions;
using FolderImportProgress = EtpApplication::Etp.Reporting.Application.Imports.FolderImportProgress;
using FolderImportFileResult = EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult;

namespace Etp.Reporting.Desktop.Modules.Imports;

public sealed record ImportWorkspaceAccess(bool CanImport, bool CanAdminister);

public partial class ImportWorkspaceView : UserControl, IAsyncDisposable
{
    private readonly DesktopImportCoordinator coordinator;
    private readonly Func<string> connectionStringProvider;
    private Func<ImportWorkspaceAccess> accessProvider = static () => new(false, false);
    // Reloads the role from the database. accessProvider returns the host's cached copy,
    // which cannot notice a role the Owner revoked after this window loaded it.
    private Func<Task<ImportWorkspaceAccess>> currentAccessLoader = static () => Task.FromResult(new ImportWorkspaceAccess(false, false));
    private Func<string, string, string, Task> auditRecorder = static (_, _, _) => Task.CompletedTask;
    private Func<Task> dashboardRefresher = static () => Task.CompletedTask;
    private IReadOnlyList<FolderImportFileResult> latestResults = [];
    private FolderImportOptions? lastImportOptions;
    private string currentTask = "import-files";
    public ImportWorkspaceView(DesktopImportCoordinator coordinator, Func<string> connectionStringProvider)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        InitializeComponent();
        CaptureImportControls();
        BuildResultColumns();
        TablePresentation.Configure(BatchResultsGrid);
        Loaded += (_, _) => EmptyStateHistoryButton.Visibility = ShowImportHistoryRequested is not null ? Visibility.Visible : Visibility.Collapsed;
    }
    /// <summary>FR-07: "Show the Daily Sales Report" for the last imported date; the shell navigates, the header date does not move silently.</summary>
    public event EventHandler<DateOnly>? ShowDailySalesReportRequested;
    /// <summary>FR-07: "Show Import History".</summary>
    public event EventHandler? ShowImportHistoryRequested;
    private DateOnly? lastImportedDate;
    public event EventHandler<string>? NotificationRequested;
    public event EventHandler<string>? ReadinessChanged;
    public event EventHandler<FolderImportProgress>? ProgressChanged;
    public event EventHandler? RetryAvailabilityChanged;
    public DateTime? BusinessDate { get; set; }
    // Session results only. The Problems tab reads the database instead, so that the
    // list survives closing the application; this stays for the in-run summary.
    public IReadOnlyList<ImportProblem> Problems => ImportProblems.From(latestResults);
    public void SetKnownStores(IReadOnlyList<string> stores) => coordinator.SetKnownStores(stores);
    public bool CanRetry => accessProvider().CanImport && !IsBusy && coordinator.FailedBatchPaths.Count > 0;

    // Retry can be unavailable because the role forbids it, because an import is
    // running, or because nothing failed. Only the first deserves an explanation
    // beside the button; the other two are obvious from the screen.
    public bool CanRetryByRole => accessProvider().CanImport;
    public void AttachHost(Func<ImportWorkspaceAccess> accessProvider, Func<Task<ImportWorkspaceAccess>> currentAccessLoader,
        Func<string, string, string, Task> auditRecorder, Func<Task> dashboardRefresher)
    {
        this.accessProvider = accessProvider;
        this.currentAccessLoader = currentAccessLoader ?? throw new ArgumentNullException(nameof(currentAccessLoader));
        this.auditRecorder = auditRecorder;
        this.dashboardRefresher = dashboardRefresher;
        RetryAvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SelectTask(string taskId)
    {
        currentTask = taskId;
        BatchResultsGrid.ItemsSource = latestResults.Where(result => taskId switch
        {
            "duplicates" => result.Status is "Duplicate" or "Duplicate content",
            "already-present" => result.AlreadyPresentRows > 0,
            "conflicts" => result.ConflictRows > 0,
            "import-failures" => result.Failed,
            "unknown-layouts" => result.Status == "Unknown layout",
            _ => true
        }).ToArray();
    }
    public bool BrowseWorkbook()
    {
        if (IsBusy) return false;
        var dialog = new OpenFileDialog { Filter = "ETP sources (*.xlsx;*.csv;*.zip)|*.xlsx;*.csv;*.zip", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return false;
        WorkbookPathInput.Text = dialog.FileName;
        return true;
    }
    public bool BrowseImportFolder()
    {
        if (IsBusy) return false;
        var dialog = new OpenFolderDialog { Title = "Choose a store folder or the parent containing the store folders" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return false;
        WorkbookPathInput.Text = dialog.FolderName;
        return true;
    }
    public Task ImportSelectedSourceAsync() => RunImportAsync(retry: false);
    public Task RetryFailedBatchAsync() => CanRetry ? RunImportAsync(retry: true) : Task.CompletedTask;
    private async Task RunImportAsync(bool retry)
    {
        using var operation = BeginImportOperation();
        if (operation is null) return;
        try
        {
            if (!accessProvider().CanImport) throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
            if (!retry && string.IsNullOrWhiteSpace(WorkbookPathInput.Text)) throw new InvalidOperationException("Choose a folder, workbook or ZIP first.");
            var restate = retry ? lastImportOptions?.RestatementEnabled == true : RestatementModeInput.IsChecked == true;
            // A manager may request and apply an Owner-approved replacement, but a role
            // revoked since this window loaded it must stop before the approval flow starts.
            // The cached check above cannot see that, so reload the role from the database.
            // The SQL layer still reloads access itself before it replaces any facts.
            if (restate && !(await currentAccessLoader()).CanImport)
                throw new UnauthorizedAccessException("Owner or Store Manager permission is required for a restatement.");
            var options = retry ? lastImportOptions! : new FolderImportOptions(Environment.UserName, restate, RestatementReasonInput.Text.Trim(),
                restate ? (ImportStoreInput.SelectedItem as ComboBoxItem)?.Content?.ToString() : null,
                restate && ImportBusinessDateInput.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null)
                { ChooseRestatementTarget = ChooseRestatementTargetAsync };
            lastImportOptions = options;
            var previousResults = latestResults;
            CancelBatchButton.IsEnabled = true;
            if (!retry) latestResults = [];
            ImportEmptyState.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Visible;
            HandOffPanel.Visibility = Visibility.Collapsed;
            DetectedScopeText.Text = "Detecting store and date range…";
            BatchResultsGrid.ItemsSource = latestResults;
            DiagnosticsGrid.ItemsSource = null;
            DiagnosticsGrid.Visibility = Visibility.Collapsed;
            FileDetailText.Text = "Select a file to see its results and diagnostics.";
            ImportProgressBar.Value = 0;
            var progress = new Progress<FolderImportProgress>(value =>
            {
                ImportProgressBar.Maximum = Math.Max(1, value.Total);
                ImportProgressBar.Value = value.Completed;
                ValidationResult.Text = $"{value.Completed:N0} of {value.Total:N0} files · {value.Stage} · {value.CurrentFile}";
                latestResults = retry ? MergeRetryResults(previousResults, value.Files) : value.Files;
                SelectTask(currentTask);
                ShowScope();
                ProgressChanged?.Invoke(this, value);
            });
            var summary = retry
                ? await coordinator.RetryFailedFolderAsync(progress)
                : await coordinator.ImportFolderAsync(WorkbookPathInput.Text, connectionStringProvider(), options, progress);
            latestResults = retry ? MergeRetryResults(previousResults, summary.Files) : summary.Files;
            SelectTask(currentTask);
            ShowScope();
            ValidationResult.Text = $"{summary.Imported:N0} imported · {summary.Duplicates:N0} duplicate/already present · {summary.NewRows:N0} new rows · {summary.Conflicts:N0} conflicts · {summary.Failed:N0} failed · {summary.UnknownLayouts:N0} unknown layouts.";
            ShowHandOffs();
            ReadinessChanged?.Invoke(this, summary.Failed > 0 ? "Review import results" : "Import completed");
            await dashboardRefresher();
            await auditRecorder("ImportBatch", summary.Failed > 0 ? "Failed" : "Succeeded", retry ? "Failed-file retry completed" : "Folder import completed");
            NotificationRequested?.Invoke(this, ValidationResult.Text);
        }
        catch (Exception exception)
        {
            ValidationResult.Text = DesktopDiagnostics.WithReference(DesktopFriendlyError.Describe(exception),
                DesktopDiagnostics.Record(exception, "Imports.Workspace", "FOLDER_IMPORT_FAILED", operation: "Folder import failed"));
        }
    }
    private static IReadOnlyList<FolderImportFileResult> MergeRetryResults(IReadOnlyList<FolderImportFileResult> previous,
        IReadOnlyList<FolderImportFileResult> retried)
    {
        var updated = retried.Where(result => result.SourcePath is not null)
            .ToDictionary(result => result.SourcePath!, StringComparer.OrdinalIgnoreCase);
        return previous.Select(result => result.Failed && result.SourcePath is { } path && updated.TryGetValue(path, out var replacement)
            ? replacement : result).ToArray();
    }
    private void ShowHandOffs()
    {
        lastImportedDate = latestResults.Where(result => !result.Failed && result.PeriodEnd is not null).Select(result => result.PeriodEnd).Max();
        ShowDsrButton.Visibility = lastImportedDate is not null && ShowDailySalesReportRequested is not null ? Visibility.Visible : Visibility.Collapsed;
        if (lastImportedDate is { } date) ShowDsrButton.Content = $"Show the Daily Sales Report for {date:dd MMM yyyy}";
        ShowHistoryButton.Visibility = ShowImportHistoryRequested is not null ? Visibility.Visible : Visibility.Collapsed;
        HandOffPanel.Visibility = ShowDsrButton.Visibility == Visibility.Visible || ShowHistoryButton.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ShowDailySalesReport_Click(object sender, RoutedEventArgs e)
    {
        if (lastImportedDate is { } date) ShowDailySalesReportRequested?.Invoke(this, date);
    }
    private void ShowImportHistory_Click(object sender, RoutedEventArgs e) => ShowImportHistoryRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>DS-13: Verdict first and widest, Result as a tone chip, counts in one column, Date range last.</summary>
    private void BuildResultColumns()
    {
        var wrap = new Style(typeof(TextBlock));
        wrap.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        var chip = new Style(typeof(TextBlock));
        chip.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(8, 3, 8, 3)));
        chip.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left));
        chip.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        chip.Setters.Add(new Setter(TextBlock.BackgroundProperty, ThemeBrushes.Get("SuccessSoft")));
        chip.Setters.Add(new Setter(TextBlock.ForegroundProperty, ThemeBrushes.Get("Success")));
        void When(string status, string fill, string ink)
        {
            var trigger = new DataTrigger { Binding = new Binding(nameof(FolderImportFileResult.Status)), Value = status };
            trigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, ThemeBrushes.Get(fill)));
            trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, ThemeBrushes.Get(ink)));
            chip.Triggers.Add(trigger);
        }
        When("Failed", "CriticalSoft", "Critical");
        When("Unknown layout", "WarningSoft", "Warning");
        When("Duplicate", "SurfaceSecondary", "SecondaryText");
        When("Duplicate content", "SurfaceSecondary", "SecondaryText");
        When("Already present", "SurfaceSecondary", "SecondaryText");
        var columns = BatchResultsGrid.Columns;
        columns.Add(new DataGridTextColumn { Header = "Verdict", Binding = new Binding(".") { Converter = new ResultTextConverter(ImportHistoryView.Verdict) }, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 320, ElementStyle = wrap });
        columns.Add(new DataGridTextColumn { Header = "File", Binding = new Binding(nameof(FolderImportFileResult.FileName)), Width = 230 });
        columns.Add(new DataGridTextColumn { Header = "Result", Binding = new Binding(nameof(FolderImportFileResult.Status)), Width = 130, ElementStyle = chip });
        columns.Add(new DataGridTextColumn { Header = "Rows: new / present / conflicts", Binding = new Binding(".") { Converter = new ResultTextConverter(RowsText) }, Width = 220, ElementStyle = wrap });
        columns.Add(new DataGridTextColumn { Header = "Store", Binding = new Binding(nameof(FolderImportFileResult.StoreCode)), Width = 80 });
        columns.Add(new DataGridTextColumn { Header = "Report", Binding = new Binding(nameof(FolderImportFileResult.ReportCode)), Width = 80 });
        columns.Add(new DataGridTextColumn { Header = "Date range", Binding = new Binding(nameof(FolderImportFileResult.Period)), Width = 215 });
    }
    /// <summary>"1,204 new / 0 present / 0 conflicts".</summary>
    internal static string RowsText(FolderImportFileResult result) => $"{result.NewRows:N0} new / {result.AlreadyPresentRows:N0} present / {result.ConflictRows:N0} conflicts";
    private sealed class ResultTextConverter(Func<FolderImportFileResult, string> text) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is FolderImportFileResult result ? text(result) : value;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
    private void ShowScope()
    {
        var stores = latestResults.Where(result => result.StoreCode is not null).Select(result => result.StoreCode).Distinct().ToArray();
        var first = latestResults.Select(result => result.PeriodStart).Min();
        var last = latestResults.Select(result => result.PeriodEnd).Max();
        if (stores.Length > 0) DetectedScopeText.Text = $"Detected: {string.Join(" + ", stores)} · {first:dd MMM yyyy} – {last:dd MMM yyyy}";
        else DetectedScopeText.Text = "Store and date range are not yet available for this source.";
    }
    private void Result_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BatchResultsGrid.SelectedItem is not FolderImportFileResult result) return;
        DiagnosticsGrid.ItemsSource = result.Diagnostics;
        DiagnosticsGrid.Visibility = result.Diagnostics?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FileDetailText.Text = $"{result.FileName} · {TablePresentation.StoreLabel(result.StoreCode ?? "")} · {result.Period} · {result.Status} · {result.RowsProcessed:N0} rows, {result.NewRows:N0} new, {result.AlreadyPresentRows:N0} already present, {result.ConflictRows:N0} conflicts. {result.Message}";
    }
    private void RestatementMode_Changed(object sender, RoutedEventArgs e)
    {
        if (OverridePanel is null) return;
        var enabled = RestatementModeInput.IsChecked == true;
        OverridePanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ImportStoreInput.IsEnabled = ImportBusinessDateInput.IsEnabled = enabled;
    }
    private async void ImportFolder_Click(object sender, RoutedEventArgs e) { if (BrowseImportFolder()) await ImportSelectedSourceAsync(); }
    private void BrowseWorkbook_Click(object sender, RoutedEventArgs e) => BrowseWorkbook();
    private async void StartBatchImport_Click(object sender, RoutedEventArgs e) => await ImportSelectedSourceAsync();
    private void CancelBatchImport_Click(object sender, RoutedEventArgs e) { if (ConfirmationSheet.Show(this, "Cancel import", "Stop after the current step? Completed imports remain saved.")) coordinator.CancelBatch(); }
    public ValueTask DisposeAsync() => coordinator.DisposeAsync();
}
