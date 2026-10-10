extern alias EtpApplication;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BatchService = EtpApplication::Etp.Reporting.Application.Accounting.ITallySalesBatchService;
using PlannedVoucher = EtpApplication::Etp.Reporting.Application.Accounting.PlannedVoucher;
using ProfileService = EtpApplication::Etp.Reporting.Application.Accounting.ITallyProfileService;
using SalesVoucherPreview = EtpApplication::Etp.Reporting.Application.Accounting.SalesVoucherPreview;
using TallyProfile = EtpApplication::Etp.Reporting.Application.Accounting.TallyProfile;
using TallyVoucherStatus = EtpApplication::Etp.Reporting.Application.Accounting.TallyVoucherStatus;
using VoucherValidation = EtpApplication::Etp.Reporting.Application.Accounting.TallySalesVoucherValidation;

namespace Etp.Reporting.Desktop.Modules.Accounting;

/// <summary>Settings → Accounting → Tally vouchers (Phase 7 plan tasks 6 and 7). Owner only.
/// Prepares one store's day as Tally Sales vouchers, one per invoice, shows each invoice's result and the validation
/// findings, saves the batch, and accepts warnings with a reason. Nothing here sends anything to Tally.</summary>
public sealed class TallyVouchersView : UserControl
{
    private readonly Func<string> connectionString;
    private readonly Func<bool> canAdminister;
    private readonly Func<string, ProfileService> profiles;
    private readonly Func<string, BatchService> batches;
    private SalesVoucherPreview? preview;
    private long? savedBatch;
    private bool busy;

    public ComboBox CompanyInput { get; } = new() { MinHeight = 44, Width = 320, DisplayMemberPath = nameof(TallyProfile.CompanyName) };
    public ComboBox StoreInput { get; } = new() { MinHeight = 44, Width = 160 };
    public DatePicker DateInput { get; } = new() { MinHeight = 44, Width = 170, SelectedDate = DateTime.Today.AddDays(-1) };
    public DataGrid VoucherGrid { get; } = NewGrid(260);
    public DataGrid FindingGrid { get; } = NewGrid(180);
    public TextBlock SummaryText { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) };
    public TextBox ReasonInput { get; } = new() { Width = 420, MinHeight = 44, Padding = new(6), MaxLength = 1000 };
    public TextBlock StatusText { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    public Button PrepareButton { get; }
    public Button SaveButton { get; }
    public Button AcceptButton { get; }

    public TallyVouchersView(Func<string> connectionString, Func<bool> canAdminister, Func<string, ProfileService> profiles, Func<string, BatchService> batches)
    {
        this.connectionString = connectionString;
        this.canAdminister = canAdminister;
        this.profiles = profiles;
        this.batches = batches;
        Column(VoucherGrid, "Invoice", nameof(VoucherRow.Invoice), 130);
        Column(VoucherGrid, "Result", nameof(VoucherRow.Result), 110);
        Column(VoucherGrid, "Amount", nameof(VoucherRow.Amount), 110);
        Column(VoucherGrid, "Reason", nameof(VoucherRow.Reason), 520);
        Column(FindingGrid, "Kind", nameof(FindingRow.Kind), 90);
        Column(FindingGrid, "Rule", nameof(FindingRow.Rule), 120);
        Column(FindingGrid, "About", nameof(FindingRow.Subject), 150);
        Column(FindingGrid, "What it means", nameof(FindingRow.Explanation), 420);
        Column(FindingGrid, "Accepted", nameof(FindingRow.Accepted), 200);
        System.Windows.Automation.AutomationProperties.SetName(VoucherGrid, "Invoices of the day");
        System.Windows.Automation.AutomationProperties.SetName(FindingGrid, "Validation findings");
        CompanyInput.SelectionChanged += (_, _) =>
        {
            StoreInput.ItemsSource = (CompanyInput.SelectedItem as TallyProfile)?.StoreCodes ?? Array.Empty<string>();
            StoreInput.SelectedIndex = StoreInput.Items.Count > 0 ? 0 : -1;
            Clear();
        };
        StoreInput.SelectionChanged += (_, _) => Clear();
        DateInput.SelectedDateChanged += (_, _) => Clear();

        var choice = new WrapPanel();
        choice.Children.Add(Field("Tally company (test books)", CompanyInput));
        choice.Children.Add(Field("Store", StoreInput));
        choice.Children.Add(Field("Business day", DateInput));
        PrepareButton = Action("Prepare vouchers", PrepareAsync);
        SaveButton = Action("Save batch", SaveAsync);
        SaveButton.IsEnabled = false;
        choice.Children.Add(PrepareButton);
        choice.Children.Add(SaveButton);

        var accept = new WrapPanel();
        accept.Children.Add(Field("Reason for accepting the selected warning", ReasonInput));
        AcceptButton = Action("Accept warning", AcceptAsync);
        AcceptButton.IsEnabled = false;
        accept.Children.Add(AcceptButton);

        var body = new StackPanel();
        foreach (var child in new UIElement[]
                 {
                     new TextBlock
                     {
                         Text = "Prepares one store's day as Tally Sales vouchers, one per invoice. An invoice this step cannot send yet (a return, a split payment, mixed GST rates) is left out; " +
                                "a missing ledger or GST row must be fixed before the day can be approved. Saving here sends nothing to Tally.",
                         TextWrapping = TextWrapping.Wrap
                     },
                     choice, SummaryText, VoucherGrid, Heading("Validation findings"), FindingGrid, accept, StatusText
                 })
            body.Children.Add(child);
        Content = new Expander { Header = "Tally vouchers", IsExpanded = true, Content = body, Margin = new(0, 18, 0, 0) };
        Loaded += async (_, _) => await RunAsync(LoadCompaniesAsync);
    }

    public async Task LoadCompaniesAsync()
    {
        var companies = (await profiles(connectionString()).LoadAsync()).Where(profile => profile.IsEnabled && profile.Environment == "TEST" && profile.Id is not null).ToArray();
        CompanyInput.ItemsSource = companies;
        CompanyInput.SelectedIndex = companies.Length > 0 ? 0 : -1;
        StatusText.Text = companies.Length == 0
            ? "No test Tally company is in use. Add one under Settings → Integrations → Tally companies."
            : "Choose the company, store and business day, then prepare the vouchers.";
    }

    public async Task PrepareAsync()
    {
        if (CompanyInput.SelectedItem is not TallyProfile { Id: { } company }) throw new ArgumentException("Choose a Tally company.");
        if (StoreInput.SelectedItem is not string store) throw new ArgumentException("Choose a store.");
        if (DateInput.SelectedDate is not { } date) throw new ArgumentException("Choose the business day.");
        var prepared = await batches(connectionString()).PreviewAsync(company, store, DateOnly.FromDateTime(date));
        preview = prepared;
        savedBatch = null;
        VoucherGrid.ItemsSource = prepared.Plan.Vouchers.Select(voucher => new VoucherRow(voucher)).ToArray();
        FindingGrid.ItemsSource = prepared.Findings.Select(finding => new FindingRow(null, finding.Severity, finding.RuleId, finding.Subject, finding.Explanation, null)).ToArray();
        SummaryText.Text = VoucherValidation.Describe(prepared.Plan);
        SaveButton.IsEnabled = true;
        AcceptButton.IsEnabled = false;
        StatusText.Text = prepared.Plan.BlockingReason is { } reason
            ? "This day can be saved but not approved yet. " + reason
            : "Ready to save. Warnings are accepted after saving. Nothing is sent to Tally.";
    }

    public async Task SaveAsync()
    {
        if (preview is null) throw new InvalidOperationException("Prepare the vouchers first.");
        var service = batches(connectionString());
        savedBatch = await service.SaveAsync(preview);
        preview = null;
        SaveButton.IsEnabled = false;
        await ShowFindingsAsync(service);
        StatusText.Text = string.Create(CultureInfo.InvariantCulture,
            $"Saved as batch {savedBatch}. Accept its warnings here, then approve it on the accounting screen. Nothing has been sent to Tally.");
    }

    public async Task AcceptAsync()
    {
        if (savedBatch is null || FindingGrid.SelectedItem is not FindingRow { Id: { } id, Severity: "WARN", Accepted: null })
            throw new ArgumentException("Select a warning of the saved batch that has not been accepted yet.");
        var service = batches(connectionString());
        await service.AcceptWarningAsync(id, ReasonInput.Text);
        ReasonInput.Clear();
        await ShowFindingsAsync(service);
        StatusText.Text = "Warning accepted.";
    }

    private async Task ShowFindingsAsync(BatchService service)
    {
        var findings = await service.LoadFindingsAsync(savedBatch!.Value);
        FindingGrid.ItemsSource = findings.Select(finding => new FindingRow(finding.Id, finding.Severity, finding.RuleId, finding.Subject, finding.Explanation,
            finding.WaivedBy is null ? null : $"{finding.WaivedBy}: {finding.WaiverReason}")).ToArray();
        AcceptButton.IsEnabled = findings.Any(finding => finding.Severity == "WARN" && finding.WaivedBy is null);
    }

    private void Clear()
    {
        preview = null;
        savedBatch = null;
        VoucherGrid.ItemsSource = null;
        FindingGrid.ItemsSource = null;
        SummaryText.Text = "";
        SaveButton.IsEnabled = false;
        AcceptButton.IsEnabled = false;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        if (!canAdminister()) { StatusText.Text = "Owner permission is required."; return; }
        busy = true; IsEnabled = false;
        try { await action(); }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.TallyVouchers", "TALLY_VOUCHER_OPERATION_FAILED");
            StatusText.Text = DesktopFriendlyError.Describe(exception);
        }
        finally { busy = false; IsEnabled = true; }
    }

    private Button Action(string text, Func<Task> action)
    {
        var button = new Button { Content = text, MinHeight = 44, Padding = new(12, 6, 12, 6), Margin = new(4), VerticalAlignment = VerticalAlignment.Bottom };
        button.Click += async (_, _) => await RunAsync(action);
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        return button;
    }

    private static DataGrid NewGrid(double height) =>
        new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = height, MinHeight = 100, RowHeight = 36, Margin = new(0, 6, 0, 8) };

    private static void Column(DataGrid grid, string header, string binding, double width) =>
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(binding), Width = width });

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new(0, 10, 0, 0) };

    private static StackPanel Field(string label, Control control)
    {
        System.Windows.Automation.AutomationProperties.SetName(control, label);
        var field = new StackPanel { Margin = new(4) };
        field.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(control.Width, 160) });
        field.Children.Add(control);
        return field;
    }

    public sealed record VoucherRow(PlannedVoucher Voucher)
    {
        public string Invoice => Voucher.DocumentNumber;
        public string Result => Voucher.Status == TallyVoucherStatus.Planned ? "Ready" : Voucher.LeftOutByScope ? "Left out" : "Fix needed";
        public string Amount => Voucher.ExpectedTotal.ToString("N2", CultureInfo.InvariantCulture);
        public string Reason => Voucher.BlockedReason ?? "";
    }

    public sealed record FindingRow(long? Id, string Severity, string Rule, string? Subject, string Explanation, string? Accepted)
    {
        public string Kind => Severity == "FAIL" ? "Failure" : "Warning";
    }
}
