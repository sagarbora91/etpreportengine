using System.Globalization;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Etp.Reporting.Reporting;

public sealed record ReportPackTable(string Name, string Status, string Message, ExcelReportData Data);

public sealed record ReportPackDocument(
    string Title,
    DateOnly DateFrom,
    DateOnly DateTo,
    string OverallStatus,
    string RuleVersion,
    string Message,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<ReportPackTable> Tables);

public static class ReportPackArchiveCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(ReportPackDocument pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return JsonSerializer.Serialize(pack, Options);
    }

    public static ReportPackDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("An archived report document is required.", nameof(json));
        var pack = JsonSerializer.Deserialize<ReportPackDocument>(json, Options)
            ?? throw new InvalidDataException("The archived report document is invalid.");
        return pack with
        {
            Tables = pack.Tables.Select(table => table with
            {
                Data = table.Data with
                {
                    Rows = table.Data.Rows.Select(row => (IReadOnlyList<object?>)row.Select(Normalize).ToArray()).ToArray(),
                    Totals = table.Data.Totals?.Select(Normalize).ToArray()
                }
            }).ToArray()
        };
    }

    private static object? Normalize(object? value)
    {
        if (value is not JsonElement element) return value;
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.GetRawText()
        };
    }
}

public sealed class OpenXmlReportPackExporter
{
    public void Export(string path,ReportPackDocument pack) => ReportWorksheetWriter.Export(path,
        new(pack.Title,pack.DateFrom,pack.DateTo,pack.OverallStatus,pack.RuleVersion,pack.Message,pack.GeneratedUtc),pack.Tables);
}
public sealed class SimplePdfReportPackExporter
{
    public void Export(string path,ReportPackDocument pack) => VisualReportPdfDocument.ExportMany(path,
        pack.Tables.Select(t=>VisualReportComposer.Compose(new(t.Name,pack.DateFrom,pack.DateTo,t.Status,pack.RuleVersion,t.Message,pack.GeneratedUtc),t.Data)).ToArray());
}
