using System.IO;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Import.Batch;
using Microsoft.Win32;

namespace Etp.Reporting.Desktop.Modules.Imports;

public sealed record ImportWorkspaceAccess(bool CanImport, bool CanAdminister);

public partial class ImportWorkspaceView : UserControl, IAsyncDisposable
{
    private readonly DesktopImportCoordinator coordinator;
    private readonly Func<string> connectionStringProvider;
    private Func<ImportWorkspaceAccess> accessProvider = static () => new(false, false);
    private Func<string, string, string, Task> auditRecorder = static (_, _, _) => Task.CompletedTask;
    private Func<Task> dashboardRefresher = static () => Task.CompletedTask;

    public ImportWorkspaceView(
        DesktopImportCoordinator coordinator,
        Func<string> connectionStringProvider)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        InitializeComponent();
        ImportBusinessDateInput.SelectedDate = DateTime.Today.AddDays(-1);
    }

    public event EventHandler<string>? NotificationRequested;
    public event EventHandler<string>? ReadinessChanged;

    public DateTime? BusinessDate
    {
        get => ImportBusinessDateInput.SelectedDate;
        set => ImportBusinessDateInput.SelectedDate = value;
    }

    public bool CanRetry => RetryBatchButton.IsEnabled;

    public void AttachHost(
        Func<ImportWorkspaceAccess> accessProvider,
        Func<string, string, string, Task> auditRecorder,
        Func<Task> dashboardRefresher)
    {
        this.accessProvider = accessProvider ?? throw new ArgumentNullException(nameof(accessProvider));
        this.auditRecorder = auditRecorder ?? throw new ArgumentNullException(nameof(auditRecorder));
        this.dashboardRefresher = dashboardRefresher ?? throw new ArgumentNullException(nameof(dashboardRefresher));
    }

    public bool BrowseWorkbook()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "ETP import sources (*.xlsx;*.zip)|*.xlsx;*.zip|Excel workbooks (*.xlsx)|*.xlsx|ZIP archives (*.zip)|*.zip",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return false;
        coordinator.ClearValidatedImport();
        PersistButton.IsEnabled = false;
        WorkbookPathInput.Text = dialog.FileName;
        return true;
    }

    public bool BrowseImportFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select folder containing ETP workbooks", Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return false;
        coordinator.ClearValidatedImport();
        PersistButton.IsEnabled = false;
        WorkbookPathInput.Text = dialog.FolderName;
        return true;
    }

    public Task RetryFailedBatchAsync() => coordinator.FailedBatchPaths.Count > 0
        ? RunBatchAsync(coordinator.FailedBatchPaths)
        : Task.CompletedTask;

    private void BrowseWorkbook_Click(object sender, RoutedEventArgs e) => BrowseWorkbook();

    private void BrowseImportFolder_Click(object sender, RoutedEventArgs e) => BrowseImportFolder();

    private async void ValidateWorkbook_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WorkbookPathInput.Text))
        {
            ValidationResult.Text = "Select an XLSX workbook first.";
            return;
        }

        ValidateButton.IsEnabled = false;
        ValidationResult.Text = "Reading and validating workbook…";
        try
        {
            var result = await coordinator.ValidateAsync(WorkbookPathInput.Text, CancellationToken.None);
            DiagnosticsGrid.ItemsSource = result.Diagnostics;
            PersistButton.IsEnabled = result.Accepted;
            ValidationResult.Text = result.Accepted
                ? $"Validated as {result.ReportCode}. {result.StagedRows:N0} rows are ready for persistence."
                : "Validation blocked. Review the diagnostics below.";
            SetReadiness(result.Accepted ? "Workbook validated" : "Validation blocked");
            Notify(ValidationResult.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ValidationResult.Text = $"Could not read workbook: {coordinator.DescribeFailure(ex).SafeMessage}";
        }
        finally
        {
            ValidateButton.IsEnabled = true;
        }
    }

    private async void PersistWorkbook_Click(object sender, RoutedEventArgs e)
    {
        if (!coordinator.HasValidatedImport) return;
        PersistButton.IsEnabled = false;
        try
        {
            RequireImportAccess();
            var context = CreateImportRunContext();
            var outcome = await coordinator.PersistValidatedAsync(connectionStringProvider(), context);
            ValidationResult.Text = outcome.ReportCode switch
            {
                "R022" => $"Imported {outcome.Result.InvoiceControls:N0} invoice controls and {outcome.Result.ReportableTenderRows:N0} reportable tender rows. {outcome.Result.QuarantinedTenderRows:N0} unresolved tender rows were quarantined.",
                "STOCK_LEDGER" or "CLOSING_STOCK" => $"Imported {outcome.Result.PersistedRows:N0} {outcome.Result.ReportCode} rows successfully.",
                "R003" or "R013" => $"Imported {outcome.Result.PersistedRows:N0} {outcome.Result.ReportCode} enrichment rows: {outcome.Result.MatchedRows:N0} matched, {outcome.Result.MissingMatches:N0} missing, {outcome.Result.AmbiguousMatches:N0} ambiguous. Revenue totals were not changed.",
                _ => $"Imported {outcome.Result.PersistedRows:N0} sales rows successfully."
            };
            SetReadiness("Import completed");
            if (outcome.RestatementApplied)
                await auditRecorder("Restatement", "Succeeded", "Controlled source restatement applied");
            await coordinator.RetainValidatedEvidenceAsync(connectionStringProvider(), context);
            coordinator.ClearValidatedImport();
            await dashboardRefresher();
            Notify(ValidationResult.Text);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or ImportSourceException)
        {
            ValidationResult.Text = $"Import failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            ValidationResult.Text = $"Import failed: {coordinator.DescribeFailure(ex).SafeMessage}";
        }
        finally
        {
            PersistButton.IsEnabled = coordinator.HasValidatedImport;
        }
    }

    private async void StartBatchImport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();
        }
        catch (UnauthorizedAccessException ex)
        {
            ValidationResult.Text = ex.Message;
            return;
        }

        if (string.IsNullOrWhiteSpace(WorkbookPathInput.Text))
        {
            ValidationResult.Text = "Select a folder, XLSX workbook, or ZIP archive first.";
            return;
        }

        try
        {
            var paths = await coordinator.OpenBatchSourceAsync(WorkbookPathInput.Text);
            await RunBatchAsync(paths);
        }
        catch (ImportSourceException ex)
        {
            ValidationResult.Text = $"Batch blocked ({ex.Code}): {ex.Message}";
        }
        catch (Exception ex)
        {
            ValidationResult.Text = $"Batch could not start: {coordinator.DescribeFailure(ex).SafeMessage}";
        }
    }

    private void CancelBatchImport_Click(object sender, RoutedEventArgs e) => coordinator.CancelBatch();

    private async void RetryBatchImport_Click(object sender, RoutedEventArgs e) => await RetryFailedBatchAsync();

    private async Task RunBatchAsync(IReadOnlyList<string> paths)
    {
        StartBatchButton.IsEnabled = RetryBatchButton.IsEnabled = false;
        CancelBatchButton.IsEnabled = true;
        ImportProgressBar.Maximum = Math.Max(1, paths.Count);
        ImportProgressBar.Value = 0;
        var progress = new Progress<BatchImportProgress>(x =>
        {
            ImportProgressBar.Maximum = Math.Max(1, x.Total);
            ImportProgressBar.Value = x.Completed;
            ValidationResult.Text = $"{x.Stage}: {x.SafeFileName}";
        });
        try
        {
            var summary = await coordinator.RunBatchAsync(
                paths,
                connectionStringProvider(),
                () => Dispatcher.Invoke(() => RestatementModeInput.IsChecked == true),
                () => Dispatcher.Invoke(CreateImportRunContext),
                _ => auditRecorder("Restatement", "Succeeded", "Controlled source restatement applied"),
                progress);
            BatchResultsGrid.ItemsSource = summary.Files;
            ValidationResult.Text = $"Batch completed: {summary.Succeeded:N0} processed, {summary.ExactDuplicates:N0} exact duplicate files, " +
                $"{summary.NewRows:N0} new rows, {summary.AlreadyPresentRows:N0} rows already present, {summary.Conflicts:N0} conflicts, " +
                $"{summary.Failed:N0} failed, {summary.Cancelled:N0} cancelled.";
            RetryBatchButton.IsEnabled = coordinator.FailedBatchPaths.Count > 0;
            await dashboardRefresher();
            await auditRecorder("ImportBatch", summary.Failed > 0 ? "Failed" : summary.Cancelled > 0 ? "Cancelled" : "Succeeded", "Batch import completed");
            Notify(ValidationResult.Text);
        }
        finally
        {
            StartBatchButton.IsEnabled = true;
            CancelBatchButton.IsEnabled = false;
        }
    }

    private DesktopImportRunContext CreateImportRunContext()
    {
        var (store, businessDate) = ImportScope();
        var restatementEnabled = RestatementModeInput.IsChecked == true;
        if (restatementEnabled && !accessProvider().CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
        return new(store, businessDate, Environment.UserName, restatementEnabled, RestatementReasonInput.Text.Trim());
    }

    private (string Store, DateOnly Date) ImportScope()
    {
        if (ImportBusinessDateInput.SelectedDate is null)
            throw new InvalidOperationException("Select the ETP business date before importing.");
        if (ImportStoreInput.SelectedItem is not ComboBoxItem storeItem)
            throw new InvalidOperationException("Select the ETP store before importing.");
        return (storeItem.Content!.ToString()!, DateOnly.FromDateTime(ImportBusinessDateInput.SelectedDate.Value));
    }

    private void RequireImportAccess()
    {
        if (!accessProvider().CanImport)
            throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
    }

    private void Notify(string message) => NotificationRequested?.Invoke(this, message);

    private void SetReadiness(string status) => ReadinessChanged?.Invoke(this, status);

    public ValueTask DisposeAsync() => coordinator.DisposeAsync();
}
