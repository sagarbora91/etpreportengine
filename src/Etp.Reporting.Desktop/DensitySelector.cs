using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Etp.Reporting.Desktop;

public sealed class DensitySelector : Border
{
    private readonly RadioButton comfortable;
    private readonly RadioButton compact;
    private bool updating;

    public event EventHandler<UiDensity>? DensityChanged;

    public DensitySelector()
    {
        Padding = new Thickness(12, 10, 12, 10);
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "DISPLAY DENSITY", FontSize = 11, FontWeight = FontWeights.SemiBold });
        var choices = new UniformGrid { Columns = 2, Margin = new Thickness(0, 7, 0, 0) };
        comfortable = Choice("Comfortable", UiDensity.Comfortable);
        compact = Choice("Compact", UiDensity.Compact);
        choices.Children.Add(comfortable);
        choices.Children.Add(compact);
        root.Children.Add(choices);
        Child = root;
        AutomationProperties.SetName(this, "Display density");
    }

    public void SetDensity(UiDensity density)
    {
        updating = true;
        comfortable.IsChecked = density == UiDensity.Comfortable;
        compact.IsChecked = density == UiDensity.Compact;
        updating = false;
        AutomationProperties.SetHelpText(this, $"Current display density: {density}");
    }

    private RadioButton Choice(string label, UiDensity density)
    {
        var button = new RadioButton { Content = label, GroupName = "ShellDensity", Tag = density, Margin = new Thickness(0, 2, 8, 2) };
        button.Checked += (_, _) => { if (!updating) DensityChanged?.Invoke(this, density); };
        AutomationProperties.SetName(button, $"Use {label.ToLowerInvariant()} display density");
        return button;
    }
}
