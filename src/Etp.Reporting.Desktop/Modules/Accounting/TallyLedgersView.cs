extern alias EtpApplication;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MappingRow = EtpApplication::Etp.Reporting.Application.Accounting.TallyLedgerMappingRow;
using MappingService = EtpApplication::Etp.Reporting.Application.Accounting.ITallyLedgerMappingService;
using ProfileService = EtpApplication::Etp.Reporting.Application.Accounting.ITallyProfileService;

namespace Etp.Reporting.Desktop.Modules.Accounting;

/// <summary>Settings → Integrations → Tally ledgers (Phase 7 plan task 13). Owner only.
/// Lists every version of the ledger names Tally vouchers use, per business event and store, names the ones a store still
/// needs, and approves a new version from a date with a reason. Earlier versions are kept.</summary>
public sealed class TallyLedgersView : UserControl
{
    private readonly Func<string> connectionString;
    private readonly Func<bool> canAdminister;
    private readonly Func<string, ProfileService> profiles;
    private readonly Func<string, MappingService> mappings;
    private IReadOnlyList<MappingRow> loaded = [];
    private bool busy;

    public DataGrid MappingGrid { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 260, MinHeight = 100, RowHeight = 36, Margin = new(0, 6, 0, 8) };
    public ComboBox StoreInput { get; } = new() { MinHeight = 44, Width = 160 };
    public ComboBox EventInput { get; } = new() { MinHeight = 44, Width = 240, IsEditable = true };
    public TextBox LedgerInput { get; } = new() { Width = 300, MinHeight = 44, Padding = new(6), MaxLength = 200 };
    public DatePicker FromInput { get; } = new() { MinHeight = 44, Width = 170, SelectedDate = DateTime.Today };
    public TextBox ReasonInput { get; } = new() { Width = 420, MinHeight = 44, Padding = new(6), MaxLength = 1000 };
    public TextBlock NeededText { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) };
    public TextBlock StatusText { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    public Button SaveButton { get; }

    public TallyLedgersView(Func<string> connectionString, Func<bool> canAdminister, Func<string, ProfileService> profiles, Func<string, MappingService> mappings)
    {
        this.connectionString = connectionString;
        this.canAdminister = canAdminister;
        this.profiles = profiles;
        this.mappings = mappings;
        Column("Business event", nameof(LedgerRow.Event), 190);
        Column("Store", nameof(LedgerRow.Store), 90);
        Column("Tally ledger", nameof(LedgerRow.Ledger), 260);
        Column("From", nameof(LedgerRow.From), 110);
        Column("To", nameof(LedgerRow.To), 110);
        Column("Version", nameof(LedgerRow.Version), 70);
        Column("In use", nameof(LedgerRow.InUse), 70);
        Column("Changed by", nameof(LedgerRow.ChangedBy), 180);
        System.Windows.Automation.AutomationProperties.SetName(MappingGrid, "Tally ledger mappings");
        StoreInput.SelectionChanged += async (_, _) => await RunAsync(ShowNeededAsync);

        var form = new WrapPanel();
        form.Children.Add(Field("Store", StoreInput));
        form.Children.Add(Field("Business event", EventInput));
        form.Children.Add(Field("Tally ledger, exactly as Tally shows it", LedgerInput));
        form.Children.Add(Field("Applies from", FromInput));
        var actions = new WrapPanel();
        actions.Children.Add(Field("Reason for this change", ReasonInput));
        SaveButton = Action("Save ledger", SaveAsync);
        actions.Children.Add(SaveButton);
        actions.Children.Add(Action("Refresh", RefreshAsync));

        var body = new StackPanel();
        foreach (var child in new UIElement[]
                 {
                     new TextBlock
                     {
                         Text = "The Tally ledgers each kind of line in a Tally voucher goes to, per store: one per payment mode, round-off, sales and each GST rate. " +
                                "A change is saved as a new version from a date; earlier versions stay listed. Tally vouchers prepared after the change use the new ledger.",
                         TextWrapping = TextWrapping.Wrap
                     },
                     MappingGrid, NeededText, form, actions, StatusText
                 })
            body.Children.Add(child);
        Content = new Expander { Header = "Tally ledgers", IsExpanded = true, Content = body, Margin = new(0, 18, 0, 0) };
        Loaded += async (_, _) => await RunAsync(RefreshAsync);
    }

    public async Task RefreshAsync()
    {
        loaded = await mappings(connectionString()).LoadAsync();
        MappingGrid.ItemsSource = loaded.Select(row => new LedgerRow(row)).ToArray();
        var stores = (await profiles(connectionString()).LoadAsync()).SelectMany(profile => profile.StoreCodes)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var selected = StoreInput.SelectedItem as string;
        StoreInput.ItemsSource = stores;
        StoreInput.SelectedItem = selected is not null && stores.Contains(selected, StringComparer.Ordinal) ? selected : stores.FirstOrDefault();
        await ShowNeededAsync();
        StatusText.Text = stores.Length == 0 ? "Link a store to a Tally company under Tally companies first." : "Choose a store to see the ledgers it still needs.";
    }

    public async Task ShowNeededAsync()
    {
        if (StoreInput.SelectedItem is not string store) { NeededText.Text = ""; EventInput.ItemsSource = null; return; }
        var needed = await mappings(connectionString()).LoadNeededEventsAsync(store);
        EventInput.ItemsSource = needed;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var missing = needed.Where(code => !loaded.Any(row => row.IsActive && row.BusinessEvent == code
            && (row.StoreCode is null || string.Equals(row.StoreCode, store, StringComparison.Ordinal))
            && row.EffectiveFrom <= today && (row.EffectiveTo is null || row.EffectiveTo >= today))).ToArray();
        NeededText.Text = missing.Length == 0
            ? $"Every ledger the invoices of {store} need has been chosen."
            : $"Still needed for {store}: {string.Join(", ", missing)}. Invoices that need one of these are held back until it is chosen.";
    }

    public async Task SaveAsync()
    {
        if (StoreInput.SelectedItem is not string store) throw new ArgumentException("Choose the store this ledger is for.");
        var from = DateOnly.FromDateTime(FromInput.SelectedDate ?? DateTime.Today);
        await mappings(connectionString()).SaveAsync(store, EventInput.Text, LedgerInput.Text, from, ReasonInput.Text);
        LedgerInput.Clear();
        ReasonInput.Clear();
        await RefreshAsync();
        StatusText.Text = "Ledger saved as a new version. Prepare the Tally vouchers again to use it. Nothing has been sent to Tally.";
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        if (!canAdminister()) { StatusText.Text = "Owner permission is required."; return; }
        busy = true; IsEnabled = false;
        try { await action(); }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.TallyLedgers", "TALLY_LEDGER_OPERATION_FAILED");
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

    private void Column(string header, string binding, double width) =>
        MappingGrid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(binding), Width = width });

    private static StackPanel Field(string label, Control control)
    {
        System.Windows.Automation.AutomationProperties.SetName(control, label);
        var field = new StackPanel { Margin = new(4) };
        field.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(control.Width, 160) });
        field.Children.Add(control);
        return field;
    }

    public sealed record LedgerRow(MappingRow Row)
    {
        public string Event => Row.BusinessEvent;
        public string Store => Row.StoreCode ?? "All stores";
        public string Ledger => string.Equals(Row.DebitLedger, Row.CreditLedger, StringComparison.Ordinal) ? Row.DebitLedger : $"{Row.DebitLedger} / {Row.CreditLedger}";
        public string From => Row.EffectiveFrom.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        public string To => Row.EffectiveTo?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "";
        public int Version => Row.Version;
        public string InUse => Row.IsActive ? "Yes" : "No";
        public string ChangedBy => Row.ChangedBy;
    }
}
