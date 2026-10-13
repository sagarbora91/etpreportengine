extern alias EtpApplication;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TallyConnectedMode = EtpApplication::Etp.Reporting.Application.Accounting.TallyConnectedMode;
using TallyProfile = EtpApplication::Etp.Reporting.Application.Accounting.TallyProfile;
using TallyProfileOptions = EtpApplication::Etp.Reporting.Application.Accounting.TallyProfileOptions;
using TallyProfileService = EtpApplication::Etp.Reporting.Application.Accounting.ITallyProfileService;

namespace Etp.Reporting.Desktop.Modules.Accounting;

/// <summary>Settings → Integrations → Tally companies (Phase 7 plan task 1). Owner only.
/// Lists the Tally companies ETP may write vouchers for and edits one at a time with a reason.
/// Nothing here sends anything to Tally or switches on live books.
/// 1.12 U3: the delivery choice, the fixed test address (port 9000, read only), the build label with its EDUCATIONAL hint and the
/// test PC name (beside "This PC is ...") are edited here. Every edit is Owner only.</summary>
public sealed class TallyCompaniesView : UserControl
{
    /// <summary>The one address ETP may use (decision 53). Shown read only; never typed.</summary>
    public const string FixedEndpoint = "http://127.0.0.1:9000/";
    public const string DeliveryFileWords = "Write a file";
    public const string DeliveryHttpWords = "Send to Tally on this PC";
    public const string EducationalHint = "For a test Tally in Educational mode, include the word EDUCATIONAL in the build (for example \"TallyPrime 5.0 EDUCATIONAL\"). ETP then writes vouchers only for the days Educational mode allows, and sends only when Tally itself says it is Educational.";

    private readonly Func<bool> canAdminister;
    private readonly Func<TallyProfileService> serviceFactory;
    private readonly Func<string> machineName;
    private IReadOnlyList<TallyProfile> loaded = [];
    // The company being edited, so fields this screen does not show (format, posting dates,
    // live-books approval) are saved unchanged instead of being reset.
    private TallyProfile editing = TallyProfile.NewTest("", "", []);
    private bool busy;

    public DataGrid CompanyGrid { get; } = NewGrid();
    public TextBox CodeInput { get; } = Input(160);
    public TextBox CompanyInput { get; } = Input(320);
    public ComboBox BooksInput { get; } = Choice(TallyProfileOptions.Environments, 160);
    public TextBox StoresInput { get; } = Input(220);
    public TextBox CostCentresInput { get; } = Input(320);
    public ComboBox DeliveryInput { get; } = new() { ItemsSource = new[] { DeliveryFileWords, DeliveryHttpWords }, Width = 240, SelectedIndex = 0 };
    public TextBox EndpointInput { get; } = new() { Width = 220, IsReadOnly = true, Focusable = false };
    public TextBox TestPcInput { get; } = Input(220);
    public TextBlock ThisPcText { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(4, 0, 4, 0) };
    public TextBlock BuildHintText { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(4, 0, 4, 0), MaxWidth = 640, Text = EducationalHint };
    public StatusChip BooksChip { get; } = new("TEST books", StatusTone.Information);
    public Button UseThisPcButton { get; }
    public ComboBox GranularityInput { get; } = Choice(TallyProfileOptions.VoucherGranularities, 200);
    public ComboBox PartyInput { get; } = Choice(TallyProfileOptions.PartyPolicies, 200);
    public TextBox LedgerInput { get; } = Input(220);
    public ComboBox TenderInput { get; } = Choice(TallyProfileOptions.TenderModels, 200);
    public ComboBox PostingInput { get; } = Choice(TallyProfileOptions.PostingModels, 200);
    public ComboBox ViewInput { get; } = Choice(TallyProfileOptions.VoucherViews, 200);
    public TextBox BuildInput { get; } = Input(220);
    public CheckBox EnabledInput { get; } = new() { Content = "Use this company", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new(8) };
    public TextBox ReasonInput { get; } = Input(420);
    public TextBlock StatusText { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    public Button SaveButton { get; }

    public TallyCompaniesView(Func<bool> canAdminister, Func<TallyProfileService> serviceFactory, Func<string>? machineName = null)
    {
        this.canAdminister = canAdminister;
        this.serviceFactory = serviceFactory;
        this.machineName = machineName ?? (() => Environment.MachineName);
        ThisPcText.SetResourceReference(StyleProperty, "CaptionText");
        BuildHintText.SetResourceReference(StyleProperty, "CaptionText");
        ThisPcText.Text = $"This PC is {this.machineName()}.";
        System.Windows.Automation.AutomationProperties.SetName(BooksChip, "Books of the company being edited");
        BooksInput.SelectionChanged += (_, _) => ShowBooks();
        DeliveryInput.SelectionChanged += (_, _) => { if (DeliveryInput.SelectedItem as string == DeliveryHttpWords) EndpointInput.Text = FixedEndpoint; };
        System.Windows.Automation.AutomationProperties.SetName(CompanyGrid, "Tally companies");
        Column("Short code", nameof(TallyCompanyRow.Code), 120);
        Column("Tally company name", nameof(TallyCompanyRow.Company), 260);
        Column("Books", nameof(TallyCompanyRow.Books), 110);
        Column("Stores covered", nameof(TallyCompanyRow.Stores), 150);
        Column("In use", nameof(TallyCompanyRow.InUse), 70);
        Column("Last changed by", nameof(TallyCompanyRow.ChangedBy), 180);
        CompanyGrid.SelectionChanged += (_, _) => { if (CompanyGrid.SelectedItem is TallyCompanyRow row) Show(row.Profile); };
        ReasonInput.MaxLength = 500;

        var identity = new WrapPanel();
        identity.Children.Add(Field("Short code (letters and digits)", CodeInput));
        identity.Children.Add(Field("Tally company name, exactly as Tally shows it", CompanyInput));
        identity.Children.Add(Field("Test books or live books", BooksInput));
        identity.Children.Add(Field("Stores covered (codes, separated by commas)", StoresInput));
        identity.Children.Add(Field("Cost centre per store, as in Tally (D12): STORE=Name; STORE=Name", CostCentresInput));
        identity.Children.Add(Field("How vouchers reach Tally", DeliveryInput));
        identity.Children.Add(Field("Where the test Tally answers on this PC (fixed)", EndpointInput));
        identity.Children.Add(Field("Tally build, as Tally shows it (optional)", BuildInput));
        UseThisPcButton = Action("Use this PC", () => { TestPcInput.Text = this.machineName(); return Task.CompletedTask; });
        UseThisPcButton.SetResourceReference(StyleProperty, "QuietButton");
        var testPc = Field("Test PC: the one PC that may send to Tally", TestPcInput);
        testPc.Children.Add(ThisPcText);
        testPc.Children.Add(UseThisPcButton);
        identity.Children.Add(testPc);
        identity.Children.Add(BooksChip);
        identity.Children.Add(EnabledInput);

        var policy = new WrapPanel();
        policy.Children.Add(Field("Vouchers: per invoice or daily summary (D13)", GranularityInput));
        policy.Children.Add(Field("Customers: one ledger or named ledgers (D14)", PartyInput));
        policy.Children.Add(Field("Ledger for retail customers (D14)", LedgerInput));
        policy.Children.Add(Field("Payments: inside the voucher or clearing ledger (D16)", TenderInput));
        policy.Children.Add(Field("Accounting only or with stock items (D17)", PostingInput));
        policy.Children.Add(Field("Voucher view of the sample voucher", ViewInput));

        var actions = new WrapPanel();
        actions.Children.Add(Field("Reason for this change", ReasonInput));
        SaveButton = Action("Save Tally company", SaveAsync);
        actions.Children.Add(SaveButton);
        actions.Children.Add(Action("New test company", () => { ShowNew(); return Task.CompletedTask; }));
        actions.Children.Add(Action("Refresh", RefreshAsync));

        var body = Panel(
            new TextBlock
            {
                Text = "The Tally companies ETP may prepare vouchers for. Start with a test company. Saving here sends nothing to Tally, " +
                       "and live books cannot be switched on from this screen. The choices marked D13–D17 wait for the owner's and accountant's decision; " +
                       "the defaults are the assumptions the first Tally steps are built on.",
                TextWrapping = TextWrapping.Wrap
            },
            CompanyGrid, Heading("Company"), identity, BuildHintText, Heading("Accounting choices"), policy, actions, StatusText);
        Content = new Expander { Header = "Tally companies", IsExpanded = true, Content = body, Margin = new(0, 18, 0, 0) };
        ShowNew();
        Loaded += async (_, _) => await RunAsync(RefreshAsync);
    }

    public async Task RefreshAsync()
    {
        loaded = await serviceFactory().LoadAsync();
        CompanyGrid.ItemsSource = loaded.Select(profile => new TallyCompanyRow(profile)).ToArray();
        StatusText.Text = loaded.Count == 0
            ? "No Tally company yet. Fill in a test company below and save it with a reason."
            : "Select a company to change it, or start a new test company.";
    }

    public async Task SaveAsync()
    {
        var stores = StoresInput.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var profile = editing with
        {
            ProfileCode = CodeInput.Text, CompanyName = CompanyInput.Text, Environment = Selected(BooksInput), EndpointUrl = EndpointInput.Text,
            DefaultDeliveryMode = DeliveryInput.SelectedItem as string == DeliveryHttpWords ? "HTTP" : "FILE", TestMachineName = TestPcInput.Text,
            VoucherGranularity = Selected(GranularityInput), PartyPolicy = Selected(PartyInput), SinglePartyLedger = LedgerInput.Text,
            TenderModel = Selected(TenderInput), PostingModel = Selected(PostingInput), VoucherView = Selected(ViewInput),
            TallyBuildLabel = BuildInput.Text, IsEnabled = EnabledInput.IsChecked == true, StoreCodes = stores,
            StoreCostCentres = ParseCostCentres(CostCentresInput.Text)
        };
        var id = await serviceFactory().SaveAsync(profile, ReasonInput.Text);
        ReasonInput.Clear();
        await RefreshAsync();
        var saved = loaded.FirstOrDefault(item => item.Id == id);
        if (saved is not null) Show(saved);
        StatusText.Text = "Tally company saved. Nothing has been sent to Tally.";
    }

    private void Show(TallyProfile profile)
    {
        editing = profile;
        CodeInput.Text = profile.ProfileCode; CompanyInput.Text = profile.CompanyName; BooksInput.SelectedItem = profile.Environment;
        StoresInput.Text = string.Join(", ", profile.StoreCodes);
        CostCentresInput.Text = string.Join("; ", (profile.StoreCostCentres ?? new Dictionary<string, string>()).Select(pair => $"{pair.Key}={pair.Value}"));
        EndpointInput.Text = profile.EndpointUrl ?? "";
        DeliveryInput.SelectedItem = profile.DefaultDeliveryMode == "HTTP" ? DeliveryHttpWords : DeliveryFileWords;
        TestPcInput.Text = profile.TestMachineName ?? "";
        ThisPcText.Text = $"This PC is {machineName()}." + (TallyConnectedMode.TestPcRefusal(profile, machineName()) is null ? " Sending is allowed here." : "");
        GranularityInput.SelectedItem = profile.VoucherGranularity; PartyInput.SelectedItem = profile.PartyPolicy;
        LedgerInput.Text = profile.SinglePartyLedger ?? ""; TenderInput.SelectedItem = profile.TenderModel;
        PostingInput.SelectedItem = profile.PostingModel; ViewInput.SelectedItem = profile.VoucherView;
        BuildInput.Text = profile.TallyBuildLabel ?? ""; EnabledInput.IsChecked = profile.IsEnabled;
        ShowBooks();
    }

    /// <summary>TEST books are Information; LIVE books are a Warning, each with its word (ST1). Only a TEST company can name a test PC.</summary>
    private void ShowBooks()
    {
        var live = Selected(BooksInput) == "PRODUCTION";
        BooksChip.Text = live ? "LIVE books" : "TEST books";
        BooksChip.Tone = live ? StatusTone.Warning : StatusTone.Information;
        TestPcInput.IsEnabled = !live;
        UseThisPcButton.IsEnabled = !live;
    }

    private void ShowNew()
    {
        CompanyGrid.SelectedItem = null;
        Show(TallyProfile.NewTest("", "", []));
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        if (!canAdminister()) { StatusText.Text = "Owner permission is required."; return; }
        busy = true; IsEnabled = false;
        try { await action(); }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.TallyCompanies", "TALLY_COMPANY_OPERATION_FAILED");
            StatusText.Text = DesktopFriendlyError.Describe(exception);
        }
        finally { busy = false; IsEnabled = true; }
    }

    private Button Action(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new(4), VerticalAlignment = VerticalAlignment.Bottom };
        button.Click += async (_, _) => await RunAsync(action);
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        return button;
    }

    private void Column(string header, string binding, double width) =>
        CompanyGrid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(binding), Width = width });

    /// <summary>Reads "STORE=Name; STORE=Name". Store codes are checked against the stores covered when saving.</summary>
    public static IReadOnlyDictionary<string, string> ParseCostCentres(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (text ?? "").Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || pair[0].Length == 0 || pair[1].Length == 0)
                throw new ArgumentException("Write each cost centre as STORE=Name, separated by semicolons, with the store code before '=' and the Tally cost centre after it.");
            if (!result.TryAdd(pair[0], pair[1]))
                throw new ArgumentException($"Store {pair[0].ToUpperInvariant()} has more than one cost centre.");
        }
        return result;
    }

    private static string Selected(ComboBox box) => box.SelectedItem?.ToString() ?? "";
    private static TextBox Input(double width) => new() { Width = width };
    private static ComboBox Choice(IReadOnlyList<string> items, double width) => new() { ItemsSource = items, Width = width, SelectedIndex = 0 };

    private static DataGrid NewGrid()
    {
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MinHeight = 160, Margin = new(0, 6, 0, 8) };
        grid.SetResourceReference(DataGrid.RowHeightProperty, "ActiveGridRowHeight");
        return grid;
    }

    private static TextBlock Heading(string text)
    {
        var heading = new TextBlock { Text = text, Margin = new(0, 10, 0, 0) };
        heading.SetResourceReference(StyleProperty, "CardTitleText");
        return heading;
    }

    private static StackPanel Panel(params UIElement[] children)
    {
        var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); return panel;
    }

    private static StackPanel Field(string label, Control control)
    {
        System.Windows.Automation.AutomationProperties.SetName(control, label);
        var field = Panel(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(control.Width, 160) }, control);
        field.Margin = new(4); return field;
    }

    public sealed record TallyCompanyRow(TallyProfile Profile)
    {
        public string Code => Profile.ProfileCode;
        public string Company => Profile.CompanyName;
        public string Books => Profile.Environment == "PRODUCTION"
            ? Profile.ProductionEnabledUtc is null ? "Live (not enabled)" : "Live"
            : "Test";
        public string Stores => Profile.StoreCodes.Count == 0 ? "—" : string.Join(", ", Profile.StoreCodes);
        public string InUse => Profile.IsEnabled ? "Yes" : "No";
        public string ChangedBy => Profile.ModifiedBy ?? "";
    }
}
