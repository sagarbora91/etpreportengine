using System.Text.Json;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

var live = args.Length > 0 && string.Equals(args[0], "--live", StringComparison.OrdinalIgnoreCase);
var businessDate = live && args.Length > 1 ? DateOnly.Parse(args[1]) : new DateOnly(2026, 8, 25);
var outputArgument = live && args.Length > 2 ? args[2] : args.Length > 0 && !live ? args[0] : $"output/pdf/ETP_Daily_Sales_Report_{businessDate:yyyy-MM-dd}.pdf";
var output = Path.GetFullPath(outputArgument);
var document = live ? await LoadLiveAsync(businessDate, args.Length > 3 ? args[3] : null) : ApprovedDsrFixture.Create();
new DailySalesReportPdfExporter().Export(output, document);
Console.WriteLine(output);

static async Task<DailySalesReportDocument> LoadLiveAsync(DateOnly businessDate, string? settingsPath)
{
    settingsPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "settings.json");
    using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
    var connectionString = settings.RootElement.GetProperty("ConnectionString").GetString();
    if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidDataException("The saved SQL Server connection is missing.");
    return await new OperationalReportRepository(connectionString).LoadDailySalesReportDocumentAsync(businessDate);
}

internal static class ApprovedDsrFixture
{
    public static DailySalesReportDocument Create()
    {
        var engine = new ManagementMetricEngine();
        var built = DailySalesReportBuilder.Build(new DateOnly(2026, 8, 25), SalesFacts(), ServiceFacts(),
            new Dictionary<string, decimal?> { ["WLMHW"] = 1_600_000m, ["HEMW"] = 1_300_000m }, 0m);
        var approvedOperational = new[]
        {
            new DsrOperationalMetric("AUPT", 1m, 1m, engine.Growth(1m, 1m), "MTD 1.08 · YTD 1.06", false),
            new DsrOperationalMetric("AVPT", 25_363m, 15_599m, engine.Growth(25_363m, 15_599m), "MTD ₹22,207", true)
        };
        return built with { Stores = built.Stores.Select(x => x with { OperationalMetrics = approvedOperational }).ToArray() };
    }

    private static DsrPeriodFact[] SalesFacts() =>
    [
        new("FTD", "WLMHW", 69_880m, 22_647m, 8m, 6m, 8, 6, 1m, 8_735m, 10m, null),
        new("MTD", "WLMHW", 973_860m, null, 199m, null, 184, null, 1.08m, 5_293m, null, null),
        new("YTD", "WLMHW", 6_703_290m, 4_890_060m, 1_325m, 1_116m, 1_250, 1_053, 1.06m, 5_363m, null, null),
        new("FTD", "HEMW", 76_090m, 46_797m, 3m, 3m, 3, 3, 1m, 25_363m, 4m, null),
        new("MTD", "HEMW", 821_668m, null, 40m, null, 37, null, 1.08m, 22_207m, null, null),
        new("YTD", "HEMW", 3_551_016m, 2_216_848m, 192m, 133m, 181, 125, 1.06m, 19_619m, null, null),
        new("FTD", "COMBINED", 145_970m, 69_444m, 11m, 9m, 11, 9, 1m, 13_270m, 14m, null),
        new("MTD", "COMBINED", 1_795_528m, null, 239m, null, 221, null, 1.08m, 8_125m, null, null),
        new("YTD", "COMBINED", 10_254_306m, 7_106_908m, 1_517m, 1_249m, 1_431, 1_178, 1.06m, 7_166m, null, null)
    ];

    private static DsrServiceFact[] ServiceFacts() =>
    [
        new("FTD", "COMBINED", 700m, 1_807m, 577m, 3_084m, 0m),
        new("MTD", "COMBINED", null, null, null, 77_128m, 51_385m),
        new("YTD", "COMBINED", null, null, null, 357_490m, 211_160m)
    ];
}
