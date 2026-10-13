extern alias EtpApplication;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Reporting;
using TallyBatchStatus = EtpApplication::Etp.Reporting.Application.Accounting.TallyBatchStatus;
using TallyConnectedMode = EtpApplication::Etp.Reporting.Application.Accounting.TallyConnectedMode;
using TallyProfile = EtpApplication::Etp.Reporting.Application.Accounting.TallyProfile;

namespace Etp.Reporting.Desktop.Modules.Accounting;

/// <summary>
/// Settings → Accounting → Tally vouchers, the transfer card (1.12 U3; ETP-DESIGN-SYSTEM template: card with a toolbar, one
/// Notice, one grid). Owner only. For the chosen company and batch it offers Write masters, Write file, Read back, Read back
/// from file, Send, Compare and Open evidence folder. The button for the batch's next step is the one primary; every other
/// button is standard. Send exists only on the test PC the company's profile names (decision 57); on any other PC a sentence
/// says why. Nothing here proves a voucher is in Tally: only Compare does, and the batch chip says so in words.
/// </summary>
public sealed class TallyTransferView : UserControl
{
    public const string SendElsewhereSentence = "Sending is set up for {0} only. On this PC, write the file and import it in Tally by hand, then read back.";
    public const string SendNowhereSentence = "Sending to Tally is not set up for any PC. Name the test PC in Settings → Tally companies to turn it on there.";

    private readonly Func<bool> canAdminister;
    private readonly Func<ITallyTransferActions> actionsFactory;
    private readonly Func<string?> chooseFolder;
    private readonly Func<string?> chooseFile;
    private readonly Action<string> openFolder;
    private TallyProfile? profile;
    private IReadOnlyList<TallyTransferBatch> batches = [];
    private readonly Dictionary<long, long> readbackOfBatch = [];
    private bool busy;

    public ComboBox BatchInput { get; } = new() { Width = 360 };
    public DatePicker ReadBackFromInput { get; } = new() { Width = 170 };
    public DatePicker ReadBackToInput { get; } = new() { Width = 170 };
    public StatusChip BooksChip { get; } = new("No company", StatusTone.Neutral);
    public StatusChip BatchChip { get; } = new("No batch", StatusTone.Neutral);
    public Notice ResultNotice { get; } = new();
    public TextBlock SendCaption { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    public TextBlock GuidanceText { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 4, 0) };
    public Button WriteMastersButton { get; }
    public Button WriteFileButton { get; }
    public Button ReadBackButton { get; }
    public Button ReadBackFileButton { get; }
    public Button SendButton { get; }
    public Button CompareButton { get; }
    public Button OpenFolderButton { get; }

    public TallyTransferView(Func<bool> canAdminister, Func<ITallyTransferActions> actions,
        Func<string?>? chooseFolder = null, Func<string?>? chooseFile = null, Action<string>? openFolder = null)
    {
        this.canAdminister = canAdminister ?? throw new ArgumentNullException(nameof(canAdminister));
        actionsFactory = actions ?? throw new ArgumentNullException(nameof(actions));
        this.chooseFolder = chooseFolder ?? ChooseFolderDialog;
        this.chooseFile = chooseFile ?? ChooseFileDialog;
        this.openFolder = openFolder ?? OpenInExplorer;

        BatchInput.ItemsSource = null;
        BatchInput.DisplayMemberPath = nameof(BatchRow.Label);
        BatchInput.SelectionChanged += (_, _) => { SelectBatchDates(); Refresh(); };
        AutomationProperties.SetName(BatchInput, "Tally batch");
        AutomationProperties.SetName(ReadBackFromInput, "Day Book from");
        AutomationProperties.SetName(ReadBackToInput, "Day Book to");
        AutomationProperties.SetName(SendCaption, "Why there is no Send button");

        WriteMastersButton = Action("Write masters", WriteMastersAsync);
        WriteFileButton = Action("Write file", WriteFileAsync);
        ReadBackButton = Action("Read back", ReadBackAsync);
        ReadBackFileButton = Action("Read back from file", ReadBackFileAsync);
        SendButton = Action("Send", SendAsync);
        CompareButton = Action("Compare", CompareAsync);
        OpenFolderButton = Action("Open evidence folder", OpenEvidenceAsync);
        OpenFolderButton.SetResourceReference(StyleProperty, "QuietButton");
        foreach (var button in new[] { WriteMastersButton, WriteFileButton, ReadBackButton, ReadBackFileButton, SendButton, CompareButton, OpenFolderButton })
            ToolTipService.SetShowOnDisabled(button, true);

        var title = new TextBlock { Text = "Send to Tally" };
        title.SetResourceReference(StyleProperty, "SectionTitleText");
        SendCaption.SetResourceReference(StyleProperty, "CaptionText");
        GuidanceText.SetResourceReference(StyleProperty, "CaptionText");

        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        BooksChip.Margin = new Thickness(4); BatchChip.Margin = new Thickness(4);
        chips.Children.Add(BooksChip); chips.Children.Add(BatchChip);

        var choice = new WrapPanel();
        choice.Children.Add(Field("Batch", BatchInput));
        choice.Children.Add(Field("Day Book from (for a file)", ReadBackFromInput));
        choice.Children.Add(Field("Day Book to (for a file)", ReadBackToInput));

        var toolbar = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var button in new[] { WriteMastersButton, WriteFileButton, ReadBackButton, ReadBackFileButton, SendButton, CompareButton, OpenFolderButton })
            toolbar.Children.Add(button);
        toolbar.Children.Add(SendCaption);

        ResultNotice.Margin = new Thickness(0, 12, 0, 0);
        var body = new StackPanel();
        foreach (var child in new UIElement[] { title, chips, choice, toolbar, GuidanceText, ResultNotice }) body.Children.Add(child);
        var card = new Border { Child = body, Margin = new Thickness(0, 18, 0, 0) };
        card.SetResourceReference(StyleProperty, "SurfaceCard");
        Content = card;
        Refresh();
    }

    /// <summary>The button of the batch's next step, or null when the next step is made elsewhere (approve on the accounting
    /// screen) or there is none. At most one button is primary.</summary>
    public Button? NextStepButton { get; private set; }

    public TallyTransferBatch? SelectedBatch => (BatchInput.SelectedItem as BatchRow)?.Batch;

    /// <summary>True when the profile names this PC as the test PC (decision 57). Shared rule: <c>TallyConnectedMode.TestPcRefusal</c>.</summary>
    public bool IsTestPc => profile is not null && TallyConnectedMode.TestPcRefusal(profile, actionsFactory().ThisPc) is null;

    /// <summary>Shows one company (null: none chosen). Loads its batches when the Owner is looking.</summary>
    public async Task ShowCompanyAsync(TallyProfile? company)
    {
        profile = company;
        batches = [];
        BatchInput.ItemsSource = null;
        readbackOfBatch.Clear();
        ResultNotice.Clear();
        if (company?.Id is { } id && canAdminister())
            await RunAsync(async () =>
            {
                batches = await actionsFactory().LoadBatchesAsync(id);
                BatchInput.ItemsSource = batches.Select(batch => new BatchRow(batch)).ToArray();
                BatchInput.SelectedIndex = batches.Count > 0 ? 0 : -1;
            });
        Refresh();
    }

    public async Task WriteMastersAsync()
    {
        if (profile?.Id is not { } id) throw new InvalidOperationException("Choose a Tally company first.");
        var folder = chooseFolder();
        if (string.IsNullOrWhiteSpace(folder)) return;
        var summary = await actionsFactory().WriteMastersAsync(id, folder);
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{summary.GroupCount} group(s), {summary.CostCentreCount} cost centre(s) and {summary.LedgerCount} ledger(s) written. The accountant's list is {summary.ReviewPath}.");
        if (summary.MappingsToApprove.Count > 0)
            text += " Approve these ledger names in Settings → Tally ledgers before vouchers use them: " + string.Join("; ", summary.MappingsToApprove) + ".";
        ResultNotice.Show(summary.MappingsToApprove.Count > 0 ? StatusTone.Warning : StatusTone.Success, "Masters file written", text);
    }

    public async Task WriteFileAsync()
    {
        var batch = RequireBatch();
        var receipt = await actionsFactory().WriteFileAsync(batch.Id);
        await ReloadBatchesAsync(batch.Id);
        ResultNotice.Show(StatusTone.Success, "File written",
            $"{IndianFormat.Count(receipt.VoucherCount)} voucher(s), total ₹{IndianFormat.MoneyCell(receipt.VoucherTotal)}. Nothing has been sent to Tally.");
    }

    public async Task ReadBackAsync()
    {
        var batch = RequireBatch();
        var record = await actionsFactory().ReadBackAsync(batch.Id);
        ShowReadback(batch, record);
        await ReloadBatchesAsync(batch.Id);
    }

    public async Task ReadBackFileAsync()
    {
        var batch = RequireBatch();
        var path = chooseFile();
        if (string.IsNullOrWhiteSpace(path)) return;
        var from = ReadBackFromInput.SelectedDate is { } f ? DateOnly.FromDateTime(f) : batch.BusinessDate;
        var to = ReadBackToInput.SelectedDate is { } t ? DateOnly.FromDateTime(t) : batch.BusinessDate;
        var record = await actionsFactory().ReadBackFileAsync(batch.Id, from, to, path);
        ShowReadback(batch, record);
        await ReloadBatchesAsync(batch.Id);
    }

    public async Task SendAsync()
    {
        var batch = RequireBatch();
        // The services refuse again on their own; this keeps the sentence in front of the Owner before a call is made.
        if (!IsTestPc) throw new InvalidOperationException(TallyConnectedMode.OnlyTheTestPc);
        var outcome = await actionsFactory().SendAsync(batch.Id);
        var tone = outcome.AttemptStatus != "RESPONDED" ? StatusTone.Warning : outcome.AllCreated ? StatusTone.Success : StatusTone.Critical;
        var title = outcome.AttemptStatus switch
        {
            "RESPONDED" => outcome.AllCreated ? "Tally answered: created" : "Tally answered: not all created",
            "TIMED_OUT" => "Tally did not answer in time",
            _ => "Tally could not be reached"
        };
        ResultNotice.Show(tone, title, outcome.Message);
        await ReloadBatchesAsync(batch.Id);
    }

    public async Task CompareAsync()
    {
        var batch = RequireBatch();
        if (!readbackOfBatch.TryGetValue(batch.Id, out var readback)) throw new InvalidOperationException("Read back from Tally first, then compare.");
        var result = await actionsFactory().CompareAsync(batch.Id, readback);
        var (word, tone) = BatchStatusWord(result.BatchStatus);
        ResultNotice.Show(tone, word, string.Create(CultureInfo.InvariantCulture,
            $"{result.ReconciledCount} of {result.VoucherCount} voucher(s) match Tally; {result.DifferenceCount} difference(s), {result.FailureCount} of them failures."));
        await ReloadBatchesAsync(batch.Id);
    }

    public async Task OpenEvidenceAsync()
    {
        var batch = RequireBatch();
        var folder = await actionsFactory().EvidenceFolderAsync(batch.Id);
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("This batch has no evidence folder yet.");
        openFolder(folder);
    }

    /// <summary>The word and tone a batch status shows as, everywhere on this card (ST1, ST2). Never says "in Tally" before Compare.</summary>
    public static (string Word, StatusTone Tone) BatchStatusWord(string? status) => status switch
    {
        null => ("No batch", StatusTone.Neutral),
        "PLANNED" or TallyBatchStatus.Draft => ("Not approved", StatusTone.Neutral),
        TallyBatchStatus.Blocked => ("Blocked", StatusTone.Critical),
        TallyBatchStatus.ApprovedReady => ("Approved", StatusTone.Information),
        TallyBatchStatus.ExportedAwaitingImport => ("File written", StatusTone.Information),
        TallyBatchStatus.SubmittedAwaitingResult => ("Sent, answer not read", StatusTone.Warning),
        TallyBatchStatus.OutcomeUnknown => ("Outcome unknown", StatusTone.Critical),
        TallyBatchStatus.PartiallyApplied => ("Partly in Tally", StatusTone.Warning),
        TallyBatchStatus.ImportReportedAwaitingReconciliation => ("Not compared yet", StatusTone.Warning),
        TallyBatchStatus.ReconciliationIncomplete => ("Compare incomplete", StatusTone.Warning),
        TallyBatchStatus.FailedReconciliation => ("Differences found", StatusTone.Critical),
        TallyBatchStatus.Reconciled => ("Matches Tally", StatusTone.Success),
        TallyBatchStatus.ReconciledWithAcceptedWarnings => ("Matches, warnings accepted", StatusTone.Success),
        TallyBatchStatus.ReAuditRequired => ("Check again", StatusTone.Warning),
        TallyBatchStatus.Rejected => ("Rejected", StatusTone.Neutral),
        TallyBatchStatus.Cancelled => ("Cancelled", StatusTone.Neutral),
        _ => (status, StatusTone.Neutral)
    };

    /// <summary>True once the batch's file has been written (every status after APPROVED_READY that is not a dead end).</summary>
    public static bool IsFileWritten(string? status) =>
        status is not null && status is not ("PLANNED" or TallyBatchStatus.Draft or TallyBatchStatus.Blocked or TallyBatchStatus.ApprovedReady
            or TallyBatchStatus.Rejected or TallyBatchStatus.Cancelled);

    private void ShowReadback(TallyTransferBatch batch, TallyReadbackSummary record)
    {
        if (record.IsComplete)
        {
            readbackOfBatch[batch.Id] = record.ReadbackId;
            ResultNotice.Show(StatusTone.Success, "Read back recorded",
                string.Create(CultureInfo.InvariantCulture, $"{record.VoucherCount} voucher(s) found in Tally's Day Book. Compare to see whether they match what ETP wrote."));
        }
        else
            ResultNotice.Show(StatusTone.Warning, "Read back incomplete",
                (record.TallyMessage ?? "Tally's answer could not be used.") + $" ({record.IncompleteReason}). Fix that and read back again; Compare needs a complete read-back.");
    }

    private async Task ReloadBatchesAsync(long keep)
    {
        if (profile?.Id is not { } id) return;
        batches = await actionsFactory().LoadBatchesAsync(id);
        BatchInput.ItemsSource = batches.Select(batch => new BatchRow(batch)).ToArray();
        BatchInput.SelectedItem = ((IEnumerable<BatchRow>)BatchInput.ItemsSource).FirstOrDefault(row => row.Batch.Id == keep);
    }

    private TallyTransferBatch RequireBatch() => SelectedBatch ?? throw new InvalidOperationException("Choose a batch first.");

    private void SelectBatchDates()
    {
        if (SelectedBatch is not { } batch) return;
        ReadBackFromInput.SelectedDate = batch.BusinessDate.ToDateTime(TimeOnly.MinValue);
        ReadBackToInput.SelectedDate = batch.BusinessDate.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>Re-reads role, company and batch state: which buttons work, which one is primary, what the chips and captions say.</summary>
    public void Refresh()
    {
        var owner = canAdminister();
        var batch = SelectedBatch;
        var status = batch?.Status;
        var written = IsFileWritten(status);
        var hasReadback = batch is not null && readbackOfBatch.ContainsKey(batch.Id);
        var connected = profile is not null && TallyConnectedMode.Refusal(profile) is null;
        var testPc = IsTestPc;
        var httpDelivery = profile?.DefaultDeliveryMode == "HTTP";

        if (profile is null) BooksChip.Text = "No company";
        else BooksChip.Text = profile.Environment == "PRODUCTION" ? "LIVE books" : "TEST books";
        BooksChip.Tone = profile is null ? StatusTone.Neutral : profile.Environment == "PRODUCTION" ? StatusTone.Warning : StatusTone.Information;
        var (word, tone) = BatchStatusWord(status);
        BatchChip.Text = word; BatchChip.Tone = tone;

        Enable(WriteMastersButton, owner && profile?.Id is not null, owner ? "Choose a Tally company first." : OwnerOnly);
        Enable(WriteFileButton, owner && status == TallyBatchStatus.ApprovedReady,
            !owner ? OwnerOnly : "Available once the batch is approved on the accounting screen.");
        Enable(ReadBackButton, owner && written && connected,
            !owner ? OwnerOnly : !written ? "Write the file first." : "Needs the test Tally's address (port 9000) in Settings → Tally companies.");
        Enable(ReadBackFileButton, owner && written, !owner ? OwnerOnly : "Write the file first.");
        Enable(CompareButton, owner && written && hasReadback, !owner ? OwnerOnly : !written ? "Write the file first." : "Read back from Tally first.");
        Enable(OpenFolderButton, owner && batch is not null, !owner ? OwnerOnly : "Choose a batch first.");

        // Decision 57: Send exists only on the profile's test PC; elsewhere a sentence takes its place.
        var sendShown = owner && testPc;
        SendButton.Visibility = sendShown ? Visibility.Visible : Visibility.Collapsed;
        Enable(SendButton, sendShown && status == TallyBatchStatus.ExportedAwaitingImport && httpDelivery && connected,
            status != TallyBatchStatus.ExportedAwaitingImport ? "Write the file first; only a written file is sent."
                : !httpDelivery ? "This company delivers by file. Change that in Settings → Tally companies." : "Needs the test Tally's address (port 9000).");
        var named = profile?.TestMachineName?.Trim();
        SendCaption.Text = !owner || profile is null || testPc ? ""
            : string.IsNullOrEmpty(named) ? SendNowhereSentence : string.Format(CultureInfo.CurrentCulture, SendElsewhereSentence, named);
        SendCaption.Visibility = SendCaption.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        NextStepButton = !owner || batch is null ? null
            : status == TallyBatchStatus.ApprovedReady ? WriteFileButton
            : !written ? null
            : status == TallyBatchStatus.Reconciled || status == TallyBatchStatus.ReconciledWithAcceptedWarnings ? null
            : hasReadback ? CompareButton
            : SendButton.IsEnabled && SendButton.Visibility == Visibility.Visible && status == TallyBatchStatus.ExportedAwaitingImport ? SendButton
            : ReadBackButton.IsEnabled ? ReadBackButton
            : ReadBackFileButton;
        foreach (var button in new[] { WriteMastersButton, WriteFileButton, ReadBackButton, ReadBackFileButton, SendButton, CompareButton })
        {
            if (button == NextStepButton) button.SetResourceReference(StyleProperty, "PrimaryButton");
            else button.ClearValue(StyleProperty);
        }
        OpenFolderButton.SetResourceReference(StyleProperty, "QuietButton");

        GuidanceText.Text = !owner ? "Owner permission is required for the Tally steps."
            : profile is null ? "Choose a Tally company above."
            : batch is null ? "No Tally voucher batch for this company yet. Prepare and save a day above."
            : status is "PLANNED" or TallyBatchStatus.Draft ? "Approve this batch on the accounting screen, then come back to write its file."
            : status == TallyBatchStatus.Blocked ? "This batch is blocked. Reject it on the accounting screen, fix the cause and prepare the day again."
            : status == TallyBatchStatus.ApprovedReady ? "Next: write the file."
            : status == TallyBatchStatus.ExportedAwaitingImport ? (sendShown ? "Next: send the file to the test Tally, or import it by hand and read back." : "Next: import the file in Tally by hand, then read back.")
            : written && !hasReadback ? "Next: read back from Tally, then compare."
            : hasReadback ? "Next: compare what Tally holds with what ETP wrote."
            : "";
        GuidanceText.Visibility = GuidanceText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BatchInput.IsEnabled = owner;
    }

    private const string OwnerOnly = "Owner permission is required.";

    private static void Enable(Button button, bool enabled, string whyNot)
    {
        button.IsEnabled = enabled;
        button.ToolTip = enabled ? null : whyNot;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        if (!canAdminister()) { ResultNotice.Show(StatusTone.Critical, "Owner permission is required.", null); Refresh(); return; }
        busy = true;
        IsEnabled = false;
        try { await action(); }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.TallyTransfer", "TALLY_TRANSFER_OPERATION_FAILED");
            ResultNotice.Show(StatusTone.Critical, "That did not work", DesktopFriendlyError.Describe(exception));
        }
        finally { busy = false; IsEnabled = true; Refresh(); }
    }

    private Button Action(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Bottom };
        button.Click += async (_, _) => await RunAsync(action);
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static StackPanel Field(string label, Control control)
    {
        AutomationProperties.SetName(control, label);
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) };
        caption.SetResourceReference(StyleProperty, "FieldLabelText");
        var field = new StackPanel { Margin = new Thickness(4) };
        field.Children.Add(caption);
        field.Children.Add(control);
        return field;
    }

    private static string? ChooseFolderDialog()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder for the Tally masters files" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string? ChooseFileDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Tally Day Book (*.xml)|*.xml", Title = "Choose the Day Book exported from Tally" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void OpenInExplorer(string folder) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });

    /// <summary>One line of the batch list: "25 Aug 2026 · STOREA · Approved".</summary>
    public sealed record BatchRow(TallyTransferBatch Batch)
    {
        public string Label => $"{IndianFormat.Date(Batch.BusinessDate)} · {Batch.StoreCode} · {BatchStatusWord(Batch.Status).Word}";
    }
}
