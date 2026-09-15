using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Stock;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class StockWorkbookParserTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("INV", -1)]
    [InlineData("SR", 1)]
    [InlineData("BC", 1)]
    [InlineData("Purchase Return", -1)]
    [InlineData("Purchase Receipt", 1)]
    [InlineData("STM Issue", -1)]
    [InlineData("STM Receipt", 1)]
    [InlineData("STM Dispatch", -1)]
    [InlineData("Stock Issue", -1)]
    [InlineData("Stock Receipt", 1)]
    public void Every_approved_stock_type_stages_and_preserves_signed_movements_including_cancellation_receipts(string type, int quantity)
    {
        var signedQuantity = (decimal)quantity;
        var values = new object?[] { type, "STORE", "Sample Store", "ITEM", "HSN", "BR", "Sample Brand", "Cluster", "U",
            "DOC", new DateTime(2026, 8, 25), null, "RETAILBIN", null, null, 8m, signedQuantity, 8m + signedQuantity, "City", "State", "Location" };
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(Book(StockImportProfiles.VariantStockLedgerHeaders, values));
        var staged = Assert.Single(accepted.Staging.Rows);
        Assert.Equal(signedQuantity, staged.Values["transaction_quantity"]);
        var result = new StockWorkbookParser().Parse(accepted);
        Assert.False(result.HasBlockers);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "UNKNOWN_STOCK_TRANSACTION_TYPE");
        var movement = Assert.Single(result.Movements);
        Assert.Equal(type, movement.SourceTransactionType);
        Assert.Equal(signedQuantity, movement.TransactionQuantity);
        Assert.Equal(8m + signedQuantity, movement.ClosingQuantity);
    }

    [Fact]
    public void Ledger_profile_preserves_source_sign_and_exact_lineage()
    {
        var values = new object?[] { "INV", "STORE", "Store", "ITEM", "HSN", "BR", "Brand", "Cluster", "U", "DOC", new DateTime(2026,8,25), null, "STORE", null, null, 8m, -1m, 7m, "City", "State", "Location" };
        var result = new StockWorkbookParser().Parse(Book(StockImportProfiles.VariantStockLedgerHeaders, values, 7));
        var movement = Assert.Single(result.Movements);
        Assert.False(result.HasBlockers);
        Assert.Equal(-1m, movement.TransactionQuantity);
        Assert.Equal(7, movement.Lineage.SourceRowNumber);
        Assert.Equal(Hash, movement.Lineage.FileSha256);
    }

    [Fact]
    public void Unknown_ledger_transaction_type_warns_and_skips_the_row()
    {
        var values = new object?[] { "NEW TYPE", "STORE", "Store", "ITEM", "HSN", "BR", "Brand", "Cluster", "U", "DOC", new DateTime(2026,8,25), null, "STORE", null, null, 1m, -1m, 0m, "City", "State", "Location" };
        var result = new StockWorkbookParser().Parse(Book(StockImportProfiles.VariantStockLedgerHeaders, values));
        Assert.False(result.HasBlockers);
        Assert.Empty(result.Movements);
        Assert.Contains(result.Diagnostics,x=>x.Code=="UNKNOWN_STOCK_TRANSACTION_TYPE" && x.Severity==ImportDiagnosticSeverity.Warning);
    }

    [Fact]
    public void Closing_stock_duplicate_horizontal_block_is_collapsed_once()
    {
        var one = new object?[] { "STORE", "Store", "Retail", "Store", "Region", "State", "City", new DateTime(2026,8,25), "ITEM", "HSN", "Description", "EAN", "BR", "Cluster", "U", 3m, 10m, 30m, null, null };
        var result = new StockWorkbookParser().Parse(Book(StockImportProfiles.ClosingStockHeaders.Concat(StockImportProfiles.ClosingStockHeaders).ToArray(), one.Concat(one).ToArray()));
        Assert.False(result.HasBlockers);
        Assert.Single(result.Snapshots);
        Assert.Contains(result.Diagnostics,x=>x.Code=="REPEATED_LAYOUT_COLLAPSED");
    }

    [Fact]
    public void Changed_header_is_not_guessed()
    {
        var headers=StockImportProfiles.ClosingStockHeaders.ToArray();headers[15]="AVAILABLE QTY";
        var result=new StockWorkbookParser().Parse(Book(headers,new object?[headers.Length]));
        Assert.True(result.HasBlockers);Assert.Equal("UNKNOWN",result.ReportCode);
    }

    private static WorkbookSnapshot Book(IReadOnlyList<string> headers,IReadOnlyList<object?> values,int row=2)=>new("sanitized.xlsx",10,Hash,[new("Sheet0",1,headers,[new(row,values.Select(x=>new WorkbookCell(x)).ToArray())])]);
}
