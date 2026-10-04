using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

// Review of FIX-10 (R-WLMHW-10): the Open items sync ran the whole live-check summary after every imported
// file, a few hundred times for a month's folder on a PC that powers off under heavy load. A folder or batch
// run now syncs once, after its last file.
public sealed class DataQualitySyncRunTests
{
    [Fact]
    public async Task A_run_syncs_once_at_its_end_however_many_imports_asked()
    {
        var syncs = new List<string>();
        var run = SqlServerImportPersistenceUseCase.DeferDataQualitySync(Record(syncs));
        run.Request("db");
        run.Request("db");
        run.Request("db");
        Assert.Empty(syncs);

        await run.DisposeAsync();

        Assert.Equal(["db"], syncs);
        Assert.Null(SqlServerImportPersistenceUseCase.CurrentDataQualitySyncRun);
    }

    [Fact]
    public async Task A_run_with_no_import_does_not_sync()
    {
        var syncs = new List<string>();
        await SqlServerImportPersistenceUseCase.DeferDataQualitySync(Record(syncs)).DisposeAsync();
        Assert.Empty(syncs);
    }

    [Fact]
    public async Task A_nested_run_leaves_the_sync_to_the_outer_one()
    {
        var syncs = new List<string>();
        var outer = SqlServerImportPersistenceUseCase.DeferDataQualitySync(Record(syncs));
        var inner = SqlServerImportPersistenceUseCase.DeferDataQualitySync(Record(syncs));
        inner.Request("db");

        await inner.DisposeAsync();
        Assert.Empty(syncs);
        Assert.Same(outer, SqlServerImportPersistenceUseCase.CurrentDataQualitySyncRun);
        Assert.True(outer.SyncPending);

        await outer.DisposeAsync();
        Assert.Equal(["db"], syncs);
    }

    [Fact]
    public async Task Every_file_of_a_folder_import_runs_inside_one_deferred_sync_run()
    {
        var persistence = new RunCapturingPersistence();
        var summary = await new FolderImportService(persistence, new Reader(Sales), knownStores: ["HEMW"])
            .RunFilesAsync(["HEMW_R025_20260825.xlsx", "HEMW_R025_20260826.xlsx"], new("sync-run-test"));

        Assert.Equal(2, summary.Imported);
        Assert.Equal(2, persistence.Runs.Count);
        Assert.NotNull(persistence.Runs[0]);
        Assert.Same(persistence.Runs[0], persistence.Runs[1]);
        Assert.Null(SqlServerImportPersistenceUseCase.CurrentDataQualitySyncRun);
    }

    private static Func<string, Task> Record(List<string> syncs) => connection =>
    {
        syncs.Add(connection);
        return Task.CompletedTask;
    };

    private static WorkbookSnapshot Sales(string path)
    {
        var date = path.Contains("0826", StringComparison.Ordinal) ? 20260826 : 20260825;
        var values = new Dictionary<string, object?>
        {
            ["TRANS_TYPE"] = "INV", ["STORE CODE"] = "HEMW", ["ITEMNUMBER"] = "TEST-001", ["INVNUMBER"] = "100000001",
            ["INVDATE"] = date, ["QTY"] = 1m, ["NETAMOUNT"] = 118m, ["NETVALUE"] = 100m, ["TAX"] = 18m
        };
        var row = new WorkbookRow(2, RetailSalesProfiles.R025Headers.Select(header => new WorkbookCell(values.GetValueOrDefault(header))).ToArray());
        return new(path, 1, new string(date == 20260826 ? 'c' : 'd', 64), [new("SDB VariantwiseSales", 1, RetailSalesProfiles.R025Headers, [row])]);
    }

    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }

    // Records the deferred run each import sees; it never asks for a sync, so no database is touched.
    private sealed class RunCapturingPersistence : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public List<SqlServerImportPersistenceUseCase.DataQualitySyncRun?> Runs { get; } = [];
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task<IReadOnlyList<RestatementCandidate>> FindRestatementCandidatesAsync(string report, string store, DateOnly start, DateOnly end,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RestatementCandidate>>([]);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default)
        {
            Runs.Add(SqlServerImportPersistenceUseCase.CurrentDataQualitySyncRun);
            return Task.FromResult(new ImportPersistenceResult(request.AcceptedImport.ProfileIdentity.ReportCode, 1) { Status = "Imported" });
        }
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportRowOutcome(1, 1, 0, 0));
    }
}
