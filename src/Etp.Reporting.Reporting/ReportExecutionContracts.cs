namespace Etp.Reporting.Reporting;

public sealed record ReportParameterValue(string ParameterId, object? Value);
public sealed record ReportRequest(string ReportId, IReadOnlyList<ReportParameterValue> Parameters)
{
    public static ReportRequest Create(string reportId, params ReportParameterValue[] parameters) => new(reportId, parameters);
}
public sealed record ReportCell(string ColumnId, object? Value);
public sealed record ReportRow(IReadOnlyList<ReportCell> Cells);
public sealed record ReportTotal(string ColumnId, object? Value, ReportAggregation Aggregation);
public sealed record ReportTable(IReadOnlyList<ReportColumnDefinition> Columns,
    IReadOnlyList<ReportRow> Rows, IReadOnlyList<ReportTotal> Totals);

public enum ReconciliationStatus { NotRun, Passed, Failed, Blocked }
public sealed record ReconciliationResult(string ControlId, ReconciliationStatus Status, string Message,
    IReadOnlyDictionary<string, decimal>? Evidence = null);
public sealed record ReportResult(ReportDefinition Definition, ReportTable Table,
    ReconciliationResult Reconciliation, DateTimeOffset GeneratedAtUtc);

public interface IReportExecutor
{
    Task<ReportResult> ExecuteAsync(ReportRequest request, CancellationToken cancellationToken = default);
}
