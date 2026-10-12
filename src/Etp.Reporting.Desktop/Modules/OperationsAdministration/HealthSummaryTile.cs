extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using EtpApplication::Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Desktop.Themes;

namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

/// <summary>
/// The one-line "System check" tile every role sees (decision 33, design 9.1): "System check: last night OK", "2 warnings"
/// or "1 problem - tell the Owner", from the latest nightly run's counts. It shows no check names, no explanations and no
/// details, so a Store Manager or Viewer learns only that something needs the Owner. It reads
/// <see cref="IHealthCheckStore.LoadLatestNightlySummaryAsync"/> (diag.load_health_check_summary, granted to all roles).
/// </summary>
public sealed class HealthSummaryTile : Border
{
    /// <summary>A nightly run older than this many days (business date to today) reads as stale. A normal run checks yesterday.</summary>
    public const int StaleAfterDays = 3;

    private readonly Func<IHealthCheckStore?> store;
    private readonly Func<DateOnly> today;
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border stripe = new() { Width = 5, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 10, 0) };
    private readonly Func<bool>? isOwner;
    private readonly Action? open;
    private int revision;
    private bool failureRecorded;

    /// <summary>
    /// <paramref name="isOwner"/> and <paramref name="open"/> (1.11.2, UX audit top 3): for the Owner the tile also names
    /// the top finding in words and opens the Health check when clicked or activated with Enter or Space. Without them (or
    /// for any other role) it is the plain one-line tile.
    /// </summary>
    public HealthSummaryTile(Func<IHealthCheckStore?> store, Func<DateOnly>? today = null, Func<bool>? isOwner = null, Action? open = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.isOwner = isOwner;
        this.open = open;
        this.today = today ?? (() => DateOnly.FromDateTime(DateTime.Today));
        Margin = new Thickness(4);
        Padding = new Thickness(12, 8, 12, 8);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        text.SetResourceReference(TextBlock.StyleProperty, "CaptionText");
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(BorderBrushProperty, "Divider");
        var row = new DockPanel();
        DockPanel.SetDock(stripe, Dock.Left);
        row.Children.Add(stripe);
        row.Children.Add(text);
        Child = row;
        AutomationProperties.SetName(this, "System check");
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        if (open is not null)
        {
            MouseLeftButtonUp += (_, _) => TryOpen();
            KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { TryOpen(); e.Handled = true; } };
        }
        Show(HealthOutcome.Skipped, "System check: not checked yet");
    }

    /// <summary>True when the tile is a link: the signed-in role is the Owner and an opener was given.</summary>
    public bool IsLink => open is not null && isOwner?.Invoke() == true;

    /// <summary>Opens the Health check for the Owner (also what a click does). Returns false when the tile is not a link.</summary>
    public bool TryOpen()
    {
        if (!IsLink) return false;
        open!();
        return true;
    }

    /// <summary>
    /// The top finding in words for the Owner: the first Fail, else the first Warning, of the latest run. The explanation
    /// is cut to one line's worth. Null when nothing needs attention.
    /// </summary>
    public static string? TopFinding(IReadOnlyList<HealthCheckResult> results)
    {
        var top = results.FirstOrDefault(x => x.Outcome == HealthOutcome.Fail) ?? results.FirstOrDefault(x => x.Outcome == HealthOutcome.Warning);
        if (top is null || string.IsNullOrWhiteSpace(top.Explanation)) return null;
        var text = top.Explanation.Trim().ReplaceLineEndings(" ");
        return text.Length <= 140 ? text : text[..137].TrimEnd() + "...";
    }

    /// <summary>The Owner's line: the counts, then the top finding, then how to open it.</summary>
    public static string OwnerText(string counts, string? finding) =>
        finding is null ? counts + " - click to open the health check"
            : counts.Replace(" - tell the Owner", "", StringComparison.Ordinal) + ": " + finding + " (click to open the health check)";

    /// <summary>The tile's one line.</summary>
    public string Text => text.Text;

    /// <summary>Pass (green), Warning (amber), Fail (red) or Skipped (grey, nothing to report yet).</summary>
    public HealthOutcome Level { get; private set; } = HealthOutcome.Skipped;

    public static string TextFor(HealthCheckNightlySummary? summary, DateOnly today)
    {
        if (summary is null) return "System check: no nightly check yet";
        if (today.DayNumber - summary.BusinessDate.DayNumber > StaleAfterDays)
            return $"System check: last run {summary.CompletedUtc.ToLocalTime().ToString("dd MMM", PresentationCulture.Indian)} - tell the Owner";
        var problems = Math.Max(summary.Counts.Fail, summary.Overall == HealthOutcome.Fail ? 1 : 0);
        if (problems > 0) return $"System check: {problems.ToString(PresentationCulture.Indian)} {(problems == 1 ? "problem" : "problems")} - tell the Owner";
        var warnings = Math.Max(summary.Counts.Warning, summary.Overall == HealthOutcome.Warning ? 1 : 0);
        if (warnings > 0) return $"System check: {warnings.ToString(PresentationCulture.Indian)} {(warnings == 1 ? "warning" : "warnings")}";
        return "System check: last night OK";
    }

    public static HealthOutcome LevelFor(HealthCheckNightlySummary? summary, DateOnly today)
    {
        if (summary is null) return HealthOutcome.Skipped;
        if (today.DayNumber - summary.BusinessDate.DayNumber > StaleAfterDays) return HealthOutcome.Warning;
        if (summary.Counts.Fail > 0 || summary.Overall == HealthOutcome.Fail) return HealthOutcome.Fail;
        if (summary.Counts.Warning > 0 || summary.Overall == HealthOutcome.Warning) return HealthOutcome.Warning;
        return HealthOutcome.Pass;
    }

    /// <summary>Reads the latest nightly summary. A failure leaves a neutral line (and is recorded once), never an error dialog on the dashboard.</summary>
    public async Task RefreshAsync()
    {
        var mine = ++revision;
        var source = store();
        if (source is null) { Show(HealthOutcome.Skipped, "System check: not available"); return; }
        try
        {
            var summary = await source.LoadLatestNightlySummaryAsync();
            if (mine != revision) return;
            var date = today();
            var level = LevelFor(summary, date);
            var line = TextFor(summary, date);
            if (IsLink && level is HealthOutcome.Fail or HealthOutcome.Warning && summary is not null)
            {
                string? finding = null;
                try
                {
                    var runs = await source.LoadRunsAsync(1, HealthCheckTrigger.Nightly);
                    if (runs.Count > 0) finding = TopFinding(await source.LoadResultsAsync(runs[0].HealthCheckRunId));
                }
                catch (Exception) { /* the plain counts line still stands; the Health check screen reports its own failures */ }
                if (mine != revision) return;
                line = OwnerText(line, finding);
            }
            Show(level, line);
        }
        catch (Exception exception)
        {
            if (mine != revision) return;
            if (!failureRecorded)
            {
                failureRecorded = true;
                DesktopDiagnostics.Record(exception, "OperationsAdministration.HealthSummaryTile", "HEALTH_SUMMARY_TILE_FAILED");
            }
            Show(HealthOutcome.Skipped, "System check: could not be read");
        }
    }

    private void Show(HealthOutcome level, string line)
    {
        Level = level;
        text.Text = line;
        // DS-04: a pass is one quiet 12 px line (no card, no stripe); a warning or a failure takes a soft tone fill with the
        // status word first, so "something broke last night" no longer looks like "last night OK".
        var loud = level is HealthOutcome.Fail or HealthOutcome.Warning;
        var tone = level == HealthOutcome.Fail ? "Critical" : "Warning";
        stripe.Visibility = loud ? Visibility.Visible : Visibility.Collapsed;
        if (loud) stripe.SetResourceReference(Border.BackgroundProperty, tone);
        SetResourceReference(BackgroundProperty, level == HealthOutcome.Fail ? "CriticalSoft" : level == HealthOutcome.Warning ? "WarningSoft" : "AppBackground");
        SetResourceReference(BorderBrushProperty, loud ? tone : "AppBackground");
        text.SetResourceReference(TextBlock.StyleProperty, loud ? "BodyText" : "CaptionText");
        text.FontWeight = loud ? FontWeights.SemiBold : FontWeights.Normal;
        text.TextDecorations = IsLink && loud ? TextDecorations.Underline : null;
        AutomationProperties.SetHelpText(this, line);
        var link = IsLink;
        Focusable = link;
        Cursor = link ? Cursors.Hand : null;
        ToolTip = link ? "Open the health check" : null;
        AutomationProperties.SetName(this, link ? "System check. Opens the health check" : "System check");
    }
}
