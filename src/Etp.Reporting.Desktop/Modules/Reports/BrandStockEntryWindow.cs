using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Modules.Reports;

/// <summary>One brand's evening count as "Enter stock by brand" saves it.</summary>
public sealed record BrandStockCount(string StoreCode, DateOnly BusinessDate, string Brand, decimal Display, decimal Backstock, decimal Defective,
    decimal YLocation, string? Remark, string User, string Reason)
{
    public decimal Total => Display + Backstock + Defective + YLocation;
}

/// <summary>
/// What the brand count window reads and writes (1.12 U2: services built by the composition root, never a connection string;
/// <c>DesktopCompositionRoot.CreateBrandStockEntryServices</c>). <c>CanImport</c> reloads the role before a save.
/// </summary>
public sealed record BrandStockEntryServices(
    Func<string, DateOnly, Task<IReadOnlyList<BrandStockEntry>>> Load,
    Func<Task<bool>> CanImport,
    Func<BrandStockCount, Task> Save);

/// <summary>"Enter stock by brand" (Today > Close day > Physical stock): the brands of the day's snapshot and today's counts.</summary>
public sealed class BrandStockEntryWindow : Window
{
    private readonly DataGrid grid=new(){AutoGenerateColumns=true,IsReadOnly=true};
    private readonly TextBox[] values=Enumerable.Range(0,4).Select(_=>new TextBox{InputScope=new InputScope{Names={new InputScopeName(InputScopeNameValue.Number)}}}).ToArray();
    private readonly TextBox remarks=new(),reason=new(){Text="Evening physical count"};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly BrandStockEntryServices services;private readonly string store;private readonly DateOnly date;
    internal string StatusText=>status.Text;

    public BrandStockEntryWindow(BrandStockEntryServices services,string store,DateOnly date)
    {
        AutomationProperties.SetName(grid,"Brand counts");AutomationProperties.SetName(remarks,"Stock remark");AutomationProperties.SetName(reason,"Stock change reason");
        this.services=services??throw new ArgumentNullException(nameof(services));this.store=store;this.date=date;
        Title=$"{store} — physical stock — {date:dd MMM yyyy}";Width=900;Height=700;MinWidth=600;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Surface");
        TablePresentation.Configure(grid);
        // Template C (Form): the grid fills the window and scrolls itself; the inputs and Save stay below it.
        var root=new DockPanel{Margin=DesignTokens.Gap("Space16")};
        var intro=new TextBlock{Text="Select a brand, check yesterday's counts, then save today's count.",TextWrapping=TextWrapping.Wrap,Margin=DesignTokens.Gap(null,null,null,"Space8")};
        intro.SetResourceReference(StyleProperty,"CaptionText");
        DockPanel.SetDock(intro,Dock.Top);root.Children.Add(intro);
        var form=new StackPanel{Margin=DesignTokens.Gap(null,"Space8",null,null)};
        var inputs=new WrapPanel();var labels=new[]{"Display","Backstock","Defective","Y Loc"};
        for(var i=0;i<4;i++){values[i].SetResourceReference(WidthProperty,"InputNarrow");inputs.Children.Add(Field(labels[i],values[i]));AutomationProperties.SetName(values[i],labels[i]);}
        form.Children.Add(inputs);
        remarks.SetResourceReference(WidthProperty,"InputWide");reason.SetResourceReference(WidthProperty,"InputWide");
        form.Children.Add(Field("Remark",remarks));form.Children.Add(Field("Reason",reason));
        var save=new Button{Content="Save",HorizontalAlignment=HorizontalAlignment.Left,Margin=DesignTokens.Gap(null,"Space8",null,"Space8")};
        save.SetResourceReference(StyleProperty,"PrimaryButton");AutomationProperties.SetName(save,"Save the selected brand's count");
        form.Children.Add(save);form.Children.Add(status);
        DockPanel.SetDock(form,Dock.Bottom);root.Children.Add(form);
        root.Children.Add(grid);
        Content=root;
        grid.SelectionChanged+=(_,_)=>{if(grid.SelectedItem is BrandStockEntry e){var v=new[]{e.Display,e.Backstock,e.Defective,e.YLoc};for(var i=0;i<4;i++)values[i].Text=v[i]?.ToString(CultureInfo.InvariantCulture)??"";remarks.Text=e.Remark??"";status.Text=e.Source;}};
        save.Click+=async(_,_)=>await Run(SaveSelectedAsync);
        Loaded+=async(_,_)=>await Run(Load);
    }

    internal async Task SaveSelectedAsync()
    {
        if(grid.SelectedItem is not BrandStockEntry e)throw new ArgumentException("Select a brand.");
        var n=new decimal[4];for(var i=0;i<4;i++)if(!TryParseCount(values[i].Text,out n[i]))throw new ArgumentException("Enter all four non-negative counts; use zero when none.");
        if(!await services.CanImport())throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
        await services.Save(new(store,date,e.Brand,n[0],n[1],n[2],n[3],remarks.Text,Environment.UserName,reason.Text));
        await Load();status.Text="Today's brand count saved.";
    }

    private static FrameworkElement Field(string label,FrameworkElement input)
    {
        var panel=new StackPanel{Margin=DesignTokens.Gap(null,"Space4","Space8","Space4")};
        var caption=new TextBlock{Text=label,Margin=DesignTokens.Gap(null,null,null,"Space4")};caption.SetResourceReference(StyleProperty,"FieldLabelText");
        input.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(caption);panel.Children.Add(input);return panel;
    }

    internal async Task Load()
    {
        var rows=await services.Load(store,date);
        grid.ItemsSource=rows;
        status.Text=DescribeLoaded(rows.Count,store,date);
    }

    private async Task Run(Func<Task> action){try{IsEnabled=false;await action();}catch(Exception e){status.Text=DesktopDiagnostics.WithReference(DesktopFriendlyError.Describe(e),DesktopDiagnostics.Record(e,"Reports.BrandStockEntry","BRAND_STOCK_ENTRY_FAILED",operation:"Brand physical stock entry failed"));}finally{IsEnabled=true;}}
    // RA-STOCK-13 (1.9.9): an empty grid used to come with a blank status, so the user could not
    // tell that the system side (the closing-stock snapshot) was missing.
    internal static string DescribeLoaded(int rows,string store,DateOnly date)=>rows==0
        ? $"No closing-stock snapshot or saved count for {store} on {Etp.Reporting.Reporting.IndianFormat.Date(date)}, so there are no brands to count. Import the Closing Stock export for that day, then open this window again."
        : $"{rows.ToString(CultureInfo.InvariantCulture)} brand(s) for {store} on {Etp.Reporting.Reporting.IndianFormat.Date(date)}. Select a brand to enter today's count.";

    // Counts are typed as plain numbers; parse them the same way on every Windows culture.
    internal static bool TryParseCount(string? text,out decimal value)=>
        decimal.TryParse(text?.Trim(),NumberStyles.Number,CultureInfo.InvariantCulture,out value)&&value>=0;
}
