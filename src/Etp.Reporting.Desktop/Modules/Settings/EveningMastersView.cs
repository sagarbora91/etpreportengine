using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Modules.Settings;

/// <summary>
/// What Settings > Brands and targets reads and writes (1.12 U2: the view takes services, never a connection string; the
/// composition root builds them in <see cref="SettingsWorkspaceView"/>). Monthly targets stay Owner-only (decision D22).
/// </summary>
public sealed record EveningMastersServices(
    Func<Task<IReadOnlyList<string>>> LoadActiveStores,
    Func<Task<IReadOnlyList<BrandRowDefinition>>> LoadBrands,
    Func<Task<IReadOnlyList<MonthlyTargetRow>>> LoadTargets,
    Func<Task<IReadOnlyList<SourceBrandEvidence>>> LoadSourceBrands,
    Func<BrandRowDefinition, Task> SaveBrand,
    Func<MonthlyTargetRow, Task> SaveTarget)
{
    /// <summary>Services that fail on use, for a view whose test drives only the parts it replaces.</summary>
    public static EveningMastersServices Unavailable { get; } = new(
        () => throw new InvalidOperationException("No database call expected."), () => throw new InvalidOperationException("No database call expected."),
        () => throw new InvalidOperationException("No database call expected."), () => throw new InvalidOperationException("No database call expected."),
        _ => throw new InvalidOperationException("No database call expected."), _ => throw new InvalidOperationException("No database call expected."));
}

/// <summary>
/// Settings > Brands and targets. Template C (Form): a SurfaceCard per section with its own Save. 1.12 U2 (wave plan C-d):
/// "Copy all stores from last month" fills the month shown in Target month for every store; nothing is saved until "Save copied
/// targets", which stays the highlighted (primary) button until it is pressed; a saved target is replaced only through the
/// Replace / Keep / Cancel dialog.
/// </summary>
public sealed class EveningMastersView : UserControl
{
    public const string CopyAllStoresText = "Copy all stores from last month";
    public const string SaveCopiedText = "Save copied targets";

    private readonly DataGrid brands=Grid(), targets=Grid(), evidence=Grid();
    private readonly ComboBox store=new(){IsEditable=false};
    private readonly TextBox label=Input("DSR row label"),order=Input("Order",true),codes=Input("Source codes, comma-separated",wide:true),amount=Input("Monthly target",true);
    private readonly DatePicker month=new(){SelectedDate=DateTime.Today};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock targetPermissionNote=new(){Text="Owner permission is required to change monthly targets.",TextWrapping=TextWrapping.Wrap};
    private readonly List<(Button Button,bool OwnerOnly)> accessButtons=[];
    private readonly EveningMastersServices services;private readonly Func<bool> owner;private readonly Func<bool> canEditBrands;private int rowId;
    // "Copy all stores from last month" fills this grid only; nothing is saved until "Save copied targets".
    private readonly DataGrid copiedTargets=CopiedTargetGrid.Create("Monthly targets copied from the previous month (not saved)",withCro:false);
    private readonly TextBlock copiedNote=new(){TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed};
    private readonly Button saveCopied;
    private IReadOnlyList<CopiedTargetRow> pendingCopy=[];private DateOnly pendingMonth;
    internal Func<Task<IReadOnlyList<MonthlyTargetRow>>> TargetLoader{get;set;}
    internal Func<MonthlyTargetRow,Task> TargetSaver{get;set;}
    internal Func<string,TargetOverwriteChoice> ConfirmOverwrite{get;set;}
    internal Func<string,bool> ConfirmDiscardCopy{get;set;}
    internal string StatusText=>status.Text;
    /// <summary>Whether "Save copied targets" is the highlighted button (copied rows are waiting to be saved).</summary>
    internal bool SaveCopiedIsHighlighted { get; private set; }

    public EveningMastersView(EveningMastersServices services,Func<bool> owner,Func<bool>? canEditBrands=null)
    {
        this.services=services??throw new ArgumentNullException(nameof(services));this.owner=owner;this.canEditBrands=canEditBrands??owner;order.Text="10";
        ConfirmOverwrite=question=>TargetCopy.FromDialog(ReplaceKeepCancelDialog.Ask(this,"Replace saved targets?",question));
        ConfirmDiscardCopy=message=>ConfirmationSheet.Show(this,"Copy again",message);
        AutomationProperties.SetName(store,"Store");
        AutomationProperties.SetName(brands,"Saved brand rows");
        AutomationProperties.SetName(targets,"Saved monthly targets");
        AutomationProperties.SetName(evidence,"Source brand evidence");
        AutomationProperties.SetName(month,"Target month");
        store.SetResourceReference(WidthProperty,"InputMedium");month.SetResourceReference(WidthProperty,"InputMedium");
        TargetLoader=()=>this.services.LoadTargets();
        TargetSaver=row=>this.services.SaveTarget(row);
        brands.SelectionChanged+=(_,_)=>{if(brands.SelectedItem is BrandRowDefinition r){rowId=r.Id;store.Text=r.StoreCode;label.Text=r.Label;order.Text=r.Order.ToString(CultureInfo.CurrentCulture);codes.Text=r.SourceCodes;}};
        targets.SelectionChanged+=(_,_)=>{if(targets.SelectedItem is MonthlyTargetRow r){store.Text=r.StoreCode;month.SelectedDate=r.Month.ToDateTime(TimeOnly.MinValue);amount.Text=r.TargetSales.ToString(CultureInfo.CurrentCulture);}};
        var panel=new StackPanel();
        var heading=new TextBlock{Text="Brand rows and monthly targets"};heading.SetResourceReference(StyleProperty,"SectionTitleText");panel.Children.Add(heading);
        panel.Children.Add(Caption("Each source brand belongs to one row. Unassigned sales remain visible as Other / unmapped."));
        panel.Children.Add(Field("Store code",store));
        var tabs=new TabControl();AutomationProperties.SetName(tabs,"Brand and target settings");var brandPanel=new StackPanel();brandPanel.Children.Add(brands);
        var brandFields=new WrapPanel();foreach(var (title,input) in new[]{("Row label",label),("Order",order),("Mapped source codes / names / clusters",codes)})brandFields.Children.Add(Field(title,input));brandPanel.Children.Add(brandFields);
        var actions=new WrapPanel();actions.Children.Add(Button("New row",()=>{rowId=0;label.Clear();codes.Clear();return Task.CompletedTask;}));
        actions.Children.Add(Button("Save",async()=>{if(!int.TryParse(order.Text,out var n))throw new ArgumentException("Enter a whole-number order.");await this.services.SaveBrand(new(rowId,store.Text,label.Text,n,codes.Text));await Refresh();},primary:true,name:"Save brand row"));brandPanel.Children.Add(actions);
        var evidenceHeading=new TextBlock{Text="Source brand evidence (read-only)"};evidenceHeading.SetResourceReference(StyleProperty,"CardTitleText");brandPanel.Children.Add(evidenceHeading);brandPanel.Children.Add(evidence);
        var targetPanel=new StackPanel();targetPanel.Children.Add(targets);
        var targetFields=new WrapPanel();targetFields.Children.Add(Field("Target month",month));targetFields.Children.Add(Field("Monthly store target",amount));targetPanel.Children.Add(targetFields);
        targetPanel.Children.Add(Button("Save",async()=>{if(month.SelectedDate is null||!decimal.TryParse(amount.Text,out var value))throw new ArgumentException("Choose a month and enter a target.");await this.services.SaveTarget(new(store.Text,DateOnly.FromDateTime(month.SelectedDate.Value),value));await Refresh();},ownerOnly:true,primary:true,name:"Save monthly target"));
        targetPanel.Children.Add(targetPermissionNote);
        var copyActions=new WrapPanel{Margin=DesignTokens.Gap(null,"Space16",null,null)};
        copyActions.Children.Add(Button(CopyAllStoresText,CopyPreviousMonthAsync,ownerOnly:true));
        saveCopied=Button(SaveCopiedText,SaveCopiedTargetsAsync,ownerOnly:true);copyActions.Children.Add(saveCopied);
        targetPanel.Children.Add(copyActions);targetPanel.Children.Add(copiedNote);targetPanel.Children.Add(copiedTargets);
        targetPanel.Children.Add(Caption("Day target = monthly target ÷ days in month. MTD balance = monthly target − MTD sales. Required daily sales = balance ÷ remaining days, including today."));
        tabs.Items.Add(new TabItem{Header="Brand rows",Content=brandPanel});tabs.Items.Add(new TabItem{Header="Monthly targets",Content=targetPanel});panel.Children.Add(tabs);panel.Children.Add(Button("Refresh",Refresh));panel.Children.Add(status);Content=panel;
        HighlightSaveCopied(false);
        RefreshAccessState();
        Loaded+=async(_,_)=>{RefreshAccessState();await Run(Refresh);};
    }

    private async Task CopyPreviousMonthAsync()
    {
        if(month.SelectedDate is null)throw new ArgumentException("Choose the target month to fill.");
        var target=TargetCopy.MonthStart(DateOnly.FromDateTime(month.SelectedDate.Value));var previous=TargetCopy.PreviousMonth(target);
        if(pendingCopy.Count>0&&!ConfirmDiscardCopy($"Copied targets for {TargetCopy.MonthLabel(pendingMonth)} are not saved yet. Replace them with a new copy?"))return;
        var saved=await TargetLoader();targets.ItemsSource=saved;
        var plan=TargetCopy.Plan(saved.Select(row=>new TargetCopySource(row.StoreCode,null,row.Month,row.TargetSales)),target);
        if(plan.Count==0){ShowCopy([],target);status.Text=$"No monthly targets are saved for {TargetCopy.MonthLabel(previous)}. Nothing was copied.";return;}
        ShowCopy(plan,target);
        status.Text=$"Copied {plan.Count} monthly target(s) for all stores from {TargetCopy.MonthLabel(previous)} into {TargetCopy.MonthLabel(target)}. Nothing is saved yet: check the values, then press {SaveCopiedText}.";
    }

    private async Task SaveCopiedTargetsAsync()
    {
        if(pendingCopy.Count==0)throw new ArgumentException($"Press {CopyAllStoresText} first.");
        copiedTargets.CommitEdit(DataGridEditingUnit.Row,true);
        var resolved=TargetCopy.Resolve(pendingCopy);
        if(resolved.Any(row=>row.ReplacesExisting))
        {
            var choice=ConfirmOverwrite(TargetCopy.OverwriteQuestion(resolved,pendingMonth));
            if(choice==TargetOverwriteChoice.Cancel){status.Text="Nothing was saved. The copied targets are still shown.";return;}
            resolved=TargetCopy.Apply(resolved,choice);
        }
        foreach(var row in resolved)await TargetSaver(new(row.StoreCode,pendingMonth,row.TargetSales));
        var label=TargetCopy.MonthLabel(pendingMonth);ShowCopy([],pendingMonth);
        targets.ItemsSource=await TargetLoader();
        status.Text=resolved.Count==0?$"No monthly target for {label} needed changing. Nothing was saved.":$"Saved {resolved.Count} monthly target(s) for {label}.";
    }

    private void ShowCopy(IReadOnlyList<CopiedTargetRow> rows,DateOnly target)
    {
        pendingCopy=rows;pendingMonth=target;copiedTargets.ItemsSource=rows;
        copiedTargets.Visibility=rows.Count>0?Visibility.Visible:Visibility.Collapsed;copiedNote.Visibility=copiedTargets.Visibility;
        copiedNote.Text=rows.Count>0?$"Copied from {TargetCopy.MonthLabel(TargetCopy.PreviousMonth(target))} for {TargetCopy.MonthLabel(target)} - not saved. Edit a value or clear it to skip that store; a target already saved for {TargetCopy.MonthLabel(target)} is replaced only after you choose Replace.":"";
        HighlightSaveCopied(rows.Count>0);
    }

    /// <summary>C-d: "Save copied targets" is the primary (highlighted) button while copied rows wait; a plain button otherwise.</summary>
    private void HighlightSaveCopied(bool waiting)
    {
        SaveCopiedIsHighlighted=waiting;
        if(waiting)saveCopied.SetResourceReference(StyleProperty,"PrimaryButton");else saveCopied.ClearValue(StyleProperty);
    }

    public void RefreshAccessState()
    {
        foreach(var (button,ownerOnly) in accessButtons)button.IsEnabled=ownerOnly?owner():canEditBrands();
        targetPermissionNote.Visibility=owner()?Visibility.Collapsed:Visibility.Visible;
    }

    private async Task Refresh(){var selected=store.Text;store.ItemsSource=await services.LoadActiveStores();store.SelectedItem=selected; if(store.SelectedIndex<0&&store.Items.Count==1)store.SelectedIndex=0;brands.ItemsSource=await services.LoadBrands();targets.ItemsSource=await services.LoadTargets();evidence.ItemsSource=await services.LoadSourceBrands();status.Text="Saved rows are shown above.";}
    private async Task Run(Func<Task> action,bool ownerOnly=false){try{if(ownerOnly?!owner():!canEditBrands())throw new UnauthorizedAccessException(ownerOnly?"Owner permission is required.":"Owner or Store Manager permission is required.");IsEnabled=false;await action();}catch(Exception ex){status.Text=DesktopDiagnostics.WithReference(DesktopFriendlyError.Describe(ex),DesktopDiagnostics.Record(ex,"Settings.EveningMasters","EVENING_MASTERS_OPERATION_FAILED",ex is UnauthorizedAccessException?DesktopDiagnosticSeverity.Warning:DesktopDiagnosticSeverity.Error,operation:"Evening masters change failed"));}finally{IsEnabled=true;RefreshAccessState();}}
    private Button Button(string title,Func<Task> action,bool ownerOnly=false,bool primary=false,string? name=null)
    {
        var b=new Button{Content=title,Margin=DesignTokens.Gap(null,"Space8","Space8",null),HorizontalAlignment=HorizontalAlignment.Left};
        if(primary)b.SetResourceReference(StyleProperty,"PrimaryButton");
        if(name is not null)AutomationProperties.SetName(b,name);
        accessButtons.Add((b,ownerOnly));b.Click+=async(_,_)=>await Run(action,ownerOnly);return b;
    }
    private static DataGrid Grid(){var grid=new DataGrid{AutoGenerateColumns=true,IsReadOnly=true,Margin=DesignTokens.Gap(null,"Space8",null,"Space8")};TablePresentation.Configure(grid);return grid;}
    private static TextBox Input(string name,bool numeric=false,bool wide=false){var t=new TextBox();t.SetResourceReference(WidthProperty,wide?"InputWide":numeric?"InputNarrow":"InputMedium");AutomationProperties.SetName(t,name);if(numeric)t.InputScope=new InputScope{Names={new InputScopeName(InputScopeNameValue.Number)}};return t;}
    private static TextBlock Caption(string text){var c=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=DesignTokens.Gap(null,"Space4",null,"Space8")};c.SetResourceReference(StyleProperty,"CaptionText");return c;}
    private static UIElement Field(string title,FrameworkElement input)
    {
        var p=new StackPanel{Margin=DesignTokens.Gap(null,"Space4","Space8","Space4")};
        var caption=new TextBlock{Text=title,Margin=DesignTokens.Gap(null,null,null,"Space4")};caption.SetResourceReference(StyleProperty,"FieldLabelText");
        input.HorizontalAlignment=HorizontalAlignment.Left;p.Children.Add(caption);p.Children.Add(input);return p;
    }
}
