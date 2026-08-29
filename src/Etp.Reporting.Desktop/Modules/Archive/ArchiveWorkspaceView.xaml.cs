extern alias EtpApplication;

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
    }

    public async Task RefreshAsync()
    {
        await RefreshReportArchiveAsync();
        await RefreshSharingContactsAsync();
    }

    private async void RefreshReportArchive_Click(object sender, RoutedEventArgs e) => await RefreshReportArchiveAsync();

    private async Task RefreshReportArchiveAsync()
    {
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
            ReportGenerationGrid.ItemsSource = rows;
            ReportArchiveDetailGrid.ItemsSource = null;
            SetStatus($"{rows.Count:N0} immutable generation(s) found. Select one to open or exactly two to compare.");
        }
        catch (Exception ex) { HandleFailure(ex, "REPORT_ARCHIVE_LOAD_FAILED", "Report archive could not be loaded"); }
    }

    private async void OpenArchivedGeneration_Click(object sender, RoutedEventArgs e)
    {
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

    private async void CompareArchivedGenerations_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess();
            var selected = ReportGenerationGrid.SelectedItems.OfType<ArchivedReportGenerationSummary>().ToArray();
            if (selected.Length != 2) throw new InvalidOperationException("Select exactly two report generations.");
            var rows = await session.CompareAsync(connectionStringProvider(), selected[0], selected[1]);
            ReportArchiveDetailGrid.ItemsSource = rows;
            SetStatus($"Compared generations {selected[0].GenerationNumber} and {selected[1].GenerationNumber}: {rows.Count(x => x.Changed):N0} report section(s) changed.");
            await auditRecorder("ReportArchive", "Succeeded", "Archived generations compared");
        }
        catch (Exception ex) { HandleFailure(ex, "GENERATION_COMPARISON_FAILED", "Generation comparison failed"); }
    }

    private async void ExportArchivedExcel_Click(object sender, RoutedEventArgs e)
    {
        if (exportInProgress) return;
        try
        {
            RequireViewAccess();
            var document = session.DocumentForExport(SelectedArchiveGeneration());
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
        if (exportInProgress) return;
        try
        {
            RequireViewAccess();
            var document = session.DocumentForExport(SelectedArchiveGeneration());
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
        try
        {
            RequireViewAccess();
            var generation = SelectedArchiveGeneration();
            var dialog = new SaveFileDialog { Filter = "ZIP report package (*.zip)|*.zip", FileName = $"ETP_ReportPack_{generation.BusinessDate:yyyy-MM-dd}_Gen{generation.GenerationNumber:D2}.zip", AddExtension = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            var result = await session.CreatePackageAsync(connectionStringProvider(), generation, dialog.FileName, accessProvider().DisplayName);
            SetStatus($"Immutable ZIP package created. SHA-256 {result.Sha256[..12]}…");
        }
        catch (Exception ex) { HandleFailure(ex, "ARCHIVED_ZIP_CREATE_FAILED", "Archived ZIP package was not created"); }
    }

    private async void ShareArchivedWhatsApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess();
            var generation = SelectedArchiveGeneration();
            var shareFile = session.ShareFileFor(generation);
            var message = $"ETP report pack for {generation.StoreCode}, {generation.BusinessDate:dd-MMM-yyyy}, generation {generation.GenerationNumber}. Please attach the prepared ZIP file.";
            shareLauncher.OpenWhatsApp(shareFile, message, SharePhoneInput.Text);
            await session.RecordAttemptAsync(connectionStringProvider(),
                new RecordDistributionAttempt(generation.Id, null, "WHATSAPP",
                    string.IsNullOrWhiteSpace(SharePhoneInput.Text) ? null : "Configured phone", shareFile, "INITIATED",
                    "WhatsApp opened; the user must attach and send the prepared file."));
            SetStatus("WhatsApp opened and the ZIP path was copied. Attach the highlighted file, then send it yourself.");
        }
        catch (Exception ex) { HandleFailure(ex, "WHATSAPP_SHARE_PREPARE_FAILED", "WhatsApp sharing was not prepared"); }
    }

    private async void ShareArchivedEmail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess();
            var generation = SelectedArchiveGeneration();
            var shareFile = session.ShareFileFor(generation);
            if (string.IsNullOrWhiteSpace(ShareEmailToInput.Text)) throw new InvalidOperationException("Enter the email recipient.");
            var policy = await session.ValidateEmailAttachmentAsync(connectionStringProvider(), generation);
            shareLauncher.OpenEmailDraft(policy.ShareFolderPath, shareFile, ShareEmailToInput.Text, ShareEmailCcInput.Text,
                $"ETP report pack - {generation.StoreCode} - {generation.BusinessDate:dd-MMM-yyyy}",
                $"Please find attached immutable ETP report generation {generation.GenerationNumber}.");
            await session.RecordAttemptAsync(connectionStringProvider(),
                new RecordDistributionAttempt(generation.Id, null, "EMAIL", "Configured recipient", shareFile, "INITIATED",
                    "Email draft opened; delivery is not claimed."));
            SetStatus("Email draft opened with the ZIP attached. Review recipients and click Send.");
        }
        catch (Exception ex) { HandleFailure(ex, "EMAIL_SHARE_PREPARE_FAILED", "Email sharing was not prepared"); }
    }

    private async Task RefreshSharingContactsAsync()
    {
        try
        {
            RequireViewAccess();
            SharingContactsGrid.ItemsSource = await session.LoadContactsAsync(connectionStringProvider());
        }
        catch (Exception ex) { HandleFailure(ex, "SHARING_CONTACTS_LOAD_FAILED", "Sharing contacts could not be loaded"); }
    }

    private void SharingContact_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SharingContactsGrid.SelectedItem is not SharingContact row) return;
        ContactNameInput.Text = row.DisplayName;
        ContactRoleInput.Text = row.ContactRole;
        ContactEmailInput.Text = row.EmailAddress;
        ContactPhoneInput.Text = row.PhoneE164;
        ContactSubscriptionsInput.Text = row.DefaultSubscriptions;
        ContactActiveInput.IsChecked = row.IsActive;
        SharePhoneInput.Text = row.PhoneE164;
        ShareEmailToInput.Text = row.EmailAddress;
    }

    private async void SaveSharingContact_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess();
            var current = SharingContactsGrid.SelectedItem as SharingContact;
            var row = new SharingContactDraft(current?.Id ?? 0, ContactNameInput.Text, ContactRoleInput.Text,
                ContactEmailInput.Text, ContactPhoneInput.Text, ContactSubscriptionsInput.Text, ContactActiveInput.IsChecked == true);
            var id = await session.SaveContactAsync(connectionStringProvider(), row, ContactReasonInput.Text);
            ContactReasonInput.Clear();
            SetStatus($"Sharing contact {id:N0} saved with audit history.");
            await RefreshSharingContactsAsync();
        }
        catch (Exception ex) { HandleFailure(ex, "SHARING_CONTACT_SAVE_FAILED", "Sharing contact was not saved"); }
    }

    private ArchivedReportGenerationSummary SelectedArchiveGeneration() =>
        ReportGenerationGrid.SelectedItems.OfType<ArchivedReportGenerationSummary>().SingleOrDefault()
        ?? throw new InvalidOperationException("Select exactly one report generation.");

    private void RequireViewAccess()
    {
        if (!accessProvider().CanView)
            throw new UnauthorizedAccessException("This Windows account does not have application access.");
    }

    private void RequireOwnerAccess()
    {
        if (!accessProvider().CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private void SetStatus(string message)
    {
        ReportArchiveStatus.Text = message;
        NotificationRequested?.Invoke(this, message);
    }

    private void HandleFailure(Exception exception, string eventId, string operation)
    {
        DesktopDiagnostics.Record(exception, "Archive.Workspace", eventId);
        SetStatus($"{operation}: {errorDescriber(exception)}");
    }
}
