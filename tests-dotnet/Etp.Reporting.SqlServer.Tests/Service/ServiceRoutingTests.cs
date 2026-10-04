using Etp.Reporting.Application.Imports;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Service;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// The Service interim rules of the folder import (lane L3, decision 15) that need no Service catalogue entry: the
/// Not needed gate by file name, the gate by matched family, the AW330 store fallback, the Snapshot History check and
/// their messages. Retail files must route exactly as before. Synthetic workbooks only.
/// </summary>
public sealed class ServiceRoutingTests
{
    [Theory]
    [InlineData("S001_RepairRegister.xlsx", "S001", ImportCodes.FamilyDerived)]
    [InlineData("S005_Tender_Collection_SUMMARY.xlsx", "S005", "SERVICE_FAMILY_NOT_NEEDED")]
    [InlineData("S038_SRNReport.xlsx", "S038", "SERVICE_FAMILY_NOT_NEEDED")]
    [InlineData("S027_TATReport.xlsx", "S027", "SERVICE_FAMILY_DEFERRED")]
    [InlineData("S028_TechnicianProductivityReport.xlsx", "S028", "SERVICE_FAMILY_DEFERRED")]
    [InlineData("S041_SomethingNew.xlsx", "S041", "SERVICE_FAMILY_NOT_NEEDED")]
    public async Task A_service_code_the_interim_does_not_land_is_not_needed_before_matching(string fileName, string code, string reason)
    {
        // The headers match a family the importer accepts (here R025), as S038's match S011's: the name decides first.
        var persistence = new CapturePersistence();
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW")))
            .RunFilesAsync([fileName], new("tester"));

        var file = Assert.Single(summary.Files);
        Assert.Equal("Not needed", file.Status);
        Assert.Equal(code, file.ReportCode);
        Assert.Null(file.StoreCode);
        Assert.Null(file.PeriodEnd);
        Assert.Null(file.Failure);
        Assert.Equal(EvidenceState.NotAttempted, file.Evidence);
        var issue = Assert.Single(file.Diagnostics!);
        Assert.Equal(reason, issue.Code);
        Assert.Equal(ImportIssueSeverity.Information, issue.Severity);
        Assert.Equal(issue.Message, file.Message);
        Assert.Empty(persistence.Requests);
        Assert.Equal(0, summary.Failed);
    }

    [Theory]
    [InlineData("S002_JobReportBooking.xlsx")]
    [InlineData("S009_PendingRepair.xlsx")]
    [InlineData("S040_WRA_Claim.xlsx")]
    public void An_importable_service_code_passes_the_name_gate(string fileName) =>
        Assert.Null(ServiceRouting.NotNeeded(fileName, (string?)null));

    [Theory]
    [InlineData("R025_VariantwiseSales.xlsx")]
    [InlineData("HEMW_R025_S001_20260825.xlsx")]
    [InlineData("00_Service_Centre_Consolidation_Control.xlsx")]
    [InlineData("PENDING REPORT 03.10.2026.csv")]
    public void Names_with_a_retail_code_or_no_service_code_are_left_to_the_other_rules(string fileName) =>
        Assert.Null(ServiceRouting.NotNeeded(fileName, (string?)null));

    [Fact]
    public async Task A_retail_file_whose_name_also_holds_an_s_code_imports_as_before()
    {
        var persistence = new CapturePersistence();
        var file = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW")))
            .RunFilesAsync(["HEMW_R025_S001_20260825.xlsx"], new("tester"))).Files);
        Assert.Equal("Imported", file.Status);
        Assert.Equal("R025", file.ReportCode);
        Assert.Single(persistence.Requests);
    }

    [Theory]
    [InlineData("S005", "TENDER COLLECTIN SUMMARY 06.10.2026 TO 09.10.2026.csv", "SERVICE_FAMILY_NOT_NEEDED")]
    [InlineData("S027", "TATA REPORT 03.09.2026 TO 03.10.2026.xlsx", "SERVICE_FAMILY_DEFERRED")]
    [InlineData("S028", "TECHNICIAN PRODUCIVITY REPORT 06.10.2026 TO 09.10.2026.xlsx", "SERVICE_FAMILY_DEFERRED")]
    [InlineData("S001", "R R REGISTER 06.10.2026.csv", ImportCodes.FamilyDerived)]
    public void A_raw_file_matched_to_a_service_family_the_interim_does_not_land_is_not_needed_after_matching(
        string code, string fileName, string reason)
    {
        var skipped = ServiceRouting.NotNeeded(fileName, ServiceFamily(code));
        Assert.NotNull(skipped);
        Assert.Equal(code, skipped.ReportCode);
        Assert.Equal(reason, skipped.Code);
    }

    [Fact]
    public void A_raw_file_matched_to_an_importable_service_family_or_a_retail_family_is_not_skipped()
    {
        Assert.Null(ServiceRouting.NotNeeded("JOB REPORT 06.10.2026 TO 09.10.2026.csv", ServiceFamily("S002")));
        Assert.Null(ServiceRouting.NotNeeded("PENDING REPORT 09.10.2026.csv", ServiceFamily("S009")));
        Assert.Null(ServiceRouting.NotNeeded("R025_VariantwiseSales.xlsx", EtpReportFamilyRegistry.Resolve("R025")));
        Assert.Null(ServiceRouting.NotNeeded("anything.xlsx", (EtpReportFamily?)null));
    }

    [Fact]
    public void Only_service_families_fall_back_to_the_service_centre_store()
    {
        Assert.Equal("AW330", ServiceInterimFamilies.ServiceStoreCode);
        Assert.Equal(ServiceInterimFamilies.ServiceStoreCode, ImportScope.ServiceStoreFallback(ServiceFamily("S011")));
        Assert.Equal(ServiceInterimFamilies.ServiceStoreCode, ImportScope.ServiceStoreFallback(ServiceFamily("S013")));
        Assert.Null(ImportScope.ServiceStoreFallback(EtpReportFamilyRegistry.Resolve("R025")));
        Assert.Null(ImportScope.ServiceStoreFallback("R025"));
        Assert.Null(ImportScope.ServiceStoreFallback("CLOSING_STOCK"));
        Assert.Null(ImportScope.ServiceStoreFallback("UNKNOWN_CODE"));
        Assert.Null(ImportScope.ServiceStoreFallback((string?)null));
        Assert.False(ServiceRouting.IsService("R025"));
        Assert.False(ServiceRouting.IsService(null));
    }

    [Fact]
    public async Task A_retail_file_with_no_store_still_fails_scope_not_detected()
    {
        var persistence = new CapturePersistence();
        var file = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, "STORE3", dated: false)), knownStores: [])
            .RunFilesAsync(["STORE3_R025_20260924.xlsx"], new("tester"))).Files);
        Assert.Equal("Failed", file.Status);
        Assert.Equal("SCOPE_NOT_DETECTED", file.Failure!.Code);
        Assert.DoesNotContain(file.Diagnostics ?? [], issue => issue.Code == ServiceInterimFamilies.Codes.ServiceStoreDefaulted);
        Assert.Empty(persistence.Requests);
    }

    [Fact]
    public void A_snapshot_history_date_that_differs_from_the_import_date_is_a_warning_only()
    {
        var workbook = HistoryWorkbook(new(2026, 9, 21), new(2026, 9, 28));

        var issue = ServiceRouting.HistoryDateDiffers(workbook, new(2026, 9, 29));
        Assert.NotNull(issue);
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceSnapshotDateDiffersFromHistory, issue.Code);
        Assert.Equal(ImportIssueSeverity.Warning, issue.Severity);
        Assert.Contains("2026-09-28", issue.Message);
        Assert.Null(ServiceRouting.HistoryDateDiffers(workbook, new(2026, 9, 28)));
        Assert.Null(ServiceRouting.HistoryDateDiffers(Sales("S002_JobReportBooking.xlsx", "AW330"), new(2026, 9, 28)));
    }

    [Fact]
    public void The_messages_are_short_owner_english_and_survive_into_history()
    {
        foreach (var (code, message) in new[]
        {
            (ImportCodes.FamilyDerived, ServiceRouting.DerivedMessage),
            (ServiceInterimFamilies.Codes.ServiceFamilyNotNeeded, ServiceRouting.NotNeededMessage),
            (ServiceInterimFamilies.Codes.ServiceFamilyDeferred, ServiceRouting.DeferredMessage),
            (ServiceInterimFamilies.Codes.ServiceStoreDefaulted, ServiceRouting.StoreDefaultedMessage),
            (ServiceInterimFamilies.Codes.ServiceSnapshotDateDiffersFromHistory, ServiceRouting.HistoryDiffersTemplate)
        })
        {
            Assert.True(ImportDiagnosticCatalogue.IsKnown(code), code);
            Assert.Equal(message, ImportDiagnosticCatalogue.SafeMessage(code, message));
            Assert.True(message.Length <= 160, code);
        }
        // The date fix names an example folder with its year, so it is kept as the refusal's own text (an unknown code).
        Assert.False(ImportDiagnosticCatalogue.IsKnown(ServiceInterimFamilies.Codes.ServiceSnapshotDateNeeded));
        Assert.Equal(ServiceRouting.DateNeededMessage,
            ImportDiagnosticCatalogue.SafeFailureMessage(ServiceInterimFamilies.Codes.ServiceSnapshotDateNeeded, ServiceRouting.DateNeededMessage, null));
        Assert.Contains("folder whose name ends with the date", ServiceRouting.DateNeededMessage);
        Assert.Contains("The Import screen's date is only for a restatement.", ServiceRouting.DateNeededMessage);
        Assert.DoesNotContain("set the snapshot date on the Import screen", ServiceRouting.DateNeededMessage);
    }

    [Fact]
    public void Automation_packs_only_the_dates_of_retail_imports()
    {
        // A synthetic catalogue stands in for L1's S entries: S009 and S002 are Service, R025 is the shipped Retail family.
        EtpReportFamily[] families = [ServiceFamily("S009"), ServiceFamily("S002"), EtpReportFamilyRegistry.Resolve("R025")];
        var retail = new DateOnly(2026, 9, 27);
        var service = new DateOnly(2026, 9, 28);
        var batch = new FolderImportSummary([new("R025.xlsx", "R025", "HEMW", retail, retail, "Imported"),
            new("unknown.xlsx", null, null, retail.AddDays(-1), retail.AddDays(-1), "Imported"),
            new("S009_PendingRepair.xlsx", "S009", "AW330", service, service, "Imported"),
            new("S002_JobReportBooking.xlsx", "S002", "AW330", service, service, "Imported")]);

        Assert.Equal(new[] { retail, retail.AddDays(-1) }, AutomatedOperationsService.ImportedDates(batch, families));
        Assert.Empty(AutomatedOperationsService.ImportedDates(new FolderImportSummary(batch.Files.Skip(2).ToArray()), families));
        Assert.True(ServiceRouting.IsService("S009", families));
        Assert.False(ServiceRouting.IsService("R025", families));
    }

    [Fact]
    public void Siblings_lend_a_store_or_date_only_within_their_business_unit()
    {
        var day = new DateOnly(2026, 9, 28);
        var folder = Path.Combine("In", "Mixed till 28 sep 2026");
        var detected = new Dictionary<string, (ImportScope Scope, bool Service)>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(folder, "HEMW_R025.xlsx")] = (new ImportScope("HEMW", day.AddDays(-1), day.AddDays(-1)), false),
            [Path.Combine(folder, "S002_JobReportBooking.xlsx")] = (new ImportScope("AW330", day, day), true),
            [Path.Combine("In", "Other", "S009_PendingRepair.xlsx")] = (new ImportScope("AW330", day.AddDays(7), day.AddDays(7)), true)
        };

        var retail = ServiceRouting.Siblings(detected, Path.Combine(folder, "R023_Stock.xlsx"), service: false);
        Assert.Equal("HEMW", Assert.Single(retail).StoreCode);
        var service = ServiceRouting.Siblings(detected, Path.Combine(folder, "S011_Undated.xlsx"), service: true);
        var lent = Assert.Single(service);
        Assert.Equal("AW330", lent.StoreCode);
        Assert.Equal(day, lent.PeriodEnd);
        Assert.Empty(ServiceRouting.Siblings(detected, Path.Combine("In", "Retail only", "R023_Stock.xlsx"), service: true));
    }

    private static EtpReportFamily ServiceFamily(string code) =>
        new(code, code, "Synthetic " + code, false, "etp_landing_" + code.ToLowerInvariant(), null, ["JOB NO"],
            [new("JOB NO", "jobno", CanonicalDataType.Text, false)])
        { BusinessUnit = BusinessUnit.Service };

    private static WorkbookSnapshot HistoryWorkbook(params DateOnly[] snapshots)
    {
        var data = new WorkbookSheet("Data", 1, ["JOB NO"], [new(2, [new WorkbookCell("JOAW330SYN0001")])]);
        var rows = snapshots.Select((date, index) => new WorkbookRow(index + 2,
            [new WorkbookCell("JOAW330SYN000" + (index + 1)), new WorkbookCell(date.ToDateTime(TimeOnly.MinValue)),
             new WorkbookCell($"PENDING REPORT {date:dd.MM.yyyy}.csv")])).ToArray();
        var history = new WorkbookSheet("Snapshot History", 1, ["JOB NO", "Snapshot_As_Of", "SourceFile"], rows);
        return new("S009_PendingRepair.xlsx", 1, new string('d', 64), [data, history]);
    }

    private static WorkbookSnapshot Sales(string path, string store, bool dated = true)
    {
        var values = new Dictionary<string, object?>
        {
            ["TRANS_TYPE"] = "INV", ["STORE CODE"] = store, ["ITEMNUMBER"] = "TEST-001", ["INVNUMBER"] = "100000001",
            ["INVDATE"] = 20260825, ["QTY"] = 1m, ["NETAMOUNT"] = 118m, ["NETVALUE"] = 100m, ["TAX"] = 18m
        };
        WorkbookRow[] rows = dated ? [new(2, RetailSalesProfiles.R025Headers.Select(header => new WorkbookCell(values.GetValueOrDefault(header))).ToArray())] : [];
        return new(path, 1, new string('b', 64), [new("SDB VariantwiseSales", 1, RetailSalesProfiles.R025Headers, rows)]);
    }

    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }

    private sealed class CapturePersistence : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public List<ImportPersistenceRequest<MatchedImportEnvelope>> Requests { get; } = [];
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task<IReadOnlyList<RestatementCandidate>> FindRestatementCandidatesAsync(string report, string store, DateOnly start, DateOnly end,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RestatementCandidate>>([]);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ImportPersistenceResult(request.AcceptedImport.ProfileIdentity.ReportCode, request.AcceptedImport.Staging.Rows.Count));
        }
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportRowOutcome(0, 0, 0, 0));
    }
}
