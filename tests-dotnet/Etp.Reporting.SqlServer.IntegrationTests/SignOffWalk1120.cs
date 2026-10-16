using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.Morning;
using Etp.Reporting.Desktop.Modules.NoSaleDays;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.ImportSourceLocations;
using Etp.Reporting.Infrastructure.SqlServer.NoSaleDays;
using Etp.Reporting.Infrastructure.SqlServer.ShopResults;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// The 1.12.0 sign-off walk (16 Oct 2026). Opt-in: it does nothing unless ETP_SIGNOFF_WALK_OUTPUT names the picture folder.
/// It builds a disposable database EtpWalk_yyyyMMdd_HHmmss from the migrations, seeds it from the synthetic fixtures only
/// (tests-dotnet\fixtures and invented store codes WALKA/WALKB/WALKC), opens the real MainWindow as Owner, Store Manager and
/// Viewer (the application role claim, as the Phase 5 role walk does), performs each checklist step that is safe to automate,
/// takes a picture per step (1366x768, Desktop density) and writes REPORT.md with PASS / CHECK / SAGAR / VM per step.
/// It never starts a scheduled task, never writes into ProgramData's shop results or health export folders, and drops its
/// database at the end. Run it only through Migration 2026-10-04\Run-SignOffWalk-1120.ps1 (elevated: CREATE DATABASE).
/// </summary>
public sealed partial class PhaseFiveFullWindowCaptureTests
{
    internal const string SignOffOutputVariable = "ETP_SIGNOFF_WALK_OUTPUT";
    internal const string SignOffDatabaseVariable = "ETP_SIGNOFF_WALK_DATABASE";
    internal const string SignOffChecklistVariable = "ETP_SIGNOFF_CHECKLIST";
    private static readonly Regex WalkDatabaseName = new("^EtpWalk_[0-9]{8}_[0-9]{6}$", RegexOptions.CultureInvariant);

    [Fact]
    public async Task SignOff_walk_1120_runs_the_checklist_on_a_disposable_database()
    {
        var folder = Environment.GetEnvironmentVariable(SignOffOutputVariable);
        if (string.IsNullOrWhiteSpace(folder))
        {
            output.WriteLine($"Sign-off walk not requested ({SignOffOutputVariable} is not set); nothing to do.");
            return;
        }
        folder = Directory.CreateDirectory(Path.GetFullPath(folder)).FullName;
        var name = Environment.GetEnvironmentVariable(SignOffDatabaseVariable) is { Length: > 0 } given
            ? given : "EtpWalk_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        // Only a generated walk name is ever accepted: never EtpReporting, never a configured database.
        if (!WalkDatabaseName.IsMatch(name)) throw new InvalidOperationException("Unsafe walk database name: " + name);

        var realSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "settings.json");
        var settingsHash = HashIfPresent(realSettings);
        var preferencesHash = HashIfPresent(UiPreferenceStore.RealFilePath);
        var livePending = ShopResultWriter.DefaultPendingFolder();
        var livePendingBefore = ListFiles(livePending);
        var healthExportsBefore = ListFiles(Etp.Reporting.Desktop.Modules.HealthCheck.HealthExportWriter.DefaultDirectory);

        var walk = new SignOffWalk(name, folder, output.WriteLine);
        await TestCleanup.RunAsync(async () =>
            {
                await walk.CreateDatabaseAsync();
                await walk.SeedAsync();
                await walk.RunOnStaAsync();
            },
            TestCleanup.Step("leave the live shop results folder unchanged", () =>
            {
                var added = ListFiles(livePending).Except(livePendingBefore, StringComparer.OrdinalIgnoreCase).ToArray();
                if (added.Length > 0) walk.Problem("A file appeared in the live shop results folder during the walk: " + string.Join(", ", added));
            }),
            TestCleanup.Step("leave the live health export folder unchanged", () =>
            {
                var added = ListFiles(Etp.Reporting.Desktop.Modules.HealthCheck.HealthExportWriter.DefaultDirectory)
                    .Except(healthExportsBefore, StringComparer.OrdinalIgnoreCase).ToArray();
                if (added.Length > 0) walk.Problem("A file appeared in the health export folder during the walk: " + string.Join(", ", added));
            }),
            TestCleanup.Step("leave the real connection settings unchanged", () =>
            {
                if (settingsHash != HashIfPresent(realSettings)) walk.Problem("The real settings.json changed during the walk.");
            }),
            TestCleanup.Step("leave the real display preferences unchanged", () =>
            {
                if (preferencesHash != HashIfPresent(UiPreferenceStore.RealFilePath)) walk.Problem("The real ui-preferences.json changed during the walk.");
            }),
            TestCleanup.StepAsync("write the picture report", walk.WriteReportAsync),
            TestCleanup.StepAsync("drop the walk database", () => DropWalkDatabaseAsync(name)),
            TestCleanup.Step("delete the walk's temporary folder", walk.DeleteTemporaryFolder));
        Assert.True(walk.Problems.Count == 0, "The walk itself had problems (the steps' own results are in REPORT.md): " + string.Join(" | ", walk.Problems));
    }

    private static string[] ListFiles(string folder)
    {
        try { return Directory.Exists(folder) ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories) : []; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
    }

    internal static async Task DropWalkDatabaseAsync(string name)
    {
        if (!WalkDatabaseName.IsMatch(name)) throw new InvalidOperationException("Unsafe walk database name: " + name);
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(TestSqlConnections.ForDatabase(name)) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", connection)
        { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>One checklist step as the report shows it.</summary>
    private sealed class WalkStep(string id, string title)
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public List<string> Lines { get; } = [];
        public List<string> Pictures { get; } = [];
        public List<string> Files { get; } = [];
        public List<string> ForVm { get; } = [];
        public HashSet<string> Roles { get; } = [];
        public bool Failed { get; private set; }
        public bool NeedsSagar { get; private set; }
        public TimeSpan Took { get; set; }

        public void Ok(string text) => Lines.Add("OK: " + text);
        public void Check(string text) { Failed = true; Lines.Add("CHECK: " + text); }
        public void Note(string text) => Lines.Add(text);
        public void Sagar(string text) { NeedsSagar = true; Lines.Add("FOR SAGAR: " + text); }
        public void Vm(string text) => ForVm.Add(text);

        public bool Expect(bool condition, string ok, string check)
        {
            if (condition) Ok(ok); else Check(check);
            return condition;
        }

        public string Status => Failed ? "CHECK"
            : NeedsSagar ? "SAGAR"
            : Lines.Count == 0 && ForVm.Count > 0 ? "VM"
            : ForVm.Count > 0 ? "PASS (part)"
            : "PASS";
    }

    /// <summary>
    /// The walk: one disposable database, one STA dispatcher, one MainWindow at a time. Every step runs in its own try block,
    /// so one step that fails or hangs is reported as CHECK and the walk goes on.
    /// </summary>
    private sealed partial class SignOffWalk
    {
        private const string Owner = "OWNER", Manager = "STORE_MANAGER", Viewer = "VIEWER";
        private static readonly CultureInfo English = CultureInfo.InvariantCulture;
        private static readonly Func<Exception?> NoFailure = static () => null;

        private readonly string databaseName;
        private readonly string connectionString;
        private readonly string folder;
        private readonly string temp;
        private readonly string settings;
        private readonly string walkPending;
        private readonly Action<string> log;
        private readonly EtpConnectionFactory factory;
        private readonly List<WalkStep> steps = [];
        private readonly List<(string Role, string File, string Text)> screenTexts = [];
        private readonly Dictionary<string, string> landing = [];
        private readonly Dictionary<string, string> titles = [];
        private readonly HashSet<Window> allowedWindows = [];
        private readonly DateTimeOffset started = DateTimeOffset.Now;
        private System.Windows.Application? application;
        private MainWindow? window;
        private string? role;
        private WalkStep? current;
        private NoSaleDayActions? noSaleActions;
        private NoSaleDayDialog? pendingNoSaleDialog;
        private int pictureNumber;

        public SignOffWalk(string databaseName, string folder, Action<string> log)
        {
            this.databaseName = databaseName;
            this.folder = folder;
            this.log = log;
            connectionString = TestSqlConnections.ForDatabase(databaseName);
            factory = new EtpConnectionFactory(connectionString);
            temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpSignOffWalk_" + Guid.NewGuid().ToString("N"))).FullName;
            settings = Directory.CreateDirectory(Path.Combine(temp, "settings")).FullName;
            walkPending = Directory.CreateDirectory(Path.Combine(temp, "ShopResults", "pending")).FullName;
        }

        public List<string> Problems { get; } = [];

        public void Problem(string text)
        {
            Problems.Add(text);
            Log("PROBLEM: " + text);
        }

        private void Log(string text)
        {
            var line = $"{DateTimeOffset.Now:HH:mm:ss} {text}";
            try { log(line); } catch (InvalidOperationException) { }
            try { File.AppendAllText(Path.Combine(folder, "walk-progress.txt"), line + Environment.NewLine); } catch (IOException) { }
        }

        public void DeleteTemporaryFolder()
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string MigrationsDirectory => Path.Combine(AppContext.BaseDirectory, "database", "migrations");

        private static string RepositoryRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) return dir.FullName;
            throw new DirectoryNotFoundException("Repository root (Etp.Reporting.slnx) not found above " + AppContext.BaseDirectory);
        }

        // ------------------------------------------------------------------ database

        public async Task CreateDatabaseAsync()
        {
            Log($"Creating {databaseName} from {MigrationsDirectory}");
            await new SqlServerDatabaseBootstrapper(connectionString, new DirectoryMigrationSource(MigrationsDirectory)).BootstrapAsync();
            // As SqlDatabaseFixture: the bootstrap's pooled connections are not handed on (shared-memory transport flake).
            await using (var pooled = new SqlConnection(connectionString)) SqlConnection.ClearPool(pooled);
        }

        private async Task<object?> ScalarAsync(string sql, int timeoutSeconds = 120)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = timeoutSeconds };
            var value = await command.ExecuteScalarAsync();
            return value is DBNull ? null : value;
        }

        private async Task<string?> TextAsync(string sql) => Convert.ToString(await ScalarAsync(sql), English);

        private async Task<decimal?> WalkInsAsync(string store, DateOnly day) =>
            await ScalarAsync($"SELECT numeric_value FROM dbo.manual_operational_inputs WHERE store_code='{store}' AND business_date='{day:yyyyMMdd}' AND field_code='WALK_INS'") is decimal value ? value : null;

        private async Task<string> DayStatusAsync(string store, DateOnly day) =>
            await TextAsync($"SELECT status FROM dbo.daily_reporting_days WHERE store_code='{store}' AND business_date='{day:yyyyMMdd}'") ?? "(no day row)";

        // ------------------------------------------------------------------ dispatcher

        public async Task RunOnStaAsync()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    // Never instantiate Desktop.App: its startup selects real user configuration.
                    application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(application.Dispatcher));
                    CultureInfo.CurrentCulture = PresentationCulture.Indian;
                    foreach (var resource in new[] { "Colors", "Spacing", "Typography", "Icons", "Controls" })
                        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Etp.Reporting.Desktop;component/Themes/{resource}.xaml", UriKind.Absolute) });
                    application.DispatcherUnhandledException += (_, args) =>
                    {
                        args.Handled = true;
                        current?.Check($"The screen raised an error the shell did not handle: {args.Exception.GetType().Name}: {args.Exception.Message}");
                        Log("Dispatcher exception: " + args.Exception);
                    };
                    application.Dispatcher.BeginInvoke(async () =>
                    {
                        try
                        {
                            StartDialogWatchdog();
                            await RunStepsAsync();
                            completion.TrySetResult();
                        }
                        catch (Exception exception) { completion.TrySetException(exception); }
                        finally
                        {
                            try { await CloseWindowAsync(); } catch (Exception exception) { Log("Closing the last window failed: " + exception.Message); }
                            application.Shutdown();
                        }
                    });
                    application.Run();
                }
                catch (Exception exception) { completion.TrySetException(exception); application?.Shutdown(); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            await completion.Task.WaitAsync(TimeSpan.FromMinutes(75));
            if (!thread.Join(TimeSpan.FromSeconds(15))) Problem("The walk's dispatcher thread did not close.");
        }

        /// <summary>
        /// A modal dialog nobody expected (a confirmation sheet, an error box) would stop the walk. Every half second this
        /// looks for one, takes its picture into the current step, says CHECK and closes it. Timers run inside the dialog's
        /// own message loop, so this works while ShowDialog is waiting.
        /// </summary>
        private void StartDialogWatchdog()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                if (application is null) return;
                foreach (var open in application.Windows.OfType<Window>().ToArray())
                {
                    if (ReferenceEquals(open, window) || !open.IsVisible || allowedWindows.Contains(open)) continue;
                    var text = ScreenText(open, requireVisible: false);
                    var step = current;
                    try
                    {
                        if (step is not null && open.Content is FrameworkElement content && open.ActualWidth > 0)
                        {
                            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(open.ActualWidth), (int)Math.Ceiling(open.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(open);
                            var file = PictureName(step, "unexpected-window");
                            SavePng(bitmap, Path.Combine(folder, file));
                            step.Pictures.Add(file);
                        }
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException) { Log("Could not draw an unexpected window: " + exception.Message); }
                    step?.Check($"A window the walk did not expect opened (\"{open.Title}\"): {Short(text, 400)}. The walk closed it.");
                    Log($"Closing unexpected window '{open.Title}'");
                    try { open.DialogResult = false; } catch (InvalidOperationException) { }
                    try { open.Close(); } catch (InvalidOperationException) { }
                }
            };
            timer.Start();
        }

        // ------------------------------------------------------------------ steps

        private async Task StepAsync(string id, Func<WalkStep, Task> body, int minutes = 4)
        {
            var step = new WalkStep(id, ChecklistTitle(id));
            steps.Add(step);
            current = step;
            if (role is not null) step.Roles.Add(role);
            Log($"Step {id} starts");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var work = body(step);
                if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromMinutes(minutes))) != work)
                    step.Check($"The walk gave up on this step after {minutes} minutes; what it saw so far is above.");
                else await work;
            }
            catch (Exception exception)
            {
                step.Check($"The step stopped: {exception.GetType().Name}: {Short(exception.Message, 600)}");
                Log($"Step {id} stopped: {exception}");
                try { if (window is not null) await ShotAsync(step, "when-it-stopped"); }
                catch (Exception picture) when (picture is not OutOfMemoryException) { Log("No picture: " + picture.Message); }
            }
            step.Took = clock.Elapsed;
            current = null;
            Log($"Step {id}: {step.Status} ({step.Took.TotalSeconds:0} s)");
        }

        private void VmStep(string id, string why)
        {
            var step = new WalkStep(id, ChecklistTitle(id));
            step.Vm(why);
            steps.Add(step);
        }

        // ------------------------------------------------------------------ windows

        private async Task OpenAsync(string wanted, bool fresh = false)
        {
            if (!fresh && window is not null && role == wanted)
            {
                current?.Roles.Add(wanted);
                return;
            }
            await CloseWindowAsync();
            await ScalarAsync($"UPDATE dbo.application_users SET role_code='{wanted}',display_name=N'Walk {RoleWord(wanted)}',is_active=1 WHERE windows_identity=SUSER_SNAME();");
            var created = new DesktopCompositionRoot(AppContext.BaseDirectory, connectionString, settings, temporaryConnection: true).CreateMainWindow();
            titles[wanted] = created.Title;
            OverrideLiveSideEffects(created);
            created.WindowState = WindowState.Normal;
            // No native caption, so the content has exactly 1366x768; RenderTargetBitmap is fixed at 96 DPI.
            created.WindowStyle = WindowStyle.None;
            created.ResizeMode = ResizeMode.NoResize;
            created.Width = 1366; created.Height = 768;
            created.ShowActivated = false;
            application!.MainWindow = created;
            window = created;
            role = wanted;
            created.Show();
            await WaitUntilAsync(() => created.ContinueButton.IsEnabled && created.WelcomeOverlay.Visibility == Visibility.Collapsed, NoFailure);
            created.ApplyDensity(UiDensity.Desktop, false);
            await SettleAsync(created);
            await WaitForWorkspaceOperationsAsync(created, NoFailure);
            landing[wanted] = created.shell.CurrentRoute.TaskId ?? "(none)";
            current?.Roles.Add(wanted);
            Log($"Opened ETP as {wanted}; landing {landing[wanted]}");
        }

        /// <summary>
        /// The composition root wires two things that reach outside the disposable database: the Fix buttons write a shop's
        /// result file into ProgramData's pending folder (which the 07:40 task publishes to the real shop), and the no-sale
        /// buttons show a modal dialog. Both are replaced with the same classes pointed at the walk's own folder and dialog.
        /// </summary>
        private void OverrideLiveSideEffects(MainWindow created)
        {
            var fixes = ImportFixServices.Current;
            ImportFixServices.Current = new ImportFixServices
            {
                Fixes = () => new SqlServerImportFixService(factory, null, null, new ShopResultWriter(walkPending), null, null, null),
                Locations = () => new SqlServerImportSourceLocationStore(factory),
                ShopResults = () => new ShopResultWriter(walkPending),
                OpenFolder = path => current?.Note("Open folder asked for: " + path + " (not opened by the walk)."),
                LoadAccess = fixes?.LoadAccess ?? (async token =>
                {
                    var access = await new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync(token);
                    return new ImportWorkspaceAccess(access.CanImport, access.CanAdminister);
                })
            };
            noSaleActions = new NoSaleDayActions(
                () => new SqlServerNoSaleDayService(factory),
                () => created.CurrentShellAccess,
                code => code switch { "WALKA" => "Walk test shop A", "WALKB" => "Walk test shop B", "WALKC" => "Walk test shop C", _ => null },
                (dialog, _) => { pendingNoSaleDialog = dialog; return false; });
            NoSaleDayActions.Current = noSaleActions;
            created.NoSaleDayButtons = new NoSaleDayButtons(
                (store, date, done) => (UIElement?)noSaleActions.CreateMarkButton(store, date, done) ?? new FrameworkElement { Visibility = Visibility.Collapsed },
                (store, date, done) => (UIElement?)noSaleActions.CreateWithdrawButton(store, date, done) ?? new FrameworkElement { Visibility = Visibility.Collapsed });
        }

        private async Task CloseWindowAsync()
        {
            if (window is not { } open) return;
            window = null;
            role = null;
            try
            {
                DiscardFixtureDrafts(open);
                await open.importWorkspaceView.DisposeAsync();
                open.Close();
                await application!.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { Log("Closing a window: " + exception.Message); }
        }

        private static string RoleWord(string value) => value switch { Owner => "Owner", Manager => "Store Manager", _ => "Viewer" };

        private MainWindow Window => window ?? throw new InvalidOperationException("No ETP window is open.");

        /// <summary>Sets the header date and store, opens the task, and waits for the screen's loads.</summary>
        private async Task GoAsync(string taskId, DateOnly? date = null, string? store = null, string? argument = null)
        {
            var w = Window;
            if (date is { } day && w.ShellBusinessDateSelector.SelectedDate != day.ToDateTime(TimeOnly.MinValue))
            {
                w.ShellBusinessDateSelector.SelectedDate = day.ToDateTime(TimeOnly.MinValue);
                await SettleAsync(w);
                await WaitForWorkspaceOperationsAsync(w, NoFailure);
            }
            if (TaskNavigation.Find(taskId) is { } task && argument is null)
                w.ApplyNavigationDecision(w.shell.Navigate(task.Route, w.CurrentShellAccess));
            else
            {
                var navigator = (TaskNavigator?)typeof(MainWindow).GetField("taskNavigator", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(w)
                    ?? throw new InvalidOperationException("The shell's task navigator was not found.");
                if (!navigator.NavigateTaskById(taskId, argument)) current?.Note($"The shell did not open \"{taskId}\" for {RoleWord(role ?? "")}.");
            }
            await WaitForWorkspaceOperationsAsync(w, NoFailure);
            await SettleAsync(w);
            if (store is not null)
            {
                var item = w.ShellStoreSelector.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Tag?.ToString(), store, StringComparison.OrdinalIgnoreCase));
                if (item is null) current?.Note($"The header store list has no {store}.");
                else if (!ReferenceEquals(w.ShellStoreSelector.SelectedItem, item))
                {
                    w.ShellStoreSelector.SelectedItem = item;
                    await SettleAsync(w);
                    await WaitForWorkspaceOperationsAsync(w, NoFailure);
                }
            }
            await WaitIdleAsync();
        }

        /// <summary>Waits until the shown screen is not busy (registered screens report IsBusy) and the dispatcher is idle.</summary>
        private async Task WaitIdleAsync(int milliseconds = 400)
        {
            var w = Window;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && w.FocusedWorkspaceHost.Content is IShellScreen { IsBusy: true }) await Task.Delay(100);
            await Task.Delay(milliseconds);
            await WaitForWorkspaceOperationsAsync(w, NoFailure);
            await SettleAsync(w);
        }

        private static async Task<T> WaitForAsync<T>(Func<T?> probe, int seconds = 45, string? what = null) where T : class
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (true)
            {
                if (probe() is { } value) return value;
                if (DateTime.UtcNow >= deadline) throw new TimeoutException($"Waited {seconds} s for {what ?? typeof(T).Name}.");
                await Task.Delay(100);
            }
        }

        private static async Task<bool> WaitForAsync(Func<bool> probe, int seconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!probe())
            {
                if (DateTime.UtcNow >= deadline) return false;
                await Task.Delay(100);
            }
            return true;
        }

        private void Swap(UserControl view)
        {
            Window.FocusedWorkspaceHost.Content = view;
            Window.FocusedWorkspaceLayer.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------------ pictures and text

        private string PictureName(WalkStep step, string name)
        {
            var id = Regex.Replace(step.Id, "[^0-9A-Za-z]", "");
            var number = int.TryParse(id, NumberStyles.None, English, out var n) ? n.ToString("00", English) : id;
            return $"S{number}-{++pictureNumber:000}-{(role ?? "NONE").Replace("_", "", StringComparison.Ordinal)}-{Regex.Replace(name, "[^0-9A-Za-z-]", "-")}.png";
        }

        /// <summary>The whole window at 1366x768, its text beside it, and the text kept for the all-roles scan (step 41).</summary>
        private async Task<string> ShotAsync(WalkStep step, string name)
        {
            var w = Window;
            w.Width = 1366; w.Height = 768;
            await SettleAsync(w);
            var root = (FrameworkElement)w.Content;
            var bitmap = new RenderTargetBitmap(1366, 768, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var file = PictureName(step, name);
            SavePng(bitmap, Path.Combine(folder, file));
            var text = ScreenText(root, requireVisible: true);
            await File.WriteAllTextAsync(Path.Combine(folder, Path.ChangeExtension(file, ".txt")), text);
            step.Pictures.Add(file);
            screenTexts.Add((role ?? "", file, text));
            return text;
        }

        /// <summary>A dialog or card that is not on screen, drawn on its own (the walk answers dialogs itself).</summary>
        private string ShotElement(WalkStep step, string name, FrameworkElement element, double width = 520)
        {
            var background = application?.TryFindResource("Surface") as Brush ?? Brushes.White;
            var host = new Border { Background = background, Child = element, Padding = new Thickness(0) };
            host.Measure(new Size(width, double.PositiveInfinity));
            host.Arrange(new Rect(0, 0, width, Math.Max(40, host.DesiredSize.Height)));
            host.UpdateLayout();
            var height = (int)Math.Ceiling(Math.Max(40, host.ActualHeight));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var file = PictureName(step, name);
            SavePng(bitmap, Path.Combine(folder, file));
            var text = ScreenText(host, requireVisible: false);
            File.WriteAllText(Path.Combine(folder, Path.ChangeExtension(file, ".txt")), text);
            step.Pictures.Add(file);
            screenTexts.Add((role ?? "", file, text));
            host.Child = null;
            return text;
        }

        /// <summary>A dialog's content drawn without showing the dialog (the content is taken out of the window).</summary>
        private string ShotDialog(WalkStep step, string name, Window dialog)
        {
            if (dialog.Content is not FrameworkElement content) return "";
            dialog.Content = null;
            var width = double.IsNaN(dialog.Width) ? 520 : dialog.Width;
            var text = ShotElement(step, name, content, width);
            dialog.Content = content;
            return text;
        }

        private static string ScreenText(DependencyObject root, bool requireVisible)
        {
            var lines = new List<string>();
            foreach (var node in Visuals(root))
            {
                switch (node)
                {
                    case TextBlock block when !string.IsNullOrWhiteSpace(block.Text) && (!requireVisible || block.IsVisible):
                        lines.Add(block.Text.Trim());
                        break;
                    case TextBox box when !string.IsNullOrWhiteSpace(box.Text) && (!requireVisible || box.IsVisible):
                        lines.Add("[field] " + box.Text.Trim());
                        break;
                }
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return string.Join(Environment.NewLine, lines.Where(seen.Add));
        }

        private static string Short(string? text, int length = 300)
        {
            var flat = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return flat.Length <= length ? flat : flat[..length] + "...";
        }

        private static string ContentText(ContentControl control) => control.Content switch
        {
            string text => text,
            TextBlock block => block.Text,
            _ => control.Content?.ToString() ?? ""
        };

        private static IEnumerable<Button> Buttons(DependencyObject root, bool requireVisible = true) =>
            Visuals(root).OfType<Button>().Where(button => !requireVisible || button.IsVisible);

        private static Button? FindButton(DependencyObject root, string content, bool requireVisible = true) =>
            Buttons(root, requireVisible).FirstOrDefault(button => string.Equals(ContentText(button), content, StringComparison.Ordinal));

        private static Button? FindButtonByName(DependencyObject root, string automationName, bool requireVisible = true) =>
            Buttons(root, requireVisible).FirstOrDefault(button => string.Equals(AutomationProperties.GetName(button), automationName, StringComparison.Ordinal));

        /// <summary>Logical-tree search (it also finds controls on a tab that is not selected).</summary>
        private static T? FindLogical<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject =>
            Attached(root).OfType<T>().FirstOrDefault(item => match?.Invoke(item) ?? true);

        private static T? FindVisual<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject =>
            Visuals(root).OfType<T>().FirstOrDefault(item => match?.Invoke(item) ?? true);

        /// <summary>Selects the tab that holds <paramref name="element"/> (the focused layout puts parts on named tabs).</summary>
        private static void RevealTab(DependencyObject element)
        {
            for (DependencyObject? node = element; node is not null; node = LogicalTreeHelper.GetParent(node) ?? VisualTreeHelper.GetParent(node))
                if (node is TabItem tab) { tab.IsSelected = true; return; }
        }

        private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        // ------------------------------------------------------------------ report

        private static readonly Lazy<IReadOnlyDictionary<string, string>> ChecklistLines = new(LoadChecklist);

        private static IReadOnlyDictionary<string, string> LoadChecklist()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var path = Environment.GetEnvironmentVariable(SignOffChecklistVariable);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return result;
            foreach (var line in File.ReadAllLines(path))
            {
                var match = Regex.Match(line, @"^(\d+)\.\s+(.*?)\s*\[ \] OK", RegexOptions.CultureInvariant);
                if (match.Success) result[match.Groups[1].Value] = match.Groups[2].Value.Trim();
            }
            return result;
        }

        private static string ChecklistTitle(string id) =>
            ChecklistLines.Value.TryGetValue(id, out var text) ? text : ExtraTitles.TryGetValue(id, out var extra) ? extra : $"Checklist step {id}";

        private static readonly IReadOnlyDictionary<string, string> ExtraTitles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["3b"] = "(Added by the walk) Settings > Database > Error register: a Ref from an error message finds its entry.",
            ["13b"] = "(Added by the walk) The store pack of a day with synthetic sales exports to PDF and Excel; the DSR shows that day.",
            ["X1"] = "(Your judgement) The Morning page is easy to read at a glance.",
            ["X2"] = "(Your judgement) The Today drop-down (rail menu and the header date and store boxes) is acceptable.",
            ["X3"] = "(Your judgement) The printed daily sheet the staff fill in reads well on paper."
        };

        public async Task WriteReportAsync()
        {
            var ordered = steps.OrderBy(step => SortKey(step.Id)).ToArray();
            var report = new StringBuilder();
            report.AppendLine("# ETP 1.12.0 sign-off walk - picture report");
            report.AppendLine();
            report.AppendLine($"Made by the automated walk on {started:dd MMM yyyy HH:mm} on PC {Environment.MachineName}, release 1.12.0 code ({titles.GetValueOrDefault(Owner) ?? DesktopProductVersion.WindowTitle}).");
            report.AppendLine($"Database: {databaseName}, a throw-away copy built from the migrations and dropped at the end. All data is invented (synthetic fixtures and test shops WALKA, WALKB, WALKC); no customer data. Roles: Owner, Store Manager and Viewer, switched in ETP's user list as the Phase 5 role walk does (the same Windows login each time).");
            report.AppendLine();
            report.AppendLine("How to read it: **PASS** = the walk did the step and saw what the checklist says. **PASS (part)** = the part the walk can do passed; the rest is listed under \"Still for the VM\". **CHECK** = the walk saw something different or could not finish; look at the picture and the lines. **SAGAR** = needs your own eyes; the picture to look at is named. **VM** = can only be done on the VM (installer, restarts, Tally, a second disk, USB keys).");
            report.AppendLine();
            foreach (var group in ordered.GroupBy(step => step.Status).OrderBy(group => group.Key, StringComparer.Ordinal))
                report.AppendLine($"- {group.Key}: {group.Count()} ({string.Join(", ", group.Select(step => step.Id))})");
            report.AppendLine();
            report.AppendLine("| Step | Result | Pictures | In one line |");
            report.AppendLine("|---|---|---|---|");
            foreach (var step in ordered)
            {
                var first = step.Lines.FirstOrDefault(line => line.StartsWith("CHECK", StringComparison.Ordinal))
                    ?? step.Lines.FirstOrDefault() ?? step.ForVm.FirstOrDefault() ?? "";
                var picture = step.Pictures.Count == 0 ? "-" : $"[{step.Pictures.Count}]({step.Pictures[0]})";
                report.AppendLine($"| [{step.Id}](#step-{Anchor(step.Id)}) | {step.Status} | {picture} | {Cell(Short(first, 160))} |");
            }
            report.AppendLine();
            foreach (var step in ordered)
            {
                report.AppendLine($"<a id=\"step-{Anchor(step.Id)}\"></a>");
                report.AppendLine($"## Step {step.Id} - {step.Status}");
                report.AppendLine();
                report.AppendLine($"Checklist: {step.Title}");
                report.AppendLine();
                if (step.Roles.Count > 0) report.AppendLine($"Roles: {string.Join(", ", step.Roles.Select(RoleWord))}. Took {step.Took.TotalSeconds:0} s.");
                if (step.Lines.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("What happened:");
                    foreach (var line in step.Lines) report.AppendLine("- " + line.Replace("\r", "", StringComparison.Ordinal).Replace("\n", " / ", StringComparison.Ordinal));
                }
                if (step.ForVm.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("Still for the VM:");
                    foreach (var line in step.ForVm) report.AppendLine("- " + line);
                }
                if (step.Pictures.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("Pictures (the .txt beside each picture holds the text the walk read on that screen):");
                    foreach (var picture in step.Pictures) report.AppendLine($"- [{picture}]({picture})");
                }
                if (step.Files.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("Files:");
                    foreach (var file in step.Files) report.AppendLine($"- [{file}]({file})");
                }
                report.AppendLine();
            }
            if (Problems.Count > 0)
            {
                report.AppendLine("## Problems of the walk itself");
                report.AppendLine();
                foreach (var problem in Problems) report.AppendLine("- " + problem);
                report.AppendLine();
            }
            await File.WriteAllTextAsync(Path.Combine(folder, "REPORT.md"), report.ToString());
            await File.WriteAllTextAsync(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(new
            {
                generated = DateTimeOffset.Now,
                database = databaseName,
                machine = Environment.MachineName,
                problems = Problems,
                steps = ordered.Select(step => new { step.Id, step.Status, step.Title, roles = step.Roles, step.Lines, forVm = step.ForVm, step.Pictures, step.Files, seconds = Math.Round(step.Took.TotalSeconds) })
            }, new JsonSerializerOptions { WriteIndented = true }));
            Log($"Report written: {Path.Combine(folder, "REPORT.md")}");
        }

        private static (int, string) SortKey(string id)
        {
            var digits = new string(id.TakeWhile(char.IsDigit).ToArray());
            return digits.Length > 0 ? (int.Parse(digits, English), id) : (1000, id);
        }

        private static string Anchor(string id) => id.ToLowerInvariant();

        private static string Cell(string text) => text.Replace("|", "/", StringComparison.Ordinal);
    }
}
