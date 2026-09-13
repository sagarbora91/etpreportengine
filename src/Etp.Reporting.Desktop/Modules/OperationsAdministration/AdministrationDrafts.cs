using System.Windows.Controls;
namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

public partial class AdministrationWorkspaceView
{
    public bool IsBusy { get; private set; }
    private bool BeginSave() { if (IsBusy) return false; IsBusy = true; IsEnabled = false; return true; }
    private void EndSave() { IsBusy = false; IsEnabled = true; }
    private sealed record MasterDraft(string Code, string Name, string Reason, int Approval, bool Active);
    private readonly Dictionary<string, MasterDraft> masters = new();
    private readonly Dictionary<string, MasterDraft> masterBaselines = new();
    private string editingMaster = "Store";
    private MasterDraft CaptureMaster() => new(MasterCodeInput.Text, MasterNameInput.Text, MasterReasonInput.Text, MasterApprovalInput.SelectedIndex, MasterActiveInput.IsChecked == true);
    private static MasterDraft EmptyMaster => new("", "", "", 0, true);
    private void ApplyMaster(MasterDraft value)
    {
        MasterCodeInput.Text = value.Code; MasterNameInput.Text = value.Name; MasterReasonInput.Text = value.Reason;
        MasterApprovalInput.SelectedIndex = value.Approval; MasterActiveInput.IsChecked = value.Active;
    }
    private void RetainMasterType()
    {
        if (MasterReasonInput is null) return;
        masters[editingMaster] = CaptureMaster(); editingMaster = SelectedContent(MasterTypeInput);
        ApplyMaster(masters.GetValueOrDefault(editingMaster) ?? EmptyMaster);
    }
    private sealed record UserDraft(string Identity, string Name, string Reason, int Role, bool Active);
    private UserDraft userBaseline = new("", "", "", 1, true);
    private UserDraft CaptureUser() => new(UserIdentityInput.Text, UserDisplayNameInput.Text, UserReasonInput.Text, UserRoleInput.SelectedIndex, UserActiveInput.IsChecked == true);
    public IReadOnlyList<string> UnsavedDrafts
    {
        get
        {
            masters[editingMaster] = CaptureMaster();
            return masters.Where(pair => pair.Value != (masterBaselines.GetValueOrDefault(pair.Key) ?? EmptyMaster)).Select(pair => "Master: " + pair.Key)
                .Concat(CaptureUser() != userBaseline ? new[] { "User access" } : Array.Empty<string>()).ToArray();
        }
    }
    public void DiscardDraft(string name)
    {
        if (name == "User access")
        {
            UserIdentityInput.Text = userBaseline.Identity; UserDisplayNameInput.Text = userBaseline.Name; UserReasonInput.Text = userBaseline.Reason;
            UserRoleInput.SelectedIndex = userBaseline.Role; UserActiveInput.IsChecked = userBaseline.Active; return;
        }
        var type = name[8..]; var baseline = masterBaselines.GetValueOrDefault(type) ?? EmptyMaster;
        masters[type] = baseline; if (editingMaster == type) ApplyMaster(baseline);
    }
    public async Task<bool> SaveDraftAsync(string name)
    {
        if (name == "User access") return await SaveUserDraftAsync();
        var type = name[8..];
        MasterTypeInput.SelectedItem = MasterTypeInput.Items.OfType<ComboBoxItem>().First(item => item.Content?.ToString() == type);
        return await SaveMasterDraftAsync();
    }
}
