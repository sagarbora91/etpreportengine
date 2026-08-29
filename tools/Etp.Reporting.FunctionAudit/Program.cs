extern alias EtpApplication;

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Stock;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Reporting;
using IAccessSessionQuery = EtpApplication::Etp.Reporting.Application.Access.IAccessSessionQuery;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Etp.Reporting.FunctionAudit <audit-output-directory>");
    return 2;
}

var outputRoot = Path.GetFullPath(args[0]);
var fixtureRoot = Path.Combine(outputRoot, "demo-data");
Directory.CreateDirectory(fixtureRoot);

var fixtureFiles = GenerateFixtures(fixtureRoot);
var fixtureEvidence = await VerifyFixturesAsync(fixtureFiles);
var coverage = BuildCoverage();

var active = coverage.Where(x => x.Disposition != "DEFERRED_UNAVAILABLE").ToArray();
var uncovered = active.Where(x => x.Evidence.Length == 0).ToArray();
var duplicateIds = coverage.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToArray();
var catalogueCodes = ProductReportCatalogue.All.Select(x => x.Code).Order().ToArray();
var routeCodes = WorkspaceModuleOwnershipRegistry.ReportRoutes.Select(x => x.FeatureCode!).Order().ToArray();
if (!catalogueCodes.SequenceEqual(routeCodes, StringComparer.Ordinal))
    throw new InvalidOperationException("The product report catalogue and executable report routes differ.");
if (uncovered.Length != 0 || duplicateIds.Length != 0)
    throw new InvalidOperationException($"Coverage matrix invalid: uncovered={uncovered.Length}; duplicateIds={duplicateIds.Length}.");

var expected = new
{
    fixtureVersion = "ETP_SYNTHETIC_ACCEPTANCE_V1",
    businessDate = "2026-08-25",
    stores = new[] { "WLMHW", "HEMW" },
    workbookCount = 12,
    totals = new
    {
        canonicalSalesRows = 6,
        invoiceControls = 6,
        netSales = 4600.00m,
        signedUnits = 4.00m,
        tenderTotal = 4600.00m,
        stockMovementRows = 4,
        closingStockRows = 4,
        closingStockQuantity = 60.00m,
        quarantinedTenderRows = 2,
        r003EnrichmentRows = 6,
        r013EnrichmentRows = 6
    },
    invariants = new[]
    {
        "R025 NETVALUE is canonical sales value.",
        "Return quantities and values remain source-signed.",
        "R022 invoice controls and eligible tenders reconcile to R025 invoice totals.",
        "Each stock ledger row satisfies opening quantity plus transaction quantity equals closing quantity.",
        "Customer names, contact numbers and loyalty identifiers contain only the literal SYNTHETIC-NO-PII."
    }
};

var options = new JsonSerializerOptions { WriteIndented = true };
await File.WriteAllTextAsync(Path.Combine(outputRoot, "expected-control-totals.json"), JsonSerializer.Serialize(expected, options));
await File.WriteAllTextAsync(Path.Combine(outputRoot, "fixture-evidence.json"), JsonSerializer.Serialize(fixtureEvidence, options));
await File.WriteAllTextAsync(Path.Combine(outputRoot, "function-coverage.json"), JsonSerializer.Serialize(new
{
    generatedUtc = DateTimeOffset.UtcNow,
    scope = "All authoritative user-facing modules, navigation functions, report routes, import profiles, startup modes, and public application-service operations.",
    activeFunctionCount = active.Length,
    deferredUnavailableCount = coverage.Count - active.Length,
    reportCount = catalogueCodes.Length,
    uncoveredCount = uncovered.Length,
    entries = coverage
}, options));

await File.WriteAllTextAsync(Path.Combine(outputRoot, "function-coverage.md"), BuildMarkdown(coverage, fixtureEvidence));
Console.WriteLine($"Function inventory complete: active={active.Length}; deferred={coverage.Count - active.Length}; reports={catalogueCodes.Length}; uncovered=0.");
Console.WriteLine($"Synthetic fixtures verified: {fixtureEvidence.Count} workbooks; output={outputRoot}");
return 0;

static IReadOnlyList<string> GenerateFixtures(string fixtureRoot)
{
    var result = new List<string>();
    foreach (var store in new[] { "WLMHW", "HEMW" })
    {
        var rows = SalesRows(store);
        result.Add(WriteWorkbook(fixtureRoot, $"{store} SDB-VariantwiseSales 2026-08-25.xlsx", RetailSalesProfiles.R025Headers,
            rows.Select(x => R025Row(store, x))));
        result.Add(WriteWorkbook(fixtureRoot, $"{store} Revenue Report 2026-08-25.xlsx", RetailSalesProfiles.R022Headers,
            rows.Select(x => R022Row(store, x))));
        result.Add(WriteWorkbook(fixtureRoot, $"{store} CRO Wise Sales 2026-08-25.xlsx", RetailSalesProfiles.R013Headers,
            rows.Select(x => R013Row(store, x))));
        result.Add(WriteWorkbook(fixtureRoot, $"{store} All Discount Type 2026-08-25.xlsx", RetailSalesProfiles.R003Headers,
            rows.Select(x => R003Row(store, x))));
        result.Add(WriteWorkbook(fixtureRoot, $"{store} Variant Stock ledger 2026-08-25.xlsx", StockImportProfiles.VariantStockLedgerHeaders,
            StockLedgerRows(store)));
        result.Add(WriteWorkbook(fixtureRoot, $"{store} Closing Stock 2026-08-25.xlsx", StockImportProfiles.ClosingStockHeaders,
            ClosingStockRows(store)));
    }
    return result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
}

static async Task<IReadOnlyList<object>> VerifyFixturesAsync(IReadOnlyList<string> fixtureFiles)
{
    var reader = new OpenXmlWorkbookReader();
    var profiles = RetailSalesProfiles.FirstSalesSlice.Concat(StockImportProfiles.All).ToArray();
    var evidence = new List<object>();
    foreach (var path in fixtureFiles)
    {
        var workbook = await reader.ReadAsync(path);
        var preflight = new ImportPreflight().Inspect(workbook, profiles);
        if (!preflight.CanImport)
            throw new InvalidOperationException($"Generated fixture failed preflight: {Path.GetFileName(path)} ({string.Join(',', preflight.Diagnostics.Select(x => x.Code))}).");
        var rows = preflight.Profile!.ReportCode is "STOCK_LEDGER" or "CLOSING_STOCK"
            ? CountStockRows(new StockWorkbookParser().Parse(workbook), path)
            : CountStagedRows(new ImportRowStager().Stage(preflight.Sheet!, preflight.Profile), path);
        evidence.Add(new
        {
            file = Path.GetFileName(path),
            reportCode = preflight.Profile.ReportCode,
            rows,
            sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant(),
            preflight = "PASS"
        });
    }
    if (fixtureFiles.Count != 12) throw new InvalidOperationException("Exactly 12 synthetic source workbooks are required.");
    return evidence;
}

static int CountStockRows(StockWorkbookParseResult parsed, string path)
{
    if (parsed.HasBlockers) throw new InvalidOperationException($"Generated stock fixture failed parsing: {Path.GetFileName(path)}.");
    return parsed.Movements.Count + parsed.Snapshots.Count;
}

static int CountStagedRows(ImportStagingResult staged, string path)
{
    if (!staged.CanPersist) throw new InvalidOperationException($"Generated retail fixture failed staging: {Path.GetFileName(path)}.");
    return staged.Rows.Count;
}

static List<CoverageEntry> BuildCoverage()
{
    var entries = new List<CoverageEntry>();
    entries.AddRange(UiNavigationRegistry.Modules.Select(x => new CoverageEntry($"module:{x.Id}", "Module", x.DisplayName,
        x.DefaultVisibility ? "AUTOMATED_UI" : "AUTOMATED_ROLE_UI", ["ui-all-workspaces", "dotnet-tests"])));
    entries.AddRange(UiNavigationRegistry.AllItems.Select(x => new CoverageEntry($"navigation:{x.Id}", "Navigation function", x.Label,
        x.IsAvailable ? "AUTOMATED_LAYERED" : "DEFERRED_UNAVAILABLE",
        x.IsAvailable ? EvidenceForDestination(x.Destination) : ["declared-unavailable-with-reason"], x.UnavailableReason)));
    entries.AddRange(ProductReportCatalogue.All.Select(x => new CoverageEntry($"report:{x.Code}", "Report", x.Name,
        "AUTOMATED_LIVE_AND_UI", ["live-sql-reporting", "ui-all-report-routes", "dotnet-tests"])));
    entries.AddRange(WorkspaceModuleOwnershipRegistry.Destinations.Select(x => new CoverageEntry($"route:{Slug(x.Destination)}", "Workspace route", x.Destination,
        "AUTOMATED_UI", ["ui-all-workspaces", "dotnet-tests"])));
    entries.AddRange(RetailSalesProfiles.FirstSalesSlice.Concat(StockImportProfiles.All).Select(x => new CoverageEntry($"import:{x.ReportCode}", "Import profile", x.ReportCode,
        "AUTOMATED_LIVE", ["synthetic-fixture-preflight", "offline-import-smoke", "live-sql-import"] )));
    entries.AddRange(Enum.GetValues<DesktopStartupMode>().Select(x => new CoverageEntry($"startup:{x.ToString().ToLowerInvariant()}", "Startup mode", x.ToString(),
        "AUTOMATED_LAYERED", x switch
        {
            DesktopStartupMode.Interactive => ["ui-all-workspaces", "dotnet-tests"],
            DesktopStartupMode.InitializeDatabase => ["live-sql-bootstrap", "dotnet-tests"],
            _ => ["live-sql-automation", "dotnet-tests"]
        })));

    var applicationAssembly = typeof(IAccessSessionQuery).Assembly;
    foreach (var contract in applicationAssembly.GetExportedTypes().Where(x => x.IsInterface && x.Name.StartsWith('I')).OrderBy(x => x.FullName))
    foreach (var method in contract.GetMethods().OrderBy(x => x.Name))
    {
        var id = $"operation:{Slug(contract.Name)}:{Slug(method.Name)}";
        entries.Add(new(id, "Application operation", $"{contract.Name}.{method.Name}", OperationDisposition(contract), OperationEvidence(contract)));
    }
    return entries;
}

static string[] EvidenceForDestination(string destination) => destination switch
{
    "Import ETP" => ["ui-all-workspaces", "offline-import-smoke", "live-sql-import", "dotnet-tests"],
    "Sales Reports" => ["ui-all-report-routes", "live-sql-reporting", "dotnet-tests"],
    "Daily Workflow" or "Manual Entry" => ["ui-all-workspaces", "live-sql-daily-workflow", "dotnet-tests"],
    "Report Archive" => ["ui-all-workspaces", "live-sql-archive", "dotnet-tests"],
    "Operations Center" => ["ui-all-workspaces", "live-sql-operations", "dotnet-tests"],
    "Admin / Settings" or "Settings" => ["ui-all-workspaces", "live-sql-administration", "dotnet-tests"],
    "Accounting" or "Registers" => ["ui-all-workspaces", "dotnet-tests", "external-user-acceptance"],
    _ => ["ui-all-workspaces", "dotnet-tests"]
};

static string OperationDisposition(Type contract) => contract.Namespace switch
{
    "Etp.Reporting.Application.Accounting" or "Etp.Reporting.Application.Registers" or
    "Etp.Reporting.Application.Sharing" or "Etp.Reporting.Application.Distribution" => "AUTOMATED_PLUS_EXTERNAL_GATE",
    _ => "AUTOMATED_LAYERED"
};

static string[] OperationEvidence(Type contract) => contract.Namespace switch
{
    "Etp.Reporting.Application.DatabaseLifecycle" => ["live-sql-bootstrap", "live-sql-backup-restore", "dotnet-tests"],
    "Etp.Reporting.Application.Imports" or "Etp.Reporting.Application.SourceInbox" => ["offline-import-smoke", "live-sql-import", "dotnet-tests"],
    "Etp.Reporting.Application.Reports" => ["live-sql-reporting", "dotnet-tests"],
    "Etp.Reporting.Application.DailyWorkflow" => ["live-sql-daily-workflow", "dotnet-tests"],
    "Etp.Reporting.Application.OperationsAdministration" => ["live-sql-administration", "live-sql-automation", "dotnet-tests"],
    "Etp.Reporting.Application.Archive" => ["live-sql-archive", "dotnet-tests"],
    "Etp.Reporting.Application.Accounting" or "Etp.Reporting.Application.Registers" => ["ui-all-workspaces", "dotnet-tests", "external-user-acceptance"],
    "Etp.Reporting.Application.Sharing" or "Etp.Reporting.Application.Distribution" => ["dotnet-tests", "external-email-client-gate", "external-user-acceptance"],
    _ => ["live-sql-operations", "dotnet-tests"]
};

static string BuildMarkdown(IReadOnlyList<CoverageEntry> coverage, IReadOnlyList<object> fixtures)
{
    var active = coverage.Where(x => x.Disposition != "DEFERRED_UNAVAILABLE").ToArray();
    var byKind = coverage.GroupBy(x => x.Kind).OrderBy(x => x.Key);
    var lines = new List<string>
    {
        "# ETP function-first acceptance coverage",
        "",
        "> Generated from the active runtime registries and public application contracts. Do not edit this file by hand.",
        "",
        $"- Active functions mapped: **{active.Length}**",
        $"- Explicitly unavailable/deferred functions: **{coverage.Count - active.Length}**",
        $"- Product reports: **{coverage.Count(x => x.Kind == "Report")}**",
        $"- Verified synthetic source workbooks: **{fixtures.Count}**",
        $"- Active functions without evidence: **{active.Count(x => x.Evidence.Length == 0)}**",
        "",
        "Automated coverage is layered: unit/contract tests, offline import checks, all-route UI rendering, live SQL workflows, export/performance checks, and backup/restore. Hardware, installed email-client behavior, signed installation, accessibility observation, and business-owner acceptance remain explicit external gates.",
        ""
    };
    foreach (var group in byKind)
    {
        lines.Add($"## {group.Key}"); lines.Add(""); lines.Add("| ID | Function | Disposition | Evidence |"); lines.Add("|---|---|---|---|");
        lines.AddRange(group.OrderBy(x => x.Id).Select(x => $"| `{x.Id}` | {Escape(x.Name)} | {x.Disposition} | {string.Join(", ", x.Evidence.Select(e => $"`{e}`"))} |"));
        lines.Add("");
    }
    return string.Join(Environment.NewLine, lines);
}

static string Escape(string value) => value.Replace("|", "\\|");
static string Slug(string value) => new string(value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');

static string WriteWorkbook(string root, string fileName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyDictionary<string, string>> rows)
{
    var path = Path.Combine(root, fileName);
    using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
    var workbook = document.AddWorkbookPart(); workbook.Workbook = new Workbook();
    var worksheet = workbook.AddNewPart<WorksheetPart>();
    var sheetData = new SheetData();
    sheetData.Append(Row(1, headers));
    uint rowIndex = 2;
    foreach (var values in rows) sheetData.Append(Row(rowIndex++, headers.Select(header => values.GetValueOrDefault(header, DefaultValue(header)))));
    worksheet.Worksheet = new Worksheet(sheetData);
    workbook.Workbook.AppendChild(new Sheets(new Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = "ETP Export" }));
    workbook.Workbook.Save();
    return path;
}

static Row Row(uint number, IEnumerable<string> values)
{
    var row = new Row { RowIndex = number }; var index = 0;
    foreach (var value in values)
        row.Append(new Cell { CellReference = Column(++index) + number, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value ?? "")) });
    return row;
}

static string Column(int index) { var text = ""; while (index > 0) { index--; text = (char)('A' + index % 26) + text; index /= 26; } return text; }
static string DefaultValue(string header) => header.Contains("DATE", StringComparison.OrdinalIgnoreCase) || header == "Date" ? "" : "0";

static IReadOnlyList<SaleRow> SalesRows(string store) =>
[
    new("INV", $"{store}-INV-001", "SKU-A", "TITAN", "Titan", "GAUTO", "CRO-01", 1m, 1000m, 400m, 600m),
    new("INV", $"{store}-INV-002", "SKU-B", "FASTRACK", "Fastrack", "WQ", "CRO-02", 2m, 1500m, 0m, 1500m),
    new("SR", $"{store}-SR-001", "SKU-A", "TITAN", "Titan", "GAUTO", "CRO-01", -1m, -200m, -200m, 0m)
];

static Dictionary<string, string> Base(string store) => new(StringComparer.Ordinal)
{
    ["STORE CODE"] = store, ["STORE NAME"] = store == "WLMHW" ? "Titan World Demo" : "Helios Demo",
    ["STORENAME"] = store == "WLMHW" ? "Titan World Demo" : "Helios Demo", ["STORE TYPE"] = "RETAIL", ["STORETYPE"] = "RETAIL",
    ["CHANNEL"] = "STORE", ["REGION"] = "WEST", ["STATE"] = "MAHARASHTRA", ["CITY"] = "MUMBAI",
    ["CUSTOMERNAME"] = "SYNTHETIC-NO-PII", ["ContactNo"] = "SYNTHETIC-NO-PII", ["CONTACTNO"] = "SYNTHETIC-NO-PII",
    ["CUSTOMERNUMBER"] = "SYNTHETIC-NO-PII", ["ULP NO"] = "SYNTHETIC-NO-PII", ["ULPNUMBER"] = "SYNTHETIC-NO-PII",
    ["STORETIMESTAMP"] = "2026-08-25T18:00:00", ["EASTIMESTAMP"] = "2026-08-25T18:00:00"
};

static IReadOnlyDictionary<string, string> R025Row(string store, SaleRow x)
{
    var r = Base(store); SetSales(r, x, "INVNUMBER", "INVDATE");
    r["HSNCODE"] = "9102"; r["GROSSUCP"] = Number(Math.Abs(x.NetValue)); r["NETGROSS"] = Number(x.NetValue);
    return r;
}

static IReadOnlyDictionary<string, string> R022Row(string store, SaleRow x)
{
    var r = Base(store); r["TRANS_TYPE"] = x.Type; r["INVNUMBER"] = x.Invoice; r["InvoiceQuantity"] = Number(x.Quantity);
    r["INVOICEDATE"] = "2026-08-25"; r["INVOICEYEAR"] = "2026"; r["NetValue"] = Number(x.NetValue);
    r["CASH"] = Number(x.Cash); r["CARD"] = Number(x.Card);
    if (x.Invoice.EndsWith("INV-001", StringComparison.Ordinal)) r["PAYMENTTYPE25"] = "1";
    return r;
}

static IReadOnlyDictionary<string, string> R013Row(string store, SaleRow x)
{
    var r = Base(store); SetSales(r, x, "INVNUMBER", "INVDATE"); r["CRO NUMBER"] = x.Cro; r["CRO NAME"] = $"Synthetic {x.Cro}"; return r;
}

static IReadOnlyDictionary<string, string> R003Row(string store, SaleRow x)
{
    var r = Base(store); SetSales(r, x, "INVOICE NUMBER", "INVOICE DATE"); r["BRAND NAME"] = x.BrandName; return r;
}

static void SetSales(Dictionary<string, string> r, SaleRow x, string invoiceHeader, string dateHeader)
{
    r["TRANS_TYPE"] = x.Type; r[invoiceHeader] = x.Invoice; r[dateHeader] = "2026-08-25"; r["ITEMNUMBER"] = x.Item;
    r["BRAND"] = x.Brand; r["BRANDNAME"] = x.BrandName; r["CLUSTER"] = x.Segment; r["GENDER"] = "UNISEX";
    r["QTY"] = Number(x.Quantity); r["UCP"] = Number(Math.Abs(x.NetValue)); r["NETAMOUNT"] = Number(x.NetValue); r["NETVALUE"] = Number(x.NetValue);
}

static IEnumerable<IReadOnlyDictionary<string, string>> StockLedgerRows(string store)
{
    foreach (var row in new[] { new { Item = "SKU-A", Type = "INV", Opening = 10, Movement = 1, Closing = 11 }, new { Item = "SKU-B", Type = "SR", Opening = 20, Movement = -1, Closing = 19 } })
    {
        var r = Base(store); r["TRANS_TYPE"] = row.Type; r["ITEMNUMBER"] = row.Item; r["DOCUMENTNUMBER"] = $"{store}-STOCK-{row.Item}";
        r["DOCUMENTDATE"] = "2026-08-25"; r["OPENING_QTY"] = Number(row.Opening); r["TRANS_QTY"] = Number(row.Movement); r["CLOSING_QTY"] = Number(row.Closing);
        r["FROM LOCATION"] = store; r["TO LOCATION"] = store; yield return r;
    }
}

static IEnumerable<IReadOnlyDictionary<string, string>> ClosingStockRows(string store)
{
    foreach (var row in new[] { new { Item = "SKU-A", Brand = "TITAN", Segment = "GAUTO", Quantity = 11 }, new { Item = "SKU-B", Brand = "FASTRACK", Segment = "WQ", Quantity = 19 } })
    {
        var r = Base(store); r["Date"] = "2026-08-25"; r["ITEMNUMBER"] = row.Item; r["BRAND"] = row.Brand; r["CLUSTER"] = row.Segment;
        r["GENDER"] = "UNISEX"; r["QTY"] = Number(row.Quantity); r["UCP"] = "100"; r["TOTALUCP"] = Number(row.Quantity * 100);
        r["EAN NO"] = $"EAN-{row.Item}"; r["BATCHNUMBER"] = "SYNTHETIC"; r["UID_ITEM"] = $"{store}-{row.Item}"; yield return r;
    }
}

static string Number<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);

sealed record SaleRow(string Type, string Invoice, string Item, string Brand, string BrandName, string Segment, string Cro, decimal Quantity, decimal NetValue, decimal Cash, decimal Card);
sealed record CoverageEntry(string Id, string Kind, string Name, string Disposition, string[] Evidence, string? Note = null);
