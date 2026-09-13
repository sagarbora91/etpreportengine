using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Modules.Registers;

public sealed partial class RegistersWorkspaceView
{
    private string? activeRegisterTask;
    private bool savingDraft;
    public bool IsBusy => savingDraft;
    private sealed record RegisterDraft(string[] Values, DateTime? Date, long? Source);
    private readonly Dictionary<string, RegisterDraft> registerDrafts = new();
    private readonly Dictionary<string, string[]> savedDrafts = new();
    private TextBox[] DraftFields => [RegisterDocumentNumberInput, RegisterCounterpartyInput, RegisterQuantityInput, RegisterAmountInput, RegisterReferenceInput, RegisterRemarksInput, RegisterReasonInput, RegisterStoreInput];

    public bool HasUnsavedChanges => activeRegisterTask is not null && !DraftFields.Select(x => x.Text).SequenceEqual(savedDrafts.GetValueOrDefault(activeRegisterTask) ?? new string[DraftFields.Length].Select(_ => string.Empty));
    private void AcceptDraft() { if (activeRegisterTask is not null) savedDrafts[activeRegisterTask] = DraftFields.Select(x => x.Text).ToArray(); }
    public void DiscardDraft()
    {
        var values = activeRegisterTask is null ? null : savedDrafts.GetValueOrDefault(activeRegisterTask);
        for (var i = 0; i < DraftFields.Length; i++) DraftFields[i].Text = values?[i] ?? string.Empty;
    }

    public void SelectTask(string taskId)
    {
        if (activeRegisterTask != taskId)
        {
            if (activeRegisterTask is not null) registerDrafts[activeRegisterTask] = new(DraftFields.Select(x => x.Text).ToArray(), BusinessDate, LinkedSourceDocumentId);
            var draft = registerDrafts.GetValueOrDefault(taskId);
            for (var i = 0; i < DraftFields.Length; i++) DraftFields[i].Text = draft?.Values[i] ?? string.Empty;
            if (draft is not null) { BusinessDate = draft.Date; LinkedSourceDocumentId = draft.Source; }
            activeRegisterTask = taskId;
        }
        var type = taskId switch { "register-outward" => "Outward", "register-credit" => "Credit Note", "register-service" => "Service Receipt", "register-transfer" => "Stock Transfer", "register-expense" => "Expense", "register-vendor" => "Vendor Invoice", _ => "Inward" };
        RegisterTypeInput.SelectedItem = RegisterTypeInput.Items.OfType<ComboBoxItem>().First(x => x.Content?.ToString() == type);
        RegisterTypeInput.IsEnabled = false;
        _ = RefreshRegistersAsync();
    }

}
