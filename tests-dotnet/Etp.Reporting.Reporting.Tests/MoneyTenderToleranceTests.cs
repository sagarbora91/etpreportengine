using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

/// <summary>
/// Gap #7 (MONEY-TEST-GAPS.md), decision 68: the invoice/tender control tolerance is Rs 1.00 (|variance| &lt;= 1.00 passes).
/// Tests that use <see cref="RetailReportingPolicy.Tender"/> rely on lane F1's production change (merged in 1.12.1).
/// Tests with an explicit rule (tolerance 0) or a ROUND_OFF tender pass on today's code. Values are synthetic and hand-computed.
/// </summary>
public sealed class MoneyTenderToleranceTests
{
    private static readonly ApprovedControlRule Exact = new("m3-exact", 0m);

    private static InvoiceTenderReconciliation Run(decimal invoice, ApprovedControlRule rule, params (string Type, decimal Amount)[] tenders) =>
        new InvoiceTenderReconciliationService().Reconcile(
            [new("S1", "I1", invoice)],
            tenders.Select(x => new TenderControlValue("S1", "I1", x.Type, x.Amount, true)),
            rule);

    [Fact]
    public void Production_tender_rule_allows_one_rupee()
    {
        Assert.Equal(1m, RetailReportingPolicy.Tender.AbsoluteTolerance);
    }

    [Fact]
    public void Forty_paise_short_passes_under_the_production_rule_and_keeps_its_variance()
    {
        // 1,129.40 - 1,129.00 = 0.40, inside Rs 1.
        var r = Run(1129.40m, RetailReportingPolicy.Tender, ("CASH", 1129.00m));
        Assert.Equal(ReconciliationStatus.Passed, r.Status);
        var d = Assert.Single(r.Documents);
        Assert.Equal(0.40m, d.Variance);
        Assert.Equal(0.40m, r.Variance);
    }

    [Fact]
    public void Exactly_one_rupee_either_way_passes_under_the_production_rule()
    {
        Assert.Equal(ReconciliationStatus.Passed, Run(1130.00m, RetailReportingPolicy.Tender, ("CASH", 1129.00m)).Status); // +1.00
        Assert.Equal(ReconciliationStatus.Passed, Run(1129.00m, RetailReportingPolicy.Tender, ("CASH", 1130.00m)).Status); // -1.00
        Assert.Equal(ReconciliationStatus.Passed, Run(1129.00m, RetailReportingPolicy.Tender, ("CASH", 1129.99m)).Status); // -0.99
    }

    [Fact]
    public void One_rupee_and_one_paisa_fails_under_the_production_rule_and_keeps_its_variance()
    {
        // Fails today (tolerance 0) and after F1 (tolerance 1): this edge holds either way.
        var over = Run(1130.01m, RetailReportingPolicy.Tender, ("CASH", 1129.00m));
        Assert.Equal(ReconciliationStatus.Failed, over.Status);
        Assert.Equal(1.01m, Assert.Single(over.Documents).Variance);
        var under = Run(1129.00m, RetailReportingPolicy.Tender, ("CASH", 1130.01m));
        Assert.Equal(ReconciliationStatus.Failed, under.Status);
        Assert.Equal(-1.01m, Assert.Single(under.Documents).Variance);
    }

    [Fact]
    public void Tolerance_applies_per_document_not_to_the_total()
    {
        // Two documents each 0.60 short: each passes; the total variance 1.20 is shown but does not fail the control.
        var r = new InvoiceTenderReconciliationService().Reconcile(
            [new("S1", "I1", 100.60m), new("S1", "I2", 200.60m)],
            [new("S1", "I1", "CASH", 100.00m, true), new("S1", "I2", "CASH", 200.00m, true)],
            RetailReportingPolicy.Tender);
        Assert.Equal(ReconciliationStatus.Passed, r.Status);
        Assert.Equal(1.20m, r.Variance);
        Assert.All(r.Documents, x => Assert.Equal(0.60m, x.Variance));
    }

    [Fact]
    public void One_failing_document_fails_the_control_even_when_totals_cancel()
    {
        // +1.01 on I1 and -1.01 on I2 net to 0, but each document is outside Rs 1 (fails today and after F1).
        var r = new InvoiceTenderReconciliationService().Reconcile(
            [new("S1", "I1", 501.01m), new("S1", "I2", 498.99m)],
            [new("S1", "I1", "CASH", 500.00m, true), new("S1", "I2", "CASH", 500.00m, true)],
            RetailReportingPolicy.Tender);
        Assert.Equal(ReconciliationStatus.Failed, r.Status);
        Assert.Equal(0m, r.Variance);
        Assert.All(r.Documents, x => Assert.Equal(ReconciliationStatus.Failed, x.Status));
    }

    [Fact]
    public void Round_off_tender_balances_exactly_under_any_rule()
    {
        // Invoice 1,129.40 paid CASH 1,129.00 + ROUND_OFF 0.40: variance 0, Passed (also with tolerance 0).
        var exact = Run(1129.40m, Exact, ("CASH", 1129.00m), ("ROUND_OFF", 0.40m));
        Assert.Equal(ReconciliationStatus.Passed, exact.Status);
        Assert.Equal(0m, Assert.Single(exact.Documents).Variance);
        Assert.Equal(1129.40m, exact.TenderTotal);
        Assert.Equal(ReconciliationStatus.Passed, Run(1129.40m, RetailReportingPolicy.Tender, ("CASH", 1129.00m), ("ROUND_OFF", 0.40m)).Status);
    }

    [Fact]
    public void ROUND_OFF_is_a_recognised_tender_code()
    {
        Assert.Contains("ROUND_OFF", RetailReportingPolicy.Mapping.TenderTypes);
        Assert.Contains("CASH_REFUND", RetailReportingPolicy.Mapping.TenderTypes);
    }

    [Fact]
    public void Explicit_zero_tolerance_still_fails_every_paisa()
    {
        // The rule is data: a caller that passes tolerance 0 keeps the strict behaviour.
        var short40 = Run(1129.40m, Exact, ("CASH", 1129.00m));
        Assert.Equal(ReconciliationStatus.Failed, short40.Status);
        Assert.Equal(0.40m, Assert.Single(short40.Documents).Variance);
        var excess1 = Run(1129.00m, Exact, ("CASH", 1129.01m));
        Assert.Equal(ReconciliationStatus.Failed, excess1.Status);
        Assert.Equal(-0.01m, Assert.Single(excess1.Documents).Variance);
    }

    [Fact]
    public void A_cash_refund_tender_offsets_a_sales_return_exactly()
    {
        // SR -300.00 paid back as CASH_REFUND -300.00; with INV 1,000 paid CASH 1,000: total 700, Passed under both rules.
        var r = new InvoiceTenderReconciliationService().Reconcile(
            [new("S1", "I1", 1000m), new("S1", "R1", -300m)],
            [new("S1", "I1", "CASH", 1000m, true), new("S1", "R1", "CASH_REFUND", -300m, true)], Exact);
        Assert.Equal(ReconciliationStatus.Passed, r.Status);
        Assert.Equal(700m, r.InvoiceTotal);
        Assert.Equal(700m, r.TenderTotal);
    }
}
