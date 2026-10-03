using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class R022SqlImportOrchestratorTests
{
    [Fact]
    public async Task Invoice_control_and_Airpay_tender_keep_distinct_lineage_and_are_reporting_eligible()
    {
        var workbook = Workbook();
        var capture = new Capture();

        await new R022SqlImportOrchestrator(capture).PersistAsync(workbook);

        var package = capture.Package!;
        Assert.Single(package.InvoiceControls);
        Assert.Equal(2, package.Tenders.Count);
        var quarantined = Assert.Single(package.Tenders, row => row.TenderType == "PAYMENTTYPE25");
        Assert.True(quarantined.IsReportingEligible);
        Assert.Null(quarantined.ExclusionReason);
        Assert.Equal(3, new[] { package.InvoiceControls[0].Lineage.SourceRecordType }
            .Concat(package.Tenders.Select(row => row.Lineage.SourceRecordType)).Distinct().Count());
        Assert.Equal(RetailSalesProfiles.R022.Identity, package.File.Profile);
    }

    [Fact]
    public async Task Invoice_year_is_financial_year_of_date_when_INVOICEYEAR_differs()
    {
        // ETP labels a return dated 1 April with the year before; the invoice is keyed by its own date (OD-1, IF-019).
        var workbook = Workbook(new DateTime(2026, 4, 1), invoiceYear: 2026);
        var capture = new Capture();

        await new R022SqlImportOrchestrator(capture).PersistAsync(workbook);

        var package = capture.Package!;
        Assert.Equal(2027, Assert.Single(package.InvoiceControls).InvoiceYear);
        Assert.Equal(2, package.Tenders.Count);
        Assert.All(package.Tenders, tender => Assert.Equal(2027, tender.InvoiceYear));
        var notice = Assert.Single(package.AcceptedImport!.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.InvoiceYearDiffers);
        Assert.Equal(ImportDiagnosticSeverity.Information, notice.Severity);
        Assert.Equal(2, notice.RowNumber);
        Assert.Equal(1, notice.Occurrences);
        Assert.Equal("INVOICEYEAR", notice.ColumnName);

        var labelled = new MatchedImportEnvelopeFactory().Inspect(Workbook(new DateTime(2026, 4, 1), invoiceYear: 2027));
        Assert.DoesNotContain(labelled.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.InvoiceYearDiffers);
    }

    [Fact]
    public void Paymenttype25_is_an_eligible_Airpay_tender()
    {
        var id = Guid.NewGuid();
        var batch = new ImportBatchRegistration(id, null, null, null, DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(
            id, RetailSalesProfiles.R022.Identity, "x.xlsx", new string('d', 64), 1);
        var tender = new TenderPersistence(
            "STORE", "DOC", 2026, new(2026, 8, 25), "PAYMENTTYPE25", 1m, "INR",
            new("Sheet0", 2, "R022_TENDER_PAYMENTTYPE25"));
        PersistenceValidation.Validate(new(batch, file, [], [tender], [], []));
    }

    private static WorkbookSnapshot Workbook(DateTime? date = null, int? invoiceYear = null)
    {
        var cells = RetailSalesProfiles.R022Headers.Select(header => new WorkbookCell(header switch
        {
            "TRANS_TYPE" => "INV",
            "STORE CODE" => "STORE",
            "INVNUMBER" => "DOC",
            "InvoiceQuantity" => 1m,
            "INVOICEDATE" => date ?? new DateTime(2026, 8, 25),
            "INVOICEYEAR" => invoiceYear,
            "CASH" => 90m,
            "PAYMENTTYPE25" => 10m,
            "NetValue" => 100m,
            _ => null
        })).ToArray();
        return new(
            "R022_Revenue_Report.xlsx",
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
