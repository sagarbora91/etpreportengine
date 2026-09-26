extern alias EtpApplication;

using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AccessSession = EtpApplication::Etp.Reporting.Application.Access.AccessSession;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;
using AccountingBatchSummary = EtpApplication::Etp.Reporting.Application.Accounting.AccountingBatchSummary;
using AccountingScope = EtpApplication::Etp.Reporting.Application.Accounting.AccountingScope;
using ApproveAccountingMapping = EtpApplication::Etp.Reporting.Application.Accounting.ApproveAccountingMapping;

namespace Etp.Reporting.Desktop.Modules.Accounting;

public sealed partial class AccountingWorkspaceView : UserControl
{
    private readonly AccountingPresentationSession session;
    private int entriesRevision;
    private readonly Func<string> connectionStringProvider;
    private Func<AccessSession> accessProvider = () => new("unknown", "Unknown user", AccessRole.None, false);
    private Func<Exception, string> errorDescriber = DesktopFriendlyError.Describe;

    public AccountingWorkspaceView(AccountingPresentationSession session, Func<string> connectionStringProvider)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        InitializeComponent();
        batchReasons = new(AccountingBatchGrid, BatchApprovalReasonInput, row => (row as AccountingBatchSummary)?.Id);
        AccountingDateInput.SelectedDate = DateTime.Today.AddDays(-1);
        AccountingDateInput.SelectedDateChanged += (_, _) => InvalidatePreview();
        AccountingStoreInput.TextChanged += (_, _) => InvalidatePreview();
        AccountingBatchGrid.SelectionChanged += async (_, _) =>
        {
            var revision = ++entriesRevision;
            RefreshActionState();
            if (AccountingBatchGrid.SelectedItem is AccountingBatchSummary selected)
            {
                try
                {
                    var entries = await session.LoadEntriesAsync(connectionStringProvider(), selected.Id);
                    if (revision == entriesRevision) AccountingEntryGrid.ItemsSource = entries;
                }
                catch (Exception exception) { SetStatus(errorDescriber(exception)); }
            }
        };
    }

    public string StoreCode { get => AccountingStoreInput.Text; set => AccountingStoreInput.Text = value; }
    public event EventHandler<string>? NotificationRequested;

    public DateTime? BusinessDate
    {
        get => AccountingDateInput.SelectedDate;
        set => AccountingDateInput.SelectedDate = value;
    }

    public void AttachHost(Func<AccessSession> accessProvider, Func<Exception, string> errorDescriber)
    {
        this.accessProvider = accessProvider ?? throw new ArgumentNullException(nameof(accessProvider));
        this.errorDescriber = errorDescriber ?? throw new ArgumentNullException(nameof(errorDescriber));
        RefreshActionState();
    }

    public Task RefreshAsync() => RefreshAccountingAsync();

    private async Task RefreshAccountingAsync()
    {
        try
        {
            RequireOwnerAccess();
            AccountingEntryGrid.ItemsSource = null; SaveTaskButton.IsEnabled = false;
            AccountingBatchGrid.ItemsSource = await session.RefreshAsync(connectionStringProvider());
            AccountingExportHistoryGrid.ItemsSource = await session.LoadExportHistoryAsync(connectionStringProvider());
            var destination = await session.LoadDestinationAsync(connectionStringProvider());
            DestinationStatus.Text = string.IsNullOrWhiteSpace(destination.CompanyName)
                ? "Tally company not decided (D12). Enter the intended TEST company in Settings."
                : $"Destination: {destination.CompanyName} · {destination.EnvironmentLabel}. Journal file only; no Tally read-back.";
            RefreshActionState();
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "Accounting.Workspace", "ACCOUNTING_REFRESH_FAILED"); SetStatus(errorDescriber(ex)); }
    }

    private async void PreviewAccountingBatch_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireOwnerAccess();
            ++entriesRevision;
            AccountingBatchGrid.SelectedItem = null;
            RefreshActionState();
            var scope = CurrentScope();
            var preview = await session.PreviewAsync(connectionStringProvider(), scope);
            AccountingEntryGrid.ItemsSource = preview.Batch.Entries;
            SaveTaskButton.IsEnabled = accessProvider().CanAdminister;
            SetStatus(preview.Batch.IsBalanced
                ? $"Balanced preview: debit {preview.Batch.DebitTotal:N2}, credit {preview.Batch.CreditTotal:N2}."
                : $"Preview blocked. Missing approved mappings: {string.Join(", ", preview.Batch.MissingMappings)}.");
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "Accounting.Workspace", "ACCOUNTING_PREVIEW_FAILED"); SetStatus(errorDescriber(ex)); }
    }

    private async void SaveAccountingBatch_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireOwnerAccess();
            var id = await session.SaveCurrentAsync(connectionStringProvider(), CurrentScope());
            SetStatus($"Accounting batch {id:N0} saved for Owner review.");
            await RefreshAccountingAsync();
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "Accounting.Workspace", "ACCOUNTING_BATCH_SAVE_FAILED"); SetStatus(errorDescriber(ex)); }
    }

    private async void ApproveAccountingBatch_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireOwnerAccess();
            if (AccountingBatchGrid.SelectedItem is not AccountingBatchSummary row)
                throw new InvalidOperationException("Select one accounting batch.");
            await session.ApproveAsync(connectionStringProvider(), row, BatchApprovalReasonInput.Text);
            BatchApprovalReasonInput.Clear();
            SetStatus($"Accounting batch {row.Id:N0} approved.");
            await RefreshAccountingAsync();
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "Accounting.Workspace", "ACCOUNTING_BATCH_APPROVAL_FAILED"); SetStatus(errorDescriber(ex)); }
    }

    private async void RejectAccountingBatch_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireOwnerAccess();
            if (AccountingBatchGrid.SelectedItem is not AccountingBatchSummary row)
                throw new InvalidOperationException("Select one accounting batch.");
            await session.RejectAsync(connectionStringProvider(), row, BatchApprovalReasonInput.Text);
            BatchApprovalReasonInput.Clear();
            SetStatus($"Accounting batch {row.Id:N0} rejected.");
            await RefreshAccountingAsync();
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "Accounting.Workspace", "ACCOUNTING_BATCH_REJECTION_FAILED"); SetStatus(errorDescriber(ex)); }
    }

    private async void ExportTallyXml_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireOwnerAccess();
            if (AccountingBatchGrid.SelectedItem is not AccountingBatchSummary row)
                throw new InvalidOperationException("Select one approved accounting batch.");
            var dialog = new SaveFileDialog
            {
                Filter = "Tally XML (*.xml)|*.xml",
                FileName = $"ETP_Tally_{row.StoreCode}_{row.BusinessDate:yyyyMMdd}_Gen{row.AccountingGeneration:D2}.xml",
                AddExtension = true
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            var receipt = await session.ExportAsync(connectionStringProvider(), row, "", dialog.FileName);
            SetStatus($"{receipt.EnvironmentLabel} file written for {receipt.CompanyName}. SHA-256 {receipt.Sha256[..12]}… Tally result has not been checked.");
            await RefreshAccountingAsync();
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "Accounting.Workspace", "ACCOUNTING_EXPORT_FAILED"); SetStatus(errorDescriber(ex)); }
    }

    private async void ApproveAccountingMapping_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireOwnerAccess();
            var scope = CurrentScope();
            var eventCode = SelectedContent(AccountingEventInput);
            await session.ApproveMappingAsync(connectionStringProvider(), new ApproveAccountingMapping(
                scope, eventCode, DebitLedgerInput.Text, CreditLedgerInput.Text,
                AccountingNarrationInput.Text, AccountingMappingReasonInput.Text));
            DebitLedgerInput.Clear();
            CreditLedgerInput.Clear();
            AccountingMappingReasonInput.Clear();
            AccountingNarrationInput.Text = DefaultNarration;
            SetStatus($"Approved {eventCode} ledger mapping is active from {scope.BusinessDate:dd-MMM-yyyy}.");
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "Accounting.Workspace", "ACCOUNTING_MAPPING_APPROVAL_FAILED"); SetStatus(errorDescriber(ex)); }
    }

    private void RequireViewAccess()
    {
        if (!accessProvider().CanView)
            throw new UnauthorizedAccessException("This Windows account does not have application access.");
    }

    private void RequireImportAccess()
    {
        if (!accessProvider().CanImport)
            throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
    }

    private void RequireOwnerAccess()
    {
        if (!accessProvider().CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private void SetStatus(string message)
    {
        AccountingStatus.Text = message;
        NotificationRequested?.Invoke(this, message);
    }

    private static string SelectedContent(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item && !string.IsNullOrWhiteSpace(item.Content?.ToString())
            ? item.Content!.ToString()!
            : throw new InvalidOperationException("Select a value.");
}
