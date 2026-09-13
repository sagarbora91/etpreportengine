extern alias EtpApplication;
using System.Windows;
using AccountingScope = EtpApplication::Etp.Reporting.Application.Accounting.AccountingScope;
namespace Etp.Reporting.Desktop.Modules.Accounting;
public sealed partial class AccountingWorkspaceView
{
    private AccountingScope CurrentScope()
    {
        if (AccountingDateInput.SelectedDate is null) throw new InvalidOperationException("Select the accounting business date.");
        if (string.IsNullOrWhiteSpace(AccountingStoreInput.Text)) throw new InvalidOperationException("Enter the accounting store.");
        return new(AccountingStoreInput.Text.Trim().ToUpperInvariant(), DateOnly.FromDateTime(AccountingDateInput.SelectedDate.Value));
    }
    private readonly WorkspaceOperationGate operationGate = new();
    public bool IsBusy => operationGate.IsBusy;
    private SelectionReasonDrafts? batchReasons;
    private const string DefaultNarration = "ETP {description} - {reference}";
    public bool HasRetainedDraft => new[] { DebitLedgerInput, CreditLedgerInput, AccountingMappingReasonInput, BatchApprovalReasonInput }.Any(input => input.Text.Length > 0)
        || AccountingNarrationInput.Text != DefaultNarration || batchReasons?.HasDrafts == true;
    public void DiscardRetainedDraft()
    {
        foreach (var input in new[] { DebitLedgerInput, CreditLedgerInput, AccountingMappingReasonInput, BatchApprovalReasonInput }) input.Clear();
        AccountingNarrationInput.Text = DefaultNarration;
        batchReasons?.Discard();
    }
    private void InvalidatePreview()
    {
        session.InvalidatePreview(); AccountingEntryGrid.ItemsSource = null;
        SaveTaskButton.IsEnabled = false;
        SetStatus("Accounting scope changed. Preview this store and business date again before saving.");
    }
    public void SelectTask(string id)
    {
        SaveTaskButton.IsEnabled = accessProvider().CanImport && session.Current.Draft is not null;
        ApproveTaskButton.IsEnabled = TallyTaskButton.IsEnabled = accessProvider().CanAdminister;
        SaveTaskButton.ToolTip = SaveTaskButton.IsEnabled ? null : "Owner or Store Manager permission is required.";
        TallyTaskButton.ToolTip = TallyTaskButton.IsEnabled ? null : "Owner permission is required to export Tally XML.";
        PreviewTaskButton.Visibility = id is "prepare-batch" or "validation" or "accounting-reconciliation" ? Visibility.Visible : Visibility.Collapsed;
        SaveTaskButton.Visibility = id == "prepare-batch" ? Visibility.Visible : Visibility.Collapsed;
        ApproveTaskButton.Visibility = id == "accounting-approval" ? Visibility.Visible : Visibility.Collapsed;
        TallyTaskButton.Visibility = id == "tally-export" ? Visibility.Visible : Visibility.Collapsed;
    }
}
