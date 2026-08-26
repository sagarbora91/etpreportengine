using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class R022PersistenceProjectionTests
{
    [Fact]
    public void Projects_invoice_control_and_signed_normalized_tenders_with_paymenttype25_quarantined()
    {
        var values = RetailSalesProfiles.R022Headers.Select(h => (object?)(h switch
        {
            "TRANS_TYPE" => "RETURN", "STORE CODE" => "STORE-1", "INVNUMBER" => "INV-1",
            "InvoiceQuantity" => "-1", "INVOICEDATE" => "2026-08-25", "NetValue" => "-100.25",
            "CASH" => "-40.25", "CARD" => "-60.00", "PAYMENTTYPE25" => "-5.00",
            "CUSTOMERNAME" or "ContactNo" => "PRIVATE", _ => "0"
        })).Select(x => new WorkbookCell(x)).ToArray();
        var sheet = new WorkbookSheet("Sheet0", 1, RetailSalesProfiles.R022Headers, [new WorkbookRow(2, values)]);

        var staged = new ImportRowStager().Stage(sheet, RetailSalesProfiles.R022);
        Assert.True(staged.CanPersist);
        Assert.DoesNotContain(staged.Rows[0].Values.Keys, x => x.Contains("customer", StringComparison.OrdinalIgnoreCase) || x.Contains("contact", StringComparison.OrdinalIgnoreCase));

        var result = new R022PersistenceProjector().Project(staged.Rows);
        var invoice = Assert.Single(result.InvoiceControls);
        Assert.Equal(-100.25m, invoice.NetValue);
        Assert.Equal(-1m, invoice.InvoiceQuantity);
        Assert.Equal(2, result.ClassifiedTenders.Count);
        Assert.Contains(result.ClassifiedTenders, x => x.TenderCode == "CASH" && x.SourceAmount == -40.25m);
        var quarantined = Assert.Single(result.QuarantinedTenders);
        Assert.Equal("PAYMENTTYPE25", quarantined.TenderCode);
        Assert.Equal(-5m, quarantined.SourceAmount);
        Assert.Equal("UNRESOLVED_PAYMENTTYPE25", quarantined.QuarantineReason);
    }

    [Fact]
    public void Omits_zero_and_blank_tenders_without_changing_invoice_control()
    {
        var staged = new StagedImportRow(7, new Dictionary<string, object?>
        {
            ["store_code"] = "STORE-1", ["invoice_number"] = "INV-2", ["transaction_date"] = new DateOnly(2026, 8, 25),
            ["source_transaction_type"] = "SALE", ["source_invoice_quantity"] = 1m, ["source_net_value"] = 10m,
            ["tender_cash"] = 0m, ["tender_card"] = null
        });
        var result = new R022PersistenceProjector().Project([staged]);
        Assert.Single(result.InvoiceControls); Assert.Empty(result.ClassifiedTenders); Assert.Empty(result.QuarantinedTenders);
    }
}
