extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border stripe = new() { Width = 5, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 10, 0) };
    private int revision;
    private bool failureRecorded;

    public HealthSummaryTile(Func<IHealthCheckStore?> store, Func<DateOnly>? today = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.today = today ?? (() => DateOnly.FromDateTime(DateTime.Today));
        Margin = new Thickness(4);
        Padding = new Thickness(12, 8, 12, 8);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Background = ThemeBrushes.Get("Surface");
        BorderBrush = ThemeBrushes.Get("SecondaryText");
        var row = new DockPanel();
        DockPanel.SetDock(stripe, Dock.Left);
        row.Children.Add(stripe);
        row.Children.Add(text);
        Child = row;
        AutomationProperties.SetName(this, "System check");
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        Show(HealthOutcome.Skipped, "System check: not checked yet");
    }

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
            Show(LevelFor(summary, date), TextFor(summary, date));
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
        stripe.Background = ThemeBrushes.Get(level switch
        {
            HealthOutcome.Fail => "Critical",
            HealthOutcome.Warning => "Warning",
            HealthOutcome.Pass => "Success",
            _ => "SecondaryText"
        });
        AutomationProperties.SetHelpText(this, line);
    }
}
