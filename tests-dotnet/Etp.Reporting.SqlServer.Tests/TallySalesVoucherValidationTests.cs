using Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

// Plan task 7 applied to the vouchers task 6 planned, on the synthetic G01 cash sale.
public sealed class TallySalesVoucherValidationTests
{
    private static readonly DateOnly Day = new(2026, 8, 25);

    private static readonly TallyLedgerMapping[] Mappings =
    [
        new("TENDER_CASH", 1, 1, "Cash", "Cash"), new("SALES_REVENUE", 2, 1, "Sales", "Sales"),
        new("OUTPUT_CGST_9", 3, 1, "Output CGST 9%", "Output CGST 9%"), new("OUTPUT_SGST_9", 4, 1, "Output SGST 9%", "Output SGST 9%")
    ];

    private static TallyProfile Profile => TallyProfile.NewTest("GOLDEN", "TEST - ETP Golden", new[] { "WLMHW" });

    private static InvoiceAccountingSource Sale(string document, params InvoiceSourceTender[] tenders) => new(
        1, "WLMHW", 2027, document, Day,
        [new("1", "SAREE1", "INV", 1m, 1180m, 1000m, 180m)],
        [new("SAREE1", 9m, 90m, 9m, 90m, 0m, 0m, 0m)],
        tenders.Length == 0 ? new[] { new InvoiceSourceTender("CASH", "Cash", 1180m) } : tenders);

    private static (SalesVoucherPlan Plan, IReadOnlyList<ValidationFinding> Findings) Run(DateOnly today, TallyProfile profile, params InvoiceAccountingSource[] invoices)
    {
        var plan = TallySalesVoucherComposer.Compose(new(profile, null, false, Mappings), invoices);
        return TallySalesVoucherValidation.Apply(plan, invoices, profile, today);
    }

    [Fact]
    public void A_clean_recent_day_has_no_findings_and_stays_planned()
    {
        var (plan, findings) = Run(Day.AddDays(1), Profile, Sale("INV-1"));
        Assert.Empty(findings);
        Assert.Equal("PLANNED", Assert.Single(plan.Vouchers).Status);
        Assert.Null(plan.BlockingReason);
    }

    [Fact]
    public void An_old_day_gets_a_warning_that_does_not_block_the_voucher()
    {
        var (plan, findings) = Run(Day.AddDays(40), Profile, Sale("INV-1"));
        var warning = Assert.Single(findings);
        Assert.Equal(("RULE-DAT-002", "WARN", (int?)1), (warning.RuleId, warning.Severity, warning.VoucherSequence));
        Assert.Equal("PLANNED", Assert.Single(plan.Vouchers).Status);
        Assert.Null(plan.BlockingReason);
    }

    [Fact]
    public void A_failure_blocks_the_voucher_and_the_day()
    {
        var closed = Profile with { PostingFromDate = Day.AddDays(1) };
        var (plan, findings) = Run(Day.AddDays(1), closed, Sale("INV-1"));
        Assert.Contains(findings, finding => finding.RuleId == "RULE-DAT-001" && finding.Severity == "FAIL");
        var voucher = Assert.Single(plan.Vouchers);
        Assert.Equal(("BLOCKED", (string?)"VALIDATION_FAILED: RULE-DAT-001."), (voucher.Status, voucher.BlockedReason));
        Assert.Empty(voucher.Entries);
        Assert.False(voucher.LeftOutByScope);
        Assert.StartsWith("1 invoice(s) need fixing", plan.BlockingReason, StringComparison.Ordinal);
        Assert.Equal(0m, plan.DebitTotal);
    }

    [Fact]
    public void Invoices_the_composer_left_out_are_not_judged_again()
    {
        var (plan, findings) = Run(Day.AddDays(1), Profile, Sale("INV-1"), Sale("INV-2", new InvoiceSourceTender("CASH", "Cash", 500m), new InvoiceSourceTender("PHONEPE", "UPI", 680m)));
        Assert.Empty(findings);
        Assert.Equal(new[] { "PLANNED", "BLOCKED" }, plan.Vouchers.Select(voucher => voucher.Status));
        Assert.Null(plan.BlockingReason);
    }

    [Fact]
    public void The_same_product_on_two_lines_is_checked_once_against_its_GST_rows()
    {
        var invoice = Sale("INV-1") with
        {
            Lines = [new("1", "SAREE1", "INV", 1m, 590m, 500m, 90m), new("2", "SAREE1", "INV", 1m, 590m, 500m, 90m)],
            TaxRows = [new("SAREE1", 9m, 45m, 9m, 45m, 0m, 0m, 0m), new("SAREE1", 9m, 45m, 9m, 45m, 0m, 0m, 0m)]
        };
        var (plan, findings) = Run(Day.AddDays(1), Profile, invoice);
        Assert.Empty(findings);
        Assert.Equal("PLANNED", Assert.Single(plan.Vouchers).Status);
    }

    [Fact]
    public void The_day_is_described_in_one_sentence()
    {
        var (plan, _) = Run(Day.AddDays(1), Profile, Sale("INV-1"), Sale("INV-2", new InvoiceSourceTender("CASH", "Cash", 500m), new InvoiceSourceTender("PHONEPE", "UPI", 680m)), Sale("INV-3", new InvoiceSourceTender("CARD", "Card", 1180m)));
        Assert.Equal("3 invoice(s): 1 ready, 1 left out for a later step, 1 need fixing. Vouchers total 1180.00.", TallySalesVoucherValidation.Describe(plan));
    }
}
