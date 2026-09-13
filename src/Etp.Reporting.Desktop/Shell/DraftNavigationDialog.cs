using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

public enum DraftNavigationChoice { Stay, Save, Discard }

public sealed class DraftNavigationDialog : Window
{
    public DraftNavigationChoice Choice { get; private set; }
    public DraftNavigationDialog(Window owner, string task, bool canSave = true)
    {
        if (owner.IsLoaded) Owner = owner;
        Title = "Unsaved changes"; Width = 480; SizeToContent = SizeToContent.Height;
        MinWidth = 360; MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "Surface");
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "Keep your changes?", FontSize = 22, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = canSave ? $"{task} has unsaved changes. Save before leaving, discard this draft, or stay here. If saving fails, you remain on the task." : $"{task} has an unfinished draft. Stay to review and complete its action, or discard it before closing. Closing never approves or submits a draft.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,20) });
        var actions = new WrapPanel();
        foreach (var choice in new[] { DraftNavigationChoice.Save, DraftNavigationChoice.Discard, DraftNavigationChoice.Stay })
        {
            if (!canSave && choice == DraftNavigationChoice.Save) continue;
            var button = new Button { Content = choice.ToString(), MinWidth = 108, MinHeight = 48, Margin = new Thickness(0,0,8,8), IsCancel = choice == DraftNavigationChoice.Stay, IsDefault = choice == DraftNavigationChoice.Stay };
            AutomationProperties.SetName(button, choice + " unsaved changes");
            button.Click += (_, _) => { Choice = choice; DialogResult = choice != DraftNavigationChoice.Stay; };
            actions.Children.Add(button);
        }
        content.Children.Add(actions);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetResourceReference(Control.BackgroundProperty, "Surface"); Content = scroll;
    }
}
