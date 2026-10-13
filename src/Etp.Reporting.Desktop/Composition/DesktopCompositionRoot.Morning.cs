extern alias EtpApplication;

using Etp.Reporting.Desktop.Modules.Morning;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.Morning;
using Etp.Reporting.Infrastructure.SqlServer.NoSaleDays;
using Etp.Reporting.Reporting;
using EtpApplication::Etp.Reporting.Application.Morning;
using RecordOperationalAudit = EtpApplication::Etp.Reporting.Application.DatabaseLifecycle.RecordOperationalAudit;

namespace Etp.Reporting.Desktop.Composition;

// Morning (1.12 U1, wave plan C-a): the Owner's landing page. Every service is built from the window's current connection
// factory when the page loads or acts, so a connection changed in Settings is the one used; no connection string reaches
// the view (ArchitectureGuardTests).
public sealed partial class DesktopCompositionRoot
{
    partial void RegisterMorning(ScreenRegistry registry)
    {
        registry.Register(MorningTask, request => new MorningView(CreateMorningServices(), request));
        // The Today checklist's no-sale lines (decision 52) read N1's service from the same current connection.
        if (registry.Window is { } window) window.noSaleDays = () => new SqlServerNoSaleDayService(CurrentConnections());
    }

    /// <summary>The Morning page's task id (TaskNavigation, Today &gt; Morning, Owner only).</summary>
    internal const string MorningTask = "morning";

    private MorningScreenServices CreateMorningServices() => new(
        () => new SqlServerMorningSummaryQuery(
            CurrentConnections,
            () => new SqlServerNoSaleDayService(CurrentConnections()),
            // Integration (U1 x G1): the footer's "Backup disk copy" age from OPS-11's receipt; absent until the copy is set up.
            diskCopyAgeHours: now => Etp.Reporting.Infrastructure.SqlServer.HealthCheck.DiskBackupProbe.CopyAgeHours(
                new Etp.Reporting.Infrastructure.SqlServer.HealthCheck.WindowsDiskBackupEnvironment(), now),
            report: (exception, eventId) => DesktopDiagnostics.Record(exception, "Morning.Query", eventId, DesktopDiagnosticSeverity.Warning),
            describe: DesktopFriendlyError.Describe),
        (businessDate, stores, progress, token) => FinaliseAllAsync(CurrentConnections(), businessDate, stores, progress, token),
        async token =>
        {
            var result = await PowerShellOperationsService.StartScheduledTaskAsync(ScheduledTaskOutcome.PublishShopResultsTask, token);
            return new MorningPublishResult(result.Succeeded, result.Message);
        });

    /// <summary>
    /// "Finalise all ready stores": Close day's own steps per store (pack, FinaliseAsync, DayFinalised audit), each store in
    /// its own transaction. The SQL adapter checks the role again (Owner or Store Manager) before it locks anything.
    /// </summary>
    internal static Task<IReadOnlyList<MorningFinaliseResult>> FinaliseAllAsync(
        IEtpConnectionFactory connections, DateOnly businessDate, IReadOnlyList<string> stores, IProgress<string>? progress, CancellationToken token)
    {
        var connection = connections.LegacyConnectionString();
        var workflow = new SqlServerDailyWorkflowService(connection);
        var lifecycle = new SqlServerDatabaseLifecycleService(connection);
        var finaliser = new MorningFinaliseAll<ReportPackDocument>(
            () => workflow,
            () => workflow,
            async (eventType, outcome, detail) =>
            {
                try { await lifecycle.RecordAuditAsync(new RecordOperationalAudit(eventType, outcome, detail)); }
                catch (Exception exception) when (DesktopFriendlyError.IsAuditFailure(exception))
                { DesktopDiagnostics.Record(exception, "OperationalAudit", "AUDIT_WRITE_FAILED", DesktopDiagnosticSeverity.Warning); }
            },
            DesktopFriendlyError.Describe);
        return finaliser.RunAsync(businessDate, stores, Environment.UserName, progress, token);
    }
}
