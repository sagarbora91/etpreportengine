using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Audit;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Audit;
using Etp.Reporting.TestSupport;
using Store = Etp.Reporting.Infrastructure.SqlServer.SqlServerTransactionalImportStore;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>Planner 1's file decision (design 5.3, 12.2 PlannerOnePlanRulesTests).</summary>
public sealed class PlannerOnePlanRulesTests
{
    private static readonly DateOnly July1 = new(2026, 7, 1), July31 = new(2026, 7, 31), August31 = new(2026, 8, 31);
    private const string Hash = "1111111111111111111111111111111111111111111111111111111111111111";

    private static Store.PreviousFile File(long id, DateOnly start, DateOnly end, IEnumerable<string> keys, int version = 1, string hash = Hash) =>
        new(id, hash, start, end, new DateTime(2026, 8, 1), keys.ToHashSet(StringComparer.Ordinal), version);

    private static string Key(char c, int n = 1) => new string(c, 64) + ":" + n;

    [Fact]
    public void An_exact_subset_is_duplicate_content()
    {
        Assert.True(PlannerOnePlanRules.Decide(July1, July31, Hash, [Key('a')], [File(1, July1, July31, [Key('a'), Key('b')])], null));
    }

    [Fact]
    public void A_superset_covering_both_files_promotes_over_them()
    {
        var previous = new[] { File(1, July1, July1, [Key('a')]), File(2, July31, July31, [Key('b')]) };

        Assert.False(PlannerOnePlanRules.Decide(July1, August31, Hash, [Key('a'), Key('b'), Key('c')], previous, null));
    }

    [Fact]
    public void An_overlap_that_does_not_cover_is_already_present()
    {
        var refusal = Assert.Throws<ImportSourceException>(() =>
            PlannerOnePlanRules.Decide(July31, August31, Hash, [Key('a'), Key('c')], [File(1, July1, July31, [Key('a'), Key('b')])], null));

        Assert.Equal("IMPORT_PERIOD_ALREADY_PRESENT", refusal.Code);
        Assert.Equal(FailureStage.Plan, refusal.Stage);
    }

    [Fact]
    public void A_covering_file_that_drops_a_row_is_already_present()
    {
        var refusal = Assert.Throws<ImportSourceException>(() =>
            PlannerOnePlanRules.Decide(July1, August31, Hash, [Key('a'), Key('c')], [File(1, July1, July31, [Key('a'), Key('b')])], null));

        Assert.Equal("IMPORT_PERIOD_ALREADY_PRESENT", refusal.Code);
    }

    [Fact]
    public void A_v0_file_with_another_hash_needs_a_legacy_restatement()
    {
        var refusal = Assert.Throws<ImportSourceException>(() =>
            PlannerOnePlanRules.Decide(July1, August31, Hash, [Key('a'), Key('b')], [File(1, July1, July31, [Key('a')], version: 0, hash: new string('2', 64))], null));

        Assert.Equal("IMPORT_LEGACY_RESTATEMENT_REQUIRED", refusal.Code);
    }

    [Fact]
    public void An_explicit_restatement_target_may_change_its_rows()
    {
        Assert.False(PlannerOnePlanRules.Decide(July1, July31, Hash, [Key('z')], [File(7, July1, July31, [Key('a')])], 7));
    }

    [Fact]
    public void A_restatement_that_partly_overlaps_another_import_names_it()
    {
        var refusal = Assert.Throws<ImportSourceException>(() => PlannerOnePlanRules.Decide(July1, July31, Hash, [Key('z')],
            [File(7, July1, July31, [Key('a')]), File(8, July31, August31, [Key('b')])], 7));

        Assert.Equal(ImportCodes.RestatementTargetNotCovered, refusal.Code);
    }
}

/// <summary>An in-memory database state for the planner-1 prediction.</summary>
internal sealed class FakePlannerOneState : IPlannerOneState
{
    public bool CaseSensitive => false;
    public HashSet<string> ExactFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Store, string Report, StoredFile File)> Files { get; } = [];
    public List<(string Store, DateOnly Day)> Locked { get; } = [];
    public List<(string Store, StoredInvoice Invoice)> Invoices { get; } = [];
    public List<StoredSalesLine> Lines { get; } = [];
    public List<(string Store, StoredMovement Row)> Movements { get; } = [];
    public List<(string Store, StoredSnapshot Row)> Snapshots { get; } = [];
    public int Lookups { get; private set; }

    public Task<long?> ExactDuplicateAsync(string sha256, string reportCode, string? store, DateOnly? start, DateOnly? end, CancellationToken cancellationToken) =>
        Task.FromResult(ExactFiles.Contains(sha256) ? 1L : (long?)null);

    public Task<IReadOnlyList<StoredFile>> CurrentFilesAsync(string store, string reportCode, DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredFile>>(Files.Where(file => file.Store == store && file.Report == reportCode && file.File.Start <= end && file.File.End >= start)
            .Select(file => file.File).ToArray());

    public Task<IReadOnlyList<DateOnly>> LockedDaysAsync(string store, DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DateOnly>>(Locked.Where(day => day.Store == store && day.Day >= start && day.Day <= end).Select(day => day.Day).ToArray());

    public Task<IReadOnlyList<StoredInvoice>> InvoicesAsync(string store, IReadOnlyCollection<(int Year, string Document)> keys, CancellationToken cancellationToken)
    {
        Lookups++;
        return Task.FromResult<IReadOnlyList<StoredInvoice>>(Invoices.Where(entry => entry.Store == store && keys.Contains((entry.Invoice.Year, entry.Invoice.Document)))
            .Select(entry => entry.Invoice).ToArray());
    }

    public Task<IReadOnlyList<StoredSalesLine>> SalesLinesAsync(IReadOnlyCollection<long> invoiceIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredSalesLine>>(Lines.Where(line => invoiceIds.Contains(line.InvoiceId)).ToArray());

    public Task<IReadOnlyList<StoredControl>> ControlsAsync(IReadOnlyCollection<long> invoiceIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredControl>>([]);

    public Task<IReadOnlyList<StoredTender>> TendersAsync(IReadOnlyCollection<long> invoiceIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredTender>>([]);

    public Task<IReadOnlyList<StoredMovement>> MovementsAsync(string store, IReadOnlyCollection<(int Year, string Document)> documents, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredMovement>>(Movements.Where(entry => entry.Store == store && documents.Contains((entry.Row.Year, entry.Row.Document)))
            .Select(entry => entry.Row).ToArray());

    public Task<IReadOnlyList<StoredSnapshot>> SnapshotsAsync(string store, IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredSnapshot>>(Snapshots.Where(entry => entry.Store == store && dates.Contains(entry.Row.Date)).Select(entry => entry.Row).ToArray());

    public Task<IReadOnlyCollection<string>> EnrichmentKeysAsync(string type, string store, IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);
}

/// <summary>The planner-1 prediction against an in-memory state (design 12.2 PlannerOnePredictorTests).</summary>
public sealed class PlannerOnePredictorTests : IDisposable
{
    private const string LocalOnly = "Data Source=.;Initial Catalog=NotUsed;Integrated Security=True";
    private readonly string folder = AuditFixtureWorkbooks.NewFolder();

    public void Dispose() => AuditFixtureWorkbooks.Delete(folder);

    private async Task<IReadOnlyList<InspectedFile>> InspectAsync() => (await new SourceInspector().InspectAsync([folder])).Files;

    private static StoredMovement Line(string document, string date, string type, int line, decimal opening, decimal transaction, decimal closing, string product) =>
        new(2027, document, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), product, type, "WLMHW", "", line, opening, transaction, closing, 100 + line);

    /// <summary>What the import stored for the ledger's first two documents: the per-unit chain in running-balance order.</summary>
    private static void StoreFirstTwoDocuments(FakePlannerOneState state, decimal lastClosing = 5m)
    {
        for (var unit = 0; unit < 5; unit++)
            state.Movements.Add(("WLMHW", Line("SYN900001", "2026-08-25", "Purchase Receipt", unit + 1, unit, 1, unit == 4 ? lastClosing : unit + 1, "SYN-W-01")));
        state.Movements.Add(("WLMHW", Line("SYN900002", "2026-08-26", "INV", 1, 1, -1, 0, "SYN-W-02")));
        state.Movements.Add(("WLMHW", Line("SYN900002", "2026-08-26", "INV", 2, 1, -1, 0, "SYN-W-02")));
    }

    [Fact]
    public async Task A_ledger_whose_first_documents_are_stored_is_present_and_new()
    {
        AuditFixtureWorkbooks.Write("r030-per-unit-ledger.json", folder);
        var state = new FakePlannerOneState();
        StoreFirstTwoDocuments(state);

        var file = Assert.Single(await InspectAsync());
        var prediction = await new PlannerOnePredictor(state, LocalOnly).PredictAsync(file, false, CancellationToken.None);

        Assert.Equal("Imported", prediction.Result);
        Assert.Equal(new AuditRowCounts(1, 7, 0), prediction.Rows);
    }

    [Fact]
    public async Task One_changed_quantity_fails_the_whole_file_with_its_count()
    {
        AuditFixtureWorkbooks.Write("r030-per-unit-ledger.json", folder);
        var state = new FakePlannerOneState();
        StoreFirstTwoDocuments(state, lastClosing: 9m);

        var prediction = await new PlannerOnePredictor(state, LocalOnly).PredictAsync(Assert.Single(await InspectAsync()), true, CancellationToken.None);

        Assert.Equal("Failed", prediction.Result);
        Assert.Equal(ImportCodes.ImportConflict, prediction.Code);
        Assert.Equal(ImportDiagnosticCatalogue.ConflictCountMessage(1), prediction.Message);
        Assert.Equal(1, prediction.Rows.Conflict);
        Assert.Contains("SYN900001", Assert.Single(prediction.ConflictSamples!));
    }

    [Fact]
    public async Task Conflict_samples_are_left_out_unless_asked_for()
    {
        AuditFixtureWorkbooks.Write("r030-per-unit-ledger.json", folder);
        var state = new FakePlannerOneState();
        StoreFirstTwoDocuments(state, lastClosing: 9m);

        var prediction = await new PlannerOnePredictor(state, LocalOnly).PredictAsync(Assert.Single(await InspectAsync()), false, CancellationToken.None);

        Assert.Null(prediction.ConflictSamples);
    }

    [Fact]
    public async Task A_finalised_day_in_the_period_fails_with_51021()
    {
        AuditFixtureWorkbooks.Write("r030-per-unit-ledger.json", folder);
        var state = new FakePlannerOneState();
        state.Locked.Add(("WLMHW", new DateOnly(2026, 8, 26)));

        var prediction = await new PlannerOnePredictor(state, LocalOnly).PredictAsync(Assert.Single(await InspectAsync()), false, CancellationToken.None);

        Assert.Equal("Failed", prediction.Result);
        Assert.Equal("SQL_51021", prediction.Code);
    }

    [Fact]
    public async Task The_same_file_already_imported_is_a_duplicate()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", folder);
        var file = Assert.Single(await InspectAsync());
        var state = new FakePlannerOneState();
        state.ExactFiles.Add(file.Sha256!);

        var prediction = await new PlannerOnePredictor(state, LocalOnly).PredictAsync(file, false, CancellationToken.None);

        Assert.Equal("Duplicate", prediction.Result);
        Assert.Equal(new AuditRowCounts(0, 3, 0), prediction.Rows);
    }

    [Fact]
    public async Task A_current_file_the_new_one_does_not_cover_refuses_it()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", folder);
        var state = new FakePlannerOneState();
        state.Files.Add(("WLMHW", "R025", new StoredFile(42, new string('9', 64), new DateOnly(2026, 9, 29), new DateOnly(2026, 9, 30),
            new DateTime(2026, 9, 30), [new string('e', 64) + ":1"], 1)));

        var prediction = await new PlannerOnePredictor(state, LocalOnly).PredictAsync(Assert.Single(await InspectAsync()), false, CancellationToken.None);

        Assert.Equal("Failed", prediction.Result);
        Assert.Equal("IMPORT_PERIOD_ALREADY_PRESENT", prediction.Code);
    }

    [Fact]
    public async Task A_sales_line_dated_otherwise_than_its_stored_invoice_is_a_conflict()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", folder);
        var state = new FakePlannerOneState();
        state.Invoices.Add(("WLMHW", new StoredInvoice(5, 2027, "SYN100002", new DateOnly(2026, 9, 1))));

        var prediction = await new PlannerOnePredictor(state, LocalOnly).PredictAsync(Assert.Single(await InspectAsync()), false, CancellationToken.None);

        Assert.Equal("Failed", prediction.Result);
        Assert.Equal(1, prediction.Rows.Conflict);
    }

    [Fact]
    public async Task The_run_overlay_lets_a_later_file_see_an_earlier_one()
    {
        // The same sales twice in one run, the second with one more invoice: the second takes the first over.
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", folder);
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", Path.Combine(folder, "later"), fixture =>
        {
            fixture.FileName = "202609301000_SDB-VariantwiseSales.xlsx";
            var extra = new Dictionary<string, object?>(fixture.Sheet("Data").Rows[0]) { ["INVNUMBER"] = "SYN100003", ["INVDATE"] = "2026-09-30" };
            fixture.Sheet("Data").Rows.Add(extra);
        });
        var files = await InspectAsync();
        var predictor = new PlannerOnePredictor(new FakePlannerOneState(), LocalOnly);

        var first = await predictor.PredictAsync(files[0], false, CancellationToken.None);
        var second = await predictor.PredictAsync(files[1], false, CancellationToken.None);

        Assert.Equal("Imported", first.Result);
        Assert.Equal(new AuditRowCounts(3, 0, 0), first.Rows);
        Assert.Equal("Imported", second.Result);
        Assert.Equal(new AuditRowCounts(1, 3, 0), second.Rows);
        // A third copy of the first file in the same run is its exact duplicate.
        Assert.Equal("Duplicate", (await predictor.PredictAsync(files[0], false, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task A_superset_promotes_over_the_stored_period_files()
    {
        AuditFixtureWorkbooks.Write("r022-period-1.json", Path.Combine(folder, "a"));
        AuditFixtureWorkbooks.Write("r022-period-2.json", Path.Combine(folder, "b"));
        AuditFixtureWorkbooks.Write("r022-superset.json", Path.Combine(folder, "c"));
        var files = await InspectAsync();
        var state = new FakePlannerOneState();
        // The two period files are current imports 10077 and 10078, holding exactly their own content keys.
        foreach (var (file, id) in new[] { (files[0], 10077L), (files[1], 10078L) })
            state.Files.Add(("WLMHW", "R022", new StoredFile(id, file.Sha256!, file.PeriodStart, file.PeriodEnd, new DateTime(2026, 4, 5),
                Store.ContentKeys(file.Accepted!, file.BusinessDate).Values.ToArray(), 1)));

        var prediction = await new PlannerOnePredictor(state, LocalOnly).PredictAsync(files[2], false, CancellationToken.None);

        Assert.Equal("Imported", prediction.Result);
        Assert.Equal([10077L, 10078L], prediction.PromotesOver);
        Assert.Equal(0, prediction.Rows.Conflict);
    }

    [Fact]
    public async Task Period_files_and_their_superset_in_one_run_are_predicted_in_order()
    {
        AuditFixtureWorkbooks.Write("r022-period-1.json", Path.Combine(folder, "a"));
        AuditFixtureWorkbooks.Write("r022-period-2.json", Path.Combine(folder, "b"));
        AuditFixtureWorkbooks.Write("r022-superset.json", Path.Combine(folder, "c"));
        var files = await InspectAsync();
        var predictor = new PlannerOnePredictor(new FakePlannerOneState(), LocalOnly);

        var results = new List<AuditPlannerOneReport>();
        foreach (var file in files) results.Add(await predictor.PredictAsync(file, false, CancellationToken.None));

        Assert.Equal(["Imported", "Imported", "Imported"], results.Select(result => result.Result));
        // The superset holds the five invoices the run already imported and one new one.
        Assert.Equal(new AuditRowCounts(1, 5, 0), results[2].Rows);
    }

    [Fact]
    public async Task Stacked_R010_predicts_its_snapshot_rows_per_date()
    {
        AuditFixtureWorkbooks.WriteSnapshot(Etp.Reporting.Import.Tests.SyntheticConsolidatedWorkbooks.LegacyStackedBinWise(), folder);

        var prediction = await new PlannerOnePredictor(new FakePlannerOneState(), LocalOnly).PredictAsync(Assert.Single(await InspectAsync()), false, CancellationToken.None);

        Assert.Equal("Imported", prediction.Result);
        Assert.Equal(new Dictionary<string, int> { ["2026-07-02"] = 3, ["2026-08-07"] = 3, ["2026-09-29"] = 2 }, prediction.SnapshotRows);
        Assert.Equal(new AuditRowCounts(8, 0, 0), prediction.Rows);
    }

    [Fact]
    public async Task A_stored_snapshot_row_with_other_values_is_a_conflict()
    {
        AuditFixtureWorkbooks.WriteSnapshot(Etp.Reporting.Import.Tests.SyntheticConsolidatedWorkbooks.LegacyStackedBinWise(), folder);
        var state = new FakePlannerOneState();
        state.Snapshots.Add(("WLMHW", new StoredSnapshot(new DateOnly(2026, 7, 2), "R010", "SYN-ITEM-0002", "", 1,
            ProtectedValues.Snapshot(null, null, null, null, null, null, null, 7m, 100m, 100m), 9)));

        var prediction = await new PlannerOnePredictor(state, LocalOnly).PredictAsync(Assert.Single(await InspectAsync()), false, CancellationToken.None);

        Assert.Equal("Failed", prediction.Result);
        Assert.Equal(1, prediction.Rows.Conflict);
    }

    [Fact]
    public async Task Files_refused_before_the_planner_keep_their_grid_result()
    {
        await File.WriteAllTextAsync(Path.Combine(folder, "202609291449_SDB-VariantwiseSales.xlsx"), "not a workbook");

        var prediction = await new PlannerOnePredictor(new FakePlannerOneState(), LocalOnly).PredictAsync(Assert.Single(await InspectAsync()), false, CancellationToken.None);

        Assert.Equal("Failed", prediction.Result);
        Assert.NotNull(prediction.Code);
    }
}

/// <summary>Every SQL text is a constant SELECT (design 6.4.1, 12.2 ReadOnlySqlGuardTests).</summary>
public sealed class ReadOnlySqlGuardTests
{
    [Fact]
    public void Every_audit_query_is_read_only()
    {
        Assert.NotEmpty(AuditQueries.All);
        Assert.All(AuditQueries.All, sql => Assert.Empty(ReadOnlySqlGuard.Violations(sql)));
    }

    [Fact]
    public void Every_constant_is_listed_for_the_guard()
    {
        var constants = typeof(AuditQueries).GetFields().Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!).ToArray();

        Assert.Equal(constants.Order(StringComparer.Ordinal), AuditQueries.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_embedded_baseline_script_is_read_only()
    {
        Assert.Empty(ReadOnlySqlGuard.Violations(BaselineQuery.Script()));
    }

    [Theory]
    [InlineData("INSERT dbo.t(a) VALUES(1)")]
    [InlineData("SELECT a INTO #t FROM dbo.x")]
    [InlineData("SELECT a FROM dbo.x; EXEC dbo.x")]
    [InlineData("SELECT a FROM dbo.x WITH (UPDLOCK)")]
    [InlineData("SELECT a FROM dbo.x WITH (HOLDLOCK)")]
    [InlineData("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='x',@LockMode='Exclusive'")]
    [InlineData("MERGE dbo.t AS a USING dbo.s AS b ON a.id=b.id WHEN MATCHED THEN DELETE;")]
    [InlineData("DROP TABLE dbo.t")]
    [InlineData("UPDATE dbo.t SET a=1")]
    [InlineData("DELETE FROM dbo.t")]
    [InlineData("TRUNCATE TABLE dbo.t")]
    [InlineData("SELECT 1; BEGIN TRANSACTION")]
    [InlineData("EXEC sys.sp_executesql N'DELETE FROM dbo.t'")]
    [InlineData("SELECT * FROM #temp")]
    [InlineData("EXEC xp_cmdshell 'dir'")]
    public void Writing_locking_or_executing_text_is_refused(string sql)
    {
        Assert.NotEmpty(ReadOnlySqlGuard.Violations(sql));
        Assert.Throws<InvalidOperationException>(() => ReadOnlySqlGuard.Require(sql));
    }

    [Theory]
    [InlineData("SELECT a FROM dbo.x WHERE b=@b")]
    [InlineData("EXEC sys.sp_executesql N'SELECT TOP (200) a FROM dbo.x', N'@n int', @n=1")]
    [InlineData("SELECT HAS_PERMS_BY_NAME(N'dbo.import_files',N'OBJECT',N'INSERT') -- a comment saying DELETE")]
    [InlineData("SELECT [update] FROM dbo.x /* DROP */")]
    [InlineData("SET LOCK_TIMEOUT 5000;")]
    public void Read_only_text_is_accepted(string sql)
    {
        Assert.Empty(ReadOnlySqlGuard.Violations(sql));
    }
}

/// <summary>The baseline runs the repository's pre-upgrade check byte for byte (design 12.2 BaselineScriptEmbeddingTests).</summary>
public sealed class BaselineScriptEmbeddingTests
{
    [Fact]
    public void The_embedded_resource_equals_the_script_file()
    {
        var repository = AuditOutputLocation.WorkTreeRoot(AppContext.BaseDirectory)!;

        Assert.Equal(File.ReadAllBytes(Path.Combine(repository, "scripts", "check-import-upgrade.sql")), BaselineQuery.ScriptBytes());
    }

    [Fact]
    public void Compare_reports_changed_fact_counts_and_new_blocking_checks()
    {
        var folder = AuditFixtureWorkbooks.NewFolder();
        try
        {
            var earlier = new AuditResultSet[]
            {
                new("summary", ["check_code", "findings", "blocks_upgrade", "detail"], [["LOCKED_DAYS", "0", "1", "d"]]),
                new("fact counts", ["table_name", "row_count"], [["sales_lines", "10"], ["stock_movements", "5"]])
            };
            var report = new AuditReport
            {
                Command = "baseline", Tool = AuditToolStamp.Current([]), Summary = new AuditSummary(),
                Baseline = new AuditBaselineReport(earlier)
            };
            var path = Path.Combine(folder, "report.json");
            File.WriteAllText(path, AuditReportWriter.Json(report));
            var now = new AuditResultSet[]
            {
                new("summary", ["check_code", "findings", "blocks_upgrade", "detail"], [["LOCKED_DAYS", "2", "1", "d"]]),
                new("fact counts", ["table_name", "row_count"], [["sales_lines", "12"], ["stock_movements", "5"]])
            };

            var differences = BaselineQuery.Compare(now, path);

            Assert.Equal(["sales_lines: 10 -> 12", "blocking check now: LOCKED_DAYS (2)"], differences);
            Assert.Throws<AuditInputException>(() => BaselineQuery.Compare(now, Path.Combine(folder, "missing.json")));
        }
        finally { AuditFixtureWorkbooks.Delete(folder); }
    }
}

/// <summary>The database commands' refusals that need no database (design 9: exit 3 and 4).</summary>
public sealed class CheckImportRefusalTests : IDisposable
{
    private readonly string folder = AuditFixtureWorkbooks.NewFolder();

    public void Dispose() => AuditFixtureWorkbooks.Delete(folder);

    [Fact]
    public async Task A_server_on_another_computer_is_refused_with_exit_4()
    {
        var error = new StringWriter();

        var code = await new AuditRunner(TextWriter.Null, error).RunAsync(["check-import", folder, "--database", "EtpReporting",
            "--server", "SHOPPC01\\SQLEXPRESS", "--format", "text"]);

        Assert.Equal(AuditExitCodes.Database, code);
        Assert.Contains("this computer", error.ToString());
    }

    [Fact]
    public async Task Baseline_on_another_computer_is_refused_with_exit_4()
    {
        var code = await new AuditRunner(TextWriter.Null, TextWriter.Null).RunAsync(["baseline", "--database", "EtpReporting",
            "--server", "SHOPPC01\\SQLEXPRESS", "--format", "text"]);

        Assert.Equal(AuditExitCodes.Database, code);
    }

    [Fact]
    public async Task An_expectation_file_of_the_wrong_schema_is_exit_3_before_any_connection()
    {
        var expect = Path.Combine(folder, "expect.json");
        await File.WriteAllTextAsync(expect, """{ "schema": "something-else/1", "files": [] }""");

        var code = await new AuditRunner(TextWriter.Null, TextWriter.Null).RunAsync(["check-import", folder, "--database", "EtpReporting",
            "--server", "SHOPPC01\\SQLEXPRESS", "--expect", expect, "--format", "text"]);

        Assert.Equal(AuditExitCodes.Input, code);
    }
}
