using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class R022SqlImportOrchestrator(ITransactionalImportStore store)
{
    public async Task<long> PersistAsync(WorkbookSnapshot workbook,WorkbookSheet normalizedSheet,R022PersistenceProjectionResult projection,int? storeId=null,string currencyCode="INR",CancellationToken cancellationToken=default,DateOnly? expectedBusinessDate=null,string? expectedStoreCode=null,string? importedBy=null,ImportRestatementRequest? restatement=null)
    {
        ArgumentNullException.ThrowIfNull(workbook);ArgumentNullException.ThrowIfNull(normalizedSheet);ArgumentNullException.ThrowIfNull(projection);
        var dates=projection.InvoiceControls.Select(x=>x.TransactionDate).ToArray();
        var stores=projection.InvoiceControls.Select(x=>x.StoreCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(stores.Length>1) throw new InvalidOperationException("A source workbook cannot contain more than one store.");
        var businessDate=dates.Length==0?(DateOnly?)null:dates.Max();
        var scope=R025SqlImportOrchestrator.ValidateScope(stores.SingleOrDefault(),businessDate,expectedStoreCode,expectedBusinessDate);
        var batchId=Guid.NewGuid();var batch=new ImportBatchRegistration(batchId,storeId,dates.Length==0?null:dates.Min(),dates.Length==0?null:dates.Max(),DateTimeOffset.UtcNow);
        var file=new ImportFileRegistration(batchId,null,workbook.FileName,workbook.Sha256,workbook.FileSizeBytes,"R022",scope.StoreCode,scope.BusinessDate,scope.BusinessDate,importedBy??Environment.UserName);
        var controls=projection.InvoiceControls.Select(x=>new SalesInvoiceControlPersistence(x.StoreCode,x.InvoiceNumber,x.TransactionDate.Year,x.TransactionDate,x.TransactionTypeRaw,x.InvoiceQuantity,x.NetValue,currencyCode,new(normalizedSheet.Name,x.SourceRowNumber,"R022_INVOICE"))).ToArray();
        var tenders=projection.ClassifiedTenders.Concat(projection.QuarantinedTenders).Select(x=>new TenderPersistence(x.StoreCode,x.InvoiceNumber,x.TransactionDate.Year,x.TransactionDate,x.TenderCode,x.SourceAmount,currencyCode,new(normalizedSheet.Name,x.SourceRowNumber,$"R022_TENDER_{x.TenderCode}"),!x.IsQuarantined,x.QuarantineReason)).ToArray();
        var package=new ImportPersistencePackage(batch,file,[],tenders,[],[]){InvoiceControls=controls,Restatement=restatement};
        return await store.PersistAsync(package,cancellationToken);
    }
}
