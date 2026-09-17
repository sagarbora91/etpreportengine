namespace Etp.Reporting.Reporting;
public sealed class SimplePdfReportExporter
{
    public void Export(string path,ExcelReportMetadata metadata,ExcelReportData data) =>
        VisualReportPdfDocument.Export(path,VisualReportComposer.Compose(metadata,data));
}
