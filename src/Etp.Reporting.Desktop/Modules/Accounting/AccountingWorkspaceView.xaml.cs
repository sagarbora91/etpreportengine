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
    private readonly Func<string> connectionStringProvider;
    private Func<AccessSession> accessProvider = () => new("unknown", "Unknown user", AccessRole.None, false);
    private Func<Exception, string> errorDescriber = exception => exception.Message;

    public AccountingWorkspaceView(AccountingPresentationSession session, Func<string> connectionStringProvider)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        InitializeComponent();
        AccountingDateInput.SelectedDate = DateTime.Today.AddDays(-1);
    }

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
    }

    public Task RefreshAsync() => RefreshAccountingAsync();

    private async Task RefreshAccountingAsync()
    {
        try
        {
            RequireViewAccess();
            AccountingBatchGrid.ItemsSource = await session.RefreshAsync(connectionStringProvider());
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private async void PreviewAccountingBatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess();
            var scope = CurrentScope();
            var preview = await session.PreviewAsync(connectionStringProvider(), scope);
            AccountingEntryGrid.ItemsSource = preview.Batch.Entries;
            SetStatus(preview.Batch.IsBalanced
                ? $"Balanced preview: debit {preview.Batch.DebitTotal:N2}, credit {preview.Batch.CreditTotal:N2}."
                : $"Preview blocked. Missing approved mappings: {string.Join(", ", preview.Batch.MissingMappings)}.");
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private async void SaveAccountingBatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();
            var id = await session.SaveCurrentAsync(connectionStringProvider(), CurrentScope());
            SetStatus($"Accounting batch {id:N0} saved for Owner review.");
            await RefreshAccountingAsync();
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private async void ApproveAccountingBatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess();
            if (AccountingBatchGrid.SelectedItem is not AccountingBatchSummary row)
                throw new InvalidOperationException("Select one accounting batch.");
            await session.ApproveAsync(connectionStringProvider(), row, AccountingMappingReasonInput.Text);
            SetStatus($"Accounting batch {row.Id:N0} approved.");
            await RefreshAccountingAsync();
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private async void ExportTallyXml_Click(object sender, RoutedEventArgs e)
    {
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
            var receipt = await session.ExportAsync(connectionStringProvider(), row, "Saagar Traders", dialog.FileName);
            SetStatus($"Approved Tally XML exported with SHA-256 {receipt.Sha256[..12]}…");
            await RefreshAccountingAsync();
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private async void ApproveAccountingMapping_Click(object sender, RoutedEventArgs e)
    {
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
            SetStatus($"Approved {eventCode} ledger mapping is active from {scope.BusinessDate:dd-MMM-yyyy}.");
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private AccountingScope CurrentScope()
    {
        if (AccountingDateInput.SelectedDate is null)
            throw new InvalidOperationException("Select the accounting business date.");
        if (string.IsNullOrWhiteSpace(AccountingStoreInput.Text))
            throw new InvalidOperationException("Enter the accounting store.");
        return new(AccountingStoreInput.Text.Trim().ToUpperInvariant(),
            DateOnly.FromDateTime(AccountingDateInput.SelectedDate.Value));
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
