using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Modules.Registers;

public sealed partial class RegistersWorkspaceView
{
    private string? activeRegisterTask;
    private bool savingDraft;
    public bool IsBusy => savingDraft;
    private sealed record RegisterDraft(string[] Values, DateTime? Date, long? Source);
    private readonly Dictionary<string, RegisterDraft> registerDrafts = new();
    private readonly Dictionary<string, RegisterDraft> savedDrafts = new();
    private TextBox[] DraftFields => [RegisterDocumentNumberInput, RegisterCounterpartyInput, RegisterQuantityInput, RegisterAmountInput, RegisterReferenceInput, RegisterRemarksInput, RegisterReasonInput, RegisterStoreInput];

    private RegisterDraft CaptureDraft() => new(DraftFields.Select(input => input.Text).ToArray(), BusinessDate, LinkedSourceDocumentId);
    public bool HasUnsavedChanges => activeRegisterTask is not null &&
        (savedDrafts.TryGetValue(activeRegisterTask, out var baseline)
            ? !DraftFields.Select(input => input.Text).SequenceEqual(baseline.Values) || BusinessDate != baseline.Date || LinkedSourceDocumentId != baseline.Source
            : DraftFields.Any(input => input.Text.Length > 0));
    private void AcceptDraft() { if (activeRegisterTask is not null) savedDrafts[activeRegisterTask] = CaptureDraft(); }
    public void DiscardDraft()
    {
        var baseline = activeRegisterTask is null ? null : savedDrafts.GetValueOrDefault(activeRegisterTask);
        for (var i = 0; i < DraftFields.Length; i++) DraftFields[i].Text = baseline?.Values[i] ?? string.Empty;
        if (baseline is not null) { BusinessDate = baseline.Date; LinkedSourceDocumentId = baseline.Source; }
    }

    public void SelectTask(string taskId)
    {
        if (activeRegisterTask != taskId)
        {
            if (activeRegisterTask is not null) registerDrafts[activeRegisterTask] = new(DraftFields.Select(x => x.Text).ToArray(), BusinessDate, LinkedSourceDocumentId);
            RegisterGrid.ItemsSource = null;
            var draft = registerDrafts.GetValueOrDefault(taskId);
            for (var i = 0; i < DraftFields.Length; i++) DraftFields[i].Text = draft?.Values[i] ?? string.Empty;
            if (draft is not null) { BusinessDate = draft.Date; LinkedSourceDocumentId = draft.Source; }
            RegisterStoreInput.IsEnabled = RegisterBusinessDateInput.IsEnabled = RegisterDocumentNumberInput.IsEnabled = true;
            activeRegisterTask = taskId;
            if (!savedDrafts.ContainsKey(taskId)) AcceptDraft();
        }
        var type = taskId switch { "register-courier" => "Courier", "register-outward" => "Outward", "register-credit" => "Credit Note", "register-service" => "Service Receipt", "register-transfer" => "Stock Transfer", "register-expense" => "Expense", "register-vendor" => "Vendor Invoice", _ => "Inward" };
        RegisterTypeInput.SelectedItem = RegisterTypeInput.Items.OfType<ComboBoxItem>().First(x => x.Content?.ToString() == type);
        RegisterTypeInput.IsEnabled = false;
        _ = RefreshRegistersAsync();
    }

}
