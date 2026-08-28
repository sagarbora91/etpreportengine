using Etp.Reporting.Application.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class ReportingTenderVarianceDiagnostic : ITenderVarianceDiagnostic
{
    public TenderVarianceDiagnosticReport Diagnose(TenderReconciliationReport reconciliation, decimal tolerance)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);
        var source = new InvoiceTenderReconciliation(
            ToReportingStatus(reconciliation.Status),
            reconciliation.Documents.Select(row => new DocumentControlResult(
                row.StoreCode,
                row.DocumentNumber,
                row.InvoiceAmount,
                row.TenderAmount,
                row.Variance,
                ToReportingStatus(row.Status))).ToArray(),
            reconciliation.InvoiceTotal,
            reconciliation.TenderTotal,
            reconciliation.Variance,
            reconciliation.RuleVersion,
            reconciliation.Message);
        var result = new TenderVarianceDiagnosticService().Diagnose(source, tolerance);
        return new(
            ToApplicationStatus(result.Status),
            result.Rows.Select(row => new TenderVarianceDiagnosticRecord(
                row.StoreCode,
                row.DocumentNumber,
                row.InvoiceAmount,
                row.TenderAmount,
                row.Variance,
                ToApplicationCause(row.LikelyCause),
                row.RecommendedCheck)).ToArray(),
            result.FailedDocuments,
            result.AbsoluteVariance,
            result.RuleVersion,
            result.Message);
    }

    public static ReconciliationStatus ToReportingStatus(ReportStatus status) => status switch
    {
        ReportStatus.Passed => ReconciliationStatus.Passed,
        ReportStatus.Failed => ReconciliationStatus.Failed,
        ReportStatus.Blocked => ReconciliationStatus.Blocked,
        _ => ReconciliationStatus.NotRun
    };

    public static ReportStatus ToApplicationStatus(ReconciliationStatus status) => status switch
    {
        ReconciliationStatus.Passed => ReportStatus.Passed,
        ReconciliationStatus.Failed => ReportStatus.Failed,
        ReconciliationStatus.Blocked => ReportStatus.Blocked,
        _ => ReportStatus.NotRun
    };

    public static Etp.Reporting.Application.Reports.TenderVarianceCause ToApplicationCause(
        Etp.Reporting.Reporting.TenderVarianceCause cause) => cause switch
    {
        Etp.Reporting.Reporting.TenderVarianceCause.Matched => Etp.Reporting.Application.Reports.TenderVarianceCause.Matched,
        Etp.Reporting.Reporting.TenderVarianceCause.MissingTender => Etp.Reporting.Application.Reports.TenderVarianceCause.MissingTender,
        Etp.Reporting.Reporting.TenderVarianceCause.PartialTender => Etp.Reporting.Application.Reports.TenderVarianceCause.PartialTender,
        Etp.Reporting.Reporting.TenderVarianceCause.ExcessTender => Etp.Reporting.Application.Reports.TenderVarianceCause.ExcessTender,
        Etp.Reporting.Reporting.TenderVarianceCause.TenderWithoutInvoice => Etp.Reporting.Application.Reports.TenderVarianceCause.TenderWithoutInvoice,
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "Unsupported tender variance cause.")
    };
}
