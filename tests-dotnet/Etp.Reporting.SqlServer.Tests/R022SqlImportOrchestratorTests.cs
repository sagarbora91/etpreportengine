using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class R022SqlImportOrchestratorTests
{
    [Fact]
    public async Task Invoice_control_and_quarantined_tender_keep_distinct_lineage_and_reporting_state()
    {
        var hash=new string('c',64);var sheet=new WorkbookSheet("Sheet0",1,[],[]);var workbook=new WorkbookSnapshot("sanitized.xlsx",1,hash,[sheet]);
        var projection=new R022PersistenceProjectionResult(
            [new(2,"STORE","DOC",new(2026,8,25),"INV",1m,100m)],
            [new(2,"STORE","DOC",new(2026,8,25),"CASH",90m,false,null)],
            [new(2,"STORE","DOC",new(2026,8,25),"PAYMENTTYPE25",10m,true,"UNRESOLVED_PAYMENTTYPE25")]);
        var capture=new Capture();await new R022SqlImportOrchestrator(capture).PersistAsync(workbook,sheet,projection);
        var package=capture.Package!;Assert.Single(package.InvoiceControls);Assert.Equal(2,package.Tenders.Count);
        var quarantined=Assert.Single(package.Tenders,x=>x.TenderType=="PAYMENTTYPE25");Assert.False(quarantined.IsReportingEligible);Assert.NotNull(quarantined.ExclusionReason);
        Assert.Equal(3,new[]{package.InvoiceControls[0].Lineage.SourceRecordType}.Concat(package.Tenders.Select(x=>x.Lineage.SourceRecordType)).Distinct().Count());
    }
    [Fact]
    public void Paymenttype25_cannot_be_accidentally_marked_reporting_eligible()
    {
        var id=Guid.NewGuid();var batch=new ImportBatchRegistration(id,null,null,null,DateTimeOffset.UtcNow);var file=new ImportFileRegistration(id,null,"x.xlsx",new string('d',64),1);
        var tender=new TenderPersistence("STORE","DOC",2026,new(2026,8,25),"PAYMENTTYPE25",1m,"INR",new("Sheet0",2,"R022_TENDER_PAYMENTTYPE25"));
        Assert.Throws<ArgumentException>(()=>PersistenceValidation.Validate(new(batch,file,[],[tender],[],[])));
    }
    private sealed class Capture:ITransactionalImportStore{public ImportPersistencePackage? Package{get;private set;}public Task<long> PersistAsync(ImportPersistencePackage package,CancellationToken cancellationToken=default){PersistenceValidation.Validate(package);Package=package;return Task.FromResult(1L);}}
}
