using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class R022SqlImportOrchestratorTests
{
    [Fact]
    public async Task Invoice_control_and_quarantined_tender_keep_distinct_lineage_reporting_state_and_profile()
    {
        var workbook = Workbook();
        var capture = new Capture();

        await new R022SqlImportOrchestrator(capture).PersistAsync(workbook);

        var package = capture.Package!;
        Assert.Single(package.InvoiceControls);
        Assert.Equal(2, package.Tenders.Count);
        var quarantined = Assert.Single(package.Tenders, row => row.TenderType == "PAYMENTTYPE25");
        Assert.False(quarantined.IsReportingEligible);
        Assert.NotNull(quarantined.ExclusionReason);
        Assert.Equal(3, new[] { package.InvoiceControls[0].Lineage.SourceRecordType }
            .Concat(package.Tenders.Select(row => row.Lineage.SourceRecordType)).Distinct().Count());
        Assert.Equal(RetailSalesProfiles.R022.Identity, package.File.Profile);
    }

    [Fact]
    public void Paymenttype25_cannot_be_accidentally_marked_reporting_eligible()
    {
        var id = Guid.NewGuid();
        var batch = new ImportBatchRegistration(id, null, null, null, DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(
            id, RetailSalesProfiles.R022.Identity, "x.xlsx", new string('d', 64), 1);
        var tender = new TenderPersistence(
            "STORE", "DOC", 2026, new(2026, 8, 25), "PAYMENTTYPE25", 1m, "INR",
            new("Sheet0", 2, "R022_TENDER_PAYMENTTYPE25"));
        Assert.Throws<ArgumentException>(() =>
            PersistenceValidation.Validate(new(batch, file, [], [tender], [], [])));
    }

    private static WorkbookSnapshot Workbook()
    {
        var cells = RetailSalesProfiles.R022Headers.Select(header => new WorkbookCell(header switch
        {
            "TRANS_TYPE" => "INV",
            "STORE CODE" => "STORE",
            "INVNUMBER" => "DOC",
            "InvoiceQuantity" => 1m,
            "INVOICEDATE" => new DateTime(2026, 8, 25),
            "CASH" => 90m,
            "PAYMENTTYPE25" => 10m,
            "NetValue" => 100m,
            _ => null
        })).ToArray();
        return new(
            "sanitized.xlsx",
            1,
            new string('c', 64),
            [new("Sheet0", 1, RetailSalesProfiles.R022Headers, [new(2, cells)])]);
    }

    private sealed class Capture : ITransactionalImportStore
    {
        public ImportPersistencePackage? Package { get; private set; }

        public Task<long> PersistAsync(
            ImportPersistencePackage package,
            CancellationToken cancellationToken = default)
        {
            PersistenceValidation.Validate(package);
            Package = package;
            return Task.FromResult(1L);
        }
    }
}
