extern alias EtpApplication;

using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Desktop.Modules.HealthCheck;
using Etp.Reporting.Desktop.Modules.Reports;
using TenderVarianceDiagnostic = EtpApplication::Etp.Reporting.Application.Reports.ITenderVarianceDiagnostic;

namespace Etp.Reporting.Desktop.Composition;

// Reports: the Reports workspace and the report probes of the health check (RPT-01..22), which run the same queries.
public sealed partial class DesktopCompositionRoot
{
    private static ReportsWorkspaceView CreateReportsWorkspaceView(Func<string> connection, IReportExportCoordinator reportExportCoordinator)
    {
        TenderVarianceDiagnostic tenderVarianceDiagnostic = new ReportingTenderVarianceDiagnostic();
        return new ReportsWorkspaceView(
            () => new SqlServerApplicationReportQuery(connection()),
            () => new SqlServerApplicationReportQuery(connection()),
            () => new SqlServerApplicationReportQuery(connection()),
            reportExportCoordinator,
            tenderVarianceDiagnostic,
            (store, from, to) => new OperationalReportRepository(connection()).LoadCashBookAsync(store, from, to));
    }

    /// <summary>
    /// The report probes' production wiring (was <c>ReportProbes.ForConnection</c> until 1.12): the same query class
    /// the Reports screen uses, on the health run's target.
    /// </summary>
    internal static ReportProbes CreateReportProbes(string targetConnectionString, TenderVarianceDiagnostic? tenderDiagnostic = null)
    {
        var query = new SqlServerApplicationReportQuery(targetConnectionString);
        var cash = new OperationalReportRepository(targetConnectionString);
        return new ReportProbes(query, query, query,
            tenderDiagnostic ?? new ReportingTenderVarianceDiagnostic(),
            (store, from, to, token) => cash.LoadCashBookAsync(store, from, to, token));
    }
}
