using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Stock;
using Etp.Reporting.Import.Workbooks;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: Etp.Reporting.Smoke <ETP source-data directory>");
    return 2;
}

var expected = new[] { "SDB-VariantwiseSales", "Revenue Report", "Variant Stock ledger", "Closing Stock" };
var files = Directory.EnumerateFiles(args[0], "*.xlsx", SearchOption.AllDirectories)
    .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
    .Where(path => expected.Any(name => Path.GetFileName(path).Contains(name, StringComparison.OrdinalIgnoreCase)))
    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
var profiles = RetailSalesProfiles.FirstSalesSlice.Concat(StockImportProfiles.All).ToArray();
var failures = 0;
foreach (var file in files)
{
    try
    {
        var workbook = await new OpenXmlWorkbookReader().ReadAsync(file);
        var preflight = new ImportPreflight().Inspect(workbook, profiles);
        if (!preflight.CanImport) { failures++; Console.WriteLine($"BLOCKED | {Path.GetFileName(file)} | {string.Join(',', preflight.Diagnostics.Select(x => x.Code))}"); continue; }
        var parsed = preflight.Profile!.ReportCode is "STOCK_LEDGER" or "CLOSING_STOCK" ? new StockWorkbookParser().Parse(workbook) : null;
        if (parsed?.HasBlockers == true) { failures++; Console.WriteLine($"BLOCKED | {Path.GetFileName(file)} | {string.Join(',', parsed.Diagnostics.Select(x => $"{x.Code}:{x.ColumnName}").Distinct())}"); continue; }
        var rows = parsed is not null ? parsed.Movements.Count + parsed.Snapshots.Count : new ImportRowStager().Stage(preflight.Sheet!, preflight.Profile).Rows.Count;
        Console.WriteLine($"PASS | {preflight.Profile.ReportCode} | {rows} rows | {Path.GetFileName(file)}");
    }
    catch (Exception ex) { failures++; Console.WriteLine($"ERROR | {Path.GetFileName(file)} | {ex.GetType().Name}"); }
}
Console.WriteLine($"Checked {files.Length} workbooks; failures: {failures}.");
return failures == 0 && files.Length >= 8 ? 0 : 1;
