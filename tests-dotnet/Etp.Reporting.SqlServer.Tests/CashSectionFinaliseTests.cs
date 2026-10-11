using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>Decision 32 (11 Oct 2026): Service cash is not required to finalise, and a cash variance warns instead of blocking.</summary>
public sealed class CashSectionFinaliseTests
{
    private static CashReconciliationResult Cash(ReconciliationStatus status, decimal? service, decimal? opening = 1000m, decimal? retail = 500m,
        decimal? expenses = 10m, decimal? deposit = 400m, decimal? closing = null, decimal? counted = null, decimal? variance = null) =>
        new("WLMHW", new DateOnly(2026, 10, 10), opening, retail, service, expenses, deposit, 0m, closing, counted, variance, status, "msg");

    [Fact]
    public void Missing_service_cash_alone_is_not_run_and_names_the_screen()
    {
        var cash = Cash(ReconciliationStatus.Blocked, service: null);
        Assert.Equal(ReconciliationStatus.NotRun, DailyReportingPackService.CashSectionStatus(cash));
        var message = DailyReportingPackService.CashSectionMessage(cash);
        Assert.Contains("not required to finalise", message);
        Assert.Contains("Today > Cash > Cash and service entries", message);
    }

    [Theory]
    [InlineData("opening")]
    [InlineData("expenses")]
    [InlineData("deposit")]
    [InlineData("retail")]
    public void Other_missing_evidence_still_blocks(string missing)
    {
        var cash = Cash(ReconciliationStatus.Blocked, service: null,
            opening: missing == "opening" ? null : 1000m, retail: missing == "retail" ? null : 500m,
            expenses: missing == "expenses" ? null : 10m, deposit: missing == "deposit" ? null : 400m);
        Assert.Equal(ReconciliationStatus.Blocked, DailyReportingPackService.CashSectionStatus(cash));
        Assert.Equal("msg", DailyReportingPackService.CashSectionMessage(cash));
    }

    [Fact]
    public void A_cash_variance_stays_failed_so_it_warns_with_its_amount()
    {
        var cash = Cash(ReconciliationStatus.Failed, service: 50m, closing: 1140m, counted: 1100m, variance: -40m);
        Assert.Equal(ReconciliationStatus.Failed, DailyReportingPackService.CashSectionStatus(cash));
        Assert.Equal(-40m, cash.Variance);
    }

    [Fact]
    public void A_passed_cash_balance_is_unchanged()
    {
        Assert.Equal(ReconciliationStatus.Passed,
            DailyReportingPackService.CashSectionStatus(Cash(ReconciliationStatus.Passed, service: 50m, closing: 1140m, counted: 1140m, variance: 0m)));
    }
}
