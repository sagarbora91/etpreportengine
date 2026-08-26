using System.Diagnostics;
using System.Text.Json;
using Etp.Reporting.Reporting;

var output = args.Length > 0 ? args[0] : "artifacts/performance/performance-smoke.json";
const int salesCount = 250_000, stockCount = 100_000, tenderCount = 50_000;
var results = new Dictionary<string, object>();

var sales = Enumerable.Range(0, salesCount).Select(i => new SalesReportingLine(
    new DateOnly(2026, 7, 1).AddDays(i % 56), i % 2 == 0 ? "WLMHW" : "HEMW", $"D{i / 3}", i.ToString(),
    $"Brand{i % 40}", $"Segment{i % 20}", $"Item{i % 5000}", i % 17 == 0 ? ReportingTransactionType.Return : ReportingTransactionType.Sale,
    i % 17 == 0 ? -1 : 1, i % 17 == 0 ? -100 : 100)).ToArray();
Measure("sales-three-dimensions", () => { var service = new SalesReportingService(); foreach (var d in new[] { SalesSummaryDimension.Daily, SalesSummaryDimension.Store, SalesSummaryDimension.BrandSegment }) service.Summarize(sales, d, RetailReportingPolicy.Sales); });

var positions = Enumerable.Range(0, stockCount).Select(i => new StockPositionValue(i % 2 == 0 ? "WLMHW" : "HEMW", $"Item{i}", 10, 11)).ToArray();
var movements = positions.Select(x => new StockMovementValue(x.StoreCode, x.ItemCode, "INV", 1, true)).ToArray();
Measure("stock-reconciliation", () => new StockReconciliationService().Reconcile(positions, movements, RetailReportingPolicy.Stock));

var invoices = Enumerable.Range(0, tenderCount).Select(i => new InvoiceControlValue(i % 2 == 0 ? "WLMHW" : "HEMW", $"D{i}", 100)).ToArray();
var tenders = invoices.Select(x => new TenderControlValue(x.StoreCode, x.DocumentNumber, "CASH", 100, true)).ToArray();
Measure("tender-reconciliation", () => new InvoiceTenderReconciliationService().Reconcile(invoices, tenders, RetailReportingPolicy.Tender));

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(new { generatedUtc = DateTimeOffset.UtcNow, salesCount, stockCount, tenderCount, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(File.ReadAllText(output));
return results.Values.Cast<Metric>().Any(x => x.ElapsedMilliseconds > 30_000) ? 1 : 0;

void Measure(string name, Action action)
{
    GC.Collect(); var before = GC.GetTotalMemory(true); var sw = Stopwatch.StartNew(); action(); sw.Stop(); var after = GC.GetTotalMemory(false);
    results[name] = new Metric(sw.ElapsedMilliseconds, Math.Max(0, after - before));
}

sealed record Metric(long ElapsedMilliseconds, long ApproximateMemoryDeltaBytes);
