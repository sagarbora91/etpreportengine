using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Morning;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.Morning;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12 U1 cross-layer (plan Appendix B item 4): three synthetic stores against the real schema, two ready and one
/// blocked. The Morning query reads them (and a real <c>diag.load_health_check_summary</c> row and a published shop
/// result file from a temp folder); "Finalise all" through C# locks the two ready stores in their own transactions, lists
/// the blocked one with its refusal, writes the DayFinalised audit rows, and the lock trigger then refuses a manual-input
/// edit on a locked store (51020). Synthetic store codes only; the pack step is a stub (the pack generator has its own tests).
/// </summary>
public sealed class MorningFinaliseAllSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private static readonly DateOnly Day = new(2026, 7, 15);
    private static readonly string[] Ready = ["SC911", "SC912"];
    private const string Blocked = "SC913";
    private const string User = "TEST\\morning-owner";
    private static readonly string[] RequiredReports = ["R025", "R022", "R013", "R003", "STOCK_LEDGER", "CLOSING_STOCK"];

    [SqlFact]
    public async Task Finalise_all_locks_the_two_ready_stores_lists_the_blocked_one_and_the_lock_trigger_refuses_a_later_edit()
    {
        await SeedAsync();
        var results = Path.Combine(Path.GetTempPath(), "EtpMorningSql", Guid.NewGuid().ToString("N"));
        try
        {
            // A published result naming SC911's sales lines file, written "at" 07:40 local.
            var published = Directory.CreateDirectory(Path.Combine(results, "published")).FullName;
            var resultFile = Path.Combine(published, $"TITAN-{Day:yyyy-MM-dd}-1.json");
            await File.WriteAllTextAsync(resultFile, $$"""{ "schemaVersion": 1, "role": "TITAN", "overall": "Accepted", "files": [ { "name": "{{FileName("SC911", "R025")}}" } ] }""");
            var resultTimeUtc = new DateTime(2026, 7, 16, 2, 10, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(resultFile, resultTimeUtc);

            // ---- the Morning page's read model against the real schema
            var query = new SqlServerMorningSummaryQuery(() => new EtpConnectionFactory(database.ConnectionString), shopResults: new ShopResultFolder(results),
                offPc: new NoOffPc(), clock: new FixedClock(new DateTimeOffset(2026, 7, 16, 3, 0, 0, TimeSpan.Zero)));
            var summary = await query.LoadAsync(Day);

            Assert.Equal(Ready, summary.ReadyStoreCodes.Where(code => code.StartsWith("SC91", StringComparison.Ordinal)).Order());
            var blocked = summary.Stores.Single(card => card.StoreCode == Blocked);
            Assert.Equal(MorningStoreStatus.Blocked, blocked.Status);
            Assert.Contains("Sales lines not imported yet", blocked.Blockers);
            var system = summary.Chips.Single(chip => chip.Label == "System check");
            Assert.Equal(("1 warning", MorningTone.Warning), (system.Word, system.Tone));
            Assert.Equal("AUTO_CLOSE is on.", system.Detail.Split(". ", 2)[1].TrimEnd());
            var sentBack = summary.Stores.Single(card => card.StoreCode == "SC911").Lines.Single(line => line.Label == "Sent back");
            Assert.Equal("Accepted", sentBack.Word);
            Assert.Contains(TimeZoneInfo.ConvertTimeFromUtc(resultTimeUtc, TimeZoneInfo.Local).ToString("HH:mm"), sentBack.Detail, StringComparison.Ordinal);

            // ---- Finalise all through C#: the real commands, one transaction per store
            var workflow = new SqlServerDailyWorkflowService(database.ConnectionString, _ => Task.FromResult(new ApplicationAccess(User, "Synthetic owner", ApplicationRole.Owner, true)));
            var lifecycle = new SqlServerDatabaseLifecycleService(database.ConnectionString);
            var finaliser = new MorningFinaliseAll<string>(() => new PassingPack(), () => workflow,
                (type, outcome, detail) => lifecycle.RecordAuditAsync(new(type, outcome, detail)), exception => exception.Message);

            var outcome = await finaliser.RunAsync(Day, [.. Ready, Blocked], User);

            Assert.Equal([true, true, false], outcome.Select(result => result.Locked));
            Assert.Contains("must be complete before finalisation", outcome[2].Message, StringComparison.Ordinal);
            foreach (var store in Ready)
                Assert.Equal("LOCKED", await Scalar<string>($"SELECT status FROM dbo.daily_reporting_days WHERE store_code='{store}' AND business_date='{Day:yyyyMMdd}'"));
            Assert.Equal("OPEN", await Scalar<string>($"SELECT status FROM dbo.daily_reporting_days WHERE store_code='{Blocked}' AND business_date='{Day:yyyyMMdd}'"));
            Assert.Equal(2, await Count($"dbo.daily_reporting_events WHERE business_date='{Day:yyyyMMdd}' AND event_type='DayFinalised' AND store_code LIKE 'SC91[12]'"));
            Assert.Equal(2, await Count("dbo.operational_audit WHERE event_type='DayFinalised'"));

            // ---- the lock protects the day: a later manual-input edit on a locked store is refused by the trigger
            var refused = await Assert.ThrowsAsync<SqlException>(() =>
                new DailyReportingWorkflowRepository(database.ConnectionString).SaveManualInputAsync("SC911", Day, "WALK_INS", 99m, null, User, "Late correction"));
            Assert.Equal(51020, refused.Number);

            // ---- the Morning page now shows the two as finalised and nothing left to finalise among them
            var after = await query.LoadAsync(Day);
            Assert.All(after.Stores.Where(card => Ready.Contains(card.StoreCode)), card => Assert.Equal(MorningStoreStatus.Locked, card.Status));
            Assert.DoesNotContain(after.ReadyStoreCodes, code => Ready.Contains(code));
        }
        finally
        {
            try { Directory.Delete(results, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private async Task SeedAsync()
    {
        await database.ExecuteAsync("""
            IF NOT EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=SUSER_SNAME())
              INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
              VALUES(SUSER_SNAME(),N'Synthetic owner','OWNER',1,SUSER_SNAME(),N'Synthetic test role');
            """);
        foreach (var store in Ready.Append(Blocked))
            await database.ExecuteAsync($"IF NOT EXISTS(SELECT 1 FROM dbo.stores WHERE store_code='{store}') INSERT dbo.stores(store_code,store_name,is_active) VALUES('{store}',N'Synthetic shop {store[^1]}',1)");

        foreach (var store in Ready)
        {
            // Every required export covers the day (the Close day rule), each its own completed batch.
            foreach (var report in RequiredReports)
                await database.ExecuteAsync($"""
                    DECLARE @batch uniqueidentifier=NEWID();
                    INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc) VALUES(@batch,'Completed','2026-07-16T01:00:00','2026-07-16T01:00:05');
                    INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end)
                    VALUES(@batch,N'{FileName(store, report)}',LOWER(CONVERT(char(64),HASHBYTES('SHA2_256',N'{FileName(store, report)}'),2)),1,'{report}','{store}','{Day:yyyyMMdd}','{Day:yyyyMMdd}','{Day:yyyyMMdd}');
                    """);
            // Every required manual input entered, through the screen's own write (so the day row exists and history is kept).
            var repository = new DailyReportingWorkflowRepository(database.ConnectionString);
            var state = await new SqlServerDailyWorkflowService(database.ConnectionString, _ => Task.FromResult(new ApplicationAccess(User, "Synthetic owner", ApplicationRole.Owner, true)))
                .LoadAsync(new DailyWorkflowScope(store, Day));
            foreach (var input in state.ManualInputs.Where(input => input.IsRequired && !input.IsPresent))
            {
                var text = input.ValueKind.Contains("TEXT", StringComparison.OrdinalIgnoreCase);
                await repository.SaveManualInputAsync(store, Day, input.FieldCode, text ? null : 0m, text ? "Synthetic" : null, User, "Synthetic close");
            }
            // The day row and one pack generation (FinaliseAsync marks it final; the pack generator is not under test here).
            await database.ExecuteAsync($"""
                IF NOT EXISTS(SELECT 1 FROM dbo.daily_reporting_days WHERE store_code='{store}' AND business_date='{Day:yyyyMMdd}')
                  INSERT dbo.daily_reporting_days(store_code,business_date,status) VALUES('{store}','{Day:yyyyMMdd}','OPEN');
                INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by)
                VALUES('{store}','{Day:yyyyMMdd}',1,REPLICATE('b',64),N'{EmptyJson}',N'synthetic');
                """);
        }
        await database.ExecuteAsync($"""
            IF NOT EXISTS(SELECT 1 FROM dbo.daily_reporting_days WHERE store_code='{Blocked}' AND business_date='{Day:yyyyMMdd}')
              INSERT dbo.daily_reporting_days(store_code,business_date,status) VALUES('{Blocked}','{Day:yyyyMMdd}','OPEN');
            """);

        // A real nightly run, read back through diag.load_health_check_summary by the Morning query.
        var started = new DateTimeOffset(2026, 7, 15, 21, 0, 0, TimeSpan.Zero);
        await new SqlServerHealthCheckStore(database.ConnectionString).RecordRunAsync(new HealthCheckRun(
            HealthCheckTrigger.Nightly, Day, started, started.AddSeconds(30), "1.12.0.0", "TESTPC", "Automation", HealthOutcome.Warning,
            new HealthCheckCounts(1, 1, 0, 0, 0), "morning-test.json",
            [
                new HealthCheckResult("DB-01", HealthCheckArea.Database, HealthOutcome.Pass, "OK", "Fine.", null, TimeSpan.FromMilliseconds(5), 1m),
                new HealthCheckResult("DB-04", HealthCheckArea.Database, HealthOutcome.Warning, "AUTO_CLOSE_ON", "AUTO_CLOSE is on.", null, TimeSpan.FromMilliseconds(12))
            ]));
    }

    private const string EmptyJson = "{}";

    private static string FileName(string store, string report) => $"synthetic-{store}-{report}.xlsx";

    private async Task<int> Count(string from) => Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM " + from));

    private async Task<T> Scalar<T>(string sql) => (T)(await database.ExecuteAsync(sql))!;

    private sealed class PassingPack : IDailyReportPackGenerator<string>
    {
        public Task<DailyPackGeneration<string>> GenerateAsync(DailyWorkflowScope scope, string? generatedBy = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DailyPackGeneration<string>(scope.StoreCode, scope.BusinessDate, DailyControlStatus.Passed,
                [new DailyPackSection("Sales", DailyControlStatus.Passed, 1m, 0m, "ok")], "ok", DateTimeOffset.UnixEpoch, "pack", 1, new string('b', 64)));

        public Task<string> GenerateCombinedAsync(DateOnly businessDate, string? generatedBy = null, CancellationToken cancellationToken = default) => Task.FromResult("combined");
    }

    private sealed class NoOffPc : IOffPcCopyEnvironment
    {
        public string? OneDriveRoot() => null;
        public string KeyFilePath() => string.Empty;
        public bool OneDriveIsRunning() => false;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
