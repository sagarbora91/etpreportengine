using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace Etp.Reporting.Desktop;

/// <summary>
/// The confirm dialog (ETP-DESIGN-SYSTEM.md L3): 480 wide, Surface background from the token, a CardTitleText heading,
/// the body, then a right-aligned row with the no-op button first and the action (PrimaryButton) last. Never taller
/// than the work area (CS-11): a long body scrolls.
/// </summary>
public static class ConfirmationSheet
{
    public static bool Show(DependencyObject source, string action, string detail)
    {
        var owner = Window.GetWindow(source);
        var dialog = new Window { Title = action, Width = 480, SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 40), WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        dialog.SetResourceReference(Control.BackgroundProperty, "Surface");
        if (owner?.IsVisible == true) dialog.Owner = owner;
        var root = new DockPanel { Margin = DesignTokens.Gap("Space24") };
        var heading = new TextBlock { Text = action, Margin = DesignTokens.Gap(null, null, null, "Space12") };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "CardTitleText");
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = DesignTokens.Gap(null, "Space16", null, null) };
        var cancel = new Button { Content = "Keep working", IsCancel = true, Margin = DesignTokens.Gap(null, null, "Space8", null) };
        var confirm = new Button { Content = action };
        confirm.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        AutomationProperties.SetName(confirm, "Confirm " + action.ToLowerInvariant());
        confirm.Click += (_,_) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(confirm);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var body = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap };
        body.SetResourceReference(FrameworkElement.StyleProperty, "BodyText");
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
        dialog.Content = root;
        return dialog.ShowDialog() == true;
    }
}
