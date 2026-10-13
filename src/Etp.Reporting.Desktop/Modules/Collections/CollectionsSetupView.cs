extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Reporting;
using EtpApplication::Etp.Reporting.Application.Collections;

namespace Etp.Reporting.Desktop.Modules.Collections;

/// <summary>
/// Settings > Collections setup (Owner only). Template: Form (ETP-DESIGN-SYSTEM.md section 11 C): one SurfaceCard per section -
/// bank accounts, providers, merchant ids, payment codes, file layouts, statement folder - each a short preview grid of what
/// exists (six rows at most), the fields, a reason (always last, InputWide) and one Save. Bank accounts take the last four
/// digits only. A layout's header comes from "Read header from a file" and "Test on this file" shows what the layout reads,
/// without saving anything. A role other than Owner gets one sentence and no data.
/// </summary>
public sealed class CollectionsSetupView : UserControl
{
    private readonly CollectionsSetupModel model;
    private readonly PageFrame frame = new(PageTemplate.Form);
    private readonly Button refreshButton = CollectionsUi.MakeButton("Refresh", "PrimaryButton");
    private readonly StackPanel sections = new();
    private readonly List<Action> pulls = [];
    private readonly DataGrid accountGrid = CollectionsUi.Grid("Bank accounts");
    private readonly DataGrid providerGrid = CollectionsUi.Grid("Providers");
    private readonly DataGrid merchantGrid = CollectionsUi.Grid("Merchant ids");
    private readonly DataGrid tenderGrid = CollectionsUi.Grid("Payment codes");
    private readonly DataGrid layoutGrid = CollectionsUi.Grid("File layouts");
    private readonly DataGrid testGrid = CollectionsUi.Grid("Test on this file: first lines");
    private readonly TextBlock headersText = CollectionsUi.Text(string.Empty, "CaptionText");
    private readonly TextBlock testText = CollectionsUi.Text(string.Empty, "BodyText");
    private readonly ComboBox accountChoice = new();
    private readonly ComboBox providerChoice = new();
    private readonly ComboBox layoutAccountChoice = new();
    private readonly ComboBox layoutProviderChoice = new();
    private bool rendering;
    private bool loaded;

    public CollectionsSetupView(CollectionsSetupModel model)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        var toolbar = CollectionsUi.Toolbar();
        toolbar.Children.Add(refreshButton);
        frame.Toolbar = toolbar;
        frame.Description = "Bank accounts, payment providers and the layouts of their files. Bank and settlement files cannot be imported until a layout exists for them.";
        AutomationProperties.SetName(refreshButton, "Refresh Collections setup");

        sections.Children.Add(BankAccountSection());
        sections.Children.Add(ProviderSection());
        sections.Children.Add(MerchantSection());
        sections.Children.Add(TenderSection());
        sections.Children.Add(LayoutSection());
        sections.Children.Add(EvidenceSection());
        frame.Body = sections;
        Content = frame;
        AutomationProperties.SetName(this, "Collections setup");

        refreshButton.Click += async (_, _) => await ReloadAsync();
        model.Changed += Render;
        Loaded += async (_, _) => { if (!loaded) { loaded = true; await ReloadAsync(); } };
    }

    public CollectionsSetupModel Model => model;
    internal PageFrame Frame => frame;
    internal StackPanel Sections => sections;

    public async Task ReloadAsync()
    {
        try { await model.LoadAsync(); }
        catch (OperationCanceledException) { }
        Render();
    }

    // ---- sections ------------------------------------------------------------------------------------------------

    private UIElement BankAccountSection()
    {
        var edit = () => model.BankAccount;
        CollectionsUi.AddText(accountGrid, "Name", nameof(AccountRow.Name), minWidth: 180, star: true);
        CollectionsUi.AddText(accountGrid, "Bank", nameof(AccountRow.Bank), minWidth: 140);
        CollectionsUi.AddText(accountGrid, "Last four digits", nameof(AccountRow.Last4), minWidth: 120);
        CollectionsUi.AddText(accountGrid, "Store", nameof(AccountRow.Store), minWidth: 90);
        CollectionsUi.AddText(accountGrid, "Takes cash deposits", nameof(AccountRow.CashDeposits), minWidth: 150);
        CollectionsUi.AddChip(accountGrid, "Status", row => ((AccountRow)row).Active ? ("In use", StatusTone.Success) : ("Switched off", StatusTone.Neutral), minWidth: 120);
        accountGrid.SelectionChanged += (_, _) => { if (!rendering && accountGrid.SelectedItem is AccountRow row) model.EditBankAccount(row.Source); };

        var fields = new WrapPanel();
        fields.Children.Add(CollectionsUi.Field("Account key", Text(() => edit().AccountKey, value => edit().AccountKey = value, "InputMedium", 30)));
        fields.Children.Add(CollectionsUi.Field("Name", Text(() => edit().DisplayName, value => edit().DisplayName = value, "InputWide", 100)));
        fields.Children.Add(CollectionsUi.Field("Bank", Text(() => edit().BankName, value => edit().BankName = value, "InputMedium", 100)));
        fields.Children.Add(CollectionsUi.Field("Last four digits", Text(() => edit().Last4, value => edit().Last4 = value, "InputNarrow", 4)));
        fields.Children.Add(CollectionsUi.Field("Store (blank for all)", Text(() => edit().StoreCode, value => edit().StoreCode = value, "InputNarrow", 30)));
        fields.Children.Add(Check("Takes cash deposits", () => edit().TakesCashDeposits, value => edit().TakesCashDeposits = value));
        fields.Children.Add(Check("In use", () => edit().Active, value => edit().Active = value));
        fields.Children.Add(CollectionsUi.Field("Reason", Text(() => edit().Reason, value => edit().Reason = value, "InputWide", 500)));
        return Section("Bank accounts", CollectionsSetupModel.BankAccountNumberHint, accountGrid, fields,
            [("Save", async () => await model.SaveBankAccountAsync(), true), ("New account", () => { model.NewBankAccount(); return Task.FromResult(true); }, false)]);
    }

    private UIElement ProviderSection()
    {
        var edit = () => model.Provider;
        CollectionsUi.AddText(providerGrid, "Provider", nameof(ProviderRow.Name), minWidth: 160, star: true);
        CollectionsUi.AddText(providerGrid, "Mode", nameof(ProviderRow.Mode), minWidth: 100);
        CollectionsUi.AddText(providerGrid, "Pays", nameof(ProviderRow.Pays), minWidth: 190);
        CollectionsUi.AddText(providerGrid, "Its file shows", nameof(ProviderRow.FileShows), minWidth: 160);
        CollectionsUi.AddText(providerGrid, "Into account", nameof(ProviderRow.Account), minWidth: 160);
        CollectionsUi.AddChip(providerGrid, "Status", row => ((ProviderRow)row).Source.Active ? ("In use", StatusTone.Success) : ("Switched off", StatusTone.Neutral), minWidth: 120);
        providerGrid.SelectionChanged += (_, _) => { if (!rendering && providerGrid.SelectedItem is ProviderRow row) model.EditProvider(row.Source); };

        var fields = new WrapPanel();
        var key = Text(() => edit().ProviderKey, value => edit().ProviderKey = value, "InputMedium", 30);
        fields.Children.Add(CollectionsUi.Field("Provider key", key));
        fields.Children.Add(CollectionsUi.Field("Name", Text(() => edit().DisplayName, value => edit().DisplayName = value, "InputWide", 100)));
        fields.Children.Add(CollectionsUi.Field("Mode", Combo(model.Modes, () => edit().Mode, value => edit().Mode = value)));
        fields.Children.Add(CollectionsUi.Field("Pays into a bank", Combo(model.SettlesVia, () => edit().SettlesVia, value => edit().SettlesVia = value, CollectionsSetupModel.WordsFor)));
        fields.Children.Add(CollectionsUi.Field("Its file shows", Combo(model.StatementKinds, () => edit().StatementKind, value => edit().StatementKind = value, CollectionsSetupModel.WordsFor)));
        fields.Children.Add(CollectionsUi.Field("File lists", Combo(model.Granularities, () => edit().Granularity, value => edit().Granularity = value, CollectionsSetupModel.WordsFor)));
        accountChoice.Width = DesignTokens.Value("InputWide");
        accountChoice.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        accountChoice.SelectionChanged += (_, _) => { if (!rendering && accountChoice.SelectedItem is IdChoice choice) edit().BankAccountId = choice.Id; };
        pulls.Add(() => accountChoice.SelectedItem = accountChoice.Items.OfType<IdChoice>().FirstOrDefault(choice => choice.Id == edit().BankAccountId));
        fields.Children.Add(CollectionsUi.Field("Into account", accountChoice));
        fields.Children.Add(CollectionsUi.Field("Rounding tolerance (₹)", Text(() => edit().RoundingTolerance, value => edit().RoundingTolerance = value, "InputNarrow", 10)));
        fields.Children.Add(Check("In use", () => edit().Active, value => edit().Active = value));
        fields.Children.Add(CollectionsUi.Field("Reason", Text(() => edit().Reason, value => edit().Reason = value, "InputWide", 500)));
        return Section("Providers", "Airpay, PhonePe, the card acquirer and the others, and the payment mode each one settles.", providerGrid, fields,
            [("Save", async () => await model.SaveProviderAsync(), true), ("New provider", () => { model.NewProvider(); return Task.FromResult(true); }, false)]);
    }

    private UIElement MerchantSection()
    {
        var edit = () => model.Merchant;
        CollectionsUi.AddText(merchantGrid, "Provider", nameof(MerchantRow.Provider), minWidth: 140);
        CollectionsUi.AddText(merchantGrid, "Merchant id", nameof(MerchantRow.MerchantId), minWidth: 160, star: true);
        CollectionsUi.AddText(merchantGrid, "Terminal id", nameof(MerchantRow.TerminalId), minWidth: 130);
        CollectionsUi.AddText(merchantGrid, "Store", nameof(MerchantRow.Store), minWidth: 90);
        CollectionsUi.AddChip(merchantGrid, "Status", row => ((MerchantRow)row).Source.Active ? ("In use", StatusTone.Success) : ("Switched off", StatusTone.Neutral), minWidth: 120);
        merchantGrid.SelectionChanged += (_, _) => { if (!rendering && merchantGrid.SelectedItem is MerchantRow row) model.EditMerchant(row.Source); };

        var fields = new WrapPanel();
        providerChoice.Width = DesignTokens.Value("InputMedium");
        providerChoice.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        providerChoice.SelectionChanged += (_, _) => { if (!rendering && providerChoice.SelectedItem is string provider) edit().ProviderKey = provider; };
        pulls.Add(() => providerChoice.SelectedItem = edit().ProviderKey.Length == 0 ? null : edit().ProviderKey);
        fields.Children.Add(CollectionsUi.Field("Provider", providerChoice));
        fields.Children.Add(CollectionsUi.Field("Merchant id", Text(() => edit().MerchantId, value => edit().MerchantId = value, "InputMedium", 50)));
        fields.Children.Add(CollectionsUi.Field("Terminal id (optional)", Text(() => edit().TerminalId, value => edit().TerminalId = value, "InputMedium", 50)));
        fields.Children.Add(CollectionsUi.Field("Store", Text(() => edit().StoreCode, value => edit().StoreCode = value, "InputNarrow", 30)));
        fields.Children.Add(Check("In use", () => edit().Active, value => edit().Active = value));
        fields.Children.Add(CollectionsUi.Field("Reason", Text(() => edit().Reason, value => edit().Reason = value, "InputWide", 500)));
        return Section("Merchant ids", "A merchant id (and terminal id) tells ETP which store a provider's settlement line belongs to.", merchantGrid, fields,
            [("Save", async () => await model.SaveMerchantAsync(), true), ("New merchant id", () => { model.NewMerchant(); return Task.FromResult(true); }, false)]);
    }

    private UIElement TenderSection()
    {
        var edit = () => model.Tender;
        CollectionsUi.AddText(tenderGrid, "Payment code", nameof(TenderRow.Code), minWidth: 170, star: true);
        CollectionsUi.AddText(tenderGrid, "Mode", nameof(TenderRow.Mode), minWidth: 110);
        CollectionsUi.AddChip(tenderGrid, "Provider", row => ((TenderRow)row).Source.ProviderKey is { } key ? (key, StatusTone.Neutral) : (CollectionsScreens.ProviderNotSetUpChip, StatusTone.Warning), minWidth: 190);
        CollectionsUi.AddText(tenderGrid, "Till cash", nameof(TenderRow.TillCash), minWidth: 90);
        tenderGrid.SelectionChanged += (_, _) => { if (!rendering && tenderGrid.SelectedItem is TenderRow row) model.EditTender(row.Source); };

        var fields = new WrapPanel();
        fields.Children.Add(CollectionsUi.Field("Payment code", ReadOnlyText(() => edit().SourceTenderCode, "InputMedium")));
        var tenderProvider = new ComboBox { Width = DesignTokens.Value("InputMedium") };
        tenderProvider.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        tenderProvider.SelectionChanged += (_, _) => { if (!rendering) edit().ProviderKey = tenderProvider.SelectedItem as string ?? string.Empty; };
        pulls.Add(() => { tenderProvider.ItemsSource = new[] { string.Empty }.Concat(model.Setup?.Providers.Select(provider => provider.ProviderKey) ?? []).ToList(); tenderProvider.SelectedItem = edit().ProviderKey; });
        fields.Children.Add(CollectionsUi.Field("Provider (blank for none)", tenderProvider));
        fields.Children.Add(Check("Physical till cash", () => edit().IsTillCash, value => edit().IsTillCash = value));
        fields.Children.Add(CollectionsUi.Field("Reason", Text(() => edit().Reason, value => edit().Reason = value, "InputWide", 500)));
        return Section("Payment codes", "Choose a payment code in the list, then the provider that settles it. A code with no provider shows \"Provider not set up\" on the statement.", tenderGrid, fields,
            [("Save", async () => await model.SaveTenderAsync(), true)]);
    }

    private UIElement LayoutSection()
    {
        var edit = () => model.Layout;
        CollectionsUi.AddText(layoutGrid, "Layout", nameof(LayoutRow.Name), minWidth: 180, star: true);
        CollectionsUi.AddText(layoutGrid, "For", nameof(LayoutRow.For), minWidth: 200);
        CollectionsUi.AddText(layoutGrid, "Header row", nameof(LayoutRow.HeaderRow), number: true, minWidth: 100);
        CollectionsUi.AddChip(layoutGrid, "Status", row => ((LayoutRow)row).Source.Active ? ("In use", StatusTone.Success) : ("Switched off", StatusTone.Neutral), minWidth: 120);
        layoutGrid.SelectionChanged += (_, _) => { if (!rendering && layoutGrid.SelectedItem is LayoutRow row) { model.EditLayout(row.Source); testGrid.ItemsSource = null; } };

        var fields = new WrapPanel();
        fields.Children.Add(CollectionsUi.Field("Layout key", Text(() => edit().LayoutKey, value => edit().LayoutKey = value, "InputMedium", 30)));
        fields.Children.Add(CollectionsUi.Field("Name", Text(() => edit().DisplayName, value => edit().DisplayName = value, "InputWide", 100)));
        fields.Children.Add(CollectionsUi.Field("Kind", Combo(model.LayoutKinds, () => edit().LayoutKind, value => edit().LayoutKind = value, kind => kind == "BANK" ? "Bank statement" : "Provider settlement")));
        layoutAccountChoice.Width = DesignTokens.Value("InputWide");
        layoutAccountChoice.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        layoutAccountChoice.SelectionChanged += (_, _) => { if (!rendering && layoutAccountChoice.SelectedItem is IdChoice choice) edit().BankAccountId = choice.Id; };
        pulls.Add(() => layoutAccountChoice.SelectedItem = layoutAccountChoice.Items.OfType<IdChoice>().FirstOrDefault(choice => choice.Id == edit().BankAccountId));
        fields.Children.Add(CollectionsUi.Field("Bank account (bank files)", layoutAccountChoice));
        layoutProviderChoice.Width = DesignTokens.Value("InputMedium");
        layoutProviderChoice.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        layoutProviderChoice.SelectionChanged += (_, _) => { if (!rendering && layoutProviderChoice.SelectedItem is string provider) edit().ProviderKey = provider; };
        pulls.Add(() => layoutProviderChoice.SelectedItem = edit().ProviderKey.Length == 0 ? null : edit().ProviderKey);
        fields.Children.Add(CollectionsUi.Field("Provider (settlement files)", layoutProviderChoice));
        fields.Children.Add(CollectionsUi.Field("Sheet name (blank for the first)", Text(() => edit().SheetName, value => edit().SheetName = value, "InputMedium", 100)));
        fields.Children.Add(CollectionsUi.Field("Header row", Text(() => edit().HeaderRow.ToString(PresentationCulture.Indian), value => { if (int.TryParse(value, out var row)) edit().HeaderRow = row; }, "InputNarrow", 4)));
        fields.Children.Add(CollectionsUi.Field("Footer lines to skip", Text(() => edit().SkipFooterRows.ToString(PresentationCulture.Indian), value => { if (int.TryParse(value, out var rows)) edit().SkipFooterRows = rows; }, "InputNarrow", 4)));
        fields.Children.Add(Check("In use", () => edit().Active, value => edit().Active = value));

        var mapping = CollectionsUi.Field("Column map (names of the file's columns)", Text(() => edit().MappingJson, value => edit().MappingJson = value, "InputWide", 4000, multiline: true));
        ((TextBox)mapping.Children[1]).Width = DesignTokens.Value("InputWide") * 2;

        var sample = new WrapPanel { Margin = DesignTokens.Gap(null, "Space8", null, null) };
        var read = CollectionsUi.MakeButton("Read header from a file");
        var test = CollectionsUi.MakeButton("Test on this file");
        read.Click += async (_, _) => { await model.ReadSampleAsync(); Render(); };
        test.Click += async (_, _) => { await model.TestOnFileAsync(); Render(); };
        sample.Children.Add(read); sample.Children.Add(test);

        CollectionsUi.AddText(testGrid, "Row", nameof(LayoutTestRow.Row), number: true, minWidth: 60);
        CollectionsUi.AddText(testGrid, "Date", nameof(LayoutTestRow.Date), minWidth: 110);
        CollectionsUi.AddText(testGrid, "Reference", nameof(LayoutTestRow.Reference), minWidth: 140);
        CollectionsUi.AddText(testGrid, IndianFormat.MoneyHeader("Credit"), nameof(LayoutTestRow.Credit), number: true, minWidth: 110);
        CollectionsUi.AddText(testGrid, IndianFormat.MoneyHeader("Debit"), nameof(LayoutTestRow.Debit), number: true, minWidth: 110);
        CollectionsUi.AddText(testGrid, IndianFormat.MoneyHeader("Gross"), nameof(LayoutTestRow.Gross), number: true, minWidth: 110);
        CollectionsUi.AddText(testGrid, IndianFormat.MoneyHeader("Net"), nameof(LayoutTestRow.Net), number: true, minWidth: 110);
        CollectionsUi.AddText(testGrid, "Kind", nameof(LayoutTestRow.Kind), minWidth: 90);
        CollectionsUi.AddChip(testGrid, "Result", row => ((LayoutTestRow)row).Readable ? ("Read", StatusTone.Success) : (((LayoutTestRow)row).Result, StatusTone.Critical), minWidth: 160);
        testGrid.Visibility = Visibility.Collapsed;

        var extra = new StackPanel();
        extra.Children.Add(mapping);
        extra.Children.Add(sample);
        extra.Children.Add(headersText);
        extra.Children.Add(testText);
        extra.Children.Add(testGrid);
        var reason = CollectionsUi.Field("Reason", Text(() => edit().Reason, value => edit().Reason = value, "InputWide", 500));
        var body = new StackPanel();
        body.Children.Add(fields);
        body.Children.Add(extra);
        body.Children.Add(reason);
        return Section("File layouts", "A layout says which column of a bank's or provider's file holds what. Read the header from a sample file first; the header is never typed.", layoutGrid, body,
            [("Save", async () => await model.SaveLayoutAsync(), true), ("New bank layout", () => { model.NewLayout("BANK"); return Task.FromResult(true); }, false),
             ("New provider layout", () => { model.NewLayout("PROVIDER"); return Task.FromResult(true); }, false)]);
    }

    private UIElement EvidenceSection()
    {
        var fields = new WrapPanel();
        fields.Children.Add(CollectionsUi.Field("Folder on this PC", Text(() => model.EvidenceRoot, value => model.EvidenceRoot = value, "InputWide", 400)));
        fields.Children.Add(CollectionsUi.Field("Reason", Text(() => model.EvidenceReason, value => model.EvidenceReason = value, "InputWide", 500)));
        return Section("Statement folder", "ETP keeps a copy of every imported statement file here. Setup gives the Owner and Store Managers access to it.", null, fields,
            [("Save", async () => await model.SaveEvidenceRootAsync(), true)]);
    }

    // ---- building blocks -----------------------------------------------------------------------------------------

    private Border Section(string title, string caption, DataGrid? preview, UIElement form, IReadOnlyList<(string Label, Func<Task<bool>> Action, bool Primary)> buttons)
    {
        var stack = new StackPanel();
        if (preview is not null)
        {
            preview.Margin = DesignTokens.Gap(null, null, null, "Space12");
            stack.Children.Add(preview);
        }
        stack.Children.Add(form);
        var row = new WrapPanel { Margin = DesignTokens.Gap(null, "Space8", null, null) };
        foreach (var (label, action, primary) in buttons)
        {
            var button = CollectionsUi.MakeButton(label, primary ? "PrimaryButton" : null, primary ? $"Save {title.ToLowerInvariant()}" : null);
            button.Click += async (_, _) =>
            {
                try { await action(); }
                catch (OperationCanceledException) { }
                Render();
            };
            row.Children.Add(button);
        }
        stack.Children.Add(row);
        return CollectionsUi.Card(title, stack, caption);
    }

    private TextBox Text(Func<string> get, Action<string> set, string widthToken, int max, bool multiline = false)
    {
        var box = CollectionsUi.Input(widthToken, max, multiline);
        var refresh = () => { var value = get(); if (box.Text != value) box.Text = value; };
        pulls.Add(refresh);
        box.TextChanged += (_, _) => { if (!rendering) set(box.Text); };
        refresh();
        return box;
    }

    private TextBox ReadOnlyText(Func<string> get, string widthToken)
    {
        var box = CollectionsUi.Input(widthToken, 80);
        box.IsReadOnly = true;
        pulls.Add(() => box.Text = get());
        return box;
    }

    private ComboBox Combo(IReadOnlyList<string> items, Func<string> get, Action<string> set, Func<string, string>? words = null)
    {
        var box = new ComboBox { Width = DesignTokens.Value("InputMedium"), ItemsSource = items.Select(item => new CodeChoice(item, words?.Invoke(item) ?? item)).ToList(), DisplayMemberPath = nameof(CodeChoice.Words) };
        box.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        box.SelectionChanged += (_, _) => { if (!rendering && box.SelectedItem is CodeChoice choice) set(choice.Code); };
        pulls.Add(() => box.SelectedItem = box.Items.OfType<CodeChoice>().FirstOrDefault(choice => choice.Code == get()));
        return box;
    }

    private StackPanel Check(string label, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Content = label };
        box.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        box.Click += (_, _) => set(box.IsChecked == true);
        pulls.Add(() => box.IsChecked = get());
        var host = new StackPanel { Margin = DesignTokens.Gap(null, "Space16", "Space16", "Space8") };
        host.Children.Add(box);
        return host;
    }

    // ---- rendering -----------------------------------------------------------------------------------------------

    private void Render()
    {
        if (rendering) return;
        rendering = true;
        try
        {
            sections.Visibility = model.IsAllowed ? Visibility.Visible : Visibility.Collapsed;
            CollectionsUi.SetEnabled(refreshButton, model.IsAllowed, model.RefusalText ?? string.Empty);
            if (!model.IsAllowed) frame.Notice.Show(model.Access.HasAssignedRole ? StatusTone.Information : StatusTone.Critical, model.RefusalText!);
            else if (model.Message.Length > 0) frame.Notice.Show(model.MessageTone, model.Message);
            else frame.Notice.Clear();
            var setup = model.Setup;
            if (setup is null) return;

            Fill(accountGrid, setup.BankAccounts.Select(row => new AccountRow(row)).ToList());
            Fill(providerGrid, setup.Providers.Select(row => new ProviderRow(row, model)).ToList());
            Fill(merchantGrid, setup.MerchantIds.Select(row => new MerchantRow(row)).ToList());
            Fill(tenderGrid, setup.TenderCodes.Select(row => new TenderRow(row)).ToList());
            Fill(layoutGrid, setup.Layouts.Select(row => new LayoutRow(row, setup)).ToList());

            accountChoice.ItemsSource = new[] { new IdChoice(null, "No account") }.Concat(setup.BankAccounts.Select(account => new IdChoice(account.Id, CollectionsScreens.MaskDigits(account.DisplayName)))).ToList();
            accountChoice.DisplayMemberPath = nameof(IdChoice.Name);
            layoutAccountChoice.ItemsSource = setup.BankAccounts.Select(account => new IdChoice(account.Id, CollectionsScreens.MaskDigits(account.DisplayName))).ToList();
            layoutAccountChoice.DisplayMemberPath = nameof(IdChoice.Name);
            providerChoice.ItemsSource = setup.Providers.Select(provider => provider.ProviderKey).ToList();
            layoutProviderChoice.ItemsSource = setup.Providers.Select(provider => provider.ProviderKey).ToList();

            var layout = model.Layout;
            headersText.Text = layout.Headers.Count == 0 ? string.Empty : "Columns in the sample: " + string.Join(" · ", layout.Headers.Select(CollectionsScreens.MaskDigits));
            headersText.Visibility = headersText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (layout.TestResult is { } result)
            {
                testText.Text = CollectionsSetupModel.TestSentence(result);
                var rows = CollectionsSetupModel.TestRows(result);
                testGrid.ItemsSource = rows; CollectionsUi.SizePreview(testGrid, rows.Count);
                testGrid.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            else { testText.Text = string.Empty; testGrid.ItemsSource = null; testGrid.Visibility = Visibility.Collapsed; }
            foreach (var pull in pulls) pull();
        }
        finally { rendering = false; }
    }

    private static void Fill<T>(DataGrid grid, List<T> rows)
    {
        grid.ItemsSource = rows;
        CollectionsUi.SizePreview(grid, rows.Count);
    }

    private sealed record CodeChoice(string Code, string Words);
    private sealed record IdChoice(int? Id, string Name);

    private sealed class AccountRow(BankAccountRow source)
    {
        public BankAccountRow Source { get; } = source;
        public string Name => CollectionsScreens.MaskDigits(Source.DisplayName);
        public string Bank => CollectionsScreens.MaskDigits(Source.BankName);
        public string Last4 => Source.AccountLast4 ?? CollectionsScreens.MissingValue;
        public string Store => Source.StoreCode ?? "All";
        public string CashDeposits => Source.TakesCashDeposits ? "Yes" : "No";
        public bool Active => Source.Active;
    }

    private sealed class ProviderRow(CollectionProviderRow source, CollectionsSetupModel model)
    {
        public CollectionProviderRow Source { get; } = source;
        public string Name => Source.DisplayName;
        public string Mode => Source.Mode;
        public string Pays => CollectionsSetupModel.WordsFor(Source.SettlesVia);
        public string FileShows => CollectionsSetupModel.WordsFor(Source.StatementKind);
        public string Account => model.AccountName(Source.BankAccountId);
    }

    private sealed class MerchantRow(MerchantIdRow source)
    {
        public MerchantIdRow Source { get; } = source;
        public string Provider => Source.ProviderKey;
        public string MerchantId => Source.MerchantId;
        public string TerminalId => Source.TerminalId ?? CollectionsScreens.MissingValue;
        public string Store => Source.StoreCode;
    }

    private sealed class TenderRow(TenderProviderRow source)
    {
        public TenderProviderRow Source { get; } = source;
        public string Code => Source.SourceTenderCode;
        public string Mode => Source.Mode;
        public string TillCash => Source.IsTillCash ? "Yes" : "No";
    }

    private sealed class LayoutRow(StatementLayoutRow source, CollectionsSetup setup)
    {
        public StatementLayoutRow Source { get; } = source;
        public string Name => Source.DisplayName;
        public string For => Source.BankAccountId is { } id
            ? CollectionsScreens.MaskDigits(setup.BankAccounts.FirstOrDefault(account => account.Id == id)?.DisplayName ?? CollectionsScreens.MissingValue)
            : Source.ProviderKey ?? CollectionsScreens.MissingValue;
        public string HeaderRow => Source.HeaderRow.ToString(PresentationCulture.Indian);
    }
}
