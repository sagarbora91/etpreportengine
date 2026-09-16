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
        PresentationCulture.Initialize();
        app.InitializeComponent();
        if (args.Contains("--controls")) { ThemeControlAudit.Run(output); return; }
        if (args.Contains("--fixture-exports")) { FixtureReportAudit.Run(output,args[^1], exportsOnly: true); return; }
        if (args.Contains("--fixture-reports")) { FixtureReportAudit.Run(output,args[^1]); return; }
        if (!args.Contains("--connection-string")) throw new ArgumentException("Provide --connection-string for an isolated review database; default live settings are never used.");
        var window = DesktopCompositionRoot.CreateForArguments(args).CreateMainWindow();
        window.Width = 1366;
        window.Height = 768;
        if (args.Contains("--inventory"))
        {
            SetAccess(window, AccessRole.Owner, "Synthetic Owner");
            ((FrameworkElement)window.FindName("WelcomeOverlay")).Visibility = Visibility.Collapsed;
            var navigator = (TaskNavigator)typeof(MainWindow).GetField("taskNavigator", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            ControlInventoryAudit.Run(window, navigator, output); return;
        }
        if (args.Contains("--tasks")) { AuditTasks(window, output); return; }
        Render(window, Path.Combine(output, "01-welcome-1366x768.png"), 1366, 768);
        SetAccess(window, AccessRole.StoreManager, "Store Manager");
        Invoke(window, "CompleteWelcomeState");
        ((FrameworkElement)window.FindName("WelcomeOverlay")).Visibility = Visibility.Collapsed;
        Invoke(window, "OpenSection", "Today", null!);
        Render(window, Path.Combine(output, "02-module-home-1366x768.png"), 1366, 768);
        Invoke(window, "NavigateToDestination", "Sales Reports");
        Render(window, Path.Combine(output, "03-reports-1366x768.png"), 1366, 768);
        Render(window, Path.Combine(output, "04-reports-960x600.png"), 960, 600);
        Invoke(window, "OpenSection", "Today", null!);
        Render(window, Path.Combine(output, "05-module-home-1920x1080.png"), 1920, 1080);
        Invoke(window, "ApplyDensity", UiDensity.Desktop, false);
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
        foreach (var destination in ShellRouteRegistry.All.Select(x => x.Destination).Distinct(StringComparer.Ordinal))
        {
            if (Invoke(window, "NavigateToDestination", destination) is not true)
                throw new InvalidOperationException($"Executable workspace route was denied during owner audit: {destination}.");
            Render(window, Path.Combine(routeOutput, $"destination-{Slug(destination)}.png"), 1366, 768);
            Render(window, Path.Combine(routeOutput, $"destination-{Slug(destination)}-960x600.png"), 960, 600);
            Render(window, Path.Combine(routeOutput, $"destination-{Slug(destination)}-1920x1080.png"), 1920, 1080);
            renderedDestinations++;
        }

        var renderedReports = 0;
        foreach (var report in ProductReportCatalogue.All)
        {
            if (Invoke(window, "ShowFocusedReportWorkspace", report.Code) is not true)
                throw new InvalidOperationException($"Executable report route was denied during owner audit: {report.Code}.");
            Render(window, Path.Combine(routeOutput, $"report-{Slug(report.Code)}.png"), 1366, 768);
            Render(window, Path.Combine(routeOutput, $"report-{Slug(report.Code)}-960x600.png"), 960, 600);
            Render(window, Path.Combine(routeOutput, $"report-{Slug(report.Code)}-1920x1080.png"), 1920, 1080);
            renderedReports++;
        }
        if (renderedReports != TaskNavigation.All.Count(x => x.ReportCode is not null))
            throw new InvalidOperationException("Rendered report-route count does not match the executable registry.");
        var named = Descendants((DependencyObject)window.Content).OfType<FrameworkElement>().Count(x => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(x)));
        Console.WriteLine($"Rendered 11 baseline views, {renderedDestinations} workspace routes and {renderedReports} report routes at three sizes (960x600, 1366x768, 1920x1080), 96-DPI offscreen renders only. Accessible named elements: {named:N0}. Output: {output}");
    }

    internal static void SetAccess(MainWindow window, AccessRole role, string displayName)
    {
        var field = typeof(MainWindow).GetField("currentAccess", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("currentAccess");
        field.SetValue(window, new AccessSession("UI-SMOKE\\user", displayName, role, true));
        Invoke(window, "UpdateOperationsAdministrationAccess");
        var settings = (Etp.Reporting.Desktop.Modules.Settings.SettingsWorkspaceView)typeof(MainWindow).GetField("settingsWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        settings.UpdateAccess(new(role != AccessRole.None, role == AccessRole.Owner));
    }

    static void AuditTasks(MainWindow window, string output)
    {
        File.WriteAllText(Path.Combine(output, "capture-metadata.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            sourceState = "Working tree; commit metadata alone does not identify uncommitted changes",
            role = "Synthetic Owner", fixture = "Default composition; loading/empty layout, no populated acceptance fixture",
            dpi = 96, capture = "Offscreen WPF RenderTargetBitmap; no installed interaction claimed"
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        SetAccess(window, AccessRole.Owner, "Synthetic Owner");
        ((FrameworkElement)window.FindName("WelcomeOverlay")).Visibility = Visibility.Collapsed;
        var navigator = (TaskNavigator)typeof(MainWindow).GetField("taskNavigator", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var evidence = new List<object>();
        foreach (var task in TaskNavigation.All.Where(x => x.Available))
        {
            try
            {
                if (!navigator.DisplayTaskRoute(task.Route)) throw new InvalidOperationException($"No task composition for {task.Id}");
                Render(window, Path.Combine(output, Slug(task.Id) + "-1000x600.png"), 1000, 600);
                VisualContractAudit.Capture((DependencyObject)window.Content,task.Id);
                if (new[] { "dashboard", "support-package", "settings", "connection", "import-files", "report-dsr", "register-inward", "walk-ins", "tally-export", "compare", "ocr-review", "users" }.Contains(task.Id))
                foreach (var density in Enum.GetValues<UiDensity>())
                foreach (var size in new[] { (1280,720), (1000,600), (960,600), (800,440), (1024,768), (1280,800), (800,1000) })
                {
                    Invoke(window, "ApplyDensity", density, false);
                    Render(window, Path.Combine(output, $"{task.Id}-{density}-{size.Item1}x{size.Item2}.png"), size.Item1, size.Item2);
                }
                Invoke(window, "ApplyDensity", UiDensity.Touch, false);
                evidence.Add(new { route = task.Id, task.Path, result = "RENDERED", method = "Direct task composition, bypasses navigation guards; 96-DPI offscreen synthetic-owner layout, not interaction", timestamp = DateTimeOffset.UtcNow });
            }
            catch (Exception ex) { evidence.Add(new { route = task.Id, result = "FAIL", error = ex.ToString() }); }
        }
        foreach (var density in Enum.GetValues<UiDensity>())
        {
            Invoke(window, "ApplyDensity", density, false);
            var dialog = new DraftNavigationDialog(window, "Synthetic register entry");
            Render(dialog, Path.Combine(output, $"unsaved-dialog-{density}-480x280.png"), 480, 280);
            Render(dialog, Path.Combine(output, $"unsaved-dialog-{density}-360x340.png"), 360, 340);
        }
        var overviews = new List<object>();
        Invoke(window, "ApplyDensity", UiDensity.Touch, false);
        foreach (var section in TaskNavigation.Sections)
        {
            Invoke(window,"OpenSection",section,null!);
            Render(window,Path.Combine(output,"section-"+Slug(section)+"-1366x768.png"),1366,768);
            Render(window,Path.Combine(output,"section-"+Slug(section)+"-816x480.png"),816,480);
            overviews.Add(new { section, method="Offscreen section composition; physical touch acceptance remains separate" });
        }
        File.WriteAllText(Path.Combine(output, "overview-layout-results.json"), System.Text.Json.JsonSerializer.Serialize(overviews, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        VisualContractAudit.Write(output);
        File.WriteAllText(Path.Combine(output, "task-layout-results.json"), System.Text.Json.JsonSerializer.Serialize(evidence, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "routes.json"), System.Text.Json.JsonSerializer.Serialize(TaskNavigation.All, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "original-menu.json"), System.Text.Json.JsonSerializer.Serialize(TaskNavigation.All, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var queries = new[] { "DSR", "support package", "backup", "restore", "users", "walk-ins", "Tally", "duplicates", "invoice", "stock" };
        var times = Enumerable.Range(0, 100).Select(i => { var watch = System.Diagnostics.Stopwatch.StartNew(); TaskNavigation.Search(queries[i % queries.Length], ShellAccess.Owner); return watch.Elapsed.TotalMilliseconds; }).Order().ToArray();
        File.WriteAllText(Path.Combine(output, "search-performance.json"), System.Text.Json.JsonSerializer.Serialize(new { samples = times.Length, p95Ms = times[94], maximumMs = times[^1], environment = Environment.MachineName, method = "Warm host index query only; excludes popup rendering and debounce. Not VM or first-open acceptance." }));
        Console.WriteLine($"Task layout audit: {evidence.Count} routes. See task-layout-results.json; rendered does not imply accepted.");
    }

    static object? Invoke(MainWindow window, string method, params object[] parameters) =>
        (typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(method)).Invoke(window, parameters);

    internal static void Render(Window window, string path, int width, int height)
    {
        window.Width = width;
        window.Height = height;
        if (window is MainWindow mainWindow) Invoke(mainWindow, "MainWindow_SizeChanged", mainWindow, null!);
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        // Content-only offscreen rendering omits the native Window background.
        // Composite it first so transparent dialog panels retain readable labels.
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
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
