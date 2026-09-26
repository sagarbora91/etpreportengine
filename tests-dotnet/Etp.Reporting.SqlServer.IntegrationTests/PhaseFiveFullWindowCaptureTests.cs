using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// The default gate walks every role-reachable task in a disposable database.
/// Optional themed WPF captures are not Windows DPI/VM or production certification.
/// </summary>
[Collection("Full MainWindow smoke")]
public sealed partial class PhaseFiveFullWindowCaptureTests(ITestOutputHelper output)
{
    private static readonly DateOnly Day = new(2026, 8, 25);
    private sealed record Capture(string Role, string Task, int Width, int Height, string File, int DistinctSampleColours);
    private sealed record RoleWalk(IReadOnlyList<Capture> Captures, IReadOnlyDictionary<string, int> Destinations, IReadOnlyList<string> Failures);

    [Fact]
    public async Task Every_role_reachable_destination_loads_with_disposable_data_and_optional_captures()
    {
        if (Environment.GetEnvironmentVariable(RoleWalkChildVariable) != "1")
        {
            await RunIsolatedRoleWalkAsync();
            AssertSubsequentWpfViewLoads();
            return;
        }
        // The role walk is a default gate. Only raster artifacts require opt-in.
        var suppliedEvidence = Environment.GetEnvironmentVariable("ETP_PHASE5_UI_EVIDENCE");
        var evidence = Environment.GetEnvironmentVariable("ETP_PHASE5_UI_CAPTURE") == "1" && !string.IsNullOrWhiteSpace(suppliedEvidence)
            ? Directory.CreateDirectory(Path.GetFullPath(suppliedEvidence)).FullName : null;
        var help = evidence is null ? null : HelpOutputDirectory();
        var settings = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpPhaseFiveCapture", Guid.NewGuid().ToString("N"))).FullName;
        var realSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "settings.json");
        var settingsHash = HashIfPresent(realSettings);
        var preferencesHash = HashIfPresent(UiPreferenceStore.FilePath);
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedAsync(database);
            new DesktopSettingsStore(settings).Save(database.ConnectionString);
            var result = await WalkOnStaAsync(database, settings, evidence, help);
            foreach (var (role, count) in result.Destinations)
                output.WriteLine($"Walked every available destination for {role}: {count}.");
            Assert.True(result.Failures.Count == 0, string.Join(Environment.NewLine, result.Failures));
            var captures = result.Captures;
            if (evidence is not null)
            {
                Assert.Equal(66, captures.Count);
                await File.WriteAllTextAsync(Path.Combine(evidence, "capture-manifest.json"), JsonSerializer.Serialize(new
                {
                generatedUtc = DateTimeOffset.UtcNow,
                fixtureDatabase = database.Name,
                data = "Synthetic demonstration data only; generated fixture database is dropped on exit.",
                evidence = "Real shown MainWindow; themed WPF at 96 DPI and exact logical viewport sizes. Application-role rendering, not separate Windows-account or native DPI validation. No export, sharing, scheduler installation or approval decision is invoked by the UI.",
                checks = "Nonblank opaque pixels, five visible rails, shell content inside viewport, role-gated destinations, successful startup and unchanged real preferences/connection settings.",
                captures
                }, new JsonSerializerOptions { WriteIndented = true }));
                output.WriteLine($"Captured {captures.Count} synthetic Phase 5 screens: {evidence}");
            }
            else Assert.Empty(captures);
        }
        finally
        {
            try { await database.DisposeAsync(); }
            finally
            {
                Directory.Delete(settings, recursive: true);
                Assert.Equal(settingsHash, HashIfPresent(realSettings));
                Assert.Equal(preferencesHash, HashIfPresent(UiPreferenceStore.FilePath));
            }
        }
    }

    private static async Task<RoleWalk> WalkOnStaAsync(SqlDatabaseFixture database, string settings, string? evidence, string? help)
    {
        var completion = new TaskCompletionSource<RoleWalk>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            System.Windows.Application? application = null;
            MainWindow? window = null;
            Exception? dispatcherFailure = null;
            try
            {
                // Never instantiate Desktop.App: its startup selects real user configuration.
                application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(application.Dispatcher));
                System.Globalization.CultureInfo.CurrentCulture = PresentationCulture.Indian;
                foreach (var name in new[] { "Colors", "Spacing", "Typography", "Icons", "Controls" })
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Etp.Reporting.Desktop;component/Themes/{name}.xaml", UriKind.Absolute) });
                application.DispatcherUnhandledException += (_, args) => { dispatcherFailure = args.Exception; args.Handled = true; };
                application.Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        var captures = new List<Capture>();
                        var destinations = new Dictionary<string, int>();
                        var failures = new List<string>();
                        foreach (var role in new[] { "OWNER", "STORE_MANAGER", "VIEWER" })
                        {
                            if (evidence is not null) File.AppendAllText(Path.Combine(evidence, "capture-progress.txt"), $"{DateTimeOffset.UtcNow:O} Starting {role}\n");
                            // Update only the fixture's application claim. SQL role enforcement
                            // has separate integration tests; this does not impersonate Windows.
                            await database.ExecuteAsync($"UPDATE dbo.application_users SET role_code='{role}',display_name=N'Demo {role.Replace('_', ' ')}',is_active=1 WHERE windows_identity=SUSER_SNAME();");
                            window = new DesktopCompositionRoot(AppContext.BaseDirectory, database.ConnectionString, settings, temporaryConnection: true).CreateMainWindow();
                            var integrationHealthHeading = (TextBlock)((Panel)window.administrationWorkspaceView.Content).Children[12];
                            window.Title = "ETP Phase 5 demonstration — synthetic data";
                            window.WindowState = WindowState.Normal;
                            // Remove the native caption so content has precisely the requested
                            // viewport; RenderTargetBitmap is intentionally fixed at 96 DPI.
                            window.WindowStyle = WindowStyle.None;
                            window.ResizeMode = ResizeMode.NoResize;
                            window.Width = 1366; window.Height = 768;
                            window.ShowActivated = false;
                            window.Show();
                            Assert.NotEqual(IntPtr.Zero, new WindowInteropHelper(window).Handle);
                            await WaitUntilAsync(() => window.ContinueButton.IsEnabled && window.WelcomeOverlay.Visibility == Visibility.Collapsed, () => dispatcherFailure);
                            Assert.Equal(role == "OWNER", window.CurrentShellAccess.CanAdminister);
                            Assert.Equal(role != "VIEWER", window.CurrentShellAccess.CanImport);
                            Assert.True(window.CurrentShellAccess.CanView);
                            window.ApplyDensity(UiDensity.Touch, false);
                            window.ShellBusinessDateSelector.SelectedDate = Day.ToDateTime(TimeOnly.MinValue);
                            await SettleAsync(window);
                            var store = Assert.Single(window.ShellStoreSelector.Items.OfType<ComboBoxItem>(), item => item.Tag?.ToString() == "CAPTURE");
                            window.ShellStoreSelector.SelectedItem = store;
                            await SettleAsync(window);
                            window.reportsWorkspaceView.ApplyScope(Day.ToDateTime(TimeOnly.MinValue), Day.ToDateTime(TimeOnly.MinValue), "CAPTURE");
                            ((DatePicker)window.operationsWorkspaceView.FindName("OperationsFromInput")).SelectedDate = Day.ToDateTime(TimeOnly.MinValue);
                            ((DatePicker)window.operationsWorkspaceView.FindName("OperationsToInput")).SelectedDate = Day.ToDateTime(TimeOnly.MinValue);
                            Assert.False(TaskNavigation.Find("prepare-batch")!.IsAllowed(window.CurrentShellAccess) && role != "OWNER");
                            Assert.False(TaskNavigation.Find("approval-centre")!.IsAllowed(window.CurrentShellAccess) && role != "OWNER");
                            Assert.False(TaskNavigation.Find("register-courier")!.IsAllowed(window.CurrentShellAccess) && role == "VIEWER");
                            Assert.False(TaskNavigation.Find("import-files")!.IsAllowed(window.CurrentShellAccess) && role == "VIEWER");

                            var screenshots = ScreenshotTasks(role);
                            var tasks = TaskNavigation.All.Where(task => task.Available && task.IsAllowed(window.CurrentShellAccess)).ToArray();
                            var walked = new List<string>();
                            var fingerprints = new List<DestinationFingerprint>();
                            foreach (var destination in tasks)
                            {
                                var id = destination.Id;
                                Assert.True(destination.IsAllowed(window.CurrentShellAccess), $"Expected {role} to access {id}.");
                                window.ApplyNavigationDecision(window.shell.Navigate(destination.Route, window.CurrentShellAccess));
                                await WaitForWorkspaceOperationsAsync(window, () => dispatcherFailure);
                                // Choose scope after the next destination is active. Changing
                                // it while still on DSR immediately restores All stores.
                                if (destination.ReportCode is not ("dsr" or "sales-combined"))
                                {
                                    window.ShellStoreSelector.SelectedItem = store;
                                    await SettleAsync(window);
                                    await WaitForWorkspaceOperationsAsync(window, () => dispatcherFailure);
                                    Assert.Equal("CAPTURE", (window.ShellStoreSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString());
                                    Assert.Equal("CAPTURE", window.StoreScopes.Resolve(window.reportsWorkspaceView.StoreScope));
                                }
                                await RefreshTaskAsync(window, destination);
                                await WaitForWorkspaceOperationsAsync(window, () => dispatcherFailure);
                                await SettleAsync(window);
                                if (window.FocusedWorkspaceHost.Content is ImportProblemsView problems)
                                    await WaitUntilAsync(() => problems.HasLoaded, () => dispatcherFailure);
                                if (screenshots.ContainsKey(id)) await AssertWorkflowDataAsync(window, id, () => dispatcherFailure);
                                Assert.Equal(id, window.shell.CurrentRoute.TaskId);
                                Assert.NotNull(window.FocusedWorkspaceHost.Content);
                                Assert.Null(dispatcherFailure);
                                walked.Add(id);
                                CheckTaskContent(window, destination, role, integrationHealthHeading, failures);
                                if (role == "OWNER") fingerprints.Add(Fingerprint(window, id));
                                if (evidence is null || !screenshots.TryGetValue(id, out var name)) continue;
                                foreach (var (width, height) in new[] { (1366, 768), (816, 480) })
                                {
                                    window.Width = width; window.Height = height;
                                    await SettleAsync(window);
                                    var root = (FrameworkElement)window.Content;
                                    Assert.InRange(root.ActualWidth, width - 1, width + 1);
                                    Assert.InRange(root.ActualHeight, height - 1, height + 1);
                                    AssertShellLayout(window, root);
                                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                                    bitmap.Render(root);
                                    var file = $"{role}-{name}-{width}x{height}-wpf.png";
                                    SavePng(bitmap, Path.Combine(evidence, file));
                                    var colours = AssertUsefulPixels(bitmap);
                                    captures.Add(new(role, id, width, height, file, colours));
                                    if (help is not null && role == "OWNER" && width == 1366 && TaskNavigation.Sections.Contains(name))
                                        SavePng(bitmap, Path.Combine(help, name + ".png"));
                                }
                            }
                            Assert.Equal(tasks.Select(task => task.Id).Order(), walked.Order());
                            if (role == "OWNER") failures.AddRange(DuplicateDestinations(fingerprints));
                            destinations.Add(role, walked.Count);
                            // No UI operation can outlive the fixture database.
                            var drafts = UnexpectedDrafts(window);
                            if (evidence is not null) File.AppendAllText(Path.Combine(evidence, "capture-progress.txt"), $"{DateTimeOffset.UtcNow:O} Closing {role}; unexpected drafts: {string.Join(", ", drafts)}\n");
                            if (drafts.Count != 0) failures.Add($"{role}: navigation created unexpected drafts: {string.Join(", ", drafts)}");
                            DiscardFixtureDrafts(window);
                            await window.importWorkspaceView.DisposeAsync();
                            window.Close(); window = null;
                            await application.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                            Assert.Null(dispatcherFailure);
                        }
                        completion.TrySetResult(new(captures, destinations, failures));
                    }
                    catch (Exception exception) { completion.TrySetException(exception); }
                    finally
                    {
                        if (window is not null) { DiscardFixtureDrafts(window); window.Close(); }
                        application.Shutdown();
                    }
                });
                application.Run();
            }
            catch (Exception exception) { completion.TrySetException(exception); application?.Shutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { return await completion.Task.WaitAsync(TimeSpan.FromMinutes(8)); }
        finally { Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Capture dispatcher did not close."); }
    }

    private static async Task RefreshTaskAsync(MainWindow window, TaskDestination task)
    {
        if (task.ReportCode is not null) await window.reportsWorkspaceView.RunReportAsync(task.ReportCode);
        else if (task.Id == "import-history") await window.importHistoryView!.ActivateAsync(new(Day, Day, "CAPTURE"));
        else if (task.Id == "source-inbox") await window.sourceInboxWorkspaceView.RefreshAsync();
        else if (task.Destination == "Dashboard") await window.RefreshDashboardAsync();
        else if (task.Destination == "Report Archive") await window.archiveWorkspaceView.RefreshAsync();
        else if (task.Destination is "Daily Workflow" or "Manual Entry") await window.dailyWorkflowWorkspace.RefreshAsync();
        else if (task.Id.StartsWith("register-", StringComparison.Ordinal)) await window.registersWorkspaceView.RefreshAsync();
        else if (task.Id == "prepare-batch") await window.accountingWorkspaceView.RefreshAsync();
        else if (task.Id == "approval-centre") await window.investigationWorkspaceView.RefreshApprovalsAsync();
        else if (task.Id is "users" or "kpi" or "health" or "stores") await window.administrationWorkspaceView.RefreshAsync();
        else if (task.Destination == "Operations Center" && task.Id is not ("investigation" or "adjustment")) await window.operationsWorkspaceView.RefreshAsync();
    }

    private static Task WaitForWorkspaceOperationsAsync(MainWindow window, Func<Exception?> failure) => WaitUntilAsync(() =>
        !window.dailyWorkflowWorkspace.IsBusy && !window.registersWorkspaceView.IsBusy && !window.accountingWorkspaceView.IsBusy &&
        !window.archiveWorkspaceView.IsBusy && !window.operationsWorkspaceView.IsBusy && !window.administrationWorkspaceView.IsBusy &&
        !window.investigationWorkspaceView.IsBusy && !window.sourceInboxWorkspaceView.IsBusy && !window.importWorkspaceView.IsBusy &&
        !window.settingsWorkspace.IsBusy && window.importHistoryView?.IsLoading != true, failure);

    private static IReadOnlyDictionary<string, string> ScreenshotTasks(string role)
    {
        // Artifact volume is deliberate; coverage is always the complete catalogue above.
        var tasks = new Dictionary<string, string>
        {
            ["report-dsr"] = "Today", [role == "VIEWER" ? "import-history" : "import-files"] = "Import",
            ["reports-list"] = "Reports", ["report-stock-closing"] = "Stock", ["settings"] = "Settings",
            ["generations"] = "Archive", ["readiness"] = "Close-day", ["help:getting-started"] = "Help"
        };
        if (role != "VIEWER")
        {
            tasks.Add("register-courier", "Courier"); tasks.Add("register-inward", "Inward"); tasks.Add("investigation", "Investigation");
        }
        if (role == "OWNER")
        {
            tasks.Add("prepare-batch", "Accounting"); tasks.Add("watch-folder", "Automatic-import"); tasks.Add("approval-centre", "Approvals");
        }
        return tasks;
    }

    private static void CheckTaskContent(MainWindow window, TaskDestination task, string role, TextBlock integrationHealthHeading, List<string> failures)
    {
        void Require(bool condition, string message)
        {
            if (!condition) failures.Add($"{role} / {task.Id}: {message}");
        }
        var nodes = Attached(window.FocusedWorkspaceHost).ToArray();
        var text = nodes.OfType<TextBlock>().ToArray();
        var buttons = nodes.OfType<Button>().ToArray();
        Require(window.FocusedWorkspaceLayer.Visibility == Visibility.Visible, "Focused workspace is hidden.");
        Require(nodes.Any(node => node is TextBlock or Button or DataGrid), "Focused workspace has no rendered content.");
        var loadFailures = new[] { "could not", "was not saved", "unable to", "does not have permission", "does not have application access", "report failed", "login failed", "permission denied", "SQL Server is unreachable", "database rejected" };
        var statuses = text.Where(block => block.Name.Contains("Status", StringComparison.Ordinal) || block.Name.Contains("Result", StringComparison.Ordinal) || block.Name.Contains("Message", StringComparison.Ordinal))
            .Select(block => (Name: block.Name, Text: block.Text)).ToList();
        statuses.Add(("ApplicationStatus", window.ApplicationStatus.Text));
        if (window.FocusedWorkspaceHost.Content is ImportHistoryView history) statuses.Add(("Import history", history.StatusText));
        if (window.FocusedWorkspaceHost.Content is ImportProblemsView problems) statuses.Add(("Import problems", problems.MessageText));
        foreach (var status in statuses)
            Require(!loadFailures.Any(phrase => status.Text.Contains(phrase, StringComparison.OrdinalIgnoreCase)), $"{status.Name} reports a load failure: {status.Text}");
        if (task.ReportCode is { } code)
        {
            Require(window.FocusedWorkspaceHost.Content switch
            {
                ReportWorkspaceControl report => report.SelectedReport?.Code == code && report.HasCurrentPreview,
                DailySalesReportWorkspace daily => code == "dsr" && daily.HasCurrentPreview,
                _ => false
            }, $"Report did not produce a current preview. Header store: {(window.ShellStoreSelector.SelectedItem as ComboBoxItem)?.Tag}; report store: {window.reportsWorkspaceView.StoreScope}; displayed status: {string.Join(" / ", statuses.Select(status => status.Text))}");
        }
        if (task.Id == "cash-input")
        {
            var fields = (WrapPanel)window.dailyWorkflowWorkspace.FindName("CashQuickFields");
            Require(nodes.Contains(fields), "F-01: CashQuickFields is detached from the focused task.");
            Require(fields.Visibility == Visibility.Visible && fields.Children.OfType<Button>().Any(), "F-01: cash-book labelled tiles did not load in their body panel.");
        }
        if (task.Id == "investigation")
            Require(buttons.All(button => button.Name != "RefreshApprovalsButton"), "F-02: Investigation carries the Approvals refresh action.");
        if (task.Id == "approval-centre")
            Require(buttons.Any(button => button.Name == "RefreshApprovalsButton"), "F-02: Approvals is missing its refresh action.");
        if (task.Id is "users" or "kpi" or "health" or "stores")
        {
            Require(text.Any(block => block.Name == "AdministrationStatus"), "F-03: administration status line is detached.");
            Require(text.Contains(integrationHealthHeading), "F-03: integration health heading is detached.");
        }
        if (task.Id is "support-package" or "backups" or "recovery")
        {
            var guidance = task.Id switch
            {
                "support-package" => "aggregate-only diagnostic package",
                "backups" => "checksum backup and verify it",
                _ => "isolated temporary database"
            };
            Require(text.Any(block => block.Text.Contains(guidance, StringComparison.Ordinal)), $"F-04: {task.Title} has no explanatory guidance.");
            Require(text.Any(block => block.Text == "Backup, recovery and support"), "F-04: maintenance task has no heading.");
        }
        if (task.Id == "watch-folder")
            Require(buttons.Any(button => button.Content?.ToString() == "Refresh operations" && button.IsEnabled), "F-05: Automatic import has no usable refresh action.");
    }

    private static IEnumerable<DependencyObject> Attached(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Attached(child)) yield return descendant;
        }
    }

    private static IReadOnlyList<string> UnexpectedDrafts(MainWindow window)
    {
        var drafts = new List<string>();
        if (window.registersWorkspaceView.HasUnsavedChanges) drafts.Add("Register entry");
        drafts.AddRange(window.dailyWorkflowWorkspace.UnsavedDrafts.Select(task => "Daily workflow: " + task));
        if (window.settingsWorkspace.HasProductDraft) drafts.Add("Integration settings");
        if (window.operationsWorkspaceView.HasWatchDraft) drafts.Add("Automatic import");
        drafts.AddRange(window.operationsWorkspaceView.UnsavedSchedules.Select(id => "Schedule " + id));
        drafts.AddRange(window.administrationWorkspaceView.UnsavedDrafts.Select(task => "Administration: " + task));
        drafts.AddRange(window.archiveWorkspaceView.UnsavedContacts.Select(id => "Sharing contact " + id));
        if (window.accountingWorkspaceView.HasRetainedDraft) drafts.Add("Accounting review");
        if (window.investigationWorkspaceView.HasRetainedDraft) drafts.Add("Adjustments/approvals");
        if (window.operationsWorkspaceView.HasRetainedDraft) drafts.Add("Data-quality review");
        return drafts;
    }

    private static void DiscardFixtureDrafts(MainWindow window)
    {
        // Failure cleanup only; the assertion above still fails for any untouched
        // screen that creates a draft. Never accept a modal or save fixture edits.
        window.registersWorkspaceView.DiscardDraft();
        foreach (var task in window.dailyWorkflowWorkspace.UnsavedDrafts.ToArray()) window.dailyWorkflowWorkspace.DiscardDraft(task);
        window.settingsWorkspace.DiscardProductDraft();
        window.operationsWorkspaceView.DiscardWatchDraft();
        foreach (var id in window.operationsWorkspaceView.UnsavedSchedules.ToArray()) window.operationsWorkspaceView.DiscardScheduleDraft(id);
        foreach (var task in window.administrationWorkspaceView.UnsavedDrafts.ToArray()) window.administrationWorkspaceView.DiscardDraft(task);
        foreach (var id in window.archiveWorkspaceView.UnsavedContacts.ToArray()) window.archiveWorkspaceView.DiscardContactDraft(id);
        window.accountingWorkspaceView.DiscardRetainedDraft();
        window.investigationWorkspaceView.DiscardRetainedDraft();
        window.operationsWorkspaceView.DiscardRetainedDraft();
    }

    private static async Task AssertWorkflowDataAsync(MainWindow window, string task, Func<Exception?> failure)
    {
        if (task.StartsWith("register-", StringComparison.Ordinal))
            Assert.True(((DataGrid)window.registersWorkspaceView.FindName("RegisterGrid")).Items.Count > 0, "Synthetic register row did not load.");
        else if (task == "generations")
            Assert.True(((DataGrid)window.archiveWorkspaceView.FindName("ReportGenerationGrid")).Items.Count > 0, "Synthetic report archive did not load.");
        else if (task == "approval-centre")
            Assert.True(window.investigationWorkspaceView.ApprovalRowCount >= 3, "Pending and decided synthetic approvals did not load.");
        else if (task == "prepare-batch")
        {
            var batches = (DataGrid)window.accountingWorkspaceView.FindName("AccountingBatchGrid");
            Assert.True(batches.Items.Count > 0, "Synthetic accounting batch did not load.");
            batches.SelectedIndex = 0;
            var entries = (DataGrid)window.accountingWorkspaceView.FindName("AccountingEntryGrid");
            await WaitUntilAsync(() => entries.Items.Count == 2, failure);
        }
        else if (task == "investigation")
        {
            ((TextBox)window.investigationWorkspaceView.FindName("GlobalSearchInput")).Text = "DEMO";
            Visuals(window.FocusedWorkspaceHost).OfType<Button>().Single(button => button.Content?.ToString() == "Search")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !window.investigationWorkspaceView.IsBusy, failure);
            Assert.True(((DataGrid)window.investigationWorkspaceView.FindName("InvestigationGrid")).Items.Count > 0, "Synthetic investigation results did not load.");
        }
        await SettleAsync(window);
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }

    private static async Task SettleAsync(MainWindow window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static void AssertShellLayout(MainWindow window, FrameworkElement root)
    {
        var rails = window.RailPanel.Children.OfType<Button>().Where(button => button.Tag is string section && TaskNavigation.Sections.Contains(section)).ToArray();
        Assert.Equal(5, rails.Length);
        foreach (var rail in rails)
        {
            Assert.True(rail.IsVisible);
            Assert.True(rail.ActualHeight >= 40, $"Rail {rail.Content} has only {rail.ActualHeight} DIP height in touch density.");
            AssertInside(rail, root);
        }
        AssertInside(window.ShellHeader, root);
        AssertInside(window.FocusedWorkspaceHost, root);
        AssertInside(window.StatusFooter, root);
        Assert.True(window.FocusedWorkspaceHost.ActualWidth >= 400 && window.FocusedWorkspaceHost.ActualHeight >= 120);
    }

    private static void AssertInside(FrameworkElement child, FrameworkElement root)
    {
        var bounds = child.TransformToAncestor(root).TransformBounds(new Rect(child.RenderSize));
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1,
            $"{child.Name} lies outside viewport: {bounds}; viewport {root.ActualWidth}x{root.ActualHeight}.");
    }

    private static int AssertUsefulPixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var colours = new HashSet<uint>();
        var nonOpaqueInterior = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            // WPF's premultiplied composition can round a fully painted text/edge
            // pixel to alpha 254. Permit that single quantisation unit, while
            // requiring coverage for EVERY interior pixel. The native HWND can
            // round the requested logical size by <1 DIP at non-100% Windows DPI;
            // only the outermost pixel may therefore have fractional coverage.
            var pixel = offset / 4;
            var x = pixel % bitmap.PixelWidth; var y = pixel / bitmap.PixelWidth;
            if (pixels[offset + 3] < 254 && x > 0 && x < bitmap.PixelWidth - 1 && y > 0 && y < bitmap.PixelHeight - 1)
                nonOpaqueInterior++;
            if (offset % 68 == 0) colours.Add(BitConverter.ToUInt32(pixels, offset));
        }
        Assert.Equal(0, nonOpaqueInterior);
        Assert.True(colours.Count >= 32, $"Only {colours.Count} sampled colours; screen may be blank.");
        return colours.Count;
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static string? HelpOutputDirectory()
    {
        var supplied = Environment.GetEnvironmentVariable("ETP_PHASE5_HELP_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(supplied)) return null;
        var path = Path.GetFullPath(supplied).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetFileName(path), "HelpScreenshots", StringComparison.Ordinal) ||
            !File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "Etp.Reporting.Desktop.csproj")))
            throw new InvalidOperationException("Help capture output must be the Desktop project's HelpScreenshots directory.");
        return Directory.CreateDirectory(path).FullName;
    }

    private static async Task WaitUntilAsync(Func<bool> ready, Func<Exception?> failure)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (!ready())
        {
            if (failure() is { } exception) throw new InvalidOperationException("Capture dispatcher failed.", exception);
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Synthetic MainWindow startup did not complete.");
            await Task.Delay(50);
        }
    }

    private static string? HashIfPresent(string path)
    {
        if (!File.Exists(path)) return null;
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private static async Task SeedAsync(SqlDatabaseFixture database)
    {
        await database.ExecuteAsync("""
            INSERT dbo.stores(store_code,store_name,is_active) VALUES('CAPTURE',N'Demo store',1);
            INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
             VALUES(N'DEMO\CaptureOwner',N'Demo backup Owner','OWNER',1,N'Synthetic fixture',N'Preserve the last-Owner guard while rendering other fixture roles');
            UPDATE dbo.application_users SET display_name=N'Demo Owner',role_code='OWNER',is_active=1 WHERE windows_identity=SUSER_SNAME();
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@lineage bigint,@invoice bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count) VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),3);
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
             VALUES(@batch,'DEMO-SALES.xlsx',REPLICATE('a',64),2048,'R025','CAPTURE','20260825','20260825','20260825',1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Demo',1,'SALES_LINE'); SET @lineage=SCOPE_IDENTITY();
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('CAPTURE','DEMO-INV-001',2027,'20260825'); SET @invoice=SCOPE_IDENTITY();
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,currency_code,source_lineage_id)
             VALUES(@invoice,'1','DEMO-WATCH','INV',2,1200,1200,'INR',@lineage);
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Demo',2,'TENDER'); SET @lineage=SCOPE_IDENTITY();
            INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id) VALUES(@invoice,'CASH',1200,'INR',@lineage);
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Demo',3,'STOCK'); SET @lineage=SCOPE_IDENTITY();
            INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,brand_code,brand_name,quantity,unit_cost,total_cost,source_lineage_id)
             VALUES('CAPTURE','20260825','DEMO-WATCH','DEMO',N'Demo brand',8,400,3200,@lineage);
            EXEC dbo.save_register_entry @type='COURIER',@store='CAPTURE',@date='20260825',@number='DEMO-COURIER-001',@counterparty=N'Demo courier',@quantity=1,@amount=80,@verification='DRAFT',@reason=N'Synthetic example';
            EXEC dbo.save_register_entry @type='INWARD',@store='CAPTURE',@date='20260825',@number='DEMO-INWARD-001',@counterparty=N'Demo supplier',@quantity=8,@amount=3200,@verification='DRAFT',@reason=N'Synthetic example';
            EXEC dbo.save_register_entry @type='INWARD',@store='CAPTURE',@date='20260825',@number='DEMO-INWARD-001',@counterparty=N'Demo supplier',@quantity=8,@amount=3200,@verification='VERIFIED',@reason=N'Demo evidence checked';
            EXEC dbo.submit_controlled_adjustment 'CAPTURE','20260825','CORRECTION',25,N'Synthetic example awaiting Owner review';
            EXEC dbo.submit_controlled_adjustment 'CAPTURE','20260825','CORRECTION',10,N'Synthetic decided example';
            DECLARE @approval bigint=(SELECT MAX(approval_request_id) FROM dbo.approval_requests);
            EXEC dbo.decide_approval_request @approval,1,N'Synthetic evidence checked';
            EXEC dbo.request_import_restatement @file, 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', 'R025','CAPTURE','20260825','20260825',N'Synthetic replacement source awaiting review';
            """);
        var pack = new ReportPackDocument("Demo store — saved report pack", Day, Day, "PASS", "Synthetic example", "Demonstration data only", new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero),
            [new("Sales summary", "PASS", "Synthetic example", new ExcelReportData([new("Store"), new("Sales", "#,##0.00")], [["CAPTURE", 1200m]]))]);
        var json = ReportPackArchiveCodec.Serialize(pack);
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final,report_document_json,document_sha256)
             VALUES('CAPTURE','20260825',1,REPLICATE('c',64),N'{}',N'Demo Owner',1,@document,@hash);
            DECLARE @generation bigint=SCOPE_IDENTITY(),@batch bigint;
            INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,created_by)
             VALUES('CAPTURE','20260825',@generation,1,1200,1200,'DRAFT',N'Demo Owner'); SET @batch=SCOPE_IDENTITY();
            INSERT dbo.accounting_entries(accounting_batch_id,line_number,business_event,ledger_name,debit_amount,credit_amount,narration,source_reference)
             VALUES(@batch,1,'CASH_RECEIPT',N'Demo cash ledger',1200,0,N'Synthetic sales example','DEMO-INV-001'),
                   (@batch,2,'SALES',N'Demo sales ledger',0,1200,N'Synthetic sales example','DEMO-INV-001');
            """, connection);
        command.Parameters.AddWithValue("@document", json);
        command.Parameters.AddWithValue("@hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant());
        await command.ExecuteNonQueryAsync();
    }
}
