using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Modules.Settings;

public sealed class EveningMastersView : UserControl
{
    private readonly DataGrid brands=Grid(), targets=Grid(), evidence=Grid();
    private readonly TextBox store=Input("Store"),label=Input("DSR row label"),order=Input("Order",true),codes=Input("Source codes, comma-separated"),amount=Input("Monthly target",true);
    private readonly DatePicker month=new(){SelectedDate=DateTime.Today,MinHeight=44};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock targetPermissionNote=new(){Text="Owner permission is required to change monthly targets.",TextWrapping=TextWrapping.Wrap};
    private readonly List<(Button Button,bool OwnerOnly)> accessButtons=[];
    private readonly Func<string> connection;private readonly Func<bool> owner;private readonly Func<bool> canEditBrands;private int rowId;
    public EveningMastersView(Func<string> connection,Func<bool> owner,Func<bool>? canEditBrands=null)
    {
        System.Windows.Automation.AutomationProperties.SetName(brands,"Saved brand rows");
        System.Windows.Automation.AutomationProperties.SetName(targets,"Saved monthly targets");
        System.Windows.Automation.AutomationProperties.SetName(evidence,"Source brand evidence");
        System.Windows.Automation.AutomationProperties.SetName(month,"Target month");
        this.connection=connection;this.owner=owner;this.canEditBrands=canEditBrands??owner;store.Text="WLMHW";order.Text="10";
        brands.SelectionChanged+=(_,_)=>{if(brands.SelectedItem is BrandRowDefinition r){rowId=r.Id;store.Text=r.StoreCode;label.Text=r.Label;order.Text=r.Order.ToString();codes.Text=r.SourceCodes;}};
        targets.SelectionChanged+=(_,_)=>{if(targets.SelectedItem is MonthlyTargetRow r){store.Text=r.StoreCode;month.SelectedDate=r.Month.ToDateTime(TimeOnly.MinValue);amount.Text=r.TargetSales.ToString(CultureInfo.CurrentCulture);}};
        var panel=new StackPanel();panel.Children.Add(new TextBlock{Text="Brand rows and monthly targets",FontSize=18,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock{Text="Each source brand belongs to one row. Unassigned sales remain visible as Other / unmapped.",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(Field("Store code",store));
        var tabs=new TabControl();System.Windows.Automation.AutomationProperties.SetName(tabs,"Brand and target settings");var brandPanel=new StackPanel();brandPanel.Children.Add(brands);
        foreach(var (title,input) in new[]{("Row label",label),("Order",order),("Mapped source codes / names / clusters",codes)})brandPanel.Children.Add(Field(title,input));
        var actions=new WrapPanel();actions.Children.Add(Button("New row",()=>{rowId=0;label.Clear();codes.Clear();return Task.CompletedTask;}));
        actions.Children.Add(Button("Save brand row",async()=>{if(!int.TryParse(order.Text,out var n))throw new ArgumentException("Enter a whole-number order.");await new EveningMasterRepository(connection()).SaveBrandAsync(new(rowId,store.Text,label.Text,n,codes.Text));await Refresh();}));brandPanel.Children.Add(actions);
        brandPanel.Children.Add(new TextBlock{Text="Source brand evidence (read-only)",FontWeight=FontWeights.Bold});brandPanel.Children.Add(evidence);
        var targetPanel=new StackPanel();targetPanel.Children.Add(targets);targetPanel.Children.Add(Field("Target month",month));targetPanel.Children.Add(Field("Monthly store target",amount));
        targetPanel.Children.Add(Button("Save monthly target",async()=>{if(month.SelectedDate is null||!decimal.TryParse(amount.Text,out var value))throw new ArgumentException("Choose a month and enter a target.");await new DataTruthMasterRepository(connection()).SaveMonthlyTargetAsync(new(store.Text,DateOnly.FromDateTime(month.SelectedDate.Value),value));await Refresh();},ownerOnly:true));
        targetPanel.Children.Add(targetPermissionNote);
        targetPanel.Children.Add(new TextBlock{Text="Day target = monthly target ÷ days in month. MTD balance = monthly target − MTD sales. Required daily sales = balance ÷ remaining days, including today.",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,0)});
        tabs.Items.Add(new TabItem{Header="Brand rows",Content=brandPanel});tabs.Items.Add(new TabItem{Header="Monthly targets",Content=targetPanel});panel.Children.Add(tabs);panel.Children.Add(Button("Refresh",Refresh));panel.Children.Add(status);Content=panel;
        RefreshAccessState();
        Loaded+=async(_,_)=>{RefreshAccessState();await Run(Refresh);};
    }
    public void RefreshAccessState()
    {
        foreach(var (button,ownerOnly) in accessButtons)button.IsEnabled=ownerOnly?owner():canEditBrands();
        targetPermissionNote.Visibility=owner()?Visibility.Collapsed:Visibility.Visible;
    }
    private async Task Refresh(){var r=new EveningMasterRepository(connection());brands.ItemsSource=await r.LoadBrandsAsync();targets.ItemsSource=await r.LoadTargetsAsync();evidence.ItemsSource=await r.LoadSourceBrandsAsync();status.Text="Saved rows are shown above.";}
    private async Task Run(Func<Task> action,bool ownerOnly=false){try{if(ownerOnly?!owner():!canEditBrands())throw new UnauthorizedAccessException(ownerOnly?"Owner permission is required.":"Owner or Store Manager permission is required.");IsEnabled=false;await action();}catch(Exception ex){status.Text=DesktopFriendlyError.Describe(ex);}finally{IsEnabled=true;RefreshAccessState();}}
    private Button Button(string title,Func<Task> action,bool ownerOnly=false){var b=new Button{Content=title,MinHeight=44,Margin=new(4),Padding=new(12,4,12,4)};accessButtons.Add((b,ownerOnly));b.Click+=async(_,_)=>await Run(action,ownerOnly);return b;}
    private static DataGrid Grid()=>new(){AutoGenerateColumns=true,IsReadOnly=true,MaxHeight=210,MinHeight=88,RowHeight=44,Margin=new(0,6,0,6)};
    private static TextBox Input(string name,bool numeric=false){var t=new TextBox{MinHeight=44,MinWidth=140};System.Windows.Automation.AutomationProperties.SetName(t,name);if(numeric)t.InputScope=new InputScope{Names={new InputScopeName(InputScopeNameValue.Number)}};return t;}
    private static UIElement Field(string title,UIElement input){var p=new StackPanel{Margin=new(0,4,0,4)};p.Children.Add(new TextBlock{Text=title});p.Children.Add(input);return p;}
}
