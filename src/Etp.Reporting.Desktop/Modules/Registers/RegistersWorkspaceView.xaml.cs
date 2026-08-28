extern alias EtpApplication;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AccessSession = EtpApplication::Etp.Reporting.Application.Access.AccessSession;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;
using DigitalRegisterEntryDraft = EtpApplication::Etp.Reporting.Application.Registers.DigitalRegisterEntryDraft;

namespace Etp.Reporting.Desktop.Modules.Registers;

public sealed partial class RegistersWorkspaceView : UserControl
{
    private readonly RegistersPresentationSession session;
    private readonly Func<string> connectionStringProvider;
    private Func<AccessSession> accessProvider = () => new("unknown", "Unknown user", AccessRole.None, false);
    private Func<Exception, string> errorDescriber = exception => exception.Message;

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

    public Task RefreshAsync() => RefreshRegistersAsync();

    private async void RefreshRegisters_Click(object sender, RoutedEventArgs e) => await RefreshRegistersAsync();

    private async Task RefreshRegistersAsync()
    {
        try
        {
            RequireViewAccess();
            var rows = await session.RefreshAsync(connectionStringProvider(), RegisterSearchInput.Text);
            RegisterGrid.ItemsSource = rows;
            SetStatus($"{rows.Count:N0} audited register entry or entries found.");
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private async void SaveRegisterEntry_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();
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
                null,
                RegisterCounterpartyInput.Text,
                OptionalDecimal(RegisterQuantityInput.Text),
                OptionalDecimal(RegisterAmountInput.Text),
                RegisterReferenceInput.Text,
                accessProvider().DisplayName,
                "DRAFT",
                RegisterRemarksInput.Text);
            var id = await session.SaveAsync(connectionStringProvider(), entry, RegisterReasonInput.Text);
            SetStatus($"Register entry {id:N0} saved with audit history.");
            RegisterDocumentNumberInput.Clear();
            RegisterReasonInput.Clear();
            await RefreshRegistersAsync();
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
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
