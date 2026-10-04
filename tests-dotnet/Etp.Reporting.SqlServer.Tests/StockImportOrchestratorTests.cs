using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class StockImportOrchestratorTests
{
    private const string Hash="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    [Fact]
    public async Task Valid_snapshot_is_mapped_to_atomic_persistence_package()
    {
        var row=new object?[]{"STORE","Store","Retail","Store","Region","State","City",new DateTime(2026,8,25),"ITEM","HSN","Description","EAN","BR","Cluster","U",3m,10m,30m,null,null};
        var capture=new CaptureStore();var result=await new StockSqlImportOrchestrator(capture).PersistAsync(Book(StockImportProfiles.ClosingStockHeaders,row));
        Assert.Equal("CLOSING_STOCK",result.ReportCode);Assert.Single(capture.Package!.StockSnapshots);Assert.Empty(capture.Package.StockMovements);Assert.Equal(2,capture.Package.StockSnapshots[0].Lineage.SourceRowNumber);
        Assert.Equal(StockImportProfiles.ClosingStock.Identity,capture.Package.File.Profile);
    }
    [Fact]
    public async Task Unknown_type_never_reaches_persistence()
    {
        var row=new object?[]{"UNKNOWN","STORE","Store","ITEM","HSN","BR","Brand","Cluster","U","DOC",new DateTime(2026,8,25),null,"STORE",null,null,1m,-1m,0m,"City","State","Location"};
        var capture=new CaptureStore();var result=await new StockSqlImportOrchestrator(capture).PersistAsync(Book(StockImportProfiles.VariantStockLedgerHeaders,row));Assert.Empty(capture.Package!.StockMovements);Assert.Contains(result.Diagnostics,d=>d.Code=="UNKNOWN_STOCK_TRANSACTION_TYPE");
    }
    [Fact]
    public async Task Ledger_bin_reaches_persistence_without_joining_the_identity()
    {
        // WLMHW FIX-14: LOCATION is stored on the movement (migration 0047). It is not part of the identity, so a
        // RETAILBIN and a DEFECTIVEBIN row of one identity are still numbered as one chain (1.9.3 line_seq rules).
        object?[] Row(string bin,decimal opening)=>["Stock Receipt","STORE","Store","ITEM","HSN","BR","Brand","Cluster","U","DOC",new DateTime(2026,9,7),null,null,null,null,opening,1m,opening+1m,"City","State",bin];
        var book=new WorkbookSnapshot("sanitized.xlsx",10,Hash,[new("Sheet0",1,StockImportProfiles.VariantStockLedgerHeaders,
            [new(2,Row("DEFECTIVEBIN",1m).Select(x=>new WorkbookCell(x)).ToArray()),new(3,Row("RETAILBIN",0m).Select(x=>new WorkbookCell(x)).ToArray())])]);
        var capture=new CaptureStore();
        await new StockSqlImportOrchestrator(capture).PersistAsync(book);
        var movements=capture.Package!.StockMovements;
        Assert.Equal("DEFECTIVEBIN|RETAILBIN",string.Join("|",movements.Select(x=>x.Location)));
        Assert.Equal([2,1],movements.Select(x=>x.LineSeq).ToArray());
    }
    private static WorkbookSnapshot Book(IReadOnlyList<string> headers,IReadOnlyList<object?> values)=>new("sanitized.xlsx",10,Hash,[new("Sheet0",1,headers,[new(2,values.Select(x=>new WorkbookCell(x)).ToArray())])]);
    private sealed class CaptureStore:ITransactionalImportStore{public ImportPersistencePackage? Package{get;private set;}public Task<long> PersistAsync(ImportPersistencePackage package,CancellationToken cancellationToken=default){Package=package;return Task.FromResult(42L);}}
}
