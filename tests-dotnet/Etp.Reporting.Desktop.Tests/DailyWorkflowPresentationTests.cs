using System.Globalization;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DailyWorkflowPresentationTests
{
    [Fact]
    public void Snapshot_is_projected_to_stable_ui_state_and_cleared_on_failure()
    {
        var session = new DailyWorkflowPresentationSession();
        var manual = new DailyManualInput("WALK_INS", "Walk-ins", "NUMBER", 12m, null, true, null, null);
        var state = new DailyWorkflowState(
            "WLMHW", new DateOnly(2026, 8, 25), DailyWorkflowStatus.ReadyWithWarnings,
            ["R025"], ["R022"], [manual], ["MONTHLY_TARGET"], true, "Ready with one warning.");

        var presentation = session.Show(state, []);

        Assert.True(session.HasSnapshot);
        Assert.Equal(DailyWorkflowTone.Warning, presentation.Tone);
        Assert.Equal("Missing ETP sources: R022", presentation.SourceStatus);
        Assert.Equal("Missing manual inputs: MONTHLY_TARGET", presentation.InputStatus);
        Assert.Same(manual, Assert.Single(presentation.ManualInputs));
        Assert.True(presentation.CanFinalise);

        var unavailable = session.ShowUnavailable("SQL unavailable");
        Assert.False(session.HasSnapshot);
        Assert.False(unavailable.IsAvailable);
        Assert.Equal("Unavailable", unavailable.Status);
        Assert.False(unavailable.CanFinalise);
    }

    [Fact]
    public void Scope_and_manual_input_validation_preserve_zero_missing_and_walk_in_rules()
    {
        var session = new DailyWorkflowPresentationSession();
        var scope = session.SelectScope(" WLMHW ", new DateTime(2026, 8, 25));

        Assert.Equal("WLMHW", scope.StoreCode);
        Assert.Equal(new DateOnly(2026, 8, 25), scope.BusinessDate);
        var zero = session.CreateManualInput(scope, "WALK_INS", "0", "owner", "Daily count", CultureInfo.InvariantCulture);
        Assert.Equal(0m, zero.NumericValue);
        Assert.Null(zero.TextValue);
        var remark = session.CreateManualInput(scope, "OPERATIONAL_REMARK", "  Store event  ", "owner", "Note", CultureInfo.InvariantCulture);
        Assert.Null(remark.NumericValue);
        Assert.Equal("Store event", remark.TextValue);

        Assert.Throws<InvalidOperationException>(() => session.SelectScope(null, new DateTime(2026, 8, 25)));
        Assert.Throws<InvalidOperationException>(() => session.SelectScope("WLMHW", null));
        Assert.Throws<InvalidOperationException>(() => session.CreateManualInput(scope, "WALK_INS", "1.5", "owner", "Count", CultureInfo.InvariantCulture));
        Assert.Throws<InvalidOperationException>(() => session.CreateManualInput(scope, "WALK_INS", "-1", "owner", "Count", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Finalise_transition_uses_application_pack_status_without_recalculating_reports()
    {
        var scope = new DailyWorkflowScope("HEMW", new DateOnly(2026, 8, 25));
        var sections = new[]
        {
            new DailyPackSection("Sales", DailyControlStatus.Passed, 10m, 0m, "Passed"),
            new DailyPackSection("Tender", DailyControlStatus.Blocked, null, null, "Missing")
        };

        var command = DailyWorkflowPresentationSession.CreateFinalise(scope, "manager", sections);

        Assert.True(command.HasBlockingReconciliationExceptions);
        Assert.Equal(scope, command.Scope);
    }

    // RA-OPS-09: SERVICE_CASH/CARD/UPI are not required to finalise; a day without them (Service section NotRun) and
    // staff/tender variances (Failed) finalise with warnings instead of being refused.
    [Fact]
    public void Missing_non_required_service_fields_and_variances_finalise_with_warnings()
    {
        var scope = new DailyWorkflowScope("WLMHW", new DateOnly(2026, 9, 26));
        var sections = new[]
        {
            new DailyPackSection("Daily Sales Report", DailyControlStatus.Passed, 10m, 0m, "Passed"),
            new DailyPackSection(DailyReportingPackService.ServiceSectionName, DailyControlStatus.NotRun, null, null, "Service cash, card and UPI are not entered (not required to finalise)."),
            new DailyPackSection("Staff / CRO Performance", DailyControlStatus.Failed, 10m, 0.01m, "Attributed and canonical sales differ"),
            new DailyPackSection("Tender Reconciliation", DailyControlStatus.Failed, 10m, 5m, "1 document variance"),
            new DailyPackSection("Manual Operational Inputs", DailyControlStatus.Passed, 4m, 0m, "Required manual inputs are complete."),
            new DailyPackSection(DailyReportingPackService.FinalisationSectionName, DailyControlStatus.NotRun, null, null, "Awaiting finalisation.")
        };

        var command = DailyWorkflowPresentationSession.CreateFinalise(scope, "manager", sections);

        Assert.False(command.HasBlockingReconciliationExceptions);
        Assert.Equal(["Service Sale Report", "Staff / CRO Performance", "Tender Reconciliation"],
            DailyWorkflowPresentationSession.FinaliseWarnings(sections).Select(x => x.Report));
        Assert.Equal(
            "Business day finalised and dashboard readiness refreshed. Finalised with 3 warning(s): Service Sale Report (not entered); Staff / CRO Performance (variance); Tender Reconciliation (variance).",
            DailyWorkflowPresentationSession.Finalised(sections));
    }

    [Fact]
    public void Missing_required_input_still_refuses_finalisation()
    {
        var scope = new DailyWorkflowScope("WLMHW", new DateOnly(2026, 9, 26));
        var sections = new[]
        {
            new DailyPackSection("Daily Sales Report", DailyControlStatus.Passed, 10m, 0m, "Passed"),
            new DailyPackSection(DailyReportingPackService.ServiceSectionName, DailyControlStatus.Passed, 3m, null, "Entered"),
            new DailyPackSection("Manual Operational Inputs", DailyControlStatus.Blocked, 3m, 1m, "Missing: OPENING_CASH")
        };

        var command = DailyWorkflowPresentationSession.CreateFinalise(scope, "manager", sections);

        Assert.True(command.HasBlockingReconciliationExceptions);
    }

    [Fact]
    public void A_fully_passed_pack_finalises_without_warnings()
    {
        var sections = new[]
        {
            new DailyPackSection("Daily Sales Report", DailyControlStatus.Passed, 10m, 0m, "Passed"),
            new DailyPackSection(DailyReportingPackService.FinalisationSectionName, DailyControlStatus.NotRun, null, null, "Awaiting finalisation.")
        };

        Assert.Empty(DailyWorkflowPresentationSession.FinaliseWarnings(sections));
        Assert.Equal("Business day finalised and dashboard readiness refreshed.", DailyWorkflowPresentationSession.Finalised(sections));
    }
}
