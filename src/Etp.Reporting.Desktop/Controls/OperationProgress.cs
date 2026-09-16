using System.Windows;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

public sealed class OperationProgress : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly Window? dialog;
    public CancellationToken Token => cancellation.Token;
    public OperationProgress(DependencyObject source, string title)
    {
        var owner = Window.GetWindow(source) ?? Application.Current?.MainWindow;
        if (owner?.IsVisible != true) return;
        var panel = new StackPanel { Margin = new Thickness(20) };
        var status = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(status);
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0,16,0,16), Height = 8 });
        var cancel = new Button { Content = "Cancel", MinHeight = 44 };
        cancel.Click += (_,_) => { cancellation.Cancel(); cancel.IsEnabled = false; status.Text = "Cancelling. Waiting for the current step to finish…"; };
        panel.Children.Add(cancel);
        dialog = new Window { Title = title, Owner = owner, Width = 440, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        dialog.Closing += (_,e) => { if (!disposing) { cancellation.Cancel(); e.Cancel = true; status.Text = "Cancelling. Waiting for the current step to finish…"; } };
        dialog.Show();
    }
    private bool disposing;
    public void Dispose() { disposing = true; dialog?.Close(); cancellation.Dispose(); }
}
