extern alias EtpApplication;
using SharingContact = EtpApplication::Etp.Reporting.Application.Sharing.SharingContact;
namespace Etp.Reporting.Desktop.Modules.Archive;

public sealed partial class ArchiveWorkspaceView
{
    private readonly WorkspaceOperationGate operationGate = new();
    public bool IsBusy => operationGate.IsBusy || exportInProgress;
    private sealed record ContactDraft(string Name, string Role, string Email, string Phone, string Subscriptions, bool Active, string Reason);
    private readonly Dictionary<int, ContactDraft> contactDrafts = new();
    private readonly Dictionary<int, ContactDraft> contactBaselines = new();
    private int editingContact;
    private bool applyingContacts;
    private static ContactDraft EmptyContact => new("", "", "", "", "", true, "");
    private ContactDraft CaptureContact() => new(ContactNameInput.Text, ContactRoleInput.Text, ContactEmailInput.Text,
        ContactPhoneInput.Text, ContactSubscriptionsInput.Text, ContactActiveInput.IsChecked == true, ContactReasonInput.Text);
    private void ApplyContact(ContactDraft value)
    {
        ContactNameInput.Text = value.Name; ContactRoleInput.Text = value.Role; ContactEmailInput.Text = value.Email;
        ContactPhoneInput.Text = value.Phone; ContactSubscriptionsInput.Text = value.Subscriptions;
        ContactActiveInput.IsChecked = value.Active; ContactReasonInput.Text = value.Reason;
    }
    private void RememberContact()
    {
        var current = CaptureContact();
        if (current == (contactBaselines.GetValueOrDefault(editingContact) ?? EmptyContact)) contactDrafts.Remove(editingContact);
        else contactDrafts[editingContact] = current;
    }
    public IReadOnlyList<int> UnsavedContacts { get { RememberContact(); return contactDrafts.Keys.Order().ToArray(); } }
    private void SelectContact()
    {
        if (applyingContacts) return;
        RememberContact(); var row = SharingContactsGrid.SelectedItem as SharingContact;
        editingContact = row?.Id ?? 0;
        var baseline = row is null ? EmptyContact : new ContactDraft(row.DisplayName, row.ContactRole ?? "", row.EmailAddress ?? "", row.PhoneE164 ?? "", row.DefaultSubscriptions ?? "", row.IsActive, "");
        contactBaselines[editingContact] = baseline;
        ApplyContact(contactDrafts.GetValueOrDefault(editingContact) ?? baseline);
        if (row is not null) { SharePhoneInput.Text = row.PhoneE164; ShareEmailToInput.Text = row.EmailAddress; }
    }
    private void ApplyContacts(IReadOnlyList<SharingContact> rows)
    {
        RememberContact(); var selected = editingContact;
        applyingContacts = true;
        try { SharingContactsGrid.ItemsSource = rows; SharingContactsGrid.SelectedItem = rows.FirstOrDefault(row => row.Id == selected); }
        finally { applyingContacts = false; }
        // Do not associate a missing record's draft with a new contact.
        if (selected != 0 && SharingContactsGrid.SelectedItem is null) return;
        SelectContact();
    }
    public void DiscardContactDraft(int id)
    {
        contactDrafts.Remove(id);
        if (editingContact == id) ApplyContact(contactBaselines.GetValueOrDefault(id) ?? EmptyContact);
    }
    public async Task<bool> SaveContactDraftAsync(int id)
    {
        if (id != editingContact)
        {
            var row = SharingContactsGrid.Items.OfType<SharingContact>().FirstOrDefault(value => value.Id == id);
            if (id != 0 && row is null) { SetStatus("This contact is no longer available. Keep or discard its draft."); return false; }
            SharingContactsGrid.SelectedItem = row;
        }
        return await SaveContactDraftAsync();
    }
}
