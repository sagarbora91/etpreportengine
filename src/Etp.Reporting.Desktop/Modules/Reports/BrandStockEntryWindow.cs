using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Modules.Reports;

public sealed class BrandStockEntryWindow : Window
{
    private readonly DataGrid grid=new(){AutoGenerateColumns=true,IsReadOnly=true,RowHeight=44,MaxHeight=300};
    private readonly TextBox[] values=Enumerable.Range(0,4).Select(_=>new TextBox{MinHeight=44,MinWidth=100,InputScope=new InputScope{Names={new InputScopeName(InputScopeNameValue.Number)}}}).ToArray();
    private readonly TextBox remarks=new(){MinHeight=44},reason=new(){Text="Evening physical count",MinHeight=44};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly string connection,store;private readonly DateOnly date;
    public BrandStockEntryWindow(string connection,string store,DateOnly date)
    {
        System.Windows.Automation.AutomationProperties.SetName(grid,"Brand counts");System.Windows.Automation.AutomationProperties.SetName(remarks,"Stock remark");System.Windows.Automation.AutomationProperties.SetName(reason,"Stock change reason");
        this.connection=connection;this.store=store;this.date=date;Title=$"{store} — physical stock — {date:dd MMM yyyy}";Width=900;Height=700;MinWidth=600;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new(16)};panel.Children.Add(new TextBlock{Text="Select a brand, check yesterday's counts, then save today's count.",TextWrapping=TextWrapping.Wrap});panel.Children.Add(grid);
        var inputs=new WrapPanel();var labels=new[]{"Display","Backstock","Defective","Y Loc"};for(var i=0;i<4;i++){var p=new StackPanel{Margin=new(4)};p.Children.Add(new TextBlock{Text=labels[i]});p.Children.Add(values[i]);System.Windows.Automation.AutomationProperties.SetName(values[i],labels[i]);inputs.Children.Add(p);}panel.Children.Add(inputs);
        panel.Children.Add(new TextBlock{Text="Remark"});panel.Children.Add(remarks);panel.Children.Add(new TextBlock{Text="Reason"});panel.Children.Add(reason);
        var save=new Button{Content="Save selected brand",MinHeight=44,Margin=new(0,10,0,10)};panel.Children.Add(save);panel.Children.Add(status);
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        grid.SelectionChanged+=(_,_)=>{if(grid.SelectedItem is BrandStockEntry e){var v=new[]{e.Display,e.Backstock,e.Defective,e.YLoc};for(var i=0;i<4;i++)values[i].Text=v[i]?.ToString()??"";remarks.Text=e.Remark??"";status.Text=e.Source;}};
        save.Click+=async(_,_)=>await Run(async()=>
        {
            if(grid.SelectedItem is not BrandStockEntry e)throw new ArgumentException("Select a brand.");
            var n=new decimal[4];for(var i=0;i<4;i++)if(!decimal.TryParse(values[i].Text,out n[i])||n[i]<0)throw new ArgumentException("Enter all four non-negative counts; use zero when none.");
            var access=await new Phase2OperationsRepository(connection).LoadCurrentAccessAsync();if(!access.CanImport)throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
            await new OperationalCompletionRepository(connection).SaveManualStockCountAsync(store,date,e.Brand,n[0],n[1],n[2],n[3],n.Sum(),remarks.Text,Environment.UserName,reason.Text);
            await Load();status.Text="Today's brand count saved.";
        });
        Loaded+=async(_,_)=>await Run(Load);
    }
    private async Task Load()
    {
        grid.ItemsSource=await new OperationalReportRepository(connection).LoadBrandStockEntryAsync(store,date);
    }

    private async Task Run(Func<Task> action){try{IsEnabled=false;await action();}catch(Exception e){status.Text=DesktopFriendlyError.Describe(e);}finally{IsEnabled=true;}}
}
