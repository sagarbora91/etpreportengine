using Etp.Reporting.Import.Preflight;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class EtpFamilySqlImportOrchestrator(ITransactionalImportStore store)
{
    public async Task<long> PersistAsync(MatchedImportEnvelope accepted, DateOnly? expectedBusinessDate=null,
        string? expectedStoreCode=null,string? importedBy=null,ImportRestatementRequest? restatement=null,
        CancellationToken cancellationToken=default)
    {
        var scope=R025SqlImportOrchestrator.ValidateScope(accepted.Scope.StoreCode,accepted.Scope.PeriodEnd,
            expectedStoreCode,expectedBusinessDate);
        var start=accepted.Scope.PeriodStart??scope.BusinessDate;
        var batch=Guid.NewGuid();
        var snapshots=accepted.ProfileIdentity.ReportCode=="R010"
            ? accepted.Staging.Rows.Select(row=>
            {
                var v=row.Values;
                string? Text(string key)=>v.GetValueOrDefault(key) as string;
                decimal? Number(string key)=>v.GetValueOrDefault(key) is decimal value?value:null;
                return new StockSnapshotPersistence(scope.StoreCode!,scope.BusinessDate!.Value,Text("itemnumber")!,null,
                    Text("brand"),Text("brandname"),Text("cluster"),Text("gender"),Text("lotnumber"),Text("uid"),
                    Number("closingbalance")??0,Number("ucp"),Number("totalucp"),
                    new(accepted.MatchedSheet.Name,row.SourceRowNumber,"R010_SNAPSHOT"));
            }).ToArray() : [];
        return await store.PersistAsync(new ImportPersistencePackage(
            new(batch,null,start,scope.BusinessDate,DateTimeOffset.UtcNow),
            new(batch,accepted.ProfileIdentity,accepted.Workbook.FileName,accepted.Workbook.Sha256,accepted.Workbook.FileSizeBytes,
                accepted.ProfileIdentity.ReportCode,scope.StoreCode,scope.BusinessDate,scope.BusinessDate,
                importedBy??Environment.UserName,start,scope.BusinessDate),[],[],[],snapshots)
            {AcceptedImport=accepted,Restatement=restatement},cancellationToken);
    }
}
