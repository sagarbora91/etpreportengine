extern alias EtpApplication;

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;
using Microsoft.Win32;
using ArchivedReportGenerationSummary = EtpApplication::Etp.Reporting.Application.Archive.ArchivedReportGenerationSummary;
using DigitalRegisterEntryDraft = EtpApplication::Etp.Reporting.Application.Registers.DigitalRegisterEntryDraft;
using SharingContact = EtpApplication::Etp.Reporting.Application.Sharing.SharingContact;
using SharingContactDraft = EtpApplication::Etp.Reporting.Application.Sharing.SharingContactDraft;
using SourceInboxDocument = EtpApplication::Etp.Reporting.Application.SourceInbox.SourceInboxDocument;
using SourceDocumentExtraction = EtpApplication::Etp.Reporting.Application.SourceInbox.SourceDocumentExtraction;
using SourceDocumentIntakeRequest = EtpApplication::Etp.Reporting.Application.SourceInbox.SourceDocumentIntakeRequest;

namespace Etp.Reporting.Desktop;

public partial class MainWindow
{
    private string? currentShareFile;
    private AccountingBatchDraft? currentAccountingDraft;
    private long? currentAccountingReportGenerationId;

    private async void RefreshSourceInbox_Click(object sender, RoutedEventArgs e) => await RefreshSourceInboxAsync();

    private async void SourceInbox_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(SourceInboxGrid.SelectedItem is not SourceInboxDocument row){DocumentExtractionGrid.ItemsSource=null;return;}
        try{DocumentExtractionGrid.ItemsSource=await sourceInboxServiceFactory(connectionState.ConnectionString).LoadExtractionsAsync(row.Id);}
        catch(Exception ex){SourceInboxStatus.Text=FriendlyError(ex);}
    }

    private async void VerifyExtraction_Click(object sender,RoutedEventArgs e)=>await ReviewExtractionAsync(true);
    private async void RejectExtraction_Click(object sender,RoutedEventArgs e)=>await ReviewExtractionAsync(false);
    private async Task ReviewExtractionAsync(bool verified)
    {
        try{RequireImportAccess();if(DocumentExtractionGrid.SelectedItem is not SourceDocumentExtraction row)throw new InvalidOperationException("Select one extraction awaiting review.");await sourceInboxServiceFactory(connectionState.ConnectionString).ReviewExtractionAsync(row.Id,verified,ExtractionReviewReasonInput.Text);ExtractionReviewReasonInput.Clear();await RefreshSourceInboxAsync();SourceInboxStatus.Text=verified?"Extraction verified by a human reviewer.":"Extraction rejected and the document quarantined.";}
        catch(Exception ex){SourceInboxStatus.Text=FriendlyError(ex);}
    }

    private async void SaveProductSettings_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess();if(!int.TryParse(MaximumAttachmentInput.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var maximum))throw new InvalidOperationException("Enter a valid maximum attachment size in MB.");
            int? port=null;if(!string.IsNullOrWhiteSpace(SmtpPortInput.Text)){if(!int.TryParse(SmtpPortInput.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var parsedPort))throw new InvalidOperationException("Enter a valid SMTP port.");port=parsedPort;}
            var settings=new ProductSettings(DocumentRepositoryInput.Text,ShareFolderInput.Text,OcrHelperInput.Text,OcrModelInput.Text,SmtpHostInput.Text,port,true,SmtpFromInput.Text,maximum,DateTime.MinValue,currentAccess.WindowsIdentity);
            await new ProductisationRepository(connectionState.ConnectionString).SaveSettingsAsync(settings,ProductSettingsReasonInput.Text);ProductSettingsReasonInput.Clear();
            ConnectionResult.Text="Product integration settings saved and audited.";await RefreshMasterAdministrationAsync();
        }
        catch(Exception ex){ConnectionResult.Text=FriendlyError(ex);}
    }

    private async Task RefreshSourceInboxAsync()
    {
        try
        {
            RequireViewAccess();
            var selected = SelectedContent(SourceInboxStatusInput);
            var status = selected.Equals("All", StringComparison.OrdinalIgnoreCase) ? null : selected.Replace(' ', '_').ToUpperInvariant();
            var rows = await sourceInboxServiceFactory(connectionState.ConnectionString).LoadDocumentsAsync(status);
            SourceInboxGrid.ItemsSource = rows;
            SourceInboxStatus.Text = $"{rows.Count:N0} source document(s). Originals are retained and SHA-256 protected.";
        }
        catch (Exception ex) { SourceInboxStatus.Text = FriendlyError(ex); }
    }

    private void BrowseSourceDocument_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Documents (*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp)|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp", Multiselect = false };
        if (dialog.ShowDialog(this) == true) SourceDocumentPathInput.Text = dialog.FileName;
    }

    private async void OpenSourceDocument_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess();if(SourceInboxGrid.SelectedItem is not SourceInboxDocument row)throw new InvalidOperationException("Select one Source Inbox document.");
            if(!await sourceInboxServiceFactory(connectionState.ConnectionString).VerifyIntegrityAsync(row))throw new InvalidOperationException("The managed source is missing or failed its SHA-256 integrity check. Do not use this copy; create a support package.");
            Process.Start(new ProcessStartInfo(row.ManagedFilePath){UseShellExecute=true});SourceInboxStatus.Text="Source integrity passed and the retained original was opened.";
        }
        catch(Exception ex){SourceInboxStatus.Text=FriendlyError(ex);}
    }

    private async void IntakeSourceDocument_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();
            if (string.IsNullOrWhiteSpace(SourceDocumentPathInput.Text)) throw new InvalidOperationException("Select a PDF or image first.");
            var date = SourceDocumentDateInput.SelectedDate is { } value ? DateOnly.FromDateTime(value) : (DateOnly?)null;
            var type = SelectedContent(SourceDocumentTypeInput).Replace(' ', '_').ToUpperInvariant();
            SourceInboxStatus.Text = "Storing the original document and checking whether text extraction is needed…";
            var outcome = await sourceInboxServiceFactory(connectionState.ConnectionString).IntakeAsync(new SourceDocumentIntakeRequest(
                SourceDocumentPathInput.Text, string.IsNullOrWhiteSpace(SourceDocumentStoreInput.Text) ? null : SourceDocumentStoreInput.Text.Trim(), date, type));
            SourceInboxStatus.Text = outcome.Duplicate
                ? "This document was already received. The existing immutable copy has been selected."
                : outcome.Extraction?.Method == "PADDLE_OCR"
                    ? "Document stored. PaddleOCR extraction was captured for human verification."
                    : string.IsNullOrWhiteSpace(outcome.Extraction?.Text)
                        ? "Document stored. No usable native text was found; manual review is required."
                        : "Document stored. Native PDF text was extracted and is awaiting human verification.";
            SourceDocumentPathInput.Clear(); await RefreshSourceInboxAsync();
        }
        catch (Exception ex) { SourceInboxStatus.Text = FriendlyError(ex); }
    }

    private async void RefreshRegisters_Click(object sender, RoutedEventArgs e) => await RefreshRegistersAsync();

    private async Task RefreshRegistersAsync()
    {
        try
        {
            RequireViewAccess();
            var rows = await digitalRegisterServiceFactory(connectionState.ConnectionString).LoadAsync(RegisterSearchInput.Text);
            RegisterGrid.ItemsSource = rows; RegisterStatus.Text = $"{rows.Count:N0} audited register entry or entries found.";
        }
        catch (Exception ex) { RegisterStatus.Text = FriendlyError(ex); }
    }

    private async void SaveRegisterEntry_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();
            if (RegisterBusinessDateInput.SelectedDate is null) throw new InvalidOperationException("Select the register business date.");
            if (string.IsNullOrWhiteSpace(RegisterStoreInput.Text) || string.IsNullOrWhiteSpace(RegisterDocumentNumberInput.Text))
                throw new InvalidOperationException("Enter the store and document number.");
            var linkedDocument = SourceInboxGrid.SelectedItem as SourceInboxDocument;
            var entry = new DigitalRegisterEntryDraft(SelectedContent(RegisterTypeInput).Replace(' ', '_').ToUpperInvariant(), linkedDocument?.Id,
                RegisterStoreInput.Text, DateOnly.FromDateTime(RegisterBusinessDateInput.SelectedDate.Value), RegisterDocumentNumberInput.Text,
                null, RegisterCounterpartyInput.Text, OptionalDecimal(RegisterQuantityInput.Text), OptionalDecimal(RegisterAmountInput.Text),
                RegisterReferenceInput.Text, currentAccess.DisplayName, "DRAFT", RegisterRemarksInput.Text);
            var id = await digitalRegisterServiceFactory(connectionState.ConnectionString).SaveAsync(entry, RegisterReasonInput.Text);
            RegisterStatus.Text = $"Register entry {id:N0} saved with audit history."; RegisterDocumentNumberInput.Clear(); RegisterReasonInput.Clear();
            await RefreshRegistersAsync();
        }
        catch (Exception ex) { RegisterStatus.Text = FriendlyError(ex); }
    }

    private async void RunGlobalSearch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess(); var rows = await new ProductisationRepository(connectionState.ConnectionString).SearchAsync(GlobalSearchInput.Text);
            InvestigationGrid.ItemsSource = rows; InvestigationStatus.Text = $"{rows.Count:N0} result(s) across canonical transactions, sources, reports and registers.";
        }
        catch (Exception ex) { InvestigationStatus.Text = FriendlyError(ex); }
    }

    private async void SubmitAdjustment_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();if(AdjustmentDateInput.SelectedDate is null)throw new InvalidOperationException("Select the adjustment business date.");
            if(string.IsNullOrWhiteSpace(AdjustmentStoreInput.Text)||string.IsNullOrWhiteSpace(AdjustmentTypeInput.Text))throw new InvalidOperationException("Enter the store and adjustment type.");
            if(!decimal.TryParse(AdjustmentAmountInput.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out var amount)||amount==0)throw new InvalidOperationException("Enter a non-zero signed adjustment amount.");
            var linkedDocument=SourceInboxGrid.SelectedItem as SourceInboxDocument;
            var id=await new ProductisationRepository(connectionState.ConnectionString).CreateAdjustmentRequestAsync(AdjustmentStoreInput.Text,DateOnly.FromDateTime(AdjustmentDateInput.SelectedDate.Value),AdjustmentTypeInput.Text,amount,AdjustmentReasonInput.Text,linkedDocument?.Id);
            AdjustmentAmountInput.Clear();AdjustmentReasonInput.Clear();InvestigationStatus.Text=$"Adjustment {id:N0} is pending Owner approval. Canonical ETP facts were not changed.";await RefreshApprovalsAsync();
        }
        catch(Exception ex){InvestigationStatus.Text=FriendlyError(ex);}
    }

    private async void RefreshApprovals_Click(object sender, RoutedEventArgs e) => await RefreshApprovalsAsync();

    private async void UpdateIssueWorkflow_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();if(sender is not Button { Tag:string status }||DataQualityGrid.SelectedItem is not DataQualityIssueRow row)throw new InvalidOperationException("Select one data-quality issue.");
            await new ProductisationRepository(connectionState.ConnectionString).UpdateIssueWorkflowAsync(row.Id,status,IssueWorkflowReasonInput.Text);
            IssueWorkflowReasonInput.Clear();await RefreshOperationsAsync();
            OperationsStatus.Text=$"Issue marked {status.Replace('_',' ').ToLowerInvariant()}. Technical control remains {row.TechnicalControlStatus}.";
        }
        catch(Exception ex){OperationsStatus.Text=FriendlyError(ex);}
    }

    private async Task RefreshApprovalsAsync()
    {
        try { RequireViewAccess(); var rows = await new ProductisationRepository(connectionState.ConnectionString).LoadApprovalsAsync(); ApprovalGrid.ItemsSource = rows; InvestigationStatus.Text = $"{rows.Count:N0} approval(s) pending."; }
        catch (Exception ex) { InvestigationStatus.Text = FriendlyError(ex); }
    }
    private async void ApproveSelected_Click(object sender, RoutedEventArgs e) => await DecideApprovalAsync(true);
    private async void RejectSelected_Click(object sender, RoutedEventArgs e) => await DecideApprovalAsync(false);
    private async Task DecideApprovalAsync(bool approve)
    {
        try
        {
            RequireOwnerAccess(); if (ApprovalGrid.SelectedItem is not ApprovalRequestRow row) throw new InvalidOperationException("Select one pending approval.");
            await new ProductisationRepository(connectionState.ConnectionString).DecideApprovalAsync(row.Id, approve, ApprovalReasonInput.Text);
            ApprovalReasonInput.Clear(); await RefreshApprovalsAsync();
        }
        catch (Exception ex) { InvestigationStatus.Text = FriendlyError(ex); }
    }

    private async Task RefreshAccountingAsync()
    {
        try { RequireViewAccess(); AccountingBatchGrid.ItemsSource = await new ProductisationRepository(connectionState.ConnectionString).LoadAccountingBatchesAsync(); }
        catch (Exception ex) { AccountingStatus.Text = FriendlyError(ex); }
    }

    private async Task RefreshSharingContactsAsync()
    {
        try{RequireViewAccess();SharingContactsGrid.ItemsSource=await sharingContactsServiceFactory(connectionState.ConnectionString).LoadAsync();}
        catch(Exception ex){ReportArchiveStatus.Text=FriendlyError(ex);}
    }

    private void SharingContact_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(SharingContactsGrid.SelectedItem is not SharingContact row)return;ContactNameInput.Text=row.DisplayName;ContactRoleInput.Text=row.ContactRole;ContactEmailInput.Text=row.EmailAddress;ContactPhoneInput.Text=row.PhoneE164;ContactSubscriptionsInput.Text=row.DefaultSubscriptions;ContactActiveInput.IsChecked=row.IsActive;SharePhoneInput.Text=row.PhoneE164;ShareEmailToInput.Text=row.EmailAddress;
    }

    private async void SaveSharingContact_Click(object sender,RoutedEventArgs e)
    {
        try{RequireOwnerAccess();var current=SharingContactsGrid.SelectedItem as SharingContact;var row=new SharingContactDraft(current?.Id??0,ContactNameInput.Text,ContactRoleInput.Text,ContactEmailInput.Text,ContactPhoneInput.Text,ContactSubscriptionsInput.Text,ContactActiveInput.IsChecked==true);var id=await sharingContactsServiceFactory(connectionState.ConnectionString).SaveAsync(row,ContactReasonInput.Text);ContactReasonInput.Clear();ReportArchiveStatus.Text=$"Sharing contact {id:N0} saved with audit history.";await RefreshSharingContactsAsync();}
        catch(Exception ex){ReportArchiveStatus.Text=FriendlyError(ex);}
    }

    private (string Store, DateOnly Date) AccountingScope()
    {
        if (AccountingDateInput.SelectedDate is null) throw new InvalidOperationException("Select the accounting business date.");
        if (string.IsNullOrWhiteSpace(AccountingStoreInput.Text)) throw new InvalidOperationException("Enter the accounting store.");
        return (AccountingStoreInput.Text.Trim().ToUpperInvariant(), DateOnly.FromDateTime(AccountingDateInput.SelectedDate.Value));
    }

    private async void PreviewAccountingBatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess(); var scope = AccountingScope(); var repository = new ProductisationRepository(connectionState.ConnectionString);
            var source = await repository.LoadAccountingSourceAsync(scope.Store, scope.Date); var mappings = await repository.LoadApprovedAccountingMappingsAsync(scope.Store, scope.Date);
            currentAccountingDraft = new AccountingBatchComposer().Compose(source.Events, mappings); currentAccountingReportGenerationId = source.GenerationId;
            AccountingEntryGrid.ItemsSource = currentAccountingDraft.Entries;
            AccountingStatus.Text = currentAccountingDraft.IsBalanced
                ? $"Balanced preview: debit {currentAccountingDraft.DebitTotal:N2}, credit {currentAccountingDraft.CreditTotal:N2}."
                : $"Preview blocked. Missing approved mappings: {string.Join(", ", currentAccountingDraft.MissingMappings)}.";
        }
        catch (Exception ex) { currentAccountingDraft = null; currentAccountingReportGenerationId = null; AccountingStatus.Text = FriendlyError(ex); }
    }

    private async void SaveAccountingBatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess(); if (currentAccountingDraft is null || currentAccountingReportGenerationId is null) throw new InvalidOperationException("Preview a balanced accounting batch first.");
            var scope = AccountingScope(); var id = await new ProductisationRepository(connectionState.ConnectionString).SaveAccountingBatchAsync(scope.Store, scope.Date, currentAccountingReportGenerationId.Value, currentAccountingDraft);
            AccountingStatus.Text = $"Accounting batch {id:N0} saved for Owner review."; await RefreshAccountingAsync();
        }
        catch (Exception ex) { AccountingStatus.Text = FriendlyError(ex); }
    }

    private async void ApproveAccountingBatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess(); if (AccountingBatchGrid.SelectedItem is not AccountingBatchRow row) throw new InvalidOperationException("Select one accounting batch.");
            await new ProductisationRepository(connectionState.ConnectionString).ApproveAccountingBatchAsync(row.Id, AccountingMappingReasonInput.Text);
            AccountingStatus.Text = $"Accounting batch {row.Id:N0} approved."; await RefreshAccountingAsync();
        }
        catch (Exception ex) { AccountingStatus.Text = FriendlyError(ex); }
    }

    private async void ExportTallyXml_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess(); if (AccountingBatchGrid.SelectedItem is not AccountingBatchRow row) throw new InvalidOperationException("Select one approved accounting batch.");
            if (row.Status != "APPROVED") throw new InvalidOperationException("Approve the accounting batch before exporting it.");
            var entries = await new ProductisationRepository(connectionState.ConnectionString).LoadAccountingEntriesAsync(row.Id);
            var draft = new AccountingBatchDraft(entries, row.DebitTotal, row.CreditTotal, row.DebitTotal == row.CreditTotal, []);
            var dialog = new SaveFileDialog { Filter = "Tally XML (*.xml)|*.xml", FileName = $"ETP_Tally_{row.StoreCode}_{row.BusinessDate:yyyyMMdd}_Gen{row.AccountingGeneration:D2}.xml", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            var hash = await new TallyXmlExportService().ExportAsync(dialog.FileName, "Saagar Traders", row.BusinessDate, draft);
            await new ProductisationRepository(connectionState.ConnectionString).RecordAccountingExportAsync(row.Id, hash);
            AccountingStatus.Text = $"Approved Tally XML exported with SHA-256 {hash[..12]}…"; await RefreshAccountingAsync();
        }
        catch (Exception ex) { AccountingStatus.Text = FriendlyError(ex); }
    }

    private async void ApproveAccountingMapping_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess(); var scope = AccountingScope(); var repository = new ProductisationRepository(connectionState.ConnectionString);
            var eventCode = SelectedContent(AccountingEventInput); var payload = new { Event = eventCode, Debit = DebitLedgerInput.Text, Credit = CreditLedgerInput.Text, Narration = AccountingNarrationInput.Text, Store = scope.Store };
            var approval = await repository.CreateApprovalAsync("ACCOUNTING_MAPPING", "AccountingMapping", eventCode, payload, scope.Store, scope.Date);
            await repository.DecideApprovalAsync(approval, true, AccountingMappingReasonInput.Text);
            await repository.SaveAccountingMappingAsync(approval, eventCode, DebitLedgerInput.Text, CreditLedgerInput.Text, AccountingNarrationInput.Text, scope.Store, scope.Date);
            DebitLedgerInput.Clear(); CreditLedgerInput.Clear(); AccountingMappingReasonInput.Clear(); AccountingStatus.Text = $"Approved {eventCode} ledger mapping is active from {scope.Date:dd-MMM-yyyy}.";
        }
        catch (Exception ex) { AccountingStatus.Text = FriendlyError(ex); }
    }

    private async void ExportArchivedZip_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess(); var generation = SelectedArchiveGeneration(); if (currentArchivedDocument is null) throw new InvalidOperationException("Open the selected generation before packaging it.");
            var dialog = new SaveFileDialog { Filter = "ZIP report package (*.zip)|*.zip", FileName = $"ETP_ReportPack_{generation.BusinessDate:yyyy-MM-dd}_Gen{generation.GenerationNumber:D2}.zip", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            var result = await new ReportPackageService().CreateAsync(dialog.FileName, currentArchivedDocument, generation.GenerationNumber, generation.StoreCode, generation.IsFinal, currentAccess.DisplayName);
            await new ProductisationRepository(connectionState.ConnectionString).RecordPackageAsync(generation.Id, generation.StoreCode == "COMBINED" ? "COMBINED" : "DAILY", result.Path, result.ManifestJson, result.Sha256, generation.IsFinal);
            currentShareFile = result.Path; ReportArchiveStatus.Text = $"Immutable ZIP package created. SHA-256 {result.Sha256[..12]}…";
        }
        catch (Exception ex) { ReportArchiveStatus.Text = FriendlyError(ex); }
    }

    private async void ShareArchivedWhatsApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess(); var generation = SelectedArchiveGeneration(); var shareFile = currentShareFile; if (shareFile is null || !File.Exists(shareFile)) throw new InvalidOperationException("Export the selected generation as ZIP first.");
            var message = $"ETP report pack for {generation.StoreCode}, {generation.BusinessDate:dd-MMM-yyyy}, generation {generation.GenerationNumber}. Please attach the prepared ZIP file.";
            Clipboard.SetText(shareFile); Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { "/select,", shareFile } });
            SafeShareLauncher.OpenWhatsApp(message, SharePhoneInput.Text);
            await new ProductisationRepository(connectionState.ConnectionString).RecordShareAttemptAsync(generation.Id, null, "WHATSAPP", string.IsNullOrWhiteSpace(SharePhoneInput.Text) ? null : "Configured phone", Path.GetFileName(shareFile), "INITIATED", "WhatsApp opened; the user must attach and send the prepared file.");
            ReportArchiveStatus.Text = "WhatsApp opened and the ZIP path was copied. Attach the highlighted file, then send it yourself.";
        }
        catch (Exception ex) { ReportArchiveStatus.Text = FriendlyError(ex); }
    }

    private async void ShareArchivedEmail_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireViewAccess(); var generation = SelectedArchiveGeneration(); var shareFile = currentShareFile; if (shareFile is null || !File.Exists(shareFile)) throw new InvalidOperationException("Export the selected generation as ZIP first.");
            if (string.IsNullOrWhiteSpace(ShareEmailToInput.Text)) throw new InvalidOperationException("Enter the email recipient.");
            var settings = await new ProductisationRepository(connectionState.ConnectionString).LoadSettingsAsync();
            if (new FileInfo(shareFile).Length > settings.MaximumAttachmentMb * 1024L * 1024L) throw new InvalidOperationException($"The attachment exceeds the configured {settings.MaximumAttachmentMb} MB email limit.");
            var draft = SafeShareLauncher.CreateEmailDraft(settings.ShareFolderPath, shareFile, ShareEmailToInput.Text, ShareEmailCcInput.Text,
                $"ETP report pack - {generation.StoreCode} - {generation.BusinessDate:dd-MMM-yyyy}", $"Please find attached immutable ETP report generation {generation.GenerationNumber}.");
            Process.Start(new ProcessStartInfo(draft) { UseShellExecute = true });
            await new ProductisationRepository(connectionState.ConnectionString).RecordShareAttemptAsync(generation.Id, null, "EMAIL", "Configured recipient", Path.GetFileName(shareFile), "INITIATED", "Email draft opened; delivery is not claimed.");
            ReportArchiveStatus.Text = "Email draft opened with the ZIP attached. Review recipients and click Send.";
        }
        catch (Exception ex) { ReportArchiveStatus.Text = FriendlyError(ex); }
    }

    private ArchivedReportGenerationSummary SelectedArchiveGeneration() => ReportGenerationGrid.SelectedItems.OfType<ArchivedReportGenerationSummary>().SingleOrDefault()
        ?? throw new InvalidOperationException("Select exactly one report generation.");

    private static string FriendlyError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Your Windows account does not have permission for this action.",
        FileNotFoundException => "The selected file is no longer available. Select it again.",
        IOException => "The file could not be read. Close it in other applications and try again.",
        Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } => "This item already exists.",
        Microsoft.Data.SqlClient.SqlException { Number: 51210 } => "This business day is finalised. Reopen it before making changes.",
        Microsoft.Data.SqlClient.SqlException sql when sql.Number >= 51000 => sql.Message,
        InvalidOperationException or ArgumentException => exception.Message,
        _ => "The action could not be completed. Technical details are available in the support package."
    };
}
