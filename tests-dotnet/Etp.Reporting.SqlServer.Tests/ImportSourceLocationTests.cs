using System.Text.RegularExpressions;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.ImportSourceLocations;
using Etp.Reporting.Infrastructure.SqlServer.ShopResults;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.12 U2 (0069, wave plan C-c), without SQL: which location rows a run writes, the routed-name and path rules the Fix buttons
/// rely on, the next shop-result sequence after a re-apply, the migration's text, and the audit types 1.12 adds.
/// </summary>
public sealed class ImportSourceLocationTests
{
    private static readonly DateOnly Day = new(2026, 10, 11);
    private static readonly string ShaA = new('a', 64);

    [Fact]
    public void A_run_writes_one_row_per_decided_file_pointing_at_the_routed_source()
    {
        var files = new[]
        {
            new FolderImportFileResult(@"C:\Temp\x\sheet.xlsx", "D001", "TST01", Day, Day, "Failed") { SourceSha256 = ShaA },
            new FolderImportFileResult("stock.csv", "R030", "TST01", Day, Day, "Imported"),
            new FolderImportFileResult("later.xlsx", null, null, null, null, "Cancelled")
        };
        var rows = AutomatedOperationsService.LocationsFor(@"C:\In\TITAN-2026-10-11.zip", @"C:\Failed\20261012-TITAN-2026-10-11.zip", ImportSourceRoute.Failed, files);

        Assert.Equal([("sheet.xlsx", ShaA), ("stock.csv", (string?)null)], rows.Select(row => (row.FileName, row.SourceSha256)));
        Assert.All(rows, row => Assert.Equal((@"C:\Failed\20261012-TITAN-2026-10-11.zip", ImportSourceRoute.Failed), (row.RoutedPath, row.Route)));
    }

    [Fact]
    public void A_run_that_threw_before_any_file_result_records_the_source_itself()
    {
        var row = Assert.Single(AutomatedOperationsService.LocationsFor(@"C:\In\broken.zip", @"C:\Failed\1-broken.zip", ImportSourceRoute.Failed, null));
        Assert.Equal(("broken.zip", (string?)null, @"C:\Failed\1-broken.zip"), (row.FileName, row.SourceSha256, row.RoutedPath));
    }

    [Fact]
    public void A_location_note_survives_the_automation_message_cut()
    {
        var message = AutomatedOperationsService.WithNote(new string('x', 600), AutomatedOperationsService.LocationNotSavedNote);
        Assert.EndsWith(AutomatedOperationsService.LocationNotSavedNote, message, StringComparison.Ordinal);
        Assert.True(message.Length <= Phase2OperationsRepository.AutomationMessageLimit);
        Assert.DoesNotMatch(@"[A-Za-z]:\\", AutomatedOperationsService.LocationNotSavedNote);
    }

    [Theory]
    [InlineData("C:\\ETP\\Failed\\file.xlsx", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    public void Only_a_row_with_a_name_and_a_path_that_fits_is_recorded(string path, bool recordable)
    {
        Assert.Equal(recordable, SqlServerImportSourceLocationStore.Recordable(new("file.xlsx", null, path, ImportSourceRoute.Hand)));
        Assert.False(SqlServerImportSourceLocationStore.Recordable(new("file.xlsx", null, "C:\\" + new string('p', 400), ImportSourceRoute.Hand)));
        Assert.False(SqlServerImportSourceLocationStore.Recordable(new(" ", null, "C:\\a.xlsx", ImportSourceRoute.Hand)));
    }

    [Theory]
    [InlineData("ABCDEF0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789", true)]
    [InlineData("not-a-hash", false)]
    [InlineData(null, false)]
    public void Only_a_sha256_is_kept_and_it_is_kept_upper_case(string? value, bool kept)
    {
        var normalised = SqlServerImportSourceLocationStore.NormalisedSha(value);
        Assert.Equal(kept, normalised is not null);
        if (normalised is not null) Assert.Equal(value!.ToUpperInvariant(), normalised);
    }

    [Theory]
    [InlineData(@"C:\ETP\Processed\20261012101112123-TITAN-2026-10-11.zip", true, "TITAN")]
    [InlineData(@"C:\ETP\Failed\0123456789abcdef0123456789abcdef-HELIOS-2026-10-11-r2.zip", true, "HELIOS")]
    [InlineData(@"C:\ETP\Failed\SERVICE-2026-10-11.zip", true, "SERVICE")]
    [InlineData(@"C:\ETP\Failed\20261012101112123-sales.xlsx", false, "")]
    [InlineData(@"C:\ETP\Failed\20261012101112123-Service Centre till 28 sep 2026.zip", false, "")]
    public void A_routed_shop_zip_is_recognised_behind_the_runs_prefix(string path, bool shop, string role)
    {
        Assert.Equal(shop, SqlServerImportFixService.TryParseRoutedShopZip(path, out var parsedRole, out var day));
        if (shop) Assert.Equal((role, Day), (parsedRole, day));
    }

    [Fact]
    public void Open_folder_is_offered_only_inside_the_configured_folders()
    {
        string[] roots = [@"C:\ETP\Inbound", @"C:\ETP\Processed\", @"C:\ETP\Failed"];
        Assert.True(SqlServerImportFixService.IsUnder(@"C:\ETP\Failed\1-a.zip", roots));
        Assert.True(SqlServerImportFixService.IsUnder(@"c:\etp\processed\Duplicate\1-a.zip", roots));
        Assert.False(SqlServerImportFixService.IsUnder(@"C:\ETP\FailedOther\1-a.zip", roots));
        Assert.False(SqlServerImportFixService.IsUnder(@"C:\ETP\Failed\..\Secret\a.zip", roots));
        Assert.False(SqlServerImportFixService.IsUnder(@"D:\Downloads\a.zip", roots));
        Assert.False(SqlServerImportFixService.IsUnder(@"C:\ETP\Failed\a.zip", []));
    }

    [Fact]
    public void The_rows_own_file_is_found_by_hash_first_then_by_name()
    {
        var summary = new FolderImportSummary(
        [
            new("sheet.xlsx", "D001", "TST01", Day, Day, "Imported") { SourceSha256 = new string('b', 64) },
            new("other.xlsx", "D001", "TST01", Day, Day, "Imported") { SourceSha256 = ShaA }
        ]);
        Assert.Equal("other.xlsx", SqlServerImportFixService.MatchingResult(summary, new("sheet.xlsx", ShaA.ToUpperInvariant(), null, null))!.FileName);
        Assert.Equal("sheet.xlsx", SqlServerImportFixService.MatchingResult(summary, new("SHEET.xlsx", null, null, null))!.FileName);
        Assert.Null(SqlServerImportFixService.MatchingResult(summary, new("missing.xlsx", null, null, null)));
    }

    [Fact]
    public void A_reapply_of_a_shop_zip_gets_the_next_sequence_of_that_day()
    {
        var root = Path.Combine(Path.GetTempPath(), "etp-shopresults-seq-" + Guid.NewGuid().ToString("N"));
        var pending = Path.Combine(root, "pending");
        var published = Path.Combine(root, "published");
        Directory.CreateDirectory(pending); Directory.CreateDirectory(published);
        try
        {
            var writer = new ShopResultWriter(pending);
            Assert.Equal(1, writer.NextSequence("TITAN", Day));
            File.WriteAllText(Path.Combine(published, "TITAN-2026-10-11-1.json"), "{}");
            File.WriteAllText(Path.Combine(pending, "TITAN-2026-10-11-2.json"), "{}");
            File.WriteAllText(Path.Combine(published, "TITAN-2026-10-12-7.json"), "{}");
            File.WriteAllText(Path.Combine(published, "HELIOS-2026-10-11-9.json"), "{}");
            Assert.Equal(3, writer.NextSequence("TITAN", Day));
            Assert.Equal(10, writer.NextSequence("HELIOS", Day));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Reopen_and_reapply_refuses_a_file_that_is_gone_before_it_reopens_anything()
    {
        var workflow = new RecordingWorkflow();
        var audits = new List<string>();
        var service = new SqlServerImportFixService(new EtpConnectionFactory(@"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;Encrypt=Optional"),
            null, new NoLocations(), new ShopResultWriter(Path.GetTempPath()), () => workflow,
            (type, outcome, detail, _) => { audits.Add(type); return Task.CompletedTask; }, _ => Task.FromResult<IReadOnlyList<string>>([]));
        var gone = new ImportFixLocation(new(1, null, null, "sheet.xlsx", ShaA, @"C:\does-not-exist\sheet.xlsx", ImportSourceRoute.Failed, DateTime.UtcNow), false, false);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReopenAndReapplyAsync(gone, new("sheet.xlsx", ShaA, "TST01", Day), "TST01", Day, "Synthetic reason"));

        Assert.Equal(SqlServerImportFixService.MissingFileMessage, refused.Message);
        Assert.Equal(0, workflow.Reopens);
        Assert.Empty(audits);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReopenAndReapplyAsync(gone, new("sheet.xlsx", ShaA, "TST01", Day), "TST01", Day, " "));
    }

    // ----- the migration and the audit types -----

    [Fact]
    public void Migration_0069_creates_the_table_with_the_viewer_denied_and_the_append_only_trigger_and_no_view_or_procedure()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "migrations", "0069_import_source_locations.sql"));
        Assert.Contains("CREATE TABLE dbo.import_source_locations", text, StringComparison.Ordinal);
        Assert.Contains("DENY SELECT,INSERT,UPDATE,DELETE ON dbo.import_source_locations TO etp_viewer;", text, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT,INSERT ON dbo.import_source_locations TO etp_owner,etp_store_manager;", text, StringComparison.Ordinal);
        Assert.Contains("THROW 51990,", text, StringComparison.Ordinal);
        Assert.Contains("CHECK(route IN ('Processed','Duplicate','Failed','Hand'))", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"CREATE\s+(OR\s+ALTER\s+)?(VIEW|PROCEDURE|FUNCTION)", RegexOptions.IgnoreCase), text);
        // THROW numbers stay inside lane U2's block.
        foreach (Match number in Regex.Matches(text, @"THROW\s+(\d+)"))
            Assert.InRange(int.Parse(number.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 51990, 51999);
    }

    [Fact]
    public void The_audit_repository_knows_every_type_0062_allows_and_the_two_no_sale_day_types()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "migrations", "0062_schema_objects_journal.sql"));
        var check = Regex.Match(text, @"CK_operational_audit_type CHECK\s*\(event_type IN \((?<list>[^)]*)\)\)", RegexOptions.Singleline);
        Assert.True(check.Success);
        var allowed = Regex.Matches(check.Groups["list"].Value, "'([A-Za-z]+)'").Select(match => match.Groups[1].Value).ToArray();
        Assert.Contains("ImportFixAction", allowed);
        Assert.Empty(allowed.Except(OperationalAuditRepository.SupportedEventTypes));
        Assert.Contains("NoSaleDayMarked", OperationalAuditRepository.SupportedEventTypes);
        Assert.Contains("NoSaleDayWithdrawn", OperationalAuditRepository.SupportedEventTypes);
        Assert.Equal("ImportFixAction", OperationalAuditRepository.ImportFixActionType);
        Assert.False(OperationalAuditRepository.ContainsPathOrIdentifier(SqlServerImportFixService.ReapplyAuditDetail));
        Assert.False(OperationalAuditRepository.ContainsPathOrIdentifier(SqlServerImportFixService.RetryAuditDetail));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }

    private sealed class NoLocations : IImportSourceLocationStore
    {
        public Task RecordAsync(IReadOnlyList<NewImportSourceLocation> locations, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportSourceLocation?> FindLatestAsync(string fileName, string? sourceSha256, CancellationToken cancellationToken = default) =>
            Task.FromResult<ImportSourceLocation?>(null);
    }

    private sealed class RecordingWorkflow : IDailyWorkflowCommands
    {
        public int Reopens { get; private set; }
        public Task SaveManualInputAsync(SaveDailyManualInput command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveStockCountAsync(SaveDailyStockCount command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveStaffTargetAsync(SaveDailyStaffTarget command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FinaliseAsync(FinaliseDailyWorkflow command, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReopenAsync(ReopenDailyWorkflow command, CancellationToken cancellationToken = default) { Reopens++; return Task.CompletedTask; }
    }
}
