using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
namespace Etp.Reporting.Desktop;

public sealed class StatusDetailsDialog : Window
{
    public StatusDetailsDialog(Window? owner, string message)
    {
        if (owner is not null && owner.IsLoaded) Owner = owner;
        Title = "Status details"; Width = Math.Min(560, owner?.ActualWidth > 0 ? owner.ActualWidth : 560); Height = 320;
        MinWidth = 320; MinHeight = 220; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Surface");
        var root = new DockPanel { Margin = new Thickness(16) };
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        close.Click += (_,_) => Close(); DockPanel.SetDock(close,Dock.Bottom); root.Children.Add(close);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 } });
        Content = root; AutomationProperties.SetName(this,"Full task status");
    }
}
