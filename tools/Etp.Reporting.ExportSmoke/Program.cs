using Etp.Reporting.Reporting;

if (args.Length != 1) return 2;
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
var metadata = new ExcelReportMetadata("Brand-Segment Sales", new(2026, 7, 1), new(2026, 8, 25), "Passed", RetailReportingPolicy.Version,
    "Synthetic visual QA only; source-signed values without sign transformation.", DateTimeOffset.UtcNow);
var data = new ExcelReportData([new("Brand Segment"), new("Units", "#,##0.00"), new("Net Sales", "#,##0.00"), new("Bills", "#,##0")],
    [["Titan Automatic", 12m, 118000m, 8], ["Titan Quartz", 18m, 142500m, 12], ["Sales Return", -1m, -9500m, 1]],
    ["Total", 29m, 251000m, 21]);
var visual = VisualReportComposer.Compose(metadata, data);
if (Path.GetExtension(args[0]).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
    new SimplePdfVisualReportExporter().Export(args[0], visual);
else
    new OpenXmlVisualReportExporter().Export(args[0], visual);
return 0;
