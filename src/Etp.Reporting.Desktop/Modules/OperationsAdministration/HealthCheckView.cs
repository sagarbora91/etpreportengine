extern alias EtpApplication;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using EtpApplication::Etp.Reporting.Application.ErrorRegister;
using EtpApplication::Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Desktop.Themes;

namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

/// <summary>
/// What the two 1.11.0 self-diagnostics screens need from the rest of the application. The composition root (lane
/// nightly) sets <see cref="DiagnosticsScreens.Services"/> once at start-up; until then the screens say they are not
/// available instead of failing to open. The screens read data only through the stores.
/// </summary>
public sealed class DiagnosticsScreenServices
{
    public Func<IHealthCheckStore?> HealthStore { get; init; } = static () => null;
    public Func<IErrorRegisterStore?> ErrorStore { get; init; } = static () => null;

    /// <summary>
    /// Runs one Manual health check, writes its lines to the progress ("Reports 7 of 22…") and returns the id of the
    /// run it recorded through <see cref="IHealthCheckStore.RecordRunAsync"/>. Null: the screen cannot run a check.
    /// </summary>
    public Func<IProgress<string>, CancellationToken, Task<long>>? RunHealthCheck { get; init; }

    /// <summary>Reads the Owner's own diagnostics log into the register (Error register, Refresh). Null: Refresh only reloads.</summary>
    public Func<CancellationToken, Task<ErrorIngestResult?>>? IngestOwnLog { get; init; }

    /// <summary>The folder the nightly exports are written to, for "Open export folder". Null or missing: the button says so.</summary>
    public Func<string?>? ExportFolder { get; init; }
}

/// <summary>The two Owner-only screens of Settings &gt; Database (1.11.0 self-diagnostics, design 9) and their factory.</summary>
public static class DiagnosticsScreens
{
    public const string Destination = "Operations Center";
    public const string HealthCheckTask = "health-check";
    public const string ErrorRegisterTask = "error-register";

    public static DiagnosticsScreenServices Services { get; set; } = new();

    private static async Task LoadThenFindAsync(ErrorRegisterView register, string reference)
    {
        await register.LoadAsync();
        await register.FindAsync(reference);
    }

    public static bool Handles(string? taskId) => taskId is HealthCheckTask or ErrorRegisterTask;

    /// <summary>
    /// Creates the screen of a task and starts its first load. <paramref name="isOwner"/> is read at every action, so a
    /// role change in the shell applies at once; <paramref name="navigate"/> opens a task by id (the "Where to fix" button).
    /// </summary>
    public static UserControl Create(string taskId, Func<bool> isOwner, Action<string>? navigate, string? argument = null)
    {
        var services = Services;
        switch (taskId)
        {
            case HealthCheckTask:
                var health = new HealthCheckView(services.HealthStore, services.RunHealthCheck, isOwner, navigate, exportFolder: services.ExportFolder);
                _ = health.LoadAsync();
                return health;
            case ErrorRegisterTask:
                var register = new ErrorRegisterView(services.ErrorStore, services.IngestOwnLog, isOwner);
                // 1.11.2: "Open in Error register" beside a Ref passes the Ref as the route argument; the register loads, then finds it.
                _ = string.IsNullOrWhiteSpace(argument) ? register.LoadAsync() : LoadThenFindAsync(register, argument);
                return register;
            default:
                throw new ArgumentOutOfRangeException(nameof(taskId), taskId, "Not a diagnostics task.");
        }
    }
}

/// <summary>Where the fix for a line is: a task the Where-to-fix button opens, or a plain instruction.</summary>
public sealed record FixTarget(string Label, string? TaskId)
{
    public bool IsTask => TaskId is not null;

    public static FixTarget Resolve(string? fix)
    {
        if (string.IsNullOrWhiteSpace(fix)) return new("", null);
        var task = TaskNavigation.Find(fix.Trim());
        return task is null ? new(fix.Trim(), null) : new(task.Path, task.Id);
    }
}

/// <summary>One check line as the grid shows it.</summary>
public sealed record HealthRow(HealthCheckResult Result, FixTarget Fix, HealthOutcome? Earlier)
{
    public string Outcome => HealthCheckView.OutcomeLabel(Result.Outcome);
    public string OutcomeKind => Result.Outcome.ToString();
    public string Area => Result.Area.ToString();
    public string Check => Result.CheckId;
    public string Finding => Result.UnchangedSince is { } since
        ? $"{Result.Explanation} (unchanged since {since.ToString("dd MMM", PresentationCulture.Indian)})"
        : Result.Explanation;
    public string FixText => Fix.Label;
    /// <summary>1.11.2: a screen reader hears which line each "Where to fix" button belongs to, not the same name 50 times.</summary>
    public string FixButtonName => $"Open where to fix for {Result.CheckId}: {Fix.Label}";
    public Visibility FixButtonVisibility => Fix.IsTask ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FixTextVisibility => Fix.IsTask ? Visibility.Collapsed : Visibility.Visible;
    public string Took => Result.Duration.TotalSeconds.ToString("0.0", PresentationCulture.Indian) + " s";
    public string EarlierText => Earlier is { } value ? HealthCheckView.OutcomeLabel(value) : "";
    public string ChangeText => Earlier is { } value && value != Result.Outcome
        ? $"{HealthCheckView.OutcomeLabel(value)} → {HealthCheckView.OutcomeLabel(Result.Outcome)}" : "";
}

/// <summary>A line whose outcome differs from the compared run.</summary>
public sealed record HealthChange(string CheckId, HealthOutcome From, HealthOutcome To);

/// <summary>
/// Settings &gt; Database &gt; Health check (design 9.1): the lines of the latest run, Fails first, with Run health check,
/// Compare with, Accept selected warning (Known) / End acceptance and Open export folder. Owner only; every action re-checks the role. The data comes only
/// from <see cref="IHealthCheckStore"/>, and a run is started through the delegate the composition root gives it.
/// </summary>
public sealed class HealthCheckView : UserControl
{
    public const string ScreenTitle = "Health check";
    public const string OwnerOnlyText = "The health check is for the Owner. Ask the Owner to open Settings > Database > Health check.";
    public const string NotAvailableText = "The health check is not available in this build.";
    public const string AboutText = "54 read-only checks of the database, security, reports, imports, data, operations and errors. The nightly run is at 02:30; this screen shows the latest run.";
    public const string NoRunText = "No health check has run yet. Press Run health check.";
    public const string ShopHoursQuestion = "This takes about a minute and reads the database. Run now?";
    public const int MaxKnownDays = 90;
    public const int MaxReasonLength = 200;

    private readonly Func<IHealthCheckStore?> store;
    private readonly Func<IProgress<string>, CancellationToken, Task<long>>? runner;
    private readonly Func<bool> isOwner;
    private readonly Action<string>? navigate;
    private readonly Func<DateTime> now;
    private readonly Func<string, bool> confirm;
    private readonly Func<DateOnly, (string Reason, DateOnly Until)?> askKnown;
    private readonly Func<string?>? exportFolder;
    private readonly Action<string> openFolder;
    // DS-20: the verdict band (what is wrong, in words) comes first; the run line below it is a caption.
    private readonly Border verdictBand = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(16), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
    private readonly TextBlock verdict = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock verdictFinding = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock headline = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock changes = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock progress = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly Button runButton = new() { Content = "Run health check", MinHeight = 44, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button cancelButton = new() { Content = "Cancel", MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    private readonly Button refreshButton = new() { Content = "Refresh", MinHeight = 44, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button folderButton = new() { Content = "Open export folder", MinHeight = 44, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button knownButton = new() { Content = "Accept selected warning (Known)…", MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
    private readonly Button endKnownButton = new() { Content = "End acceptance", MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
    private readonly ComboBox compareBox = new() { MinHeight = 44, MinWidth = 260, IsEnabled = false };
    private readonly CheckBox showPassedBox = new() { Content = "Show passed lines", Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid grid = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
    private readonly TextBox details = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private IReadOnlyList<HealthCheckResult> results = [];
    private IReadOnlyList<HealthCheckResult> earlierResults = [];
    private IReadOnlyList<HealthCheckRunSummary> runs = [];
    private HealthCheckRunSummary? current;
    private HealthCheckRunSummary? compared;
    private CancellationTokenSource? runCancellation;
    private int revision;
    private bool applyingCompare;

    public HealthCheckView(
        Func<IHealthCheckStore?> store,
        Func<IProgress<string>, CancellationToken, Task<long>>? runner,
        Func<bool> isOwner,
        Action<string>? navigate = null,
        Func<DateTime>? now = null,
        Func<string, bool>? confirm = null,
        Func<DateOnly, (string Reason, DateOnly Until)?>? askKnown = null,
        Func<string?>? exportFolder = null,
        Action<string>? openFolder = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.runner = runner;
        this.isOwner = isOwner ?? throw new ArgumentNullException(nameof(isOwner));
        this.navigate = navigate;
        this.now = now ?? (() => DateTime.Now);
        this.confirm = confirm ?? (text => ConfirmationSheet.Show(this, "Run health check", text));
        this.askKnown = askKnown ?? AskKnownWithDialog;
        this.exportFolder = exportFolder;
        this.openFolder = openFolder ?? OpenInExplorer;

        var root = new Grid { Margin = new Thickness(4) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });

        var heading = new StackPanel();
        // DS-20/DS-24: the shell header owns the title; the explanation is a tooltip on the verdict, not a paragraph.
        var bandContent = new StackPanel();
        verdict.SetResourceReference(FrameworkElement.StyleProperty, "MetricText");
        verdictFinding.SetResourceReference(FrameworkElement.StyleProperty, "BodyText");
        bandContent.Children.Add(verdict);
        bandContent.Children.Add(verdictFinding);
        verdictBand.Child = bandContent;
        verdictBand.ToolTip = AboutText;
        AutomationProperties.SetName(verdictBand, "Health check verdict");
        headline.SetResourceReference(FrameworkElement.StyleProperty, "CaptionText");
        changes.SetResourceReference(FrameworkElement.StyleProperty, "CaptionText");
        progress.SetResourceReference(FrameworkElement.StyleProperty, "BodyText");
        status.SetResourceReference(FrameworkElement.StyleProperty, "CaptionText");
        heading.Children.Add(verdictBand);
        heading.Children.Add(headline);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        runButton.Click += async (_, _) => await RunAsync();
        cancelButton.Click += (_, _) => Cancel();
        refreshButton.Click += async (_, _) => await LoadAsync();
        folderButton.Click += (_, _) => OpenExportFolder();
        // One primary action: Run health check. Refresh, Compare and Open export folder sit under the grid (DS-20).
        runButton.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        buttons.Children.Add(runButton);
        buttons.Children.Add(cancelButton);
        heading.Children.Add(buttons);
        heading.Children.Add(progress);
        heading.Children.Add(changes);
        heading.Children.Add(status);
        root.Children.Add(heading);

        AutomationProperties.SetName(grid, "Health check lines");
        AutomationProperties.SetName(compareBox, "Compare with an earlier run");
        AutomationProperties.SetName(details, "Selected line details");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        BuildColumns();
        grid.RowStyle = BuildRowStyle();
        grid.SelectionChanged += (_, _) => ShowDetails();
        compareBox.SelectionChanged += async (_, _) =>
        {
            if (applyingCompare || compareBox.SelectedItem is not CompareChoice choice) return;
            await CompareWithAsync(choice.Run.HealthCheckRunId);
        };
        showPassedBox.Checked += (_, _) => ApplyFilter();
        showPassedBox.Unchecked += (_, _) => ApplyFilter();
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);

        var footer = new StackPanel();
        var footerBar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        knownButton.Click += async (_, _) => await MarkKnownAsync();
        endKnownButton.Click += async (_, _) => await EndKnownAsync();
        footerBar.Children.Add(showPassedBox);
        footerBar.Children.Add(knownButton);
        footerBar.Children.Add(endKnownButton);
        footerBar.Children.Add(refreshButton);
        footerBar.Children.Add(folderButton);
        footerBar.Children.Add(new TextBlock { Text = "Compare with", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
        footerBar.Children.Add(compareBox);
        footer.Children.Add(footerBar);
        footer.Children.Add(details);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        AutomationProperties.SetName(this, ScreenTitle);
        ApplyAccess();
    }

    // ---- state the tests read ------------------------------------------------------------------------------------

    public IReadOnlyList<HealthRow> AllRows { get; private set; } = [];
    public IReadOnlyList<HealthRow> Rows { get; private set; } = [];
    public IReadOnlyList<HealthCheckRunSummary> Runs => runs;
    public HealthCheckRunSummary? CurrentRun => current;
    public HealthCheckRunSummary? ComparedRun => compared;
    public string HeadlineText => headline.Text;
    public string ChangesText => changes.Text;
    public string ProgressText => progress.Text;
    public string StatusText { get => status.Text; private set => status.Text = value; }
    public bool IsRunning { get; private set; }
    public bool ShowPassed { get => showPassedBox.IsChecked == true; set => showPassedBox.IsChecked = value; }
    public HealthRow? SelectedRow => grid.SelectedItem as HealthRow;
    public bool RunEnabled => runButton.IsEnabled;
    public bool KnownEnabled => knownButton.IsEnabled;
    public bool EndKnownEnabled => endKnownButton.IsEnabled;
    public string DetailsText => details.Text;
    public DataGrid Table => grid;
    public int CompareChoiceCount => compareBox.Items.Count;

    public void Select(HealthRow? row) { grid.SelectedItem = row; ShowDetails(); }

    // ---- pure rules ----------------------------------------------------------------------------------------------

    public static string OutcomeLabel(HealthOutcome outcome) => outcome switch
    {
        HealthOutcome.Fail => "FAIL",
        HealthOutcome.Warning => "WARN",
        HealthOutcome.Known => "KNOWN",
        HealthOutcome.Skipped => "SKIPPED",
        _ => "PASS"
    };

    private static int Rank(HealthOutcome outcome) => outcome switch
    {
        HealthOutcome.Fail => 0,
        HealthOutcome.Warning => 1,
        HealthOutcome.Known => 2,
        HealthOutcome.Skipped => 3,
        _ => 4
    };

    private static int CatalogueIndex(string checkId)
    {
        var all = HealthCheckId.All;
        for (var index = 0; index < all.Count; index++)
            if (string.Equals(all[index], checkId, StringComparison.Ordinal)) return index;
        return int.MaxValue;
    }

    /// <summary>Fails first, then Warning, Known, Skipped, Pass; inside an outcome by area, then catalogue order.</summary>
    public static IReadOnlyList<HealthCheckResult> Order(IEnumerable<HealthCheckResult> lines) =>
        lines.OrderBy(line => Rank(line.Outcome)).ThenBy(line => (int)line.Area).ThenBy(line => CatalogueIndex(line.CheckId)).ToArray();

    /// <summary>The lines whose outcome differs from the compared run, in screen order.</summary>
    public static IReadOnlyList<HealthChange> Compare(IReadOnlyList<HealthCheckResult> currentLines, IReadOnlyList<HealthCheckResult> earlierLines)
    {
        var before = new Dictionary<string, HealthOutcome>(StringComparer.Ordinal);
        foreach (var line in earlierLines) before.TryAdd(line.CheckId, line.Outcome);
        return Order(currentLines)
            .Where(line => before.TryGetValue(line.CheckId, out var old) && old != line.Outcome)
            .Select(line => new HealthChange(line.CheckId, before[line.CheckId], line.Outcome))
            .ToArray();
    }

    /// <summary>Shop hours are 09:00 up to 21:00 local (decision 36, Q13): a manual run then asks first.</summary>
    public static bool IsShopHours(DateTime local) => local.TimeOfDay >= TimeSpan.FromHours(9) && local.TimeOfDay < TimeSpan.FromHours(21);

    /// <summary>
    /// The reason of a Known (design 5.1): required, at most 200 characters, plain words only. E-mail addresses, paths,
    /// quotes of file names and other symbols are refused so the reason can be exported (the procedure checks again).
    /// </summary>
    public static string? ValidateSafeText(string? text, string what)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0) return $"Enter {what}.";
        if (value.Length > MaxReasonLength) return $"{char.ToUpperInvariant(what[0])}{what[1..]} can be at most {MaxReasonLength} characters.";
        foreach (var character in value)
            if (!(char.IsLetterOrDigit(character) || character is ' ' or '.' or ',' or ';' or ':' or '\'' or '"' or '!' or '?' or '(' or ')' or '-' or '_' or '/' or '%' or '+'))
                return $"{char.ToUpperInvariant(what[0])}{what[1..]} can use letters, digits, spaces and . , ; : ' \" ! ? ( ) - _ / % + only.";
        return null;
    }

    /// <summary>A Known lasts at most 90 days and cannot end in the past (procedure error 51823).</summary>
    public static string? ValidateKnown(string? reason, DateOnly? until, DateOnly today)
    {
        var reasonProblem = ValidateSafeText(reason, "a reason");
        if (reasonProblem is not null) return reasonProblem;
        if (until is not { } date) return "Choose the date the Known ends.";
        if (date < today) return "The Known cannot end in the past.";
        if (date > today.AddDays(MaxKnownDays)) return $"A Known lasts at most {MaxKnownDays} days. Choose {today.AddDays(MaxKnownDays).ToString("dd MMM yyyy", PresentationCulture.Indian)} or earlier.";
        return null;
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count.ToString(PresentationCulture.Indian)} {noun}s";

    /// <summary>DS-20: the one sentence the Owner reads first, e.g. "1 problem, 5 warnings" or "No problems".</summary>
    public static string VerdictText(HealthCheckCounts counts)
    {
        if (counts.Fail == 0 && counts.Warning == 0) return "No problems";
        return counts.Fail == 0 ? $"No problems, {Plural(counts.Warning, "warning")}"
            : $"{Plural(counts.Fail, "problem")}, {Plural(counts.Warning, "warning")}";
    }

    private void ShowVerdict()
    {
        if (current is null) { verdictBand.Visibility = Visibility.Collapsed; return; }
        var counts = current.Counts;
        verdict.Text = VerdictText(counts);
        var fail = AllRows.FirstOrDefault(row => row.Result.Outcome == HealthOutcome.Fail);
        var warn = AllRows.FirstOrDefault(row => row.Result.Outcome == HealthOutcome.Warning);
        verdictFinding.Text = (fail ?? warn)?.Finding ?? "";
        verdictFinding.Visibility = verdictFinding.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var (fill, edge) = counts.Fail > 0 ? ("CriticalSoft", "Critical") : counts.Warning > 0 ? ("WarningSoft", "Warning") : ("SuccessSoft", "Success");
        verdictBand.SetResourceReference(Border.BackgroundProperty, fill);
        verdictBand.SetResourceReference(Border.BorderBrushProperty, edge);
        verdict.SetResourceReference(TextBlock.ForegroundProperty, edge);
        verdictBand.Visibility = Visibility.Visible;
    }

    /// <summary>"Last nightly run: Fri 10 Oct 2026 02:31, checked Thu 09 Oct - 1 problem, 5 warnings, 2 known".</summary>
    public static string RunLine(string label, HealthCheckRunSummary run)
    {
        var culture = PresentationCulture.Indian;
        var counts = run.Counts;
        return $"{label}: {run.CompletedUtc.ToLocalTime().ToString("ddd dd MMM yyyy HH:mm", culture)}, checked {run.BusinessDate.ToString("ddd dd MMM", culture)} - "
            + $"{Plural(counts.Fail, "problem")}, {Plural(counts.Warning, "warning")}, {counts.Known.ToString(culture)} known";
    }

    /// <summary>"Changes since 09 Oct 02:30: DATA-01 Pass -> Warning; …" (at most six, then "and n more").</summary>
    public static string ChangesLine(HealthCheckRunSummary? earlier, IReadOnlyList<HealthChange> list)
    {
        if (earlier is null) return "";
        var label = earlier.CompletedUtc.ToLocalTime().ToString("dd MMM HH:mm", PresentationCulture.Indian);
        if (list.Count == 0) return $"No changes since {label}.";
        var shown = string.Join("; ", list.Take(6).Select(change => $"{change.CheckId} {OutcomeLabel(change.From)} → {OutcomeLabel(change.To)}"));
        return $"Changes since {label}: {shown}" + (list.Count > 6 ? $"; and {list.Count - 6} more" : "") + ".";
    }

    public static string DetailsFor(HealthRow row)
    {
        var result = row.Result;
        var text = new StringBuilder();
        // DS-21: the finding sentence first, the codes on a last "Details" line.
        text.AppendLine(row.Finding);
        if (row.Fix.Label.Length > 0) text.AppendLine("Where to fix: " + row.Fix.Label);
        if (result.MetricValue is { } metric) text.AppendLine("Measured: " + metric.ToString("0.##", PresentationCulture.Indian));
        if (result.SubResults is { Count: > 0 } subs)
        {
            text.AppendLine($"Detail ({subs.Count.ToString(PresentationCulture.Indian)}):");
            foreach (var sub in subs)
            {
                text.Append("  ").Append(sub.Key).Append(": ").Append(OutcomeLabel(sub.Outcome));
                if (!string.IsNullOrEmpty(sub.FindingCode)) text.Append(' ').Append(sub.FindingCode);
                if (sub.MetricValue is { } value) text.Append(" (").Append(value.ToString("0.##", PresentationCulture.Indian)).Append(')');
                if (sub.Fields is { Count: > 0 } fields)
                    text.Append(" - ").Append(string.Join(", ", fields.Select(field => field.Key + " " + field.Value)));
                text.AppendLine();
            }
        }
        text.AppendLine($"Details: {result.CheckId}  {row.Outcome}  finding {result.FindingCode}");
        return text.ToString().TrimEnd();
    }

    // ---- loading -------------------------------------------------------------------------------------------------

    /// <summary>Reads the latest run and its previous run through the store and shows them. A slower earlier load never overwrites a newer one.</summary>
    public async Task LoadAsync(long? preferRunId = null)
    {
        var mine = ++revision;
        ApplyAccess();
        if (!isOwner()) { StatusText = OwnerOnlyText; ShowNothing(); return; }
        var source = store();
        if (source is null) { StatusText = NotAvailableText; ShowNothing(); return; }
        StatusText = "Loading the health check…";
        try
        {
            var list = await source.LoadRunsAsync(30, null);
            if (mine != revision) return;
            var shown = (preferRunId is { } id ? list.FirstOrDefault(run => run.HealthCheckRunId == id) : null) ?? list.FirstOrDefault();
            if (shown is null)
            {
                runs = list; current = null; compared = null; results = []; earlierResults = [];
                ShowNothing();
                StatusText = NoRunText;
                return;
            }
            var lines = await source.LoadResultsAsync(shown.HealthCheckRunId);
            if (mine != revision) return;
            var older = list.Where(run => run.HealthCheckRunId != shown.HealthCheckRunId && run.CompletedUtc <= shown.CompletedUtc).ToArray();
            var keep = compared is { } previous ? older.FirstOrDefault(run => run.HealthCheckRunId == previous.HealthCheckRunId) : null;
            var against = keep ?? older.FirstOrDefault();
            IReadOnlyList<HealthCheckResult> earlier = [];
            if (against is not null)
            {
                earlier = await source.LoadResultsAsync(against.HealthCheckRunId);
                if (mine != revision) return;
            }
            runs = list; current = shown; compared = against; results = lines; earlierResults = earlier;
            FillCompareBox(older, against);
            ShowRun();
            StatusText = HiddenPassedNote();
        }
        catch (Exception exception)
        {
            if (mine != revision) return;
            StatusText = FailureText("The health check could not be loaded. ", exception, "HEALTH_CHECK_LOAD_FAILED");
        }
    }

    /// <summary>Compares the shown run with an earlier one chosen from the Compare with list.</summary>
    public async Task CompareWithAsync(long runId)
    {
        var source = store();
        if (source is null || current is null) return;
        var choice = runs.FirstOrDefault(run => run.HealthCheckRunId == runId);
        if (choice is null) return;
        try
        {
            var lines = await source.LoadResultsAsync(runId);
            compared = choice; earlierResults = lines;
            ShowRun();
        }
        catch (Exception exception)
        {
            StatusText = FailureText("The earlier run could not be loaded. ", exception, "HEALTH_CHECK_COMPARE_FAILED");
        }
    }

    private void ShowNothing()
    {
        AllRows = []; Rows = []; grid.ItemsSource = null;
        headline.Text = ""; changes.Text = ""; details.Text = "";
        verdictBand.Visibility = Visibility.Collapsed;
        FillCompareBox([], null);
    }

    private void ShowRun()
    {
        var earlierById = new Dictionary<string, HealthOutcome>(StringComparer.Ordinal);
        foreach (var line in earlierResults) earlierById.TryAdd(line.CheckId, line.Outcome);
        AllRows = Order(results)
            .Select(line => new HealthRow(line, FixTarget.Resolve(line.FixTaskId), earlierById.TryGetValue(line.CheckId, out var old) ? old : null))
            .ToArray();
        var nightly = runs.FirstOrDefault(run => run.Trigger == HealthCheckTrigger.Nightly);
        var lines = new List<string>();
        if (nightly is not null) lines.Add(RunLine("Last nightly run", nightly));
        else lines.Add("No nightly run has been recorded yet.");
        if (current is not null && (nightly is null || nightly.HealthCheckRunId != current.HealthCheckRunId))
            lines.Add(RunLine(current.Trigger == HealthCheckTrigger.Manual ? "Shown: manual run" : "Shown: run", current));
        headline.Text = string.Join(Environment.NewLine, lines);
        ShowVerdict();
        changes.Text = ChangesLine(compared, compared is null ? [] : Compare(results, earlierResults));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selected = SelectedRow?.Result.CheckId;
        Rows = ShowPassed ? AllRows : AllRows.Where(row => row.Result.Outcome != HealthOutcome.Pass).ToArray();
        grid.ItemsSource = Rows;
        grid.SelectedItem = selected is null ? null : Rows.FirstOrDefault(row => row.Result.CheckId == selected);
        ShowDetails();
        if (current is not null) StatusText = HiddenPassedNote();
    }

    private string HiddenPassedNote()
    {
        var hidden = AllRows.Count - Rows.Count;
        return hidden switch
        {
            0 => "",
            1 => "1 passed line is hidden. Tick Show passed lines to see it.",
            _ => $"{hidden.ToString(PresentationCulture.Indian)} passed lines are hidden. Tick Show passed lines to see them."
        };
    }

    private sealed record CompareChoice(HealthCheckRunSummary Run)
    {
        public override string ToString() =>
            $"{Run.CompletedUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", PresentationCulture.Indian)} ({Run.Trigger})";
    }

    private void FillCompareBox(IReadOnlyList<HealthCheckRunSummary> older, HealthCheckRunSummary? selected)
    {
        applyingCompare = true;
        try
        {
            compareBox.Items.Clear();
            foreach (var run in older) compareBox.Items.Add(new CompareChoice(run));
            compareBox.SelectedItem = selected is null ? null : compareBox.Items.OfType<CompareChoice>().FirstOrDefault(c => c.Run.HealthCheckRunId == selected.HealthCheckRunId);
            compareBox.IsEnabled = older.Count > 0 && !IsRunning;
        }
        finally { applyingCompare = false; }
    }

    private void ShowDetails()
    {
        var row = SelectedRow;
        details.Text = row is null ? "" : DetailsFor(row);
        ApplyAccess();
    }

    private void ApplyAccess()
    {
        var owner = isOwner();
        runButton.IsEnabled = owner && runner is not null && !IsRunning;
        refreshButton.IsEnabled = !IsRunning;
        folderButton.IsEnabled = owner;
        knownButton.IsEnabled = owner && !IsRunning && SelectedRow?.Result.Outcome == HealthOutcome.Warning;
        endKnownButton.IsEnabled = owner && !IsRunning && SelectedRow?.Result.Outcome == HealthOutcome.Known;
        compareBox.IsEnabled = owner && !IsRunning && compareBox.Items.Count > 0;
    }

    // ---- actions -------------------------------------------------------------------------------------------------

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    public void Cancel() => runCancellation?.Cancel();

    /// <summary>Runs a Manual health check in the background. During shop hours it asks "Run now?" first (Q13).</summary>
    public async Task RunAsync()
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return; }
        if (runner is null) { StatusText = NotAvailableText; return; }
        if (IsRunning) return;
        if (IsShopHours(now()) && !confirm(ShopHoursQuestion)) { StatusText = "The health check was not started."; return; }
        IsRunning = true;
        runCancellation = new CancellationTokenSource();
        cancelButton.Visibility = Visibility.Visible;
        progress.Text = "Starting the health check…";
        StatusText = "";
        ApplyAccess();
        try
        {
            var id = await runner(new InlineProgress(text => progress.Text = text), runCancellation.Token);
            progress.Text = "";
            IsRunning = false;
            await LoadAsync(id);
            StatusText = "The health check finished. " + StatusText;
        }
        catch (OperationCanceledException)
        {
            progress.Text = "";
            StatusText = "The health check was cancelled. Nothing was changed.";
        }
        catch (Exception exception)
        {
            progress.Text = "";
            StatusText = FailureText("The health check could not be run. ", exception, "HEALTH_CHECK_RUN_FAILED");
        }
        finally
        {
            IsRunning = false;
            runCancellation?.Dispose();
            runCancellation = null;
            cancelButton.Visibility = Visibility.Collapsed;
            ApplyAccess();
        }
    }

    /// <summary>Marks the selected Warning as Known until a date (at most 90 days). It applies from the next run.</summary>
    public async Task<bool> MarkKnownAsync()
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return false; }
        var row = SelectedRow;
        if (row is null || row.Result.Outcome != HealthOutcome.Warning) { StatusText = "Select a warning line first. Only a warning can be marked as Known; a failure cannot."; return false; }
        var source = store();
        if (source is null) { StatusText = NotAvailableText; return false; }
        var today = DateOnly.FromDateTime(now());
        if (askKnown(today) is not { } answer) return false;
        var problem = ValidateKnown(answer.Reason, answer.Until, today);
        if (problem is not null) { StatusText = problem; return false; }
        try
        {
            await source.SetKnownAsync(row.Result.CheckId, row.Result.FindingCode, answer.Until, answer.Reason.Trim());
            StatusText = $"{row.Result.CheckId} is Known until {answer.Until.ToString("dd MMM yyyy", PresentationCulture.Indian)}. It shows as Known from the next health check.";
            return true;
        }
        catch (Exception exception)
        {
            StatusText = FailureText("The Known could not be saved. ", exception, "HEALTH_CHECK_KNOWN_FAILED");
            return false;
        }
    }

    /// <summary>Ends the Known of the selected Known line; it is a Warning again from the next run.</summary>
    public async Task<bool> EndKnownAsync()
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return false; }
        var row = SelectedRow;
        if (row is null || row.Result.Outcome != HealthOutcome.Known) { StatusText = "Select a Known line first."; return false; }
        var source = store();
        if (source is null) { StatusText = NotAvailableText; return false; }
        try
        {
            // The line may be Known through its sub-findings (DATA-01 DAY_INCOMPLETE with Known rows under
            // INPUTS_MISSING, ...): end every Known of this line, not only the line's own code (1.11.0 review finding 4).
            foreach (var code in KnownCodesOf(row.Result))
                await source.ClearKnownAsync(row.Result.CheckId, code);
            StatusText = $"The Known of {row.Result.CheckId} was ended. It is a warning again from the next health check.";
            return true;
        }
        catch (Exception exception)
        {
            StatusText = FailureText("The Known could not be ended. ", exception, "HEALTH_CHECK_KNOWN_FAILED");
            return false;
        }
    }

    /// <summary>The finding codes whose Known rows make <paramref name="line"/> Known: its own, then each Known sub-finding's.</summary>
    internal static IReadOnlyList<string> KnownCodesOf(HealthCheckResult line) =>
        new[] { line.FindingCode }
            .Concat((line.SubResults ?? []).Where(sub => sub.Outcome == HealthOutcome.Known && sub.FindingCode is { Length: > 0 }).Select(sub => sub.FindingCode!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>The "Where to fix" button: opens the task the line names.</summary>
    public void OpenFix(HealthRow row)
    {
        if (row.Fix.TaskId is { } id) navigate?.Invoke(id);
    }

    public void OpenExportFolder()
    {
        if (!isOwner()) { StatusText = OwnerOnlyText; return; }
        var folder = exportFolder?.Invoke();
        if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder))
        {
            StatusText = "The export folder does not exist yet. It is created by ETP setup and the first nightly run.";
            return;
        }
        try { openFolder(folder); }
        catch (Exception exception) { StatusText = FailureText("The export folder could not be opened. ", exception, "HEALTH_EXPORT_FOLDER_OPEN_FAILED"); }
    }

    private static void OpenInExplorer(string folder)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add(folder);
        Process.Start(start);
    }

    private (string Reason, DateOnly Until)? AskKnownWithDialog(DateOnly today)
    {
        var answer = DiagnosticsDialog.Ask(this, "Accept a warning (Known)",
            "A Known is a warning you have accepted for a while. It never hides a failure and ends on its own.", "Accept warning",
            [
                new("Reason (plain words)", DialogFieldKind.Text),
                new($"Known until (at most {MaxKnownDays} days)", DialogFieldKind.Date, today.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            ],
            values => ValidateKnown(values[0], DateOnly.TryParseExact(values[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null, today));
        if (answer is null || !DateOnly.TryParseExact(answer[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var until)) return null;
        return (answer[0], until);
    }

    internal static string FailureText(string prefix, Exception exception, string eventId) =>
        DesktopDiagnostics.WithReference(prefix + DesktopFriendlyError.Describe(exception),
            DesktopDiagnostics.Record(exception, "OperationsAdministration.HealthCheck", eventId));

    // ---- grid ----------------------------------------------------------------------------------------------------

    private void BuildColumns()
    {
        DataGridTextColumn Text(string header, string path, double width) => new()
        {
            Header = header,
            Binding = new Binding(path),
            Width = width,
            ElementStyle = WrapStyle(secondary: true)
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Result", Binding = new Binding(nameof(HealthRow.Outcome)), Width = 96, ElementStyle = ResultChipStyle() });
        grid.Columns.Add(new DataGridTextColumn { Header = "What was found", Binding = new Binding(nameof(HealthRow.Finding)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 260, ElementStyle = WrapStyle() });
        grid.Columns.Add(BuildFixColumn());
        grid.Columns.Add(Text("Area", nameof(HealthRow.Area), 90));
        grid.Columns.Add(Text("Check", nameof(HealthRow.Check), 80));
        grid.Columns.Add(Text("Took", nameof(HealthRow.Took), 70));
        grid.Columns.Add(Text("Earlier run", nameof(HealthRow.EarlierText), 90));
        grid.Columns.Add(Text("Changed", nameof(HealthRow.ChangeText), 130));
    }

    /// <summary>DS-21: Result as a tone chip (FAIL red, WARN amber, KNOWN/SKIPPED grey, PASS green), always with the word.</summary>
    private static Style ResultChipStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(8, 3, 8, 3)));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        void When(string kind, string fill, string ink)
        {
            var trigger = new DataTrigger { Binding = new Binding(nameof(HealthRow.OutcomeKind)), Value = kind };
            trigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, ThemeBrushes.Get(fill)));
            trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, ThemeBrushes.Get(ink)));
            style.Triggers.Add(trigger);
        }
        When(nameof(HealthOutcome.Fail), "CriticalSoft", "Critical");
        When(nameof(HealthOutcome.Warning), "WarningSoft", "Warning");
        When(nameof(HealthOutcome.Pass), "SuccessSoft", "Success");
        When(nameof(HealthOutcome.Known), "SurfaceSecondary", "SecondaryText");
        When(nameof(HealthOutcome.Skipped), "SurfaceSecondary", "SecondaryText");
        return style;
    }

    private static Style WrapStyle(bool secondary = false)
    {
        var style = new Style(typeof(TextBlock));
        if (secondary) style.Setters.Add(new Setter(TextBlock.ForegroundProperty, ThemeBrushes.Get("SecondaryText")));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 4, 6, 4)));
        return style;
    }

    private DataGridTemplateColumn BuildFixColumn()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(HealthRow.FixText)));
        button.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(HealthRow.FixButtonVisibility)));
        button.SetValue(FrameworkElement.MinHeightProperty, 32.0);
        button.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
        button.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(HealthRow.FixButtonName)));
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is FrameworkElement { DataContext: HealthRow row }) OpenFix(row);
        }));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(HealthRow.FixText)));
        text.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(HealthRow.FixTextVisibility)));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 4, 6, 4));
        panel.AppendChild(button);
        panel.AppendChild(text);
        return new DataGridTemplateColumn { Header = "Where to fix", Width = 220, CellTemplate = new DataTemplate { VisualTree = panel } };
    }

    private static Style BuildRowStyle()
    {
        var style = new Style(typeof(DataGridRow));
        void When(string kind, Action<DataTrigger> fill)
        {
            var trigger = new DataTrigger { Binding = new Binding(nameof(HealthRow.OutcomeKind)), Value = kind };
            fill(trigger);
            style.Triggers.Add(trigger);
        }
        When(nameof(HealthOutcome.Fail), t => { t.Setters.Add(new Setter(Control.BackgroundProperty, ThemeBrushes.Get("CriticalSoft"))); t.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); });
        When(nameof(HealthOutcome.Warning), t => t.Setters.Add(new Setter(Control.ForegroundProperty, ThemeBrushes.Get("Warning"))));
        When(nameof(HealthOutcome.Known), t => t.Setters.Add(new Setter(Control.ForegroundProperty, ThemeBrushes.Get("SecondaryText"))));
        When(nameof(HealthOutcome.Skipped), t => t.Setters.Add(new Setter(Control.ForegroundProperty, ThemeBrushes.Get("SecondaryText"))));
        return style;
    }
}

public enum DialogFieldKind { Text, Date, Choice }

/// <summary>One input of <see cref="DiagnosticsDialog"/>. A Date field starts from and returns yyyy-MM-dd.</summary>
public sealed record DialogField(string Label, DialogFieldKind Kind, string Initial = "", IReadOnlyList<string>? Choices = null);

/// <summary>
/// A small modal form for the diagnostics screens (Known reason and date; Error register reason, ticket, version, merge
/// target). The OK button stays disabled in effect while <c>validate</c> returns a problem: the problem shows in the dialog.
/// Returns the values in field order, or null when cancelled.
/// </summary>
public static class DiagnosticsDialog
{
    public static string[]? Ask(DependencyObject source, string title, string intro, string action, IReadOnlyList<DialogField> fields, Func<string[], string?> validate)
    {
        var owner = Window.GetWindow(source);
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        if (owner?.IsVisible == true) dialog.Owner = owner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        if (intro.Length > 0) panel.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var inputs = new List<Control>();
        foreach (var field in fields)
        {
            panel.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(0, 6, 0, 2) });
            Control input = field.Kind switch
            {
                DialogFieldKind.Date => new DatePicker
                {
                    MinHeight = 36,
                    SelectedDate = DateTime.TryParseExact(field.Initial, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null
                },
                DialogFieldKind.Choice => BuildChoice(field),
                _ => new TextBox { MinHeight = 36, Padding = new Thickness(6), Text = field.Initial }
            };
            AutomationProperties.SetName(input, field.Label);
            inputs.Add(input);
            panel.Children.Add(input);
        }
        var problem = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        problem.SetResourceReference(TextBlock.ForegroundProperty, "Critical");
        AutomationProperties.SetLiveSetting(problem, AutomationLiveSetting.Assertive);
        panel.Children.Add(problem);
        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 44, Margin = new Thickness(0, 0, 8, 0) };
        var confirm = new Button { Content = action, MinHeight = 44, IsDefault = true };
        AutomationProperties.SetName(confirm, "Confirm " + action.ToLowerInvariant());
        string[] Values() => inputs.Select(input => input switch
        {
            DatePicker picker => picker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            ComboBox combo => combo.Text?.Trim() ?? "",
            TextBox box => box.Text.Trim(),
            _ => ""
        }).ToArray();
        confirm.Click += (_, _) =>
        {
            var message = validate(Values());
            if (message is not null) { problem.Text = message; return; }
            dialog.DialogResult = true;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        panel.Children.Add(actions);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? Values() : null;
    }

    private static ComboBox BuildChoice(DialogField field)
    {
        var combo = new ComboBox { MinHeight = 36 };
        foreach (var choice in field.Choices ?? []) combo.Items.Add(choice);
        combo.SelectedItem = field.Initial.Length > 0 ? field.Initial : combo.Items.Count > 0 ? combo.Items[0] : null;
        return combo;
    }
}
