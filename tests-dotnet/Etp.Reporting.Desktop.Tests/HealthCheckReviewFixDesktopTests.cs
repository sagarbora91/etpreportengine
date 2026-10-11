using System.Text.RegularExpressions;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Desktop.Diagnostics;
using Etp.Reporting.Desktop.Modules.HealthCheck;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.11.0 code review (111-CODE-REVIEW.md) findings 1, 2, 4 and 6 on the desktop side. Findings 1 and 2 are each
/// two rules that passed their own tests (the probe or log side, and the SQL side) and failed together, so these tests
/// put the real producer's output through the other side's rule. All values are synthetic.
/// </summary>
public sealed class HealthCheckReviewFixDesktopTests
{
    // diag.is_token (0054) and the CHECK on diag.error_occurrences.operation.
    private static bool IsSqlToken(string? value, int max) =>
        value is { Length: > 0 } && value.Length <= max && Regex.IsMatch(value, "^[A-Za-z0-9._:<>+-]+$");

    private static readonly DateTimeOffset Now = new(2026, 10, 11, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Finding_1_a_realistic_run_exports_with_no_redaction_so_err01_can_stay_a_pass()
    {
        var eventLog = EventLogProbe.Evaluate(new EventLogFindings(1, 0, new Dictionary<int, int> { [17204] = 2 }, new Dictionary<int, int> { [823] = 1 }, 0, 0));
        var ops06 = new HealthCheckResult(HealthCheckId.Ops06, HealthCheckArea.Operations, HealthOutcome.Warning, "SQL_RTM",
            "SQL Server 17.0.1000.7 (RTM) has no cumulative update installed; install the latest cumulative update (see SQL-CU-AND-AUTOCLOSE-RUNBOOK).",
            "Install the latest SQL Server cumulative update.", TimeSpan.Zero, null,
            [new("sql-server", HealthOutcome.Pass, null, null, new Dictionary<string, string>
                { ["version"] = "17.0.1000.7", ["updateLevel"] = "RTM", ["edition"] = HealthCheckTexts.DetailToken("Express Edition (64-bit)") })]);
        var data01 = new HealthCheckResult(HealthCheckId.Data01, HealthCheckArea.Data, HealthOutcome.Warning, "DAY_INCOMPLETE",
            "09 Oct 2026 is not complete: exports missing 2 (stores HEMW,WLMHW; R022,R025).", "import-files", TimeSpan.Zero, 2m,
            [new("EXPORTS_MISSING", HealthOutcome.Warning, "EXPORTS_MISSING", 2m, new Dictionary<string, string> { ["codes"] = "R022,R025", ["stores"] = "HEMW,WLMHW" })]);
        var err01 = new HealthCheckResult(HealthCheckId.Err01, HealthCheckArea.Errors, HealthOutcome.Pass, "OK", "Ingested 3 entries.", null, TimeSpan.Zero);
        var results = new[] { eventLog, ops06, data01, err01 }.Select(HealthCheckRunner.Normalise).ToArray();

        Assert.Contains(results[0].SubResults!, sub => sub.Key == "event:17204");
        Assert.All(results.SelectMany(line => line.SubResults ?? []), sub =>
        {
            Assert.True(IsSqlToken(sub.Key, 100), sub.Key);
            Assert.All(sub.Fields ?? new Dictionary<string, string>(), field => Assert.True(IsSqlToken(field.Value, 100), field.Key + "=" + field.Value));
        });

        var counts = HealthExportWriter.Recount(results);
        var run = new HealthCheckRun(HealthCheckTrigger.Nightly, new DateOnly(2026, 10, 10), Now.AddMinutes(-1), Now, "1.11.0.0",
            "TESTPC", "Automation", HealthExportWriter.OverallOf(counts), counts, null, results);
        var writer = new HealthExportWriter(Path.Combine(Path.GetTempPath(), "EtpHealthExportUnused"), []);
        var redactions = writer.CountRedactions(new HealthExportInput(run, null, []));

        Assert.Equal(0, redactions);
        Assert.Equal(HealthOutcome.Pass, HealthExportWriter.WithRedactionWarning(run, redactions).Results.Single(line => line.CheckId == HealthCheckId.Err01).Outcome);
    }

    [Fact]
    public void Finding_1_privacy_a_name_or_phone_in_a_detail_never_reaches_the_export()
    {
        var line = HealthCheckRunner.Normalise(new HealthCheckResult(HealthCheckId.Data01, HealthCheckArea.Data, HealthOutcome.Warning, "DAY_INCOMPLETE",
            "Day is not complete.", null, TimeSpan.Zero, null,
            [new("Ramesh Kumar", HealthOutcome.Warning, "EXPORTS_MISSING", 1m,
                new Dictionary<string, string> { ["customer"] = "Ramesh Kumar", ["mobile"] = "98765 43210", ["bill"] = "INV-0012345", ["codes"] = "R022, R025" })]));
        var counts = HealthExportWriter.Recount([line]);
        var run = new HealthCheckRun(HealthCheckTrigger.Nightly, new DateOnly(2026, 10, 10), Now.AddMinutes(-1), Now, "1.11.0.0",
            "TESTPC", "Automation", HealthExportWriter.OverallOf(counts), counts, null, [line]);
        var writer = new HealthExportWriter(Path.Combine(Path.GetTempPath(), "EtpHealthExportUnused"), []);
        var guard = writer.CreateGuard();

        var text = System.Text.Encoding.UTF8.GetString(writer.Serialize(new HealthExportInput(run, null, []), guard));

        foreach (var fragment in new[] { "Ramesh", "43210", "0012345" })
            Assert.DoesNotContain(fragment, text, StringComparison.Ordinal);
        Assert.Contains("R022+R025", text, StringComparison.Ordinal);
        // The guard still refuses an invoice-like token by itself: the runner's list join does not hide it.
        Assert.Equal(HealthExportPrivacyGuard.Redacted, writer.CreateGuard().Token("INV-0012345"));
    }

    [Theory]
    [InlineData("Run report", "Run_report")]
    [InlineData("Health check report probe failed", "Health_check_report_probe_failed")]
    [InlineData("Error register ingest", "Error_register_ingest")]
    [InlineData("Import (folder) / retry, later", "Import_folder_retry+later")]
    public void Finding_2_every_operation_the_log_accepts_is_a_register_token(string operation, string expected)
    {
        var logged = DesktopDiagnostics.SafeOperation(operation);
        Assert.Equal(operation, logged);

        var stored = ErrorRegisterIngest.RegisterOperation(logged);

        Assert.Equal(expected, stored);
        Assert.True(IsSqlToken(stored, ErrorRegisterIngest.MaxOperationLength));
    }

    [Fact]
    public void Finding_4_end_known_names_the_line_code_and_every_known_sub_finding_code()
    {
        var line = new HealthCheckResult(HealthCheckId.Data01, HealthCheckArea.Data, HealthOutcome.Known, "DAY_INCOMPLETE", "x", null, TimeSpan.Zero, null,
        [
            new("EXPORTS_MISSING", HealthOutcome.Known, "EXPORTS_MISSING"),
            new("INPUTS_MISSING", HealthOutcome.Known, "INPUTS_MISSING"),
            new("TARGETS", HealthOutcome.Pass, "STAFF_TARGETS_MISSING"),
            new("OTHER", HealthOutcome.Known)
        ]);

        Assert.Equal(["DAY_INCOMPLETE", "EXPORTS_MISSING", "INPUTS_MISSING"], HealthCheckView.KnownCodesOf(line));
    }

    [Theory]
    [InlineData("S-1-5-21-1000000001-2000000002-3000000003-1001", null, true)]       // an account Windows cannot resolve
    [InlineData("S-1-5-21-1000000001-2000000002-3000000003-1001", 1, true)]
    [InlineData("S-1-5-21-1000000001-2000000002-3000000003-513", null, false)]       // Domain Users
    [InlineData("S-1-5-21-1000000001-2000000002-3000000003-500", 1, false)] // built-in RID
    [InlineData("S-1-5-21-1000000001-2000000002-3000000003-1005", 4, false)] // a local group
    [InlineData("S-1-5-21-1000000001-2000000002-3000000003-1006", 2, false)]
    [InlineData("S-1-5-32-545", null, false)]                                          // BUILTIN\Users
    [InlineData("S-1-1-0", null, false)]                                               // Everyone
    public void Finding_6_health_exports_modifiers_must_be_individual_accounts(string sid, int? use, bool expected)
    {
        Assert.Equal(expected, ProtectedOperationPath.IsIndividualAccount(sid, _ => (ProtectedOperationPath.AccountUse?)use));
    }
}
