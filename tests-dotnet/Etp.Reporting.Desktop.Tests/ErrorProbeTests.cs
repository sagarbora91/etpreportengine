using System.Security.AccessControl;
using Etp.Reporting.Application.ErrorRegister;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Desktop.Diagnostics;
using Etp.Reporting.Desktop.Modules.HealthCheck;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.11.0 ERR-01..04 (integrator; design 5.2 Errors). The register is a list in memory; nothing reads a database or a log.
/// </summary>
public sealed class ErrorProbeTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 2, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Previous = Start.AddDays(-1);

    private static HealthCheckContext Context(DateTimeOffset? previousRun = null) => new(
        new DateOnly(2026, 10, 11), HealthCheckTrigger.Nightly, Start, ["ZZSYN"],
        previousRun is { } at ? new HealthCheckPreviousRun(7, at, []) : null,
        HealthCheckBudget.Default, new Dictionary<string, object>());

    private static ErrorRegisterEntry Entry(int id, string eventCode, ErrorStatus status, DateTimeOffset firstSeen, DateTimeOffset? lastSeen = null,
        ErrorRegisterSeverity severity = ErrorRegisterSeverity.Error, DateTimeOffset? recurred = null, int? mergedInto = null) => new(
        id, new string('a', 64), 1, eventCode, null, "Test.Source", null, "InvalidOperationException", 0, severity, status,
        firstSeen, lastSeen ?? firstSeen, 3, "1.11.0.0", "1.11.0.0", status is ErrorStatus.Fixed or ErrorStatus.Recurred ? "1.10.1" : null,
        null, null, recurred, recurred is null ? null : "1.11.0.0", [], null, firstSeen, "Ingest", mergedInto);

    private static ErrorRegisterIngestRun Ingest(int accepted = 2, int rejected = 0, int skippedInvalid = 0, bool failed = false, bool busy = false) =>
        new(accepted, accepted, skippedInvalid, 0, 1, accepted, 1, rejected, 0, 0, failed, busy);

    private static async Task<IReadOnlyList<HealthCheckResult>> Run(IReadOnlyList<ErrorRegisterEntry>? register, ErrorRegisterIngestRun? ingest,
        bool ingestAttempted = true, DateTimeOffset? previousRun = null, Exception? loadFailure = null)
    {
        var probe = new ErrorProbes(
            register is null && loadFailure is null ? null : _ => loadFailure is null ? Task.FromResult(register!) : Task.FromException<IReadOnlyList<ErrorRegisterEntry>>(loadFailure),
            ingestAttempted ? () => ingest : null);
        return await probe.RunAsync(Context(previousRun), CancellationToken.None);
    }

    [Fact]
    public async Task Returns_the_four_ids_in_catalogue_order_with_the_Errors_area()
    {
        var results = await Run([], Ingest());
        Assert.Equal([HealthCheckId.Err01, HealthCheckId.Err02, HealthCheckId.Err03, HealthCheckId.Err04], results.Select(line => line.CheckId));
        Assert.All(results, line => Assert.Equal(HealthCheckArea.Errors, line.Area));
        Assert.All(results, line => Assert.Equal(HealthOutcome.Pass, line.Outcome));
        Assert.Equal(new ErrorProbes(null, null).CheckIds, results.Select(line => line.CheckId));
    }

    [Fact]
    public void Err01_follows_the_ingest_result()
    {
        Assert.Equal(HealthOutcome.Pass, ErrorProbes.EvaluateIngest(true, Ingest(), TimeSpan.Zero).Outcome);
        var rejected = ErrorProbes.EvaluateIngest(true, Ingest(rejected: 1, skippedInvalid: 2), TimeSpan.Zero);
        Assert.Equal((HealthOutcome.Warning, ErrorProbes.EntriesRejected, 3m), (rejected.Outcome, rejected.FindingCode, rejected.MetricValue));
        Assert.Equal((HealthOutcome.Fail, ErrorProbes.IngestFailed), Pair(ErrorProbes.EvaluateIngest(true, Ingest(failed: true), TimeSpan.Zero)));
        Assert.Equal((HealthOutcome.Fail, ErrorProbes.IngestFailed), Pair(ErrorProbes.EvaluateIngest(true, null, TimeSpan.Zero)));
        Assert.Equal((HealthOutcome.Skipped, ErrorProbes.IngestBusy), Pair(ErrorProbes.EvaluateIngest(true, Ingest(busy: true), TimeSpan.Zero)));
        Assert.Equal((HealthOutcome.Skipped, ErrorProbes.IngestNotRun), Pair(ErrorProbes.EvaluateIngest(false, null, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Err02_warns_for_new_errors_since_the_previous_run_and_fails_for_a_new_critical()
    {
        var old = Entry(1, "REPORT_FAILED", ErrorStatus.New, Previous.AddHours(-2));
        var fresh = Entry(2, "REPORT_FAILED", ErrorStatus.New, Previous.AddHours(1));
        var seen = Entry(3, "CASH_BOOK_FAILED", ErrorStatus.Seen, Previous.AddHours(1));
        var warning = (await Run([old, fresh, seen], Ingest(), previousRun: Previous))[1];
        Assert.Equal((HealthOutcome.Warning, ErrorProbes.NewErrors), Pair(warning));
        Assert.Equal(1m, warning.MetricValue);
        Assert.Equal("ER-0002", Assert.Single(warning.SubResults!).Key);
        Assert.Contains("ER-0002", warning.Explanation, StringComparison.Ordinal);

        var critical = Entry(4, "SAVE_FAILED", ErrorStatus.New, Previous.AddHours(2), severity: ErrorRegisterSeverity.Critical);
        var fail = (await Run([fresh, critical], Ingest(), previousRun: Previous))[1];
        Assert.Equal((HealthOutcome.Fail, ErrorProbes.NewCritical), Pair(fail));
        Assert.Equal("ER-0004", fail.SubResults![0].Key);
    }

    [Fact]
    public async Task Without_a_previous_run_new_means_the_last_24_hours()
    {
        var results = await Run([Entry(1, "X_FAILED", ErrorStatus.New, Start.AddHours(-25)), Entry(2, "X_FAILED", ErrorStatus.New, Start.AddHours(-23))], Ingest());
        Assert.Equal(1m, results[1].MetricValue);
    }

    [Fact]
    public async Task Err03_fails_while_any_entry_is_recurred()
    {
        var results = await Run([Entry(5, "CASH_BOOK_FAILED", ErrorStatus.Recurred, Start.AddDays(-30), Start.AddHours(-1), recurred: Start.AddHours(-1)),
            Entry(6, "OTHER_FAILED", ErrorStatus.Fixed, Start.AddDays(-30))], Ingest(), previousRun: Previous);
        Assert.Equal((HealthOutcome.Fail, ErrorProbes.Recurred), Pair(results[2]));
        Assert.Contains("1 since the previous run", results[2].Explanation, StringComparison.Ordinal);
        Assert.Equal(HealthOutcome.Pass, (await Run([Entry(6, "OTHER_FAILED", ErrorStatus.Fixed, Start.AddDays(-30))], Ingest()))[2].Outcome);
    }

    [Theory]
    [InlineData("DISPATCHER_UNHANDLED", true)]
    [InlineData("APPDOMAIN_UNHANDLED", true)]
    [InlineData("TASK_UNOBSERVED", true)]
    [InlineData("STARTUP_FAILED", true)]
    [InlineData("WINDOW_CLOSE_FAILED", true)]
    [InlineData("IMPORT_UNHANDLED", true)]
    [InlineData("REPORT_FAILED", false)]
    [InlineData("", false)]
    public void Crash_like_event_codes(string code, bool crashLike) => Assert.Equal(crashLike, ErrorProbes.IsCrashLike(code));

    [Fact]
    public async Task Err04_fails_for_a_crash_seen_since_the_previous_run_but_not_for_an_expected_or_older_one()
    {
        var recent = Entry(7, "DISPATCHER_UNHANDLED", ErrorStatus.Seen, Start.AddDays(-10), Previous.AddHours(3));
        var expected = Entry(8, "STARTUP_FAILED", ErrorStatus.Expected, Start.AddDays(-10), Previous.AddHours(3));
        var older = Entry(9, "TASK_UNOBSERVED", ErrorStatus.New, Start.AddDays(-10), Previous.AddHours(-3));
        var merged = Entry(10, "APPDOMAIN_UNHANDLED", ErrorStatus.New, Previous.AddHours(1), mergedInto: 7);
        var line = (await Run([recent, expected, older, merged], Ingest(), previousRun: Previous))[3];
        Assert.Equal((HealthOutcome.Fail, ErrorProbes.CrashLike), Pair(line));
        Assert.Equal("ER-0007", Assert.Single(line.SubResults!).Key);
        Assert.Equal(HealthOutcome.Pass, (await Run([expected, older], Ingest(), previousRun: Previous))[3].Outcome);
    }

    [Fact]
    public async Task A_permission_refusal_of_the_register_is_skipped_and_no_register_is_not_available()
    {
        var notReadable = await Run(null, Ingest(), loadFailure: SqlExceptionFactory.Create(new SqlExceptionFactory.Error(229, "permission denied")));
        Assert.All(notReadable.Skip(1), line => Assert.Equal((HealthOutcome.Skipped, HealthCheckTexts.NotPermitted), Pair(line)));
        Assert.Equal(HealthOutcome.Pass, notReadable[0].Outcome);

        var noRegister = await Run(null, Ingest());
        Assert.All(noRegister.Skip(1), line => Assert.Equal((HealthOutcome.Skipped, HealthCheckTexts.NotAvailable), Pair(line)));
    }

    [Fact]
    public async Task Any_other_register_failure_reaches_the_runner()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => Run(null, Ingest(), loadFailure: new InvalidOperationException("boom")));

    [Fact]
    public async Task Lines_carry_ids_codes_and_counts_only()
    {
        var results = await Run([Entry(11, "REPORT_FAILED", ErrorStatus.New, Previous.AddHours(1))], Ingest(), previousRun: Previous);
        foreach (var sub in results.SelectMany(line => line.SubResults ?? []))
        {
            Assert.Matches("^ER-[0-9]{4}$", sub.Key);
            Assert.Equal(["event", "severity", "status"], sub.Fields!.Keys.Order(StringComparer.Ordinal));
        }
        Assert.All(results, line => Assert.DoesNotContain("Test.Source", line.Explanation, StringComparison.Ordinal));
    }

    // ---- 1.11.0 integrator: F1 on the two new screens, and OPS-05's HealthExports rule ----

    [Theory]
    [InlineData("health-check")]
    [InlineData("error-register")]
    public void F1_on_the_diagnostics_screens_opens_the_health_check_topic(string taskId)
    {
        Assert.Equal("health-check", ContextHelpRouter.ResolveTopicId("Operations Centre", null, taskId));
        Assert.NotNull(HelpCentreRegistry.Find("health-check"));
        Assert.Equal("exception-centre", ContextHelpRouter.ResolveTopicId("Operations Centre"));
    }

    private const string Owner = "S-1-5-21-1000000001-2000000002-3000000003-1001";

    [Fact]
    public void Health_exports_may_be_modified_by_individual_accounts_but_not_by_groups_or_re_permissioned()
    {
        static Exception? Target(string ace, Func<string, bool>? modifier) =>
            Record.Exception(() => ProtectedOperationPath.Check(@"C:\ProgramData\EtpReporting\HealthExports", Folder("O:BAG:SYD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)" + ace),
                ProtectedOperationPath.Role.Target, targetModifier: modifier));
        var modify = $"(A;OICI;0x1301bf;;;{Owner})";
        Assert.NotNull(Target(modify, null));
        Assert.Null(Target(modify, ProtectedOperationPath.IsIndividualAccount));
        Assert.NotNull(Target("(A;OICI;0x1301bf;;;BU)", ProtectedOperationPath.IsIndividualAccount));
        Assert.NotNull(Target("(A;OICI;0x1301bf;;;AU)", ProtectedOperationPath.IsIndividualAccount));
        Assert.NotNull(Target($"(A;OICI;FA;;;{Owner})", ProtectedOperationPath.IsIndividualAccount));
        Assert.NotNull(Target($"(A;OICI;WD;;;{Owner})", ProtectedOperationPath.IsIndividualAccount));
    }

    private static DirectorySecurity Folder(string sddl)
    {
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm(sddl);
        return security;
    }

    private static (HealthOutcome, string) Pair(HealthCheckResult line) => (line.Outcome, line.FindingCode);
}
