using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Reporting;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "output/uiux-v4");
        Directory.CreateDirectory(output);
        var app = new App();
        app.InitializeComponent();
        var window = DesktopCompositionRoot.CreateDefault().CreateMainWindow();
        window.Width = 1366;
        window.Height = 768;
        Render(window, Path.Combine(output, "01-welcome-1366x768.png"), 1366, 768);
        SetAccess(window, AccessRole.StoreManager, "Store Manager");
        ((TextBlock)window.FindName("AccessStatus")).Text = "Store Manager — Store Manager";
        Invoke(window, "CompleteWelcomeState");
        ((FrameworkElement)window.FindName("WelcomeOverlay")).Visibility = Visibility.Collapsed;
        Invoke(window, "ShowModuleHome");
        Render(window, Path.Combine(output, "02-module-home-1366x768.png"), 1366, 768);
        Invoke(window, "NavigateToDestination", "Sales Reports");
        Render(window, Path.Combine(output, "03-reports-1366x768.png"), 1366, 768);
        Render(window, Path.Combine(output, "04-reports-960x600.png"), 960, 600);
        Invoke(window, "ShowModuleHome");
        Render(window, Path.Combine(output, "05-module-home-1920x1080.png"), 1920, 1080);
        Invoke(window, "ApplyDensity", UiDensity.Compact, false);
        Render(window, Path.Combine(output, "06-module-home-compact-1366x768.png"), 1366, 768);
        Invoke(window, "NavigateToDestination", "Manual Entry");
        Render(window, Path.Combine(output, "07-manual-entry-1366x768.png"), 1366, 768);
        Invoke(window, "NavigateToDestination", "Sales Reports");
        Invoke(window, "ShowFocusedReportWorkspace", "dsr");
        Render(window, Path.Combine(output, "08-dsr-screen-1366x768.png"), 1366, 768);
        Invoke(window, "ShowHelpWorkspace", HelpCentreRegistry.HomeTopicId, false);
        Render(window, Path.Combine(output, "09-help-centre-1366x768.png"), 1366, 768);
        Invoke(window, "ShowHelpWorkspace", HelpCentreRegistry.KeyboardShortcutsTopicId, false);
        Render(window, Path.Combine(output, "10-keyboard-shortcuts-1366x768.png"), 1366, 768);
        Invoke(window, "ShowFocusedReportWorkspace", "stock-closing");
        Render(window, Path.Combine(output, "11-stock-workspace-1366x768.png"), 1366, 768);

        SetAccess(window, AccessRole.Owner, "Owner");
        var routeOutput = Path.Combine(output, "all-workspace-routes");
        Directory.CreateDirectory(routeOutput);
        var renderedDestinations = 0;
        foreach (var destination in WorkspaceModuleOwnershipRegistry.Destinations.Select(x => x.Destination).Distinct(StringComparer.Ordinal))
        {
            if (Invoke(window, "NavigateToDestination", destination) is not true)
                throw new InvalidOperationException($"Executable workspace route was denied during owner audit: {destination}.");
            Render(window, Path.Combine(routeOutput, $"destination-{Slug(destination)}.png"), 1366, 768);
            renderedDestinations++;
        }

        var renderedReports = 0;
        foreach (var report in ProductReportCatalogue.All)
        {
            if (Invoke(window, "ShowFocusedReportWorkspace", report.Code) is not true)
                throw new InvalidOperationException($"Executable report route was denied during owner audit: {report.Code}.");
            Render(window, Path.Combine(routeOutput, $"report-{Slug(report.Code)}.png"), 1366, 768);
            renderedReports++;
        }
        if (renderedReports != WorkspaceModuleOwnershipRegistry.ReportRoutes.Count)
            throw new InvalidOperationException("Rendered report-route count does not match the executable registry.");
        var named = Descendants((DependencyObject)window.Content).OfType<FrameworkElement>().Count(x => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(x)));
        Console.WriteLine($"Rendered 11 baseline views, {renderedDestinations} workspace routes and {renderedReports} report routes. Accessible named elements: {named:N0}. Output: {output}");
    }

    static void SetAccess(MainWindow window, AccessRole role, string displayName)
    {
        var field = typeof(MainWindow).GetField("currentAccess", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("currentAccess");
        field.SetValue(window, new AccessSession("UI-SMOKE\\user", displayName, role, true));
    }

    static object? Invoke(MainWindow window, string method, params object[] parameters) =>
        (typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(method)).Invoke(window, parameters);

    static void Render(Window window, string path, int width, int height)
    {
        window.Width = width;
        window.Height = height;
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    static string Slug(string value) => new string(value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
}
