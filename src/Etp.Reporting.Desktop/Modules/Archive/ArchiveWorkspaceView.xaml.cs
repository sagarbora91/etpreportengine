extern alias EtpApplication;

using System.IO;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Reporting;
using Microsoft.Win32;
using AccessSession = EtpApplication::Etp.Reporting.Application.Access.AccessSession;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;
using ArchivedReportGenerationSummary = EtpApplication::Etp.Reporting.Application.Archive.ArchivedReportGenerationSummary;
using ReportArchiveSearch = EtpApplication::Etp.Reporting.Application.Archive.ReportArchiveSearch;
using RecordDistributionAttempt = EtpApplication::Etp.Reporting.Application.Distribution.RecordDistributionAttempt;
using SharingContact = EtpApplication::Etp.Reporting.Application.Sharing.SharingContact;
using SharingContactDraft = EtpApplication::Etp.Reporting.Application.Sharing.SharingContactDraft;

namespace Etp.Reporting.Desktop.Modules.Archive;

public delegate Task ExportArchivedReportAsync(string filePath, ReportPackDocument document);

public sealed partial class ArchiveWorkspaceView : UserControl
{
    private readonly ArchiveDistributionPresentationSession session;
    private readonly Func<string> connectionStringProvider;
    private readonly ExportArchivedReportAsync exportExcelAsync;
    private readonly ExportArchivedReportAsync exportPdfAsync;
    private readonly IArchiveShareLauncher shareLauncher;
    private Func<AccessSession> accessProvider = () => new("unknown", "Unknown user", AccessRole.None, false);
    private Func<string, string, string, Task> auditRecorder = (_, _, _) => Task.CompletedTask;
    private Func<Exception, string> errorDescriber = DesktopFriendlyError.Describe;
    private bool exportInProgress;

    public ArchiveWorkspaceView(
        ArchiveDistributionPresentationSession session,
        Func<string> connectionStringProvider,
        ExportArchivedReportAsync exportExcelAsync,
        ExportArchivedReportAsync exportPdfAsync,
        IArchiveShareLauncher shareLauncher)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        this.exportExcelAsync = exportExcelAsync ?? throw new ArgumentNullException(nameof(exportExcelAsync));
        this.exportPdfAsync = exportPdfAsync ?? throw new ArgumentNullException(nameof(exportPdfAsync));
        this.shareLauncher = shareLauncher ?? throw new ArgumentNullException(nameof(shareLauncher));
        InitializeComponent();
        ReportGenerationGrid.RowHeight = double.NaN;
        ArchiveDateInput.SelectedDate = DateTime.Today.AddDays(-1);
    }

    public event EventHandler<string>? NotificationRequested;

    public DateTime? BusinessDate
    {
        get => ArchiveDateInput.SelectedDate;
        set => ArchiveDateInput.SelectedDate = value;
    }

    public void AttachHost(
        Func<AccessSession> accessProvider,
        Func<string, string, string, Task> auditRecorder,
        Func<Exception, string> errorDescriber)
    {
        this.accessProvider = accessProvider ?? throw new ArgumentNullException(nameof(accessProvider));
        this.auditRecorder = auditRecorder ?? throw new ArgumentNullException(nameof(auditRecorder));
        this.errorDescriber = errorDescriber ?? throw new ArgumentNullException(nameof(errorDescriber));
        TestSmtpButton.IsEnabled = accessProvider().CanAdminister;
    }

    public async Task RefreshAsync()
    {
        TestSmtpButton.IsEnabled = accessProvider().CanAdminister;
        await RefreshReportArchiveAsync();
        await RefreshSharingContactsAsync();
    }

    private async void RefreshReportArchive_Click(object sender, RoutedEventArgs e) => await RefreshReportArchiveAsync();

    private async Task RefreshReportArchiveAsync()
    {
        var revision = ++archiveRefreshRevision;
        try
        {
            RequireViewAccess();
            var store = ArchiveStoreInput.SelectedItem is ComboBoxItem item &&
                        !string.Equals(item.Content?.ToString(), "All", StringComparison.OrdinalIgnoreCase)
                ? item.Content?.ToString()
                : null;
            var date = ArchiveAllDatesInput.IsChecked == true || ArchiveDateInput.SelectedDate is null
                ? (DateOnly?)null
                : DateOnly.FromDateTime(ArchiveDateInput.SelectedDate.Value);
            var rows = await session.SearchAsync(connectionStringProvider(), new ReportArchiveSearch(store, date));
            if (revision != archiveRefreshRevision) return;
            archiveRows = rows; ApplyArchiveFilter();
            ReportArchiveDetailGrid.ItemsSource = null;
            SetStatus($"{rows.Count:N0} saved pack(s) found. Use the actions on any row to open, export or share it.");
        }
        catch (Exception ex) { if (revision != archiveRefreshRevision) return; HandleFailure(ex, "REPORT_ARCHIVE_LOAD_FAILED", "Report archive could not be loaded"); }
    }

    private async void OpenArchivedGeneration_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireViewAccess();
            if (ReportGenerationGrid.SelectedItem is not ArchivedReportGenerationSummary generation)
                throw new InvalidOperationException("Select one report generation.");
            var opened = await session.OpenAsync(connectionStringProvider(), generation);
            ReportArchiveDetailGrid.ItemsSource = opened.Sections;
            SetStatus($"Generation {generation.GenerationNumber} passed its document SHA-256 check and is ready to re-export.");
            await auditRecorder("ReportArchive", "Succeeded", "Archived report opened");
        }
        catch (Exception ex) { HandleFailure(ex, "ARCHIVED_GENERATION_OPEN_FAILED", "Archived generation could not be opened"); }
    }

    private async void ExportArchivedExcel_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        if (exportInProgress) return;
        try
        {
            RequireViewAccess();
            var document = (await session.OpenAsync(connectionStringProvider(), SelectedArchiveGeneration())).Document;
            var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = $"ETP_Archived_Pack_{document.DateTo:yyyyMMdd}.xlsx", AddExtension = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            exportInProgress = true;
            await exportExcelAsync(dialog.FileName, document);
            SetStatus($"Archived Excel pack saved to {dialog.FileName}");
            await auditRecorder("ExportExcel", "Succeeded", "Archived report pack exported");
        }
        catch (Exception ex) { HandleFailure(ex, "ARCHIVED_EXCEL_EXPORT_FAILED", "Archived Excel export failed"); }
        finally { exportInProgress = false; }
    }

    private async void ExportArchivedPdf_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        if (exportInProgress) return;
        try
        {
            RequireViewAccess();
            var document = (await session.OpenAsync(connectionStringProvider(), SelectedArchiveGeneration())).Document;
            var dialog = new SaveFileDialog { Filter = "PDF report (*.pdf)|*.pdf", FileName = $"ETP_Archived_Pack_{document.DateTo:yyyyMMdd}.pdf", AddExtension = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            exportInProgress = true;
            await exportPdfAsync(dialog.FileName, document);
            SetStatus($"Archived PDF pack saved to {dialog.FileName}");
            await auditRecorder("ExportPdf", "Succeeded", "Archived report pack exported");
        }
        catch (Exception ex) { HandleFailure(ex, "ARCHIVED_PDF_EXPORT_FAILED", "Archived PDF export failed"); }
        finally { exportInProgress = false; }
    }

    private async void ExportArchivedZip_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireViewAccess();
            var generation = SelectedArchiveGeneration();
            var dialog = new SaveFileDialog { Filter = "ZIP report package (*.zip)|*.zip", FileName = $"ETP_ReportPack_{generation.BusinessDate:yyyy-MM-dd}_Gen{generation.GenerationNumber:D2}.zip", AddExtension = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            await session.OpenAsync(connectionStringProvider(), generation);
            var result = await session.CreatePackageAsync(connectionStringProvider(), generation, dialog.FileName, accessProvider().DisplayName);
            SetStatus($"saved ZIP package created. SHA-256 {result.Sha256[..12]}…");
        }
        catch (Exception ex) { HandleFailure(ex, "ARCHIVED_ZIP_CREATE_FAILED", "Archived ZIP package was not created"); }
    }

    private async void ShareArchivedWhatsApp_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireViewAccess();
            var generation = SelectedArchiveGeneration();
            var shareFile = await session.PreparePdfAsync(connectionStringProvider(), generation.Id);
            var attemptKey = Guid.NewGuid();
            await session.RecordAttemptAsync(connectionStringProvider(), new RecordDistributionAttempt(generation.Id, null,
                "WHATSAPP", "Configured phone", shareFile, "INITIATED", "Preparing the manual WhatsApp handoff; no delivery is claimed.", attemptKey));
            try
            {
                var message = $"ETP report pack for {generation.StoreCode}, {generation.BusinessDate:dd-MMM-yyyy}, generation {generation.GenerationNumber}. Please attach the prepared PDF.";
                shareLauncher.OpenWhatsApp(shareFile, message, SharePhoneInput.Text);
            }
            catch
            {
                await session.RecordAttemptAsync(connectionStringProvider(), new RecordDistributionAttempt(generation.Id, null,
                    "WHATSAPP", "Configured phone", shareFile, "FAILED", "WhatsApp handoff could not be completed. Install WhatsApp Desktop and retry; no delivery is claimed.", attemptKey));
                throw;
            }
            await session.RecordAttemptAsync(connectionStringProvider(), new RecordDistributionAttempt(generation.Id, null,
                "WHATSAPP", "Configured phone", shareFile, "HANDOFF_READY", "PDF path copied and WhatsApp Desktop opened. Attach and send it manually; delivery is not confirmed.", attemptKey));
            SetStatus("WhatsApp Desktop opened and the PDF path was copied. Paste that path into the attachment chooser, then send the PDF yourself. Delivery is not confirmed.");
            await RefreshSharingHistoryAsync(generation.Id);
        }
        catch (Exception ex) { HandleFailure(ex, "WHATSAPP_SHARE_PREPARE_FAILED", "WhatsApp sharing was not prepared"); }
    }

    private async void ShareArchivedEmail_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireViewAccess();
            var generation = SelectedArchiveGeneration();
            if (string.IsNullOrWhiteSpace(ShareEmailToInput.Text)) throw new InvalidOperationException("Enter the email recipient.");
            if (!ConfirmationSheet.Show(this, "Send report email", $"Send generation {generation.GenerationNumber} as a PDF to {ShareEmailToInput.Text}" +
                (string.IsNullOrWhiteSpace(ShareEmailCcInput.Text) ? "?" : $" (CC: {ShareEmailCcInput.Text})?"))) return;
            var shareFile = await session.PreparePdfAsync(connectionStringProvider(), generation.Id);
            var result = await session.SendEmailAsync(connectionStringProvider(), new(generation.Id, shareFile, ShareEmailToInput.Text, ShareEmailCcInput.Text));
            SetStatus(result.Message);
            await RefreshSharingHistoryAsync(generation.Id);
        }
        catch (Exception ex) { HandleFailure(ex, "EMAIL_SHARE_FAILED", "Email sharing did not finish"); }
    }

    private async void TestEmail_Click(object sender, RoutedEventArgs e)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireOwnerAccess();
            if (string.IsNullOrWhiteSpace(ShareEmailToInput.Text)) throw new InvalidOperationException("Enter the test email recipient above.");
            if (!ConfirmationSheet.Show(this, "Send test email", $"Send one test email with no report attachment to {ShareEmailToInput.Text}?")) return;
            SetStatus((await session.TestEmailAsync(connectionStringProvider(), ShareEmailToInput.Text)).Message);
        }
        catch (Exception ex) { HandleFailure(ex, "SMTP_TEST_FAILED", "SMTP test did not finish"); }
    }

    private async void SaveSmtpCredentials_Click(object sender, RoutedEventArgs e) => await SaveSmtpCredentialsAsync(false);
    private async void ClearSmtpCredentials_Click(object sender, RoutedEventArgs e) => await SaveSmtpCredentialsAsync(true);
    private async Task SaveSmtpCredentialsAsync(bool clear)
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return;
        try
        {
            RequireViewAccess();
            await session.SaveSmtpCredentialsAsync(connectionStringProvider(), clear ? "" : SmtpUserInput.Text, clear ? "" : SmtpPasswordInput.Password);
            SmtpPasswordInput.Clear();
            if (clear) SmtpUserInput.Clear();
            SetStatus(clear ? "This Windows user's saved SMTP credentials were cleared." : "SMTP credentials saved, protected for this Windows user. They apply to the current saved host, port and sender.");
        }
        catch (Exception ex) { HandleFailure(ex, "SMTP_CREDENTIAL_SAVE_FAILED", "SMTP credentials were not saved"); }
    }

    private async void ArchiveGeneration_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReportGenerationGrid.SelectedItem is ArchivedReportGenerationSummary row) await RefreshSharingHistoryAsync(row.Id);
        else ShareHistoryGrid.ItemsSource = null;
    }
    private async Task RefreshSharingHistoryAsync(long generationId)
    {
        try
        {
            var rows = await session.LoadHistoryAsync(connectionStringProvider(), generationId);
            if (ReportGenerationGrid.SelectedItem is ArchivedReportGenerationSummary selected && selected.Id == generationId)
                ShareHistoryGrid.ItemsSource = rows;
        }
        catch (Exception ex) { HandleFailure(ex, "SHARE_HISTORY_LOAD_FAILED", "Sharing history could not be loaded"); }
    }

    private void ArchiveRowAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ArchivedReportGenerationSummary row, Tag: string action }) return;
        ReportGenerationGrid.SelectedItem = row;
        switch (action)
        {
            case "Open": OpenArchivedGeneration_Click(sender, e); break;
            case "Excel": ExportArchivedExcel_Click(sender, e); break;
            case "PDF": ExportArchivedPdf_Click(sender, e); break;
            case "ZIP": ExportArchivedZip_Click(sender, e); break;
            case "Share":
                SetStatus($"Sharing generation {row.GenerationNumber}. Select a contact or enter a recipient, then choose WhatsApp or Send email. A PDF is prepared automatically.");
                RevealShareRecipient(); break;
        }
    }

    internal void RevealShareRecipient()
    {
        for (DependencyObject? current = ShareEmailToInput; current is not null; current = LogicalTreeHelper.GetParent(current))
            if (current is TabItem tab) tab.IsSelected = true;
        ShareEmailToInput.BringIntoView(); ShareEmailToInput.Focus();
    }

    private void NewContact_Click(object sender, RoutedEventArgs e)
    {
        RememberContact(); SharingContactsGrid.SelectedItem = null; SelectContact(); ContactNameInput.Focus();
    }

    private async Task RefreshSharingContactsAsync()
    {
        try
        {
            RequireViewAccess();
            ApplyContacts(await session.LoadContactsAsync(connectionStringProvider()));
        }
        catch (Exception ex) { HandleFailure(ex, "SHARING_CONTACTS_LOAD_FAILED", "Sharing contacts could not be loaded"); }
    }

    private void ShareContactPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShareContactPicker.SelectedItem is SharingContact contact)
        { SharePhoneInput.Text = contact.PhoneE164 ?? ""; ShareEmailToInput.Text = contact.EmailAddress ?? ""; }
    }

    private void SharingContact_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectContact();
    }

    private async void SaveSharingContact_Click(object sender, RoutedEventArgs e) => await SaveContactDraftAsync();
    public async Task<bool> SaveContactDraftAsync()
    {
        using var operation = operationGate.TryEnter(this); if (operation is null) return false;
        try
        {
            RequireOwnerAccess();
            var row = new SharingContactDraft(editingContact, ContactNameInput.Text, ContactRoleInput.Text,
                ContactEmailInput.Text, ContactPhoneInput.Text, ContactSubscriptionsInput.Text, ContactActiveInput.IsChecked == true);
            var id = await session.SaveContactAsync(connectionStringProvider(), row, ContactReasonInput.Text);
            ContactReasonInput.Clear();
            contactDrafts.Remove(editingContact); editingContact = id; contactBaselines[id] = CaptureContact();
            SetStatus($"Sharing contact {id:N0} saved with audit history.");
            await RefreshSharingContactsAsync();
            return true;
        }
        catch (Exception ex) { HandleFailure(ex, "SHARING_CONTACT_SAVE_FAILED", "Sharing contact was not saved"); return false; }
    }
    private void HandleFailure(Exception exception, string eventId, string operation)
    {
        DesktopDiagnostics.Record(exception, "Archive.Workspace", eventId);
        SetStatus($"{operation}: {errorDescriber(exception)}");
    }
}
