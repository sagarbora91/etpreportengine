using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace Etp.Reporting.Desktop;

public static class ConfirmationSheet
{
    public static bool Show(DependencyObject source, string action, string detail)
    {
        var owner = Window.GetWindow(source);
        var dialog = new Window { Title = action, Width = 480, SizeToContent = SizeToContent.Height,
            MaxHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        if (owner?.IsVisible == true) dialog.Owner = owner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) });
        var actions = new WrapPanel();
        var cancel = new Button { Content = "Keep working", IsCancel = true, MinHeight = 44, Margin = new Thickness(0,0,8,0) };
        var confirm = new Button { Content = action, MinHeight = 44 };
        AutomationProperties.SetName(confirm, "Confirm " + action.ToLowerInvariant());
        confirm.Click += (_,_) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(confirm); panel.Children.Add(actions);
        dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }
}
