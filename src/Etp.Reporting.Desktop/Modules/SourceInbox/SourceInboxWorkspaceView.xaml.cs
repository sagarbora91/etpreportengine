extern alias EtpApplication;

using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;
using AccessSession = EtpApplication::Etp.Reporting.Application.Access.AccessSession;
using SourceInboxDocument = EtpApplication::Etp.Reporting.Application.SourceInbox.SourceInboxDocument;
using SourceInboxService = EtpApplication::Etp.Reporting.Application.SourceInbox.ISourceInboxService;
using SourceDocumentExtraction = EtpApplication::Etp.Reporting.Application.SourceInbox.SourceDocumentExtraction;
using SourceDocumentIntakeRequest = EtpApplication::Etp.Reporting.Application.SourceInbox.SourceDocumentIntakeRequest;

namespace Etp.Reporting.Desktop.Modules.SourceInbox;

public sealed partial class SourceInboxWorkspaceView : UserControl
{
    private readonly Func<string, SourceInboxService> serviceFactory;
    private readonly Func<string> connectionStringProvider;
    private readonly ISourceDocumentLauncher documentLauncher;
    private Func<AccessSession> accessProvider = () => new("unknown", "Unknown user", AccessRole.None, false);
    private Func<Exception, string> errorDescriber = exception => exception.Message;

    public SourceInboxWorkspaceView(
        Func<string, SourceInboxService> serviceFactory,
        Func<string> connectionStringProvider,
        ISourceDocumentLauncher documentLauncher)
    {
        this.serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        this.documentLauncher = documentLauncher ?? throw new ArgumentNullException(nameof(documentLauncher));
        InitializeComponent();
        DocumentDateInput.SelectedDate = DateTime.Today;
    }

    public event EventHandler<string>? NotificationRequested;
    public event EventHandler<long?>? SelectedDocumentIdChanged;

    public DateTime? BusinessDate
    {
        get => DocumentDateInput.SelectedDate;
        set => DocumentDateInput.SelectedDate = value;
    }

    public void AttachHost(Func<AccessSession> accessProvider, Func<Exception, string> errorDescriber)
    {
        this.accessProvider = accessProvider ?? throw new ArgumentNullException(nameof(accessProvider));
        this.errorDescriber = errorDescriber ?? throw new ArgumentNullException(nameof(errorDescriber));
    }

    public Task RefreshAsync() => RefreshInboxAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshInboxAsync();

    private async Task RefreshInboxAsync()
    {
        try
        {
            RequireViewAccess();
            var status = SourceInboxPresentation.LifecycleStatus(SelectedContent(StatusInput));
            var rows = await Service().LoadDocumentsAsync(status);
            DocumentsGrid.ItemsSource = rows;
            SetStatus($"{rows.Count:N0} source document(s). Originals are retained and SHA-256 protected.");
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private async void Documents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DocumentsGrid.SelectedItem is not SourceInboxDocument document)
        {
            ExtractionsGrid.ItemsSource = null;
            SelectedDocumentIdChanged?.Invoke(this, null);
            return;
        }

        SelectedDocumentIdChanged?.Invoke(this, document.Id);
        try { ExtractionsGrid.ItemsSource = await Service().LoadExtractionsAsync(document.Id); }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Documents (*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp)|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) DocumentPathInput.Text = dialog.FileName;
    }

    private async void Intake_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();
            if (string.IsNullOrWhiteSpace(DocumentPathInput.Text))
                throw new InvalidOperationException("Select a PDF or image first.");
            var date = DocumentDateInput.SelectedDate is { } value ? DateOnly.FromDateTime(value) : (DateOnly?)null;
            var type = SelectedContent(DocumentTypeInput).Replace(' ', '_').ToUpperInvariant();
            SetStatus("Storing the original document and checking whether text extraction is needed…");
            var outcome = await Service().IntakeAsync(new SourceDocumentIntakeRequest(
                DocumentPathInput.Text,
                string.IsNullOrWhiteSpace(DocumentStoreInput.Text) ? null : DocumentStoreInput.Text.Trim(),
                date,
                type));
            var outcomeMessage = SourceInboxPresentation.IntakeOutcome(outcome);
            DocumentPathInput.Clear();
            await RefreshInboxAsync();
            DocumentsGrid.SelectedItem = DocumentsGrid.Items.OfType<SourceInboxDocument>().FirstOrDefault(row => row.Id == outcome.Document.Id);
            SetStatus(outcomeMessage);
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess();
            if (DocumentsGrid.SelectedItem is not SourceInboxDocument document)
                throw new InvalidOperationException("Select one Source Inbox document.");
            if (!await Service().VerifyIntegrityAsync(document))
                throw new InvalidOperationException("The managed source is missing or failed its SHA-256 integrity check. Do not use this copy; create a support package.");
            documentLauncher.Open(document.ManagedFilePath);
            SetStatus("Source integrity passed and the retained original was opened.");
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private async void Verify_Click(object sender, RoutedEventArgs e) => await ReviewExtractionAsync(true);
    private async void Reject_Click(object sender, RoutedEventArgs e) => await ReviewExtractionAsync(false);

    private async Task ReviewExtractionAsync(bool verified)
    {
        try
        {
            RequireImportAccess();
            if (ExtractionsGrid.SelectedItem is not SourceDocumentExtraction extraction)
                throw new InvalidOperationException("Select one extraction awaiting review.");
            await Service().ReviewExtractionAsync(extraction.Id, verified, ReviewReasonInput.Text);
            ReviewReasonInput.Clear();
            await RefreshInboxAsync();
            SetStatus(verified
                ? "Extraction verified by a human reviewer."
                : "Extraction rejected and the document quarantined.");
        }
        catch (Exception ex) { SetStatus(errorDescriber(ex)); }
    }

    private SourceInboxService Service() => serviceFactory(connectionStringProvider());

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
        StatusText.Text = message;
        NotificationRequested?.Invoke(this, message);
    }

    private static string SelectedContent(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item && !string.IsNullOrWhiteSpace(item.Content?.ToString())
            ? item.Content!.ToString()!
            : throw new InvalidOperationException("Select a value.");
}
