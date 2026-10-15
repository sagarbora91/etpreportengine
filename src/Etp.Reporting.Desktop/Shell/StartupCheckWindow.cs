using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

/// <summary>
/// 1.12.1 P3 (review m-5). The small window shown while the start-up database check (A3's planner) runs, so an Owner who
/// opens ETP is not left looking at nothing for up to 20 seconds. It has no buttons: the check always ends by itself
/// (result, error or its own time-out) and the caller closes it. Built in code like the other small dialogs; colours come
/// from the theme resources (Surface, TextPrimary through the TextBlock default).
/// </summary>
public sealed class StartupCheckWindow : Window
{
    public const string Message = "Checking the database...";
    public const string Detail = "This takes a few seconds. ETP opens when it is done.";

    public StartupCheckWindow()
    {
        Title = "ETP Reporting Engine";
        Width = 420; SizeToContent = SizeToContent.Height; MinWidth = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        SetResourceReference(BackgroundProperty, "Surface");
        AutomationProperties.SetName(this, "Checking the database");
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = Message, FontSize = 22, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = Detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 16) });
        content.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6 });
        Content = content;
    }
}
