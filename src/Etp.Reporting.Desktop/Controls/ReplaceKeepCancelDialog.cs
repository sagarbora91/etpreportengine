using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

/// <summary>What the person chose when copied values meet values already saved.</summary>
public enum ReplaceKeepCancelChoice { Cancel, Replace, Keep }

/// <summary>
/// The three-way question that replaces the Windows Yes / No / Cancel box (1.12 U2, wave plan C-d): "Replace saved targets",
/// "Keep existing" or "Cancel", each button saying what it does. Dialog L3 of the design system: 480 wide, Surface
/// background, CardTitleText heading, the body (scrolls when long), then a right-aligned row with the no-op (Cancel) first and
/// the primary action (Replace) last. Escape and the close box are Cancel.
/// </summary>
public sealed class ReplaceKeepCancelDialog : Window
{
    public const string ReplaceText = "Replace saved targets";
    public const string KeepText = "Keep existing";
    public const string CancelText = "Cancel";

    internal Button ReplaceButton { get; }
    internal Button KeepButton { get; }
    internal Button CancelButton { get; }
    public ReplaceKeepCancelChoice Choice { get; private set; } = ReplaceKeepCancelChoice.Cancel;

    public ReplaceKeepCancelDialog(Window? owner, string heading, string body, string replaceText = ReplaceText, string keepText = KeepText)
    {
        if (owner?.IsVisible == true) Owner = owner;
        Title = heading; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Surface");
        AutomationProperties.SetName(this, heading);

        var root = new DockPanel { Margin = DesignTokens.Gap("Space24") };
        var title = new TextBlock { Text = heading, Margin = DesignTokens.Gap(null, null, null, "Space12"), TextWrapping = TextWrapping.Wrap };
        title.SetResourceReference(StyleProperty, "CardTitleText");
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = DesignTokens.Gap(null, "Space16", null, null) };
        CancelButton = new Button { Content = CancelText, IsCancel = true, Margin = DesignTokens.Gap(null, null, "Space8", null) };
        KeepButton = new Button { Content = keepText, Margin = DesignTokens.Gap(null, null, "Space8", null) };
        ReplaceButton = new Button { Content = replaceText };
        ReplaceButton.SetResourceReference(StyleProperty, "PrimaryButton");
        AutomationProperties.SetHelpText(CancelButton, "Save nothing; the copied values stay on screen.");
        AutomationProperties.SetHelpText(KeepButton, "Keep the values already saved and save only the new rows.");
        AutomationProperties.SetHelpText(ReplaceButton, "Replace the saved values with the copied ones.");
        CancelButton.Click += (_, _) => Choose(ReplaceKeepCancelChoice.Cancel);
        KeepButton.Click += (_, _) => Choose(ReplaceKeepCancelChoice.Keep);
        ReplaceButton.Click += (_, _) => Choose(ReplaceKeepCancelChoice.Replace);
        actions.Children.Add(CancelButton); actions.Children.Add(KeepButton); actions.Children.Add(ReplaceButton);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);

        var text = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(StyleProperty, "BodyText");
        root.Children.Add(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
        Content = root;
        Loaded += (_, _) => KeepButton.Focus();
    }

    private void Choose(ReplaceKeepCancelChoice choice)
    {
        Choice = choice;
        // A dialog that was never shown modally (a test) has no DialogResult to set.
        if (IsVisible) DialogResult = choice != ReplaceKeepCancelChoice.Cancel;
    }

    /// <summary>Shows the dialog over the window that holds <paramref name="source"/>; Cancel when it is closed any other way.</summary>
    public static ReplaceKeepCancelChoice Ask(DependencyObject? source, string heading, string body)
    {
        var dialog = new ReplaceKeepCancelDialog(source is null ? null : Window.GetWindow(source), heading, body);
        dialog.ShowDialog();
        return dialog.Choice;
    }
}
