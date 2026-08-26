using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class CashBalanceReconciliationServiceTests
{
    [Fact]
    public void Reconciles_retail_and_service_cash_without_mixing_non_cash_tenders()
    {
        var result = new CashBalanceReconciliationService().Reconcile(new(
            OpeningCash: 1_000m, RetailCash: 4_395m, ServiceCash: 700m, Expenses: 95m,
            CashDeposit: 5_000m, Adjustment: 0m, CountedClosingCash: 1_000m));

        Assert.Equal(1_000m, result.CalculatedClosingCash);
        Assert.Equal(0m, result.Variance);
        Assert.Equal(ReconciliationStatus.Passed, result.Status);
    }

    [Fact]
    public void Zero_is_valid_but_missing_is_blocked()
    {
        var zero = new CashBalanceReconciliationService().Reconcile(new(0m, 0m, 0m, 0m, 0m, 0m, 0m));
        var missing = new CashBalanceReconciliationService().Reconcile(new(null, 0m, 0m, 0m, 0m, 0m, 0m));

        Assert.Equal(ReconciliationStatus.Passed, zero.Status);
        Assert.Equal(ReconciliationStatus.Blocked, missing.Status);
        Assert.Contains("OPENING_CASH", missing.MissingInputs);
    }

    [Fact]
    public void Variance_is_not_hidden_or_rounded_away()
    {
        var result = new CashBalanceReconciliationService().Reconcile(new(0m, 100m, 0m, 0m, 0m, 0m, 98m));

        Assert.Equal(-2m, result.Variance);
        Assert.Equal(ReconciliationStatus.Failed, result.Status);
    }
}
