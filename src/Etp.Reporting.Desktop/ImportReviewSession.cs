using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop;

/// <summary>Reproducible review of the live UI against a disposable database, with no mocked results.</summary>
internal static class ImportReviewSession
{
    public static async Task RunAsync(MainWindow window, string connection, string[] arguments)
    {
        string? Option(string name) { var index = Array.IndexOf(arguments, name); return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null; }
        var output = Option("--capture-review");
        if (output is null) return;
        Directory.CreateDirectory(output);
        try
        {
            if (!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith("EtpPhase1Test_", StringComparison.Ordinal))
                throw new InvalidOperationException("UI review runs only against an EtpPhase1Test_ disposable database.");
            var folder = Option("--review-folder") ?? throw new ArgumentException("Provide --review-folder.");
            var corrupted = Option("--review-corrupt-file") ?? throw new ArgumentException("Provide --review-corrupt-file.");
            for (var attempt = 0; (!window.CurrentShellAccess.CanImport || !window.ContinueButton.IsEnabled) && attempt < 100; attempt++) await Task.Delay(100);
            if (!window.CurrentShellAccess.CanImport) throw new InvalidOperationException("The review session could not load import permission.");
            window.ContinueButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(700);
            void Navigate(string id) => window.ApplyNavigationDecision(window.shell.Navigate(TaskNavigation.Find(id)!.Route, window.CurrentShellAccess));
            Navigate("import-files");
            var view = window.importWorkspaceView;
            if (!arguments.Contains("--review-existing-data"))
            {
            ((TextBox)view.FindName("WorkbookPathInput")).Text = folder;
            Task? progressCapture = null;
            view.ProgressChanged += (_, progress) =>
            {
                if (progress.Stage == "Importing" && progressCapture is null)
                    progressCapture = ReviewCapture.CaptureSizesAsync(window, output, "folder-progress");
            };
            await view.ImportSelectedSourceAsync();
            if (progressCapture is not null) await progressCapture;
            await ReviewCapture.CaptureSizesAsync(window, output, "folder-results");
            await CaptureFocusSizesAsync(window, output, "folder-results-scrolled", (ScrollViewer)view.Content,
                (DataGrid)view.FindName("BatchResultsGrid"));
            var options = (Expander)view.FindName("ImportOptionsExpander");
            options.IsExpanded = true;
            ((CheckBox)view.FindName("RestatementModeInput")).IsChecked = true;
            await CaptureFocusSizesAsync(window, output, "folder-restatement-options", (ScrollViewer)view.Content, options);
            ((CheckBox)view.FindName("RestatementModeInput")).IsChecked = false;
            options.IsExpanded = false;
            }
            ((TextBox)view.FindName("WorkbookPathInput")).Text = corrupted;
            await view.ImportSelectedSourceAsync();
            ((DataGrid)view.FindName("BatchResultsGrid")).SelectedIndex = 0;
            await CaptureFocusSizesAsync(window, output, "folder-diagnostics", (ScrollViewer)view.Content,
                (DataGrid)view.FindName("DiagnosticsGrid"));
            Navigate("masters");
            await Task.Delay(700);
            await ReviewCapture.CaptureSizesAsync(window, output, "settings-masters");
            var masterTabs = Descendants(window.FocusedWorkspaceHost).OfType<TabControl>().FirstOrDefault();
            if (masterTabs is not null)
            {
                masterTabs.SelectedIndex = 1;
                await ReviewCapture.CaptureSizesAsync(window, output, "settings-staff");
            }
            Navigate("sharing");
            await window.settingsWorkspace.PrepareForDisplayAsync(true);
            await ReviewCapture.CaptureSizesAsync(window, output, "settings-integrations");
            Navigate("source-inbox");
            await window.sourceInboxWorkspaceView.RefreshAsync();
            await ReviewCapture.CaptureSizesAsync(window, output, "scanned-documents");
            await CaptureFocusSizesAsync(window, output, "scanned-documents-scrolled", (ScrollViewer)window.sourceInboxWorkspaceView.Content,
                (DataGrid)window.sourceInboxWorkspaceView.FindName("DocumentsGrid"));
            await File.WriteAllTextAsync(Path.Combine(output, "capture-complete.txt"), "Captured the live WPF app surface at both requested window sizes. Native Windows frame is not included.\n");
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(output, "capture-error.txt"), error.ToString());
        }
    }

    private static async Task CaptureFocusSizesAsync(Window window, string directory, string stem, ScrollViewer scroll, FrameworkElement target)
    {
        foreach (var (width, height) in new[] { (1366, 768), (816, 480) })
        {
            window.WindowState = WindowState.Normal;
            window.Width = width; window.Height = height;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Render);
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + target.TranslatePoint(new Point(0, 0), scroll).Y - 8);
            await ReviewCapture.CaptureAsync(window, Path.Combine(directory, $"{stem}-{width}x{height}.png"), width, height);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var child = VisualTreeHelper.GetChild(node, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
