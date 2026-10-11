using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;

namespace Etp.Reporting.Desktop;

/// <summary>
/// 1.11.0 self-diagnostics in the shell: the one-line "System check" tile on Today &gt; Sales (every role, decision 33;
/// Store Managers and Viewers see only this) and the one-time error-register ingest after a successful start (design 4.3).
/// </summary>
public partial class MainWindow
{
    /// <summary>Set by the composition root: reads this account's diagnostics log into the error register. Never throws.</summary>
    internal Func<CancellationToken, Task>? startupErrorIngest;

    private HealthSummaryTile? healthSummaryTile;
    private bool startupIngestStarted;

    internal HealthSummaryTile HealthSummaryTile =>
        healthSummaryTile ??= new HealthSummaryTile(() => DiagnosticsScreens.Services.HealthStore()) { Margin = new Thickness(0, 8, 0, 0) };

    /// <summary>The tile above the "What's missing" panel; both go into the DSR's checklist slot.</summary>
    private UIElement TodayStatusPanel(UIElement checklist)
    {
        if (HealthSummaryTile.Parent is Panel previous) previous.Children.Remove(HealthSummaryTile);
        if (checklist is FrameworkElement { Parent: Panel owner }) owner.Children.Remove(checklist);
        var panel = new StackPanel();
        panel.Children.Add(HealthSummaryTile);
        panel.Children.Add(checklist);
        _ = HealthSummaryTile.RefreshAsync();
        return panel;
    }

    /// <summary>Once per process, after the start-up checks passed: ingest in the background, never blocking or failing the start.</summary>
    private void StartErrorRegisterIngestOnce()
    {
        if (startupIngestStarted || startupErrorIngest is not { } ingest) return;
        startupIngestStarted = true;
        _ = Task.Run(async () =>
        {
            try { await ingest(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) { DesktopDiagnostics.Record(exception, "Startup.ErrorRegister", "ERROR_REGISTER_STARTUP_INGEST_FAILED", DesktopDiagnosticSeverity.Warning); }
        });
    }
}
