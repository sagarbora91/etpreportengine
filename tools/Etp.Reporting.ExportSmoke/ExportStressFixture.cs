using System.Globalization;
using System.Text.Json;
using Etp.Reporting.Reporting;

internal static class ExportStressFixture
{
    public static int Run(string output)
    {
        Directory.CreateDirectory(output);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        var metadata = new ExcelReportMetadata("Brand Sales", new(2026, 8, 25), new(2026, 8, 25), "Passed", "synthetic-stress", "Signed values, Unicode, wide rows, explicit zero, missing values and long notes.", DateTimeOffset.UtcNow);
        var columns = new[] { new ExcelReportColumn("Invoice"), new("Net Sales", "#,##0.00"), new("Missing value"), new("Explicit zero", "#,##0.00"), new("Unicode label"), new("Long source path"), new("Remarks") }
            .Concat(Enumerable.Range(8, 6).Select(c => new ExcelReportColumn($"Complete field {c}"))).ToArray();
        var rows = Enumerable.Range(1, 60).Select(i => (IReadOnlyList<object?>)new object?[] { $"Invoice-{i:000}", i == 1 ? -12345678901234.5678m : i * 100m, null, 0m, "Café ₹ signed return", "C:/Synthetic/" + new string('A', 240) + $"/row-{i}.xlsx", i == 1 ? string.Join(" ", Enumerable.Range(1, 350).Select(n => $"note-{n:000}")) : $"Complete remark {i}" }
            .Concat(Enumerable.Range(8, 6).Select(c => (object?)$"row-{i}-field-{c}")).ToArray()).ToArray();
        var data = new ExcelReportData(columns, rows, ["Total", -12345678718334.5678m, null, 0m]);
        var visual = VisualReportComposer.Compose(metadata, data);
        new OpenXmlVisualReportExporter().Export(Path.Combine(output, "stress-populated.xlsx"), visual);
        new SimplePdfVisualReportExporter().Export(Path.Combine(output, "stress-populated.pdf"), visual);
        File.WriteAllText(Path.Combine(output, "report-state-results.json"), JsonSerializer.Serialize(new[] { new { Code = "stress", state = "populated", metadata, data } }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
