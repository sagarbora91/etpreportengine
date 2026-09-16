using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Modules.Settings;

/// <summary>The reporting masters used by the imports and report queries.</summary>
public sealed class DataTruthMastersView : UserControl
{
    private readonly Func<string> connectionString;
    private readonly Func<bool> canAdminister;
    private readonly DataGrid tenders = Table();
    private readonly DataGrid staff = Table();
    private readonly TextBox sourceCode = Input();
    private readonly TextBox agency = Input(220);
    private readonly ComboBox mode = new() { ItemsSource = DataTruthMasterRepository.Modes, MinHeight = 44, Width = 155, SelectedIndex = 0 };
    private readonly CheckBox tenderActive = new() { Content = "Active", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new(8) };
    private readonly TextBox storeCode = Input();
    private readonly TextBox staffCode = Input();
    private readonly TextBox staffName = Input(220);
    private readonly CheckBox staffActive = new() { Content = "Active", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new(8) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private bool busy;

    public DataTruthMastersView(Func<string> connectionString, Func<bool> canAdminister)
    {
        this.connectionString = connectionString;
        this.canAdminister = canAdminister;
        System.Windows.Automation.AutomationProperties.SetName(tenders, "Tender modes");
        System.Windows.Automation.AutomationProperties.SetName(staff, "Staff master");
        Column(tenders, "Source payment code", nameof(TenderModeRow.SourceCode), 170);
        Column(tenders, "Agency evidence", nameof(TenderModeRow.AgencyName), 235);
        Column(tenders, "Cash-book mode", nameof(TenderModeRow.Mode), 150);
        Column(tenders, "Active", nameof(TenderModeRow.Active), 65);
        Column(staff, "Store", nameof(StaffMasterRow.StoreCode), 100);
        Column(staff, "CRO code", nameof(StaffMasterRow.Code), 130);
        Column(staff, "CRO name", nameof(StaffMasterRow.Name), 260);
        Column(staff, "Active", nameof(StaffMasterRow.Active), 65);
        tenders.SelectionChanged += (_, _) =>
        {
            if (tenders.SelectedItem is not TenderModeRow row) return;
            sourceCode.Text = row.SourceCode; agency.Text = row.AgencyName; mode.SelectedItem = row.Mode; tenderActive.IsChecked = row.Active;
        };
        staff.SelectionChanged += (_, _) =>
        {
            if (staff.SelectedItem is not StaffMasterRow row) return;
            storeCode.Text = row.StoreCode; staffCode.Text = row.Code; staffName.Text = row.Name; staffActive.IsChecked = row.Active;
        };
        var tenderEditor = new WrapPanel();
        tenderEditor.Children.Add(Field("Payment code", sourceCode));
        tenderEditor.Children.Add(Field("Agency", agency));
        tenderEditor.Children.Add(Field("Mode", mode));
        tenderEditor.Children.Add(tenderActive);
        tenderEditor.Children.Add(Button("Save tender mode", async () =>
        {
            await Repository.SaveTenderModeAsync(new(sourceCode.Text, agency.Text, mode.SelectedItem?.ToString() ?? "", tenderActive.IsChecked == true));
            await RefreshDataAsync(); status.Text = "Tender mode saved. Reports now use this mapping.";
        }));
        var staffEditor = new WrapPanel();
        staffEditor.Children.Add(Field("Store", storeCode));
        staffEditor.Children.Add(Field("CRO code", staffCode));
        staffEditor.Children.Add(Field("CRO name", staffName));
        staffEditor.Children.Add(staffActive);
        staffEditor.Children.Add(Button("Save staff", async () =>
        {
            await Repository.SaveStaffAsync(new(storeCode.Text, staffCode.Text, staffName.Text, staffActive.IsChecked == true));
            await RefreshDataAsync(); status.Text = "Staff details saved.";
        }));
        var tabs = new TabControl();
        System.Windows.Automation.AutomationProperties.SetName(tabs, "Reporting master category");
        tabs.Items.Add(new TabItem { Header = "Tender modes", Content = Panel(tenders, tenderEditor) });
        tabs.Items.Add(new TabItem { Header = "Staff", Content = Panel(staff, staffEditor) });
        var body = Panel(Button("Refresh reporting masters", RefreshDataAsync), tabs, status);
        Content = new Expander { Header = "Reporting masters — tender modes and staff", IsExpanded = true, Content = body, Margin = new(0, 18, 0, 0) };
        Loaded += async (_, _) => await RunAsync(RefreshDataAsync);
    }

    private DataTruthMasterRepository Repository => new(connectionString());
    private async Task RefreshDataAsync()
    {
        var repository = Repository;
        tenders.ItemsSource = await repository.LoadTenderModesAsync();
        staff.ItemsSource = await repository.LoadStaffAsync();
        status.Text = "Select a row to edit. Imported CRO names seed the staff list.";
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        if (!canAdminister()) { status.Text = "Owner permission is required."; return; }
        busy = true; IsEnabled = false;
        try { await action(); }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.Masters", "REPORTING_MASTER_OPERATION_FAILED");
            status.Text = DesktopFriendlyError.Describe(exception);
        }
        finally { busy = false; IsEnabled = true; }
    }

    private Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, MinHeight = 44, Padding = new(12, 6, 12, 6), Margin = new(4) };
        button.Click += async (_, _) => await RunAsync(action);
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        return button;
    }
    private static DataGrid Table() => new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 190, MinHeight = 100, RowHeight = 44, Margin = new(0, 6, 0, 8) };
    private static TextBox Input(double width = 130) => new() { Width = width, MinHeight = 44, Padding = new(6) };
    private static void Column(DataGrid grid, string header, string binding, double width) => grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(binding), Width = width });
    private static StackPanel Panel(params UIElement[] children)
    {
        var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); return panel;
    }
    private static StackPanel Field(string label, Control control)
    {
        System.Windows.Automation.AutomationProperties.SetName(control, label);
        var field = Panel(new TextBlock { Text = label }, control); field.Margin = new(4); return field;
    }
}
