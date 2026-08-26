using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Stock;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class StockImportBlockedException(IReadOnlyList<ImportDiagnostic> diagnostics)
    : InvalidOperationException("Stock import was blocked by validation diagnostics.")
{
    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; } = diagnostics;
}

public sealed record StockImportPersistenceOutcome(Guid BatchId,long ImportFileId,string ReportCode,int PersistedRows,IReadOnlyList<ImportDiagnostic> Diagnostics);

public sealed class StockSqlImportOrchestrator(ITransactionalImportStore store)
{
    public async Task<StockImportPersistenceOutcome> PersistAsync(WorkbookSnapshot workbook,int? storeId=null,CancellationToken cancellationToken=default,DateOnly? expectedBusinessDate=null,string? expectedStoreCode=null,string? importedBy=null)
    {
        var parsed=new StockWorkbookParser().Parse(workbook);
        if(parsed.HasBlockers) throw new StockImportBlockedException(parsed.Diagnostics);
        var batchId=Guid.NewGuid();
        var dates=parsed.Movements.Select(x=>x.DocumentDate).Concat(parsed.Snapshots.Select(x=>x.SnapshotDate)).ToArray();
        var stores=parsed.Movements.Select(x=>x.StoreCode).Concat(parsed.Snapshots.Select(x=>x.StoreCode)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(stores.Length>1) throw new InvalidOperationException("A source workbook cannot contain more than one store.");
        var businessDate=dates.Length==0?(DateOnly?)null:dates.Max();
        var scope=R025SqlImportOrchestrator.ValidateScope(stores.SingleOrDefault(),businessDate,expectedStoreCode,expectedBusinessDate);
        var batch=new ImportBatchRegistration(batchId,storeId,dates.Length==0?null:dates.Min(),dates.Length==0?null:dates.Max(),DateTimeOffset.UtcNow);
        var file=new ImportFileRegistration(batchId,null,workbook.FileName,workbook.Sha256,workbook.FileSizeBytes,parsed.ReportCode,scope.StoreCode,scope.BusinessDate,scope.BusinessDate,importedBy??Environment.UserName);
        var movements=parsed.Movements.Select(x=>new StockMovementPersistence(x.StoreCode,x.DocumentNumber,x.DocumentDate.Year,x.DocumentDate,x.ProductCode,x.SourceTransactionType,x.FromLocation,x.ToLocation,x.OpeningQuantity,x.TransactionQuantity,x.ClosingQuantity,new(x.Lineage.SheetName,x.Lineage.SourceRowNumber,parsed.ReportCode))).ToArray();
        var snapshots=parsed.Snapshots.Select(x=>new StockSnapshotPersistence(x.StoreCode,x.SnapshotDate,x.ProductCode,x.Ean,x.BrandCode,null,x.Cluster,x.Gender,x.BatchNumber,x.SourceUid,x.Quantity,x.UnitCost,x.TotalCost,new(x.Lineage.SheetName,x.Lineage.SourceRowNumber,parsed.ReportCode))).ToArray();
        var id=await store.PersistAsync(new(batch,file,[],[],movements,snapshots),cancellationToken);
        return new(batchId,id,parsed.ReportCode,movements.Length+snapshots.Length,parsed.Diagnostics);
    }
}
