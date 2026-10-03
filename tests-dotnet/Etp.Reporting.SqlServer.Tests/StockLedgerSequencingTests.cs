using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

// The stock-ledger orchestrator passes StockUnitSequencer's line_seq to persistence (IF-018).
public sealed class StockLedgerSequencingTests
{
    [Fact]
    public async Task Ledger_rows_carry_chain_line_seq_and_repeats_warn()
    {
        var capture = new CaptureStore();
        var outcome = await new StockSqlImportOrchestrator(capture).PersistAsync(Ledger(
            Unit("UNIT-DOC", 3m, 1m), Unit("UNIT-DOC", 0m, 1m), Unit("UNIT-DOC", 3m, 1m), Unit("UNIT-DOC", 1m, 1m), Unit("OTHER-DOC", 5m, -1m)));

        Assert.Equal([3, 1, 4, 2, 1], capture.Package!.StockMovements.Select(movement => movement.LineSeq));
        var warning = Assert.Single(outcome.Warnings);
        Assert.Equal(ImportCodes.StockRowRepeated, warning.Code);
        Assert.Equal(2, warning.Occurrences);
        Assert.Equal(2, warning.RowNumber);
        Assert.Contains(outcome.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.StockRowRepeated);
    }

    [Fact]
    public async Task Persistence_warnings_reach_the_file_result()
    {
        var issue = new ImportIssue(ImportIssueSeverity.Warning, ImportCodes.StockRowRepeated, "2 identical stock-ledger rows were all kept.", 2,
            DocumentRef: "UNIT-DOC 2026-08-25 ITEM", Occurrences: 2);
        var service = new FolderImportService(new WarningPersistence([issue]), new Reader(_ => Ledger(Unit("UNIT-DOC", 0m, 1m))), knownStores: ["STORE"]);

        var result = Assert.Single((await service.RunFilesAsync(["R030_StockLedger.xlsx"], new("automation-test"))).Files);

        Assert.Equal("Imported", result.Status);
        Assert.Contains(issue, result.Diagnostics!);
    }

    private static object?[] Unit(string document, decimal opening, decimal transaction) =>
        ["STM Receipt", "STORE", "Store", "ITEM", "HSN", "BR", "Brand", "Cluster", "U", document, new DateTime(2026, 8, 25), null, "STORE",
         null, null, opening, transaction, opening + transaction, "City", "State", "Location"];

    private static WorkbookSnapshot Ledger(params object?[][] rows) =>
        new("ledger.xlsx", 10, new string('c', 64), [new("Sheet0", 1, StockImportProfiles.VariantStockLedgerHeaders,
            rows.Select((values, index) => new WorkbookRow(index + 2, values.Select(value => new WorkbookCell(value)).ToArray())).ToArray())]);

    private sealed class CaptureStore : ITransactionalImportStore
    {
        public ImportPersistencePackage? Package { get; private set; }
        public Task<long> PersistAsync(ImportPersistencePackage package, CancellationToken cancellationToken = default)
        { Package = package; return Task.FromResult(42L); }
    }

    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }

    private sealed class WarningPersistence(IReadOnlyList<ImportIssue> issues) : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportPersistenceResult(request.AcceptedImport.ProfileIdentity.ReportCode, request.AcceptedImport.Staging.Rows.Count) { Issues = issues });
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(new ImportRowOutcome(1, 1, 0, 0));
    }
}
