using System.Windows;
using System.Windows.Controls;
namespace Etp.Reporting.Desktop.Modules.Reports;

public sealed class ReportScopeDialog : Window
{
    public DatePicker From { get; } = new();
    public DatePicker To { get; } = new();
    public ComboBox Store { get; } = new();
    public ReportScopeDialog(ReportWorkspaceControl report)
    {
        var owner=Window.GetWindow(report); if(owner?.IsLoaded==true) Owner=owner;
        Title="Report period and store"; Width=400;Height=370;MinWidth=320;MinHeight=250;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Surface");
        var root=new DockPanel { Margin=new Thickness(16) }; var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0) };
        var cancel=new Button { Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,8,0) };var apply=new Button { Content="Apply",IsDefault=true };apply.SetResourceReference(StyleProperty,"PrimaryButton");
        apply.Click+=(_,_)=> { report.DateFromPicker.SelectedDate=From.SelectedDate;report.DateToPicker.SelectedDate=To.SelectedDate;report.SetStoreScope(Store.SelectedItem?.ToString()??"Select one store");DialogResult=true; };
        actions.Children.Add(cancel);actions.Children.Add(apply);DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var fields=new StackPanel();
        From.SelectedDate=report.DateFromPicker.SelectedDate;To.SelectedDate=report.DateToPicker.SelectedDate;
        Store.ItemsSource=report.ScopeSelector.ItemsSource;Store.SelectedItem=report.ScopeSelector.SelectedItem;Store.IsEnabled=report.ScopeSelector.IsEnabled;
        if(report.DateFromPicker.IsEnabled)Field("From",From);Field("To / business date",To);Field("Store scope",Store);
        root.Children.Add(new ScrollViewer { Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });Content=root;
        void Field(string label,Control control) {fields.Children.Add(new TextBlock { Text=label,Margin=new Thickness(0,8,0,4) });System.Windows.Automation.AutomationProperties.SetName(control,label);fields.Children.Add(control);}
    }
}
