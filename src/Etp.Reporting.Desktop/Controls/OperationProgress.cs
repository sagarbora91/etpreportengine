using System.Windows;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

/// <summary>The "working" dialog of a long operation (L3): 480 wide, Surface background, the message, a progress bar
/// and a right-aligned Cancel. The style owns the button height (Touch/Desktop density, S4).</summary>
public sealed class OperationProgress : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly Window? dialog;
    public CancellationToken Token => cancellation.Token;
    public OperationProgress(DependencyObject source, string title)
    {
        var owner = Window.GetWindow(source) ?? Application.Current?.MainWindow;
        if (owner?.IsVisible != true) return;
        var panel = new StackPanel { Margin = DesignTokens.Gap("Space24") };
        var status = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        status.SetResourceReference(FrameworkElement.StyleProperty, "BodyText");
        panel.Children.Add(status);
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Margin = DesignTokens.Gap(null, "Space16", null, "Space16") });
        var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_,_) => { cancellation.Cancel(); cancel.IsEnabled = false; status.Text = "Cancelling. Waiting for the current step to finish…"; };
        panel.Children.Add(cancel);
        dialog = new Window { Title = title, Owner = owner, Width = 480, SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height - 40),
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        dialog.SetResourceReference(Control.BackgroundProperty, "Surface");
        dialog.Closing += (_,e) => { if (!disposing) { cancellation.Cancel(); e.Cancel = true; status.Text = "Cancelling. Waiting for the current step to finish…"; } };
        dialog.Show();
    }
    private bool disposing;
    public void Dispose() { disposing = true; dialog?.Close(); cancellation.Dispose(); }
}
