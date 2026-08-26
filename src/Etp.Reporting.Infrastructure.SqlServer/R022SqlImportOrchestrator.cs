using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class R022SqlImportOrchestrator(ITransactionalImportStore store)
{
    public async Task<long> PersistAsync(WorkbookSnapshot workbook,WorkbookSheet normalizedSheet,R022PersistenceProjectionResult projection,int? storeId=null,string currencyCode="INR",CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(workbook);ArgumentNullException.ThrowIfNull(normalizedSheet);ArgumentNullException.ThrowIfNull(projection);
        var dates=projection.InvoiceControls.Select(x=>x.TransactionDate).ToArray();
        var batchId=Guid.NewGuid();var batch=new ImportBatchRegistration(batchId,storeId,dates.Length==0?null:dates.Min(),dates.Length==0?null:dates.Max(),DateTimeOffset.UtcNow);
        var file=new ImportFileRegistration(batchId,null,workbook.FileName,workbook.Sha256,workbook.FileSizeBytes);
        var controls=projection.InvoiceControls.Select(x=>new SalesInvoiceControlPersistence(x.StoreCode,x.InvoiceNumber,x.TransactionDate.Year,x.TransactionDate,x.TransactionTypeRaw,x.InvoiceQuantity,x.NetValue,currencyCode,new(normalizedSheet.Name,x.SourceRowNumber,"R022_INVOICE"))).ToArray();
        var tenders=projection.ClassifiedTenders.Concat(projection.QuarantinedTenders).Select(x=>new TenderPersistence(x.StoreCode,x.InvoiceNumber,x.TransactionDate.Year,x.TransactionDate,x.TenderCode,x.SourceAmount,currencyCode,new(normalizedSheet.Name,x.SourceRowNumber,$"R022_TENDER_{x.TenderCode}"),!x.IsQuarantined,x.QuarantineReason)).ToArray();
        var package=new ImportPersistencePackage(batch,file,[],tenders,[],[]){InvoiceControls=controls};
        return await store.PersistAsync(package,cancellationToken);
    }
}
