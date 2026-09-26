extern alias EtpApplication;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AccessSession = EtpApplication::Etp.Reporting.Application.Access.AccessSession;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;
using DigitalRegisterEntry = EtpApplication::Etp.Reporting.Application.Registers.DigitalRegisterEntry;
using DigitalRegisterEntryDraft = EtpApplication::Etp.Reporting.Application.Registers.DigitalRegisterEntryDraft;

namespace Etp.Reporting.Desktop.Modules.Registers;

public sealed partial class RegistersWorkspaceView : UserControl
{
    private readonly RegistersPresentationSession session;
    private readonly Func<string> connectionStringProvider;
    private Func<AccessSession> accessProvider = () => new("unknown", "Unknown user", AccessRole.None, false);
    private bool restoringSelection;
    private int registerRefreshRevision;
    private Func<Exception, string> errorDescriber = DesktopFriendlyError.Describe;

    public RegistersWorkspaceView(RegistersPresentationSession session, Func<string> connectionStringProvider)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        InitializeComponent();
        RegisterBusinessDateInput.SelectedDate = DateTime.Today;
    }

    public event EventHandler<string>? NotificationRequested;
    public long? LinkedSourceDocumentId { get; set; }

    public DateTime? BusinessDate
    {
        get => RegisterBusinessDateInput.SelectedDate;
        set => RegisterBusinessDateInput.SelectedDate = value;
    }

    public void AttachHost(Func<AccessSession> accessProvider, Func<Exception, string> errorDescriber)
    {
        this.accessProvider = accessProvider ?? throw new ArgumentNullException(nameof(accessProvider));
        this.errorDescriber = errorDescriber ?? throw new ArgumentNullException(nameof(errorDescriber));
    }

    public string StoreCode { get => RegisterStoreInput.Text; set => RegisterStoreInput.Text = value; }
    public void SearchFor(string documentNumber) => RegisterSearchInput.Text = documentNumber;

    public Task RefreshAsync() => RefreshRegistersAsync();

    private async void RefreshRegisters_Click(object sender, RoutedEventArgs e) => await RefreshRegistersAsync();

    private async Task RefreshRegistersAsync()
    {
        var revision = ++registerRefreshRevision;
        try
        {
            RequireViewAccess();
            VerifyRegisterButton.IsEnabled = accessProvider().CanAdminister;
            var registerType = SelectedContent(RegisterTypeInput).Replace(' ', '_').ToUpperInvariant();
            var rows = await session.RefreshAsync(connectionStringProvider(), RegisterSearchInput.Text, registerType: registerType);
            if (revision != registerRefreshRevision) return;
            var visible = rows.Where(x => x.RegisterType == registerType).ToArray();
            RegisterGrid.ItemsSource = visible;
            SetStatus($"{visible.Length:N0} audited register entry or entries found.");
        }
        catch (Exception ex) { if (revision != registerRefreshRevision) return; DesktopDiagnostics.Record(ex, "Registers.Workspace", "REGISTER_REFRESH_FAILED"); SetStatus(errorDescriber(ex)); }
    }

    private async void SaveRegisterEntry_Click(object sender, RoutedEventArgs e) => await SaveDraftAsync();

    public Task<bool> SaveDraftAsync() => SaveEntryAsync(false);

    public async Task<bool> SaveEntryAsync(bool verify)
    {
        if (savingDraft) return false;
        savingDraft = true; var enabled = IsEnabled; IsEnabled = false;
        try
        {
            RequireImportAccess();
            if (verify && !accessProvider().CanAdminister) throw new UnauthorizedAccessException("Owner permission is required to verify entries.");
            if (verify && RegisterGrid.SelectedItem is not DigitalRegisterEntry) throw new InvalidOperationException("Select a saved draft to verify.");
            if (RegisterBusinessDateInput.SelectedDate is null)
                throw new InvalidOperationException("Select the register business date.");
            if (string.IsNullOrWhiteSpace(RegisterStoreInput.Text) || string.IsNullOrWhiteSpace(RegisterDocumentNumberInput.Text))
                throw new InvalidOperationException("Enter the store and document number.");
            var entry = new DigitalRegisterEntryDraft(
                SelectedContent(RegisterTypeInput).Replace(' ', '_').ToUpperInvariant(),
                LinkedSourceDocumentId,
                RegisterStoreInput.Text,
                DateOnly.FromDateTime(RegisterBusinessDateInput.SelectedDate.Value),
                RegisterDocumentNumberInput.Text,
                (RegisterGrid.SelectedItem as DigitalRegisterEntry)?.DocumentDate,
                RegisterCounterpartyInput.Text,
                OptionalDecimal(RegisterQuantityInput.Text),
                OptionalDecimal(RegisterAmountInput.Text),
                RegisterReferenceInput.Text,
                accessProvider().DisplayName,
                verify ? "VERIFIED" : "DRAFT",
                RegisterRemarksInput.Text);
            var id = await session.SaveAsync(connectionStringProvider(), entry, RegisterReasonInput.Text);
            SetStatus($"Register entry {id:N0} saved with audit history.");
            ClearEntry();
            RegisterReasonInput.Clear();
            AcceptDraft();
            await RefreshRegistersAsync();
            return true;
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "Registers.Workspace", "REGISTER_SAVE_FAILED"); SetStatus(errorDescriber(ex)); return false; }
        finally { savingDraft = false; IsEnabled = enabled; }
    }

    private async void VerifyRegisterEntry_Click(object sender, RoutedEventArgs e) => await SaveEntryAsync(true);
    private void NewRegisterEntry_Click(object sender, RoutedEventArgs e)
    {
        if (HasUnsavedChanges && !ConfirmationSheet.Show(this, "Discard register changes", "Discard the current unsaved register changes and start a new entry?")) return;
        StartNewEntry(discardConfirmed: true);
    }

    public bool StartNewEntry(bool discardConfirmed = false)
    {
        if (savingDraft) return false;
        if (HasUnsavedChanges && !discardConfirmed)
        {
            SetStatus("The current register has unsaved changes. Save them or confirm discarding them before starting a new entry.");
            return false;
        }
        ClearEntry(); AcceptDraft();
        return true;
    }
    private void ClearEntry()
    {
        RegisterGrid.SelectedItem = null;
        foreach (var field in DraftFields.Where(x => x != RegisterStoreInput)) field.Clear();
        LinkedSourceDocumentId = null;
        RegisterStoreInput.IsEnabled = RegisterBusinessDateInput.IsEnabled = RegisterDocumentNumberInput.IsEnabled = true;
    }
    private void RegisterSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (restoringSelection || RegisterGrid.SelectedItem is not DigitalRegisterEntry row) return;
        if (HasUnsavedChanges)
        {
            restoringSelection = true;
            try { RegisterGrid.SelectedItem = e.RemovedItems.OfType<DigitalRegisterEntry>().FirstOrDefault(); }
            finally { restoringSelection = false; }
            SetStatus("Save or discard the current draft before selecting another entry.");
            return;
        }
        RegisterStoreInput.Text = row.StoreCode; BusinessDate = row.BusinessDate.ToDateTime(TimeOnly.MinValue);
        RegisterDocumentNumberInput.Text = row.DocumentNumber; RegisterCounterpartyInput.Text = row.Counterparty ?? "";
        RegisterQuantityInput.Text = row.Quantity?.ToString(CultureInfo.CurrentCulture) ?? "";
        RegisterAmountInput.Text = row.Amount?.ToString(CultureInfo.CurrentCulture) ?? "";
        RegisterReferenceInput.Text = row.Reference ?? ""; RegisterRemarksInput.Text = row.Remarks ?? "";
        RegisterReasonInput.Clear(); LinkedSourceDocumentId = row.SourceDocumentId;
        RegisterStoreInput.IsEnabled = RegisterBusinessDateInput.IsEnabled = RegisterDocumentNumberInput.IsEnabled = false;
        AcceptDraft();
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

    private void SetStatus(string message)
    {
        RegisterStatus.Text = message;
        NotificationRequested?.Invoke(this, message);
    }

    private static string SelectedContent(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item && !string.IsNullOrWhiteSpace(item.Content?.ToString())
            ? item.Content!.ToString()!
            : throw new InvalidOperationException("Select a value.");

    private static decimal? OptionalDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException("Enter a valid number or leave the field blank.");
    }
}
