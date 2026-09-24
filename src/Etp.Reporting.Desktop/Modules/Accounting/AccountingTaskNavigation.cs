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
        ++entriesRevision;
        session.InvalidatePreview(); AccountingEntryGrid.ItemsSource = null;
        SaveTaskButton.IsEnabled = false;
        SetStatus("Accounting scope changed. Preview this store and business date again before saving.");
    }
    public void SelectTask(string id) => RefreshActionState();

    private void RefreshActionState()
    {
        var owner = accessProvider().CanAdminister;
        var selected = AccountingBatchGrid.SelectedItem as EtpApplication::Etp.Reporting.Application.Accounting.AccountingBatchSummary;
        PreviewTaskButton.IsEnabled = owner;
        SaveTaskButton.IsEnabled = owner && session.Current.Draft is not null;
        ApproveTaskButton.IsEnabled = owner && selected?.Status == "DRAFT";
        RejectTaskButton.IsEnabled = owner && selected?.Status is "DRAFT" or "BLOCKED" or "APPROVED_READY";
        TallyTaskButton.IsEnabled = owner && selected?.Status == "APPROVED_READY";
        foreach (var button in new[] { PreviewTaskButton, SaveTaskButton, ApproveTaskButton, RejectTaskButton, TallyTaskButton }) button.Visibility = Visibility.Visible;
        ActionGuidance.Text = !owner ? "Owner permission is required for Accounting."
            : selected is null ? "Select a saved batch to review, approve or reject. Enter a decision reason below."
            : selected.Status == "BLOCKED" ? $"Blocked: {selected.BlockingReason} Correct the setup, reject this batch with a reason, and prepare it again."
            : selected.Status == "EXPORTED_AWAITING_IMPORT" ? "File recorded below. Tally import and read-back have not been verified; this batch cannot be exported again."
            : selected.Status == "REJECTED" ? "Rejected. You can prepare a replacement for this day."
            : "Review the saved entries and enter a reason before approving or rejecting. Only an approved batch can be exported.";
    }
}
