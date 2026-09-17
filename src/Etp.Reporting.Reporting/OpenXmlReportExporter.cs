using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Etp.Reporting.Reporting;

public sealed record ExcelReportColumn(string Header, string NumberFormat = "General");
public sealed record ExcelReportMetadata(string ReportName, DateOnly DateFrom, DateOnly DateTo,
    string Status, string RuleVersion, string Message, DateTimeOffset GeneratedUtc, string? AppliedScope = null);
public sealed record ExcelReportData(IReadOnlyList<ExcelReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<object?>? Totals = null);

public sealed class OpenXmlReportExporter
{
    public void Export(string path, ExcelReportMetadata metadata, ExcelReportData data) =>
        ReportWorksheetWriter.Export(path, metadata, [new(metadata.ReportName, metadata.Status, metadata.Message, data)]);
}
