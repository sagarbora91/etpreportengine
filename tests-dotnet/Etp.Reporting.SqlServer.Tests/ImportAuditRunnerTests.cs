using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Audit;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer.Audit;
using Etp.Reporting.TestSupport;
using static Etp.Reporting.Import.Tests.SyntheticConsolidatedWorkbooks;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>A run of the ImportAudit command line in this process: its exit code, stdout, stderr and report folder.</summary>
internal sealed record AuditRun(int ExitCode, string Output, string Error, string Folder)
{
    public string Json => File.Exists(Path.Combine(Folder, "report.json")) ? File.ReadAllText(Path.Combine(Folder, "report.json")) : "";
    public string Text => File.Exists(Path.Combine(Folder, "report.txt")) ? File.ReadAllText(Path.Combine(Folder, "report.txt")) : "";
    public JsonElement Report => JsonDocument.Parse(Json).RootElement;

    public static async Task<AuditRun> RunAsync(string outFolder, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var withOut = args.Length > 0 && args[0] is not "seed" && !args.Contains("--out") && args.Length > 1 ? [.. args, "--out", outFolder] : args;
        var code = await new AuditRunner(output, error).RunAsync(withOut);
        return new(code, output.ToString(), error.ToString(), outFolder);
    }
}

/// <summary>ImportAudit's commands, exit codes and report files (design 7, 9, 12.2), without a database.</summary>
public sealed class ImportAuditRunnerTests : IDisposable
{
    private readonly string input = AuditFixtureWorkbooks.NewFolder();
    private readonly string reports = AuditFixtureWorkbooks.NewFolder("reports");

    public void Dispose()
    {
        AuditFixtureWorkbooks.Delete(input);
        AuditFixtureWorkbooks.Delete(reports);
    }

    [Fact]
    public async Task No_arguments_prints_usage_and_exits_2_without_touching_a_database()
    {
        var run = await AuditRun.RunAsync(reports);

        Assert.Equal(AuditExitCodes.Usage, run.ExitCode);
        Assert.Contains("Usage:", run.Error);
        Assert.Equal("", run.Output);
    }

    [Fact]
    public async Task Seed_refuses_live_before_connecting()
    {
        var run = await AuditRun.RunAsync(reports, "seed", "--database", "EtpReporting");

        Assert.Equal(AuditExitCodes.Usage, run.ExitCode);
    }

    [Fact]
    public async Task Inspect_writes_the_text_and_json_reports_and_exits_0()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", input);

        var run = await AuditRun.RunAsync(reports, "inspect", input);

        Assert.Equal(AuditExitCodes.Clean, run.ExitCode);
        Assert.Contains("SUMMARY  files 1  pass 1", run.Output);
        Assert.Equal(run.Output, run.Text);
        var report = run.Report;
        Assert.Equal(AuditReport.SchemaName, report.GetProperty("schema").GetString());
        Assert.Equal("inspect", report.GetProperty("command").GetString());
        var file = report.GetProperty("files")[0];
        Assert.Equal("R025", file.GetProperty("reportCode").GetString());
        Assert.Equal("Pass", file.GetProperty("verdict").GetString());
        Assert.Equal("Raw", file.GetProperty("sourceKind").GetString());
        Assert.Equal(2, file.GetProperty("documents").GetProperty("total").GetInt32());
        Assert.Equal(0, report.GetProperty("summary").GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task A_file_the_import_would_refuse_is_a_finding_exit_1()
    {
        await File.WriteAllTextAsync(Path.Combine(input, "202609291449_SDB-VariantwiseSales.xlsx"), "not a workbook");

        var run = await AuditRun.RunAsync(reports, "inspect", input);

        Assert.Equal(AuditExitCodes.Findings, run.ExitCode);
        Assert.Equal("Blocked", run.Report.GetProperty("files")[0].GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task A_missing_input_is_exit_3()
    {
        var run = await AuditRun.RunAsync(reports, "inspect", Path.Combine(input, "missing"));

        Assert.Equal(AuditExitCodes.Input, run.ExitCode);
        Assert.Contains("IMPORT_SOURCE_NOT_FOUND", run.Error);
    }

    [Fact]
    public async Task Text_format_writes_no_folder()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", input);
        var folder = Path.Combine(reports, "text-only");

        var run = await AuditRun.RunAsync(folder, "inspect", input, "--format", "text");

        Assert.Equal(AuditExitCodes.Clean, run.ExitCode);
        Assert.Contains("SUMMARY", run.Output);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Json_format_keeps_stdout_empty()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", input);

        var run = await AuditRun.RunAsync(reports, "inspect", input, "--format", "json");

        Assert.Equal("", run.Output);
        Assert.Contains("\"schema\"", run.Json);
    }

    [Fact]
    public async Task Documents_detail_adds_key_texts_that_summary_leaves_out()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", input);

        var summary = await AuditRun.RunAsync(Path.Combine(reports, "summary"), "inspect", input);
        var documents = await AuditRun.RunAsync(Path.Combine(reports, "documents"), "inspect", input, "--detail", "documents");

        Assert.DoesNotContain("SYN100001", summary.Json + summary.Output);
        Assert.Contains("2027|SYN100001", documents.Json);
        Assert.Equal(2, documents.Report.GetProperty("files")[0].GetProperty("documentList").GetArrayLength());
    }

    [Fact]
    public async Task Strict_turns_a_hold_into_a_finding()
    {
        // The ledger fixture repeats one unit row exactly (STOCK_ROW_REPEATED, a warning).
        AuditFixtureWorkbooks.Write("r030-per-unit-ledger.json", input);

        var relaxed = await AuditRun.RunAsync(Path.Combine(reports, "relaxed"), "inspect", input);
        var strict = await AuditRun.RunAsync(Path.Combine(reports, "strict"), "inspect", input, "--strict");

        Assert.Equal(AuditExitCodes.Clean, relaxed.ExitCode);
        Assert.Equal(AuditExitCodes.Findings, strict.ExitCode);
    }

    [Fact]
    public async Task A_legacy_hold_is_listed_and_exits_0_unless_strict()
    {
        AuditFixtureWorkbooks.Write("r025-legacy-blocks-differ.json", input);

        var relaxed = await AuditRun.RunAsync(Path.Combine(reports, "relaxed"), "inspect", input);
        var strict = await AuditRun.RunAsync(Path.Combine(reports, "strict"), "inspect", input, "--strict");

        Assert.Equal(AuditExitCodes.Clean, relaxed.ExitCode);
        var hold = Assert.Single(relaxed.Report.GetProperty("summary").GetProperty("legacyHolds").EnumerateArray());
        Assert.Equal(ImportCodes.LegacyBlocksDiffer, hold.GetProperty("code").GetString());
        Assert.Contains("legacy hold", relaxed.Output);
        Assert.Equal(AuditExitCodes.Findings, strict.ExitCode);
    }

    [Fact]
    public async Task Paths_in_reports_are_relative_and_arguments_are_reduced_to_names()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", Path.Combine(input, "Retail", "WLMHW"));

        var run = await AuditRun.RunAsync(reports, "inspect", input);

        Assert.Equal(Path.Combine("Retail", "WLMHW", "202609291449_SDB-VariantwiseSales.xlsx"), run.Report.GetProperty("files")[0].GetProperty("file").GetString());
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\'), run.Json.Replace("\\\\", "\\"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["inspect", "input", "--out", "reports"], run.Report.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()));
    }
}

/// <summary><c>validate-contract</c> (design 3.2, 12.2): contract blockers, the raw rebuild check and legacy workbooks.</summary>
public sealed class ValidateContractCommandTests : IDisposable
{
    private readonly string input = AuditFixtureWorkbooks.NewFolder();
    private readonly string raw = AuditFixtureWorkbooks.NewFolder("raw");
    private readonly string reports = AuditFixtureWorkbooks.NewFolder("reports");

    public void Dispose()
    {
        AuditFixtureWorkbooks.Delete(input);
        AuditFixtureWorkbooks.Delete(raw);
        AuditFixtureWorkbooks.Delete(reports);
    }

    private static readonly (int Row, string Item, decimal Quantity)[] July = [(2, "SYN-A", 1m), (3, "SYN-B", 2m), (4, "SYN-C", 3m)];
    private static readonly (int Row, string Item, decimal Quantity)[] August = [(2, "SYN-A", 1m), (3, "SYN-D", 4m)];

    private static WorkbookSheet BinWise(IEnumerable<(int Row, string Item, decimal Quantity)> rows, string name = "Data") =>
        new(name, 1, BinWiseHeaders, rows.Select(row => Row(BinWiseHeaders, new Dictionary<string, object?>
        {
            ["STORE CODE"] = "WLMHW", ["ITEMNUMBER"] = row.Item, ["CLOSINGBALANCE"] = row.Quantity, ["UCP"] = 100m, ["TOTALUCP"] = 100m * row.Quantity
        }, row.Row)).ToArray());

    private const string JulyFile = "202607021446_BinWise-Stock - BinWise-Stock.xlsx";
    private const string AugustFile = "202608071848_BinWise-Stock - BinWise-Stock.xlsx";

    /// <summary>Writes the two raw exports (July altered when asked) and a contract v1 workbook stacking them.</summary>
    private string WriteContract(bool alterJuly = false, bool writeAugustRaw = true)
    {
        var julyRows = alterJuly ? July.Select(row => row.Item == "SYN-C" ? row with { Quantity = 5m } : row).ToArray() : July;
        var julyPath = AuditFixtureWorkbooks.WriteSnapshot(new WorkbookSnapshot(JulyFile, 1, Hash, [BinWise(julyRows)]), raw);
        var augustPath = Path.Combine(Path.GetTempPath(), "etp-import-audit-tests", Guid.NewGuid().ToString("N"), AugustFile);
        augustPath = AuditFixtureWorkbooks.WriteSnapshot(new WorkbookSnapshot(AugustFile, 1, Hash, [BinWise(August)]),
            writeAugustRaw ? raw : Path.GetDirectoryName(augustPath)!);
        string Sha(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        string[] Block(int no, int first, int last, string file, string sha, string time, string date) =>
            [no.ToString(), "Data", first.ToString(), last.ToString(), (last - first + 1).ToString(), file, "xlsx", sha, time, "", "", "none", date,
             (last - first + 1).ToString(), "0", "0", "complete", $"Snapshot {date} retained"];
        var info = ContractInfo(SnapshotKeys("R010", "WLMHW", 5, 2),
        [
            Block(1, 2, 4, JulyFile, Sha(julyPath), "2026-07-02T14:46", "2026-07-02"),
            Block(2, 5, 6, AugustFile, Sha(augustPath), "2026-08-07T18:48", "2026-08-07")
        ]);
        var data = BinWise([.. July, (5, "SYN-A", 1m), (6, "SYN-D", 4m)]);
        return AuditFixtureWorkbooks.WriteSnapshot(new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash, [data, info]), input);
    }

    [Fact]
    public async Task A_valid_contract_exits_0()
    {
        WriteContract();

        var run = await AuditRun.RunAsync(reports, "validate-contract", input);

        Assert.True(run.ExitCode == AuditExitCodes.Clean, run.Output);
        var contract = run.Report.GetProperty("files")[0].GetProperty("contract");
        Assert.Equal("Consolidated", contract.GetProperty("kind").GetString());
        Assert.Equal(0, contract.GetProperty("blockers").GetInt32());
    }

    [Fact]
    public async Task Raw_exports_that_match_are_checked_and_pass()
    {
        WriteContract();

        var run = await AuditRun.RunAsync(reports, "validate-contract", input, "--raw", raw);

        Assert.True(run.ExitCode == AuditExitCodes.Clean, run.Output);
        Assert.Equal(2, run.Report.GetProperty("files")[0].GetProperty("contract").GetProperty("rebuildChecked").GetInt32());
    }

    [Fact]
    public async Task An_altered_raw_export_is_a_rebuild_mismatch_exit_1()
    {
        WriteContract(alterJuly: true);

        var run = await AuditRun.RunAsync(reports, "validate-contract", input, "--raw", raw);

        Assert.Equal(AuditExitCodes.Findings, run.ExitCode);
        Assert.Contains(ImportCodes.Contract.BlockRebuildMismatch, run.Json);
        Assert.Equal("Blocked", run.Report.GetProperty("files")[0].GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task A_raw_export_not_in_the_folder_is_counted_not_checked_exit_0()
    {
        WriteContract(writeAugustRaw: false);

        var run = await AuditRun.RunAsync(reports, "validate-contract", input, "--raw", raw);

        Assert.True(run.ExitCode == AuditExitCodes.Clean, run.Output);
        var contract = run.Report.GetProperty("files")[0].GetProperty("contract");
        Assert.Equal(1, contract.GetProperty("rebuildChecked").GetInt32());
        Assert.Equal(1, contract.GetProperty("rebuildNotChecked").GetInt32());
    }

    [Fact]
    public async Task A_legacy_workbook_is_reported_by_kind_and_fails_only_with_require_contract()
    {
        AuditFixtureWorkbooks.WriteSnapshot(LegacyStackedBinWise(), input);

        var relaxed = await AuditRun.RunAsync(Path.Combine(reports, "a"), "validate-contract", input);
        var required = await AuditRun.RunAsync(Path.Combine(reports, "b"), "validate-contract", input, "--require-contract");

        Assert.Equal(AuditExitCodes.Clean, relaxed.ExitCode);
        var contract = relaxed.Report.GetProperty("files")[0].GetProperty("contract");
        Assert.Equal("ConsolidatedLegacy", contract.GetProperty("kind").GetString());
        Assert.True(contract.GetProperty("legacyInfoTiles").GetBoolean());
        Assert.Equal(AuditExitCodes.Findings, required.ExitCode);
        Assert.Contains(AuditCodes.ContractRequired, required.Json);
    }

    [Fact]
    public async Task A_contract_blocker_is_reported_by_code_exit_1()
    {
        var path = WriteContract();
        // Rewrite the contract with a block table whose second block overlaps the first: a contract blocker.
        var workbook = await new OpenXmlWorkbookReader().ReadAsync(path);
        var info = workbook.Sheets.Single(sheet => sheet.Name == "Info");
        var rows = info.Rows.Select(row => row.Cells.Count > 2 && Equals(row.Cells[0].Value, "2") && Equals(row.Cells[1].Value, "Data")
            ? row with { Cells = row.Cells.Select((cell, index) => index == 2 ? new WorkbookCell("4", "4") : cell).ToArray() }
            : row).ToArray();
        File.Delete(path);
        AuditFixtureWorkbooks.WriteSnapshot(workbook with { Sheets = workbook.Sheets.Select(sheet => sheet.Name == "Info" ? info with { Rows = rows } : sheet).ToArray() }, input);

        var run = await AuditRun.RunAsync(reports, "validate-contract", input);

        Assert.Equal(AuditExitCodes.Findings, run.ExitCode);
        var file = run.Report.GetProperty("files")[0];
        Assert.Equal("Blocked", file.GetProperty("verdict").GetString());
        Assert.Contains(file.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
            diagnostic.GetProperty("severity").GetString() == "Blocker" && diagnostic.GetProperty("code").GetString()!.StartsWith("CONTRACT_", StringComparison.Ordinal));
    }
}

/// <summary>
/// Privacy (design 8, 12.2): no customer value, at any detail level, reaches stdout, stderr or a report file, and every
/// message in a report is a catalogue template.
/// </summary>
public sealed class AuditPrivacyTests : IDisposable
{
    private static readonly string[] Sentinels = ["ZZSENTINEL", "9999900000"];
    private readonly string input = AuditFixtureWorkbooks.NewFolder();
    private readonly string reports = AuditFixtureWorkbooks.NewFolder("reports");

    public AuditPrivacyTests()
    {
        AuditFixtureWorkbooks.Write("r025-customer-sentinel.json", input);
        // The same export with a sentinel in a header cell: no layout matches, and the column name is workbook text.
        var header = AuditFixtureWorkbooks.Load("r025-customer-sentinel.json");
        var grid = header.Sheet("Data").Grid().Select(row => row.ToList()).ToList();
        grid[0].Add("ZZSENTINEL-HDR");
        for (var row = 1; row < grid.Count; row++) grid[row].Add("ZZSENTINEL-CELL");
        AuditFixtureWorkbooks.WriteWorkbook(Path.Combine(input, "202609291501_SDB-VariantwiseSales.xlsx"),
            [("Data", grid.Select(row => (IReadOnlyList<object?>)row).ToArray())]);
        // A file that is not a workbook at all, whose bytes hold the sentinel: the forced exception path.
        File.WriteAllText(Path.Combine(input, "202609291502_SDB-VariantwiseSales.xlsx"), "ZZSENTINEL-NAME 9999900000");
    }

    public void Dispose()
    {
        AuditFixtureWorkbooks.Delete(input);
        AuditFixtureWorkbooks.Delete(reports);
    }

    public static TheoryData<string, string> Runs() => new()
    {
        { "inspect", "summary" }, { "inspect", "documents" }, { "inspect", "rows" },
        { "validate-contract", "summary" }, { "validate-contract", "documents" }, { "validate-contract", "rows" }
    };

    [Theory]
    [MemberData(nameof(Runs))]
    public async Task No_sentinel_reaches_any_output(string command, string detail)
    {
        var run = await AuditRun.RunAsync(reports, command, input, "--detail", detail, "--debug");

        Assert.Equal(3, run.Report.GetProperty("files").GetArrayLength());
        foreach (var sentinel in Sentinels)
        {
            Assert.DoesNotContain(sentinel, run.Output);
            Assert.DoesNotContain(sentinel, run.Error);
            Assert.DoesNotContain(sentinel, run.Json);
            Assert.DoesNotContain(sentinel, run.Text);
        }
    }

    [Fact]
    public async Task Every_message_in_the_report_is_a_catalogue_template()
    {
        var run = await AuditRun.RunAsync(reports, "inspect", input, "--detail", "rows");

        var messages = run.Report.GetProperty("files").EnumerateArray()
            .SelectMany(file => file.GetProperty("diagnostics").EnumerateArray())
            .Select(diagnostic => (Code: diagnostic.GetProperty("code").GetString()!, Message: diagnostic.GetProperty("message").GetString()!)).ToArray();
        Assert.NotEmpty(messages);
        Assert.All(messages, message => Assert.Equal(AuditReportBuilder.SafeMessage(message.Code, message.Message), message.Message));
        Assert.All(messages, message => Assert.True(ImportDiagnosticCatalogue.IsKnown(message.Code) || AuditCodes.Messages.ContainsKey(message.Code)
            || message.Message == ImportDiagnosticCatalogue.GenericMessage, message.Code));
    }
}

/// <summary>Where reports may go (design 8.6, 12.2).</summary>
public sealed class AuditOutputLocationTests : IDisposable
{
    private readonly string root = AuditFixtureWorkbooks.NewFolder("repo");

    public void Dispose() => AuditFixtureWorkbooks.Delete(root);

    [Fact]
    public async Task Out_inside_a_git_work_tree_with_business_input_is_refused_exit_2()
    {
        await File.WriteAllTextAsync(Path.Combine(root, ".git"), "gitdir: elsewhere");
        var business = AuditFixtureWorkbooks.NewFolder("business");
        try
        {
            AuditFixtureWorkbooks.Write("r025-raw-minute.json", business);

            var run = await AuditRun.RunAsync(Path.Combine(root, "out"), "inspect", business);

            Assert.Equal(AuditExitCodes.Usage, run.ExitCode);
            Assert.False(Directory.Exists(Path.Combine(root, "out")));
        }
        finally { AuditFixtureWorkbooks.Delete(business); }
    }

    [Fact]
    public async Task Out_inside_a_work_tree_is_allowed_for_synthetic_fixtures_only()
    {
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var fixtures = Path.Combine(root, "tests-dotnet", "fixtures", "import-audit");
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", fixtures);

        var run = await AuditRun.RunAsync(Path.Combine(root, "out"), "inspect", fixtures);

        Assert.Equal(AuditExitCodes.Clean, run.ExitCode);
        Assert.True(File.Exists(Path.Combine(root, "out", "report.json")));
    }

    [Fact]
    public void The_default_folder_is_under_local_app_data()
    {
        var folder = AuditOutputLocation.Resolve(null, AuditCommandKind.Inspect, ["x"], new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero)).Folder!;

        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folder, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("-inspect", folder, StringComparison.Ordinal);
    }
}

/// <summary>Exit codes (design 9): 75 means "lock busy" to Invoke-Serialized.ps1, so no audit source may return it.</summary>
public sealed class AuditExitCodeTests
{
    [Fact]
    public void The_codes_are_the_design_table()
    {
        Assert.Equal([0, 1, 2, 3, 4, 5, 130], new[] { AuditExitCodes.Clean, AuditExitCodes.Findings, AuditExitCodes.Usage, AuditExitCodes.Input,
            AuditExitCodes.Database, AuditExitCodes.Internal, AuditExitCodes.Cancelled });
    }

    [Fact]
    public void No_audit_source_uses_75()
    {
        var repo = AuditOutputLocation.WorkTreeRoot(AppContext.BaseDirectory)!;
        var sources = new[] { Path.Combine(repo, "src", "Etp.Reporting.Import", "Audit"), Path.Combine(repo, "src", "Etp.Reporting.Infrastructure.SqlServer", "Audit"),
                Path.Combine(repo, "tools", "Etp.Reporting.ImportAudit") }
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs")).ToArray();

        Assert.NotEmpty(sources);
        // Code that returns or assigns the literal 75; the comments that say why it is never used are fine.
        Assert.All(sources, path => Assert.DoesNotMatch(new Regex(@"(return|=>|=|case)\s*75(?![\d.])"), File.ReadAllText(path)));
        Assert.DoesNotContain(75, typeof(AuditExitCodes).GetFields().Select(field => (int)field.GetValue(null)!));
    }

    [Fact]
    public async Task Cancellation_exits_130()
    {
        var folder = AuditFixtureWorkbooks.NewFolder();
        try
        {
            AuditFixtureWorkbooks.Write("r025-raw-minute.json", folder);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            var code = await new AuditRunner(TextWriter.Null, TextWriter.Null).RunAsync(["inspect", folder, "--format", "text"], cancelled.Token);

            Assert.Equal(AuditExitCodes.Cancelled, code);
        }
        finally { AuditFixtureWorkbooks.Delete(folder); }
    }
}

/// <summary>A golden <c>report.json</c> shape (design 12.2): any renamed field is caught. Volatile fields are masked.</summary>
public sealed class ReportJsonShapeTests : IDisposable
{
    private readonly string input = AuditFixtureWorkbooks.NewFolder();
    private readonly string reports = AuditFixtureWorkbooks.NewFolder("reports");

    public void Dispose()
    {
        AuditFixtureWorkbooks.Delete(input);
        AuditFixtureWorkbooks.Delete(reports);
    }

    [Fact]
    public async Task The_inspect_report_has_the_v1_field_names()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", input);

        var report = (await AuditRun.RunAsync(reports, "inspect", input)).Report;

        Assert.Equal(["schema", "command", "arguments", "tool", "detail", "startedUtc", "elapsedMs", "files", "runDiagnostics", "summary"],
            report.EnumerateObject().Select(property => property.Name));
        // The commit is left out when the build carries none.
        Assert.Equal(["version", "catalogueSha256", "contentHashVersion", "rulesets"],
            report.GetProperty("tool").EnumerateObject().Select(property => property.Name).Where(name => name != "commit"));
        var file = report.GetProperty("files")[0];
        Assert.Equal(["file", "sha256", "bytes", "elapsedMs", "reportCode", "familyCode", "store", "layout", "outcome", "periodStart", "periodEnd",
                "sourceKind", "stagedRows", "blocks", "snapshotDates", "virtualRows", "documents", "diagnostics", "verdict"],
            file.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["blockNo", "sheet", "firstRow", "lastRow", "rows", "completeness", "origin", "exportTime", "exportTimeBasis", "periodFrom",
                "periodTo", "periodBasis", "rawRows", "virtualRows", "rebuiltRows", "sourceFileName", "sourceSha256", "contentSha256",
                "contentHashVersion", "exportKey"],
            file.GetProperty("blocks")[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(["files", "pass", "findings", "blocked", "error", "unexpectedBlockers", "legacyHolds", "skippedCsvFiles", "exitCode"],
            report.GetProperty("summary").EnumerateObject().Select(property => property.Name));
    }
}
