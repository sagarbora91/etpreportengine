namespace Etp.Reporting.Reporting;
public sealed class OpenXmlVisualReportExporter
{
    public void Export(string path,VisualReportModel model) => new OpenXmlReportExporter().Export(path,
        new(model.Metadata.ReportName,model.Metadata.DateFrom,model.Metadata.DateTo,model.Controls.FirstOrDefault()?.Status??"",model.Metadata.RuleVersion,model.Controls.FirstOrDefault()?.Message??"",model.Metadata.GeneratedUtc),model.Detail);
}
public sealed class SimplePdfVisualReportExporter
{
    public void Export(string path,VisualReportModel model) => VisualReportPdfDocument.Export(path,model);
}
