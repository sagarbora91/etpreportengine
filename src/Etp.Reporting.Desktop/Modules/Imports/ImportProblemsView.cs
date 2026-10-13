extern alias EtpApplication;

using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Desktop.Themes;
using FolderImportFileResult = EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult;
using ImportFixAdvisor = EtpApplication::Etp.Reporting.Application.Imports.ImportFixAdvisor;

namespace Etp.Reporting.Desktop.Modules.Imports;

public sealed record ImportProblem(string File, string Status, string Store, string Period, string Detail, string NextStep = "")
{
    /// <summary>The attempt the row was built from (null for a Received-files problem); the Fix buttons need its code and hash.</summary>
    public FolderImportFileResult? Source { get; init; }
}

/// <summary>
/// What counts as a problem, defined once. The Problems tab reads persisted outcomes
/// from the database so the list survives closing the application; the same rule has
/// to classify a result whether it arrived from a query or from the import just run.
/// </summary>
public static class ImportProblems
{
    public static bool IsProblem(FolderImportFileResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Failed
            || result.ConflictRows > 0
            || result.Status.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)
            || result.Status == "Unknown layout";
    }

    public static ImportProblem Describe(FolderImportFileResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(
            result.FileName,
            result.ConflictRows > 0 ? "Conflict" : result.Status,
            result.StoreCode ?? "",
            result.Period,
            $"{result.NewRows} new rows; {result.AlreadyPresentRows} present; {result.ConflictRows} conflicts",
            NextStep(result)) { Source = result };
    }

    /// <summary>
    /// The one thing to do about a problem row. 1.12 U2: the Fix buttons beside it do the work (Reopen day and re-apply, Retry,
    /// Tell the shop, Open folder); this line says which one, or what to do when no button can.
    /// </summary>
    public static string NextStep(FolderImportFileResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var code = result.Failure?.Code ?? "";
        if (ImportFixAdvisor.IsDayFinalised(code, result.Failure?.SqlNumber))
            return "The day is finalised. The Owner presses Reopen day and re-apply.";
        if (result.ConflictRows > 0)
            return "Compare with the saved figures in History, then decide which file is right.";
        if (result.Status.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
            return "Nothing to do - this file was imported before.";
        if (result.Status == "Unknown layout")
            return "Check this is the right report and re-export it; the file stays in the folder.";
        if (ImportFixAdvisor.IsRetryable(code, result.Failure?.SqlNumber))
            return "Press Retry. If it fails again, open the folder and check the file.";
        return "Correct the file and import it again. Tell the shop when the shop sent it.";
    }

    public static IReadOnlyList<ImportProblem> From(IEnumerable<FolderImportFileResult>? results) =>
        results is null ? [] : results.Where(IsProblem).Select(Describe).ToArray();

    /// <summary>
    /// A file that later imported cleanly is no longer a problem. import_attempts is
    /// append-only, so the failed attempt outlives the retry that fixed it; without
    /// this a successful "Retry failed" leaves the problem on screen and looks as
    /// though it did nothing.
    /// A clean import clears a failure only for the same store, report and file name
    /// (Titan store report audit item R-09, 3 Oct 2026): exports carry the same file name in every store, so
    /// in All stores one store's clean R022_Revenue_Report.xlsx used to hide another
    /// store's failed one. A report the failed attempt never learned does not stop
    /// the match (its store still decides it). A store it never learned (it failed
    /// before the file was recognised) is different: the history keeps such a row in
    /// every store scope but drops other stores' clean rows, so letting any store's
    /// clean file clear it would again show more problems with a store selected than
    /// in All stores. Such a failure is cleared only by a clean import of the same
    /// bytes (same source hash) or by a clean row that also has no store. Accepted gap:
    /// a failure recorded with neither store nor hash (the file could not be read)
    /// stays listed after a successful retry until it leaves the date range.
    /// </summary>
    public static IReadOnlyList<ImportProblem> FromHistory(
        IEnumerable<(DateTime RecordedUtc, FolderImportFileResult Result)>? entries)
    {
        if (entries is null) return [];
        var ordered = entries.ToArray();
        var clean = ordered.Where(entry => !IsProblem(entry.Result)).ToArray();
        return ordered
            .Where(entry => IsProblem(entry.Result))
            .Where(entry => !clean.Any(later => later.RecordedUtc >= entry.RecordedUtc && SameSource(entry.Result, later.Result)))
            .Select(entry => Describe(entry.Result))
            .ToArray();
    }

    private static bool SameSource(FolderImportFileResult failed, FolderImportFileResult clean)
    {
        if (!string.Equals(failed.FileName, clean.FileName, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(failed.StoreCode) && !string.IsNullOrWhiteSpace(clean.StoreCode))
            return SameBytes(failed.SourceSha256, clean.SourceSha256);
        return SameOrUnknown(failed.StoreCode, clean.StoreCode) && SameOrUnknown(failed.ReportCode, clean.ReportCode);
    }

    private static bool SameBytes(string? failed, string? clean) =>
        !string.IsNullOrWhiteSpace(failed) && !string.IsNullOrWhiteSpace(clean)
        && string.Equals(failed.Trim(), clean.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameOrUnknown(string? first, string? second) =>
        string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)
        || string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>ST2: the word a problem status is shown with, and its tone (the same word has the same tone on every screen).</summary>
    public static (string Word, StatusTone Tone) VerdictOf(string status) => status switch
    {
        "Failed" => ("Refused", StatusTone.Critical),
        "Conflict" => ("Conflict", StatusTone.Critical),
        "Unknown layout" => ("Unknown layout", StatusTone.Warning),
        "Quarantined" => ("Quarantined", StatusTone.Warning),
        _ when status.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) || status == "Already present" => ("Already imported", StatusTone.Neutral),
        _ => (status, StatusTone.Neutral)
    };
}

/// <summary>One grid row of Problems: the problem, its verdict chip and its Fix buttons (filled in after the location lookup).</summary>
public sealed class ImportProblemRow(ImportProblem problem) : INotifyPropertyChanged
{
    private IReadOnlyList<ImportFixButtonModel> fixes = [];

    public ImportProblem Problem { get; } = problem;
    public string Verdict { get; } = ImportProblems.VerdictOf(problem.Status).Word;
    public string VerdictTone { get; } = ImportProblems.VerdictOf(problem.Status).Tone.ToString();
    public string File => Problem.File;
    public string Store => TablePresentation.StoreLabel(Problem.Store);
    public string Period => Problem.Period;
    public string NextStep => Problem.NextStep;
    public string Detail => Problem.Detail;

    public IReadOnlyList<ImportFixButtonModel> Fixes
    {
        get => fixes;
        set { fixes = value ?? []; PropertyChanged?.Invoke(this, new(nameof(Fixes))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Imports > Problems. Template A (List) of the design system: toolbar (Refresh, Retry failed, status filter, the Owner's evidence
/// action), one caption line, the grid as the body. 1.12 U2 (wave plan C-c, DS-12): Verdict first as a tone chip, then the file,
/// what to do and one QuietButton per Fix action.
/// </summary>
public sealed class ImportProblemsView : UserControl
{
    private readonly ComboBox status = new() { ItemsSource = new[] { "All problems", "Quarantined", "Conflict", "Duplicate", "Failed", "Unknown layout" }, SelectedIndex = 0 };
    private readonly DataGrid rows = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ImportWorkspaceView? imports;
    private IReadOnlyList<ImportProblem> problems = [];
    private IReadOnlyList<ImportProblemRow> problemRows = [];
    private int revision;

    /// <summary>The problems currently held, so a restart test can assert what survived.</summary>
    public IReadOnlyList<ImportProblem> Rows => problems;
    /// <summary>The grid rows with their Fix buttons (1.12 U2).</summary>
    public IReadOnlyList<ImportProblemRow> GridRows => problemRows;
    public string MessageText => message.Text;
    public bool HasLoaded { get; private set; }

    private TextBlock? retryDeniedText;
    /// <summary>Whether the Viewer explanation beside Retry failed is on screen.</summary>
    public bool RetryDeniedNoticeVisible => retryDeniedText?.Visibility == Visibility.Visible;

    /// <summary>The Owner's "Keep source files for earlier imports…" on this tab (spec 12), when it is hosted.</summary>
    public ImportEvidenceView? Evidence { get; }
    private Button? keepEarlierSources;
    /// <summary>Whether the Owner's "Keep source files for earlier imports…" button is on screen.</summary>
    public bool KeepEarlierSourcesVisible => keepEarlierSources?.Visibility == Visibility.Visible;

    /// <summary>The Fix buttons' services; <see cref="ImportFixServices.Current"/> unless a test sets its own.</summary>
    internal ImportFixServices? FixServices { get; set; } = ImportFixServices.Current;
    internal ImportFixHandlers FixHandlers { get; }

    /// <summary>Reloads the problems (the same as Refresh).</summary>
    public Task RefreshAsync() => RefreshCoreAsync();

    private readonly Func<Task<IReadOnlyList<ImportProblem>>> load;
    private readonly Button refresh;
    private readonly Button retry;

    public ImportProblemsView(ImportWorkspaceView imports, Func<Task<IReadOnlyList<ImportProblem>>> load,
        ImportEvidenceView? evidence = null, bool revealEvidence = false)
    {
        this.imports = imports ?? throw new ArgumentNullException(nameof(imports));
        this.load = load ?? throw new ArgumentNullException(nameof(load));
        FixHandlers = new ImportFixHandlers(this, text => message.Text = text, RefreshCoreAsync);
        var root = new DockPanel();
        var actions = new WrapPanel();
        refresh = new Button { Content = "Refresh", Margin = DesignTokens.Gap(null, null, "Space8", null) };
        retry = new Button { Content = "Retry failed", Margin = DesignTokens.Gap(null, null, "Space8", null), IsEnabled = imports.CanRetry };
        status.SetResourceReference(WidthProperty, "InputMedium");
        status.Margin = DesignTokens.Gap(null, null, "Space8", null);
        // R3. A Viewer sees the button disabled; without this they see a dead control
        // and no reason for it.
        var retryDenied = new TextBlock
        {
            Text = "Owner or store manager can retry",
            Margin = DesignTokens.Gap(null, null, "Space8", null),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Visibility = imports.CanRetryByRole ? Visibility.Collapsed : Visibility.Visible
        };
        retryDenied.SetResourceReference(StyleProperty, "CaptionText");
        AutomationProperties.SetName(retryDenied, "Owner or store manager can retry");
        actions.Children.Add(refresh); actions.Children.Add(retry); actions.Children.Add(retryDenied); actions.Children.Add(status);
        Evidence = evidence;
        if (evidence is not null)
        {
            // Spec 12: the Owner keeps the source files of earlier imports from here as well as from Settings.
            var keep = new Button { Content = ImportEvidenceView.KeepEarlierSourcesText };
            AutomationProperties.SetName(keep, ImportEvidenceView.KeepEarlierSourcesText);
            AutomationProperties.SetHelpText(keep, "Owner only. Keeps the source files of earlier imports from the folders you choose. Nothing is imported.");
            keep.Click += async (_,_) => { evidence.Reveal(); await evidence.KeepEarlierSourcesAsync(); };
            actions.Children.Add(keep);
            keepEarlierSources = keep;
            UpdateEvidenceAccess();
        }
        message.SetResourceReference(StyleProperty, "CaptionText");
        message.Margin = DesignTokens.Gap(null, "Space8", null, "Space8");
        DockPanel.SetDock(actions,Dock.Top);root.Children.Add(actions);
        DockPanel.SetDock(message,Dock.Top);root.Children.Add(message);
        if (evidence is not null) { DockPanel.SetDock(evidence,Dock.Top);root.Children.Add(evidence); }
        BuildColumns();
        TablePresentation.Configure(rows);
        root.Children.Add(rows);Content=root;
        AutomationProperties.SetName(status,"Problem status filter");
        AutomationProperties.SetName(rows,"Import problems");
        AutomationProperties.SetName(refresh,"Refresh import problems");
        AutomationProperties.SetName(retry,"Retry failed imports");
        AutomationProperties.SetHelpText(retry,"Retry only failed files from the latest import. Keyboard shortcut: Ctrl+R.");
        void UpdateRetry(object? sender, EventArgs args)
        {
            retry.IsEnabled = imports.CanRetry;
            retryDenied.Visibility = imports.CanRetryByRole ? Visibility.Collapsed : Visibility.Visible;
        }
        retryDeniedText = retryDenied;
        status.SelectionChanged += (_,_) => ApplyFilter();
        refresh.Click += async (_,_) => await RefreshCoreAsync();
        retry.Click += async (_,_) =>
        {
            if (!imports.CanRetry) return;
            await imports.RetryFailedBatchAsync();
            await RefreshCoreAsync();
        };
        Loaded += async (_,_) =>
        {
            imports.RetryAvailabilityChanged += UpdateRetry;
            UpdateEvidenceAccess();
            if (revealEvidence && evidence is { IsOwner: true }) evidence.Reveal();
            await RefreshCoreAsync();
        };
        Unloaded += (_,_) => imports.RetryAvailabilityChanged -= UpdateRetry;
    }

    private async Task RefreshCoreAsync()
    {
        var current = ++revision;
        refresh.IsEnabled=false;
        try
        {
            var loaded = await load();
            if (current != revision) return;
            problems = loaded;
            problemRows = loaded.Select(problem => new ImportProblemRow(problem)).ToArray();
            ApplyFilter();
            message.Text = $"{problems.Count} problems. Select a status to filter.";
            await LoadFixesAsync(current);
        }
        catch(Exception ex) { if (current == revision) message.Text=DesktopFriendlyError.Describe(ex); }
        finally { refresh.IsEnabled=true; retry.IsEnabled=imports?.CanRetry == true; HasLoaded=true; }
    }

    /// <summary>The Fix buttons of each row with an attempt behind it (one location lookup per row, the first 60 rows).</summary>
    private async Task LoadFixesAsync(int current)
    {
        if (FixServices is not { } services || imports is null) return;
        var access = imports.Access;
        foreach (var row in problemRows.Where(row => row.Problem.Source is not null).Take(ImportFixServices.MaximumLookups))
        {
            if (current != revision) return;
            try { row.Fixes = await ImportFixRows.ForAsync(row.Problem.Source!, access, services, FixHandlers); }
            catch (Exception exception)
            {
                // A lookup that fails leaves the row with its text only; the reason goes to diagnostics once.
                DesktopDiagnostics.Record(exception, "Imports.Problems", "IMPORT_FIX_LOOKUP_FAILED", DesktopDiagnosticSeverity.Warning);
                return;
            }
        }
    }

    private void BuildColumns()
    {
        rows.Columns.Add(new DataGridTextColumn { Header = "Verdict", Binding = new Binding(nameof(ImportProblemRow.Verdict)), Width = 140, ElementStyle = VerdictChipStyle() });
        rows.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new Binding(nameof(ImportProblemRow.File)), Width = 230, ElementStyle = WrapStyle() });
        rows.Columns.Add(new DataGridTextColumn { Header = "What to do", Binding = new Binding(nameof(ImportProblemRow.NextStep)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 260, ElementStyle = WrapStyle() });
        rows.Columns.Add(FixColumn("Fix"));
        rows.Columns.Add(new DataGridTextColumn { Header = "Store", Binding = new Binding(nameof(ImportProblemRow.Store)), Width = 120 });
        rows.Columns.Add(new DataGridTextColumn { Header = "Period", Binding = new Binding(nameof(ImportProblemRow.Period)), Width = 200 });
        rows.Columns.Add(new DataGridTextColumn { Header = "Rows", Binding = new Binding(nameof(ImportProblemRow.Detail)), Width = 220, ElementStyle = WrapStyle() });
    }

    /// <summary>DS-12: the verdict as a tone chip with its word (Refused red, Unknown layout amber, Already imported grey).</summary>
    internal static Style VerdictChipStyle(string toneProperty = nameof(ImportProblemRow.VerdictTone))
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(8, 3, 8, 3)));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(TextBlock.BackgroundProperty, ThemeBrushes.Get("SurfaceSecondary")));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, ThemeBrushes.Get("SecondaryText")));
        foreach (var tone in new[] { StatusTone.Critical, StatusTone.Warning, StatusTone.Success, StatusTone.Information })
        {
            var (fill, _, text) = StatusTones.BrushesFor(tone);
            var trigger = new DataTrigger { Binding = new Binding(toneProperty), Value = tone.ToString() };
            trigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, ThemeBrushes.Get(fill)));
            trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, ThemeBrushes.Get(text)));
            style.Triggers.Add(trigger);
        }
        return style;
    }

    private static Style WrapStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        return style;
    }

    /// <summary>One QuietButton per Fix action of the row (the row's <c>Fixes</c>), in the advisor's order.</summary>
    internal static DataGridTemplateColumn FixColumn(string header)
    {
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetResourceReference(FrameworkElement.StyleProperty, "QuietButton");
        button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(ImportFixButtonModel.Label)));
        button.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(ImportFixButtonModel.Explanation)));
        button.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(ImportFixButtonModel.AutomationName)));
        button.SetBinding(AutomationProperties.HelpTextProperty, new Binding(nameof(ImportFixButtonModel.Explanation)));
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler(async (sender, _) =>
        {
            if (sender is not Button { DataContext: ImportFixButtonModel fix } pressed) return;
            pressed.IsEnabled = false;
            try { await fix.Run(); }
            finally { pressed.IsEnabled = true; }
        }));
        var panel = new FrameworkElementFactory(typeof(WrapPanel));
        var list = new FrameworkElementFactory(typeof(ItemsControl));
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(ImportProblemRow.Fixes)));
        list.SetValue(ItemsControl.ItemTemplateProperty, new DataTemplate { VisualTree = button });
        list.SetValue(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(panel));
        list.SetValue(UIElement.FocusableProperty, false);
        return new DataGridTemplateColumn { Header = header, Width = 300, CellTemplate = new DataTemplate { VisualTree = list } };
    }

    // Only the Owner sees the evidence action and section here; Settings still shows the size to every role.
    private void UpdateEvidenceAccess()
    {
        if (Evidence is null || keepEarlierSources is null) return;
        var owner = Evidence.IsOwner;
        keepEarlierSources.Visibility = Evidence.Visibility = owner ? Visibility.Visible : Visibility.Collapsed;
        keepEarlierSources.IsEnabled = owner;
        Evidence.RefreshAccessState();
    }
    private void ApplyFilter() => rows.ItemsSource = problemRows.Where(p => status.SelectedIndex == 0 || p.Problem.Status.Contains(status.SelectedItem?.ToString() ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
}
