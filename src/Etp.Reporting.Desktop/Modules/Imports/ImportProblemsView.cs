extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Settings;
using FolderImportFileResult = EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult;

namespace Etp.Reporting.Desktop.Modules.Imports;

public sealed record ImportProblem(string File, string Status, string Store, string Period, string Detail);

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
            $"{result.NewRows} new rows; {result.AlreadyPresentRows} present; {result.ConflictRows} conflicts");
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
}

public sealed class ImportProblemsView : UserControl
{
    private readonly ComboBox status = new() { ItemsSource = new[] { "All problems", "Quarantined", "Conflict", "Duplicate", "Failed", "Unknown layout" }, SelectedIndex = 0, MinWidth = 180 };
    private readonly DataGrid rows = new() { AutoGenerateColumns = true, IsReadOnly = true };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private IReadOnlyList<ImportProblem> problems = [];

    /// <summary>The problems currently held, so a restart test can assert what survived.</summary>
    public IReadOnlyList<ImportProblem> Rows => problems;
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

    public ImportProblemsView(ImportWorkspaceView imports, Func<Task<IReadOnlyList<ImportProblem>>> load,
        ImportEvidenceView? evidence = null, bool revealEvidence = false)
    {
        var root = new DockPanel { Margin = new Thickness(8) };
        var actions = new WrapPanel();
        var refresh = new Button { Content = "Refresh problems", Margin = new Thickness(8,0,0,0) };
        var retry = new Button { Content = "Retry failed", MinHeight = 44, MinWidth = 112, Margin = new Thickness(8,0,0,0), IsEnabled = imports.CanRetry };
        // R3. A Viewer sees the button disabled; without this they see a dead control
        // and no reason for it.
        var retryDenied = new TextBlock
        {
            Text = "Owner or store manager can retry",
            Margin = new Thickness(8, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = imports.CanRetryByRole ? Visibility.Collapsed : Visibility.Visible
        };
        AutomationProperties.SetName(retryDenied, "Owner or store manager can retry");
        actions.Children.Add(status); actions.Children.Add(retry); actions.Children.Add(retryDenied); actions.Children.Add(refresh);
        Evidence = evidence;
        if (evidence is not null)
        {
            // Spec 12: the Owner keeps the source files of earlier imports from here as well as from Settings.
            var keep = new Button { Content = ImportEvidenceView.KeepEarlierSourcesText, MinHeight = 44, Margin = new Thickness(8,0,0,0) };
            AutomationProperties.SetName(keep, ImportEvidenceView.KeepEarlierSourcesText);
            AutomationProperties.SetHelpText(keep, "Owner only. Keeps the source files of earlier imports from the folders you choose. Nothing is imported.");
            keep.Click += async (_,_) => { evidence.Reveal(); await evidence.KeepEarlierSourcesAsync(); };
            actions.Children.Add(keep);
            keepEarlierSources = keep;
            UpdateEvidenceAccess();
        }
        DockPanel.SetDock(actions,Dock.Top);root.Children.Add(actions);
        DockPanel.SetDock(message,Dock.Top);root.Children.Add(message);
        if (evidence is not null) { DockPanel.SetDock(evidence,Dock.Top);root.Children.Add(evidence); }
        root.Children.Add(rows);Content=root;
        AutomationProperties.SetName(status,"Problem status filter");
        AutomationProperties.SetName(rows,"Import problems");
        AutomationProperties.SetName(retry,"Retry failed imports");
        AutomationProperties.SetHelpText(retry,"Retry only failed files from the latest import. Keyboard shortcut: Ctrl+R.");
        void UpdateRetry(object? sender, EventArgs args)
        {
            retry.IsEnabled = imports.CanRetry;
            retryDenied.Visibility = imports.CanRetryByRole ? Visibility.Collapsed : Visibility.Visible;
        }
        retryDeniedText = retryDenied;
        status.SelectionChanged += (_,_) => ApplyFilter();
        async Task Refresh()
        {
            refresh.IsEnabled=false;
            try { problems=await load(); ApplyFilter(); message.Text=$"{problems.Count} problems. Select a status to filter."; }
            catch(Exception ex) { message.Text=DesktopFriendlyError.Describe(ex); }
            finally { refresh.IsEnabled=true; retry.IsEnabled=imports.CanRetry; HasLoaded=true; }
        }
        refresh.Click += async (_,_) => await Refresh();
        retry.Click += async (_,_) =>
        {
            if (!imports.CanRetry) return;
            await imports.RetryFailedBatchAsync();
            await Refresh();
        };
        Loaded += async (_,_) =>
        {
            imports.RetryAvailabilityChanged += UpdateRetry;
            UpdateEvidenceAccess();
            if (revealEvidence && evidence is { IsOwner: true }) evidence.Reveal();
            await Refresh();
        };
        Unloaded += (_,_) => imports.RetryAvailabilityChanged -= UpdateRetry;
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
    private void ApplyFilter() => rows.ItemsSource = problems.Where(p => status.SelectedIndex == 0 || p.Status.Contains(status.SelectedItem?.ToString() ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
}
