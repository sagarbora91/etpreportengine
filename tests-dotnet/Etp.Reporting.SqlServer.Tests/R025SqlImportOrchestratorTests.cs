using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class R025SqlImportOrchestratorTests
{
    [Fact]
    public async Task Three_sales_values_FY_identity_sign_and_brand_segment_are_persisted()
    {
        var cells = RetailSalesProfiles.R025Headers.Select(header => new WorkbookCell(Value(header))).ToArray();
        var workbook = new WorkbookSnapshot("sanitized.xlsx", 10, new string('a', 64),
            [new("Sheet0", 1, RetailSalesProfiles.R025Headers, [new(2, cells)])]);
        var capture = new CaptureStore();
        var outcome = await new R025SqlImportOrchestrator(capture).PersistAsync(workbook);
        var line = Assert.Single(capture.Package!.SalesLines);
        Assert.Equal(-100m, line.SourceNetAmount);
        Assert.Equal(-118m,line.SourceGrossAmount);
        Assert.Equal(-18m,line.SourceTaxAmount);
        Assert.Equal(2027,line.InvoiceYear);
        Assert.Equal(-1m, line.SourceQuantity);
        Assert.Equal("GAUTO", line.BrandSegment);
        Assert.Equal("Titan Automatic", line.SourceBrandName);
        Assert.Equal(RetailSalesProfiles.R025.Identity, capture.Package.File.Profile);
        Assert.DoesNotContain(line.GetType().GetProperties(), x => x.Name.Contains("Customer", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Contact", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, outcome.PersistedRows);
    }

    private static object? Value(string header) => header switch
    {
        "TRANS_TYPE" => "SR", "STORE CODE" => "STORE", "ITEMNUMBER" => "ITEM",
        "BRAND" => "TITAN", "BRANDNAME" => "Titan Automatic", "CLUSTER" => "GAUTO",
        "INVNUMBER" => "DOC", "INVDATE" => new DateTime(2026, 8, 25), "QTY" => -1m,
        "NETAMOUNT" => -118m, "NETVALUE" => -100m, "TAX" => -18m, "INVREFDATE" => 0m,
        "CUSTOMERNAME" => "restricted", "CONTACTNO" => "restricted",
        _ when header.Contains('%') || header is "UCP" or "GROSSUCP" or "SCH_DISCOUNTS" or "USER_DISCOUNTS" or
            "HELIOS_CREDITNOTE" or "PROMO_GC" or "NETGROSS" or "PRE_DISCOUNTS" or "SGST/UTGST VALUE" or
            "CSGT VALUE" or "IGST VALUE" or "CESS VALUE" or "TAX" => 0m,
        _ => null
    };

    private sealed class CaptureStore : ITransactionalImportStore
    {
        public ImportPersistencePackage? Package { get; private set; }
        public Task<long> PersistAsync(ImportPersistencePackage package, CancellationToken cancellationToken = default)
        { PersistenceValidation.Validate(package); Package = package; return Task.FromResult(1L); }
    }
}
