using Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

// Plan task 6 on synthetic invoices. G01: a cash sale of 1,180.00 (1,000.00 sales, CGST 9% 90.00, SGST 9% 90.00).
// The ledger names are placeholders until the accountant fills in D15/D16; the expected postings follow the
// decision sheet's recommendations and have not yet been reviewed by the accountant.
public sealed class TallySalesVoucherComposerTests
{
    private static readonly DateOnly Day = new(2026, 8, 25);

    private static readonly TallyLedgerMapping[] Mappings =
    [
        new("TENDER_CASH", 1, 1, "Cash", "Cash"),
        new("TENDER_UPI", 2, 1, "UPI receivable - PhonePe", "UPI receivable - PhonePe"),
        new("ROUND_OFF", 3, 1, "Round Off", "Round Off"),
        new("SALES_REVENUE", 4, 1, "Sales", "Sales"),
        new("OUTPUT_CGST_9", 5, 1, "Output CGST 9%", "Output CGST 9%"),
        new("OUTPUT_SGST_9", 6, 1, "Output SGST 9%", "Output SGST 9%")
    ];

    private static SalesVoucherContext Context(string? costCentre = "Titan World", bool required = true, IReadOnlyList<TallyLedgerMapping>? mappings = null) =>
        new(TallyProfile.NewTest("GOLDEN", "TEST - ETP Golden", new[] { "WLMHW", "HEMW" }), costCentre, required, mappings ?? Mappings);

    private static InvoiceAccountingSource G01(string document = "INV-1", params InvoiceSourceTender[] tenders) => new(
        1, "WLMHW", 2027, document, Day,
        [new("1", "SAREE1", "INV", 1m, 1180m, 1000m, 180m)],
        [new("SAREE1", 9m, 90m, 9m, 90m, 0m, 0m, 0m)],
        tenders.Length == 0 ? new[] { new InvoiceSourceTender("CASH", "Cash", 1180m) } : tenders);

    private static PlannedVoucher One(params InvoiceAccountingSource[] invoices) =>
        Assert.Single(TallySalesVoucherComposer.Compose(Context(), invoices).Vouchers);

    [Fact]
    public void G01_cash_sale_becomes_one_balanced_sales_voucher_with_the_stores_cost_centre()
    {
        var plan = TallySalesVoucherComposer.Compose(Context(), [G01()]);

        var voucher = Assert.Single(plan.Vouchers);
        Assert.Equal(("PLANNED", "Sales", "SALES", 1180m), (voucher.Status, voucher.VoucherType, voucher.ComponentRole, voucher.ExpectedTotal));
        Assert.Equal("ETP:WLMHW:2027:INV-1:SALES:1", voucher.CorrespondenceKey);
        Assert.StartsWith("ETP:WLMHW:2027:INV-1:SALES:1 | ETP WLMHW invoice INV-1 dated 25-Aug-2026", voucher.Narration, StringComparison.Ordinal);
        Assert.Equal(new[] { "TENDER_CASH|Cash|1180|0", "SALES_REVENUE|Sales|0|1000", "OUTPUT_CGST_9|Output CGST 9%|0|90", "OUTPUT_SGST_9|Output SGST 9%|0|90" },
            voucher.Entries.Select(entry => $"{entry.BusinessEvent}|{entry.LedgerName}|{entry.Debit:0.##}|{entry.Credit:0.##}"));
        Assert.All(voucher.Entries, entry => Assert.Equal("Titan World", entry.CostCentre));
        Assert.Equal(new decimal?[] { null, null, 9m, 9m }, voucher.Entries.Select(entry => entry.TaxRate));
        Assert.Equal((1180m, 1180m), (plan.DebitTotal, plan.CreditTotal));
        Assert.Null(plan.BlockingReason);
        Assert.Equal(new[] { "OUTPUT_CGST_9", "OUTPUT_SGST_9", "SALES_REVENUE", "TENDER_CASH" }, plan.MappingsUsed.Select(mapping => mapping.BusinessEvent));
        Assert.Matches("^[0-9a-f]{64}$", voucher.SourceSha256);
        Assert.Matches("^[0-9a-f]{64}$", voucher.PlanSha256);
    }

    [Fact]
    public void Round_off_is_its_own_line_and_never_counted_as_a_payment()
    {
        // 1,179.60 of goods paid with 1,180.00 cash: the 0.40 is credited to round-off.
        var paidMore = G01() with
        {
            Lines = [new("1", "SAREE1", "INV", 1m, 1179.60m, 999.66m, 179.94m)],
            TaxRows = [new("SAREE1", 9m, 89.97m, 9m, 89.97m, null, null, null)],
            Tenders = [new("CASH", "Cash", 1180m), new("ROUND_OFF", "Cash", -0.40m)]
        };
        var credit = Assert.Single(One(paidMore).Entries, entry => entry.BusinessEvent == "ROUND_OFF");
        Assert.Equal(("Round Off", 0m, 0.40m), (credit.LedgerName, credit.Debit, credit.Credit));

        // 1,180.40 of goods paid with 1,180.00 cash: the 0.40 rounded away is debited.
        var paidLess = paidMore with
        {
            Lines = [new("1", "SAREE1", "INV", 1m, 1180.40m, 1000.34m, 180.06m)],
            TaxRows = [new("SAREE1", 9m, 90.03m, 9m, 90.03m, null, null, null)],
            Tenders = [new("CASH", "Cash", 1180m), new("ROUND_OFF", "Cash", 0.40m)]
        };
        var voucher = One(paidLess);
        Assert.Equal("PLANNED", voucher.Status);
        Assert.Equal(0.40m, Assert.Single(voucher.Entries, entry => entry.BusinessEvent == "ROUND_OFF").Debit);
        Assert.Equal(1180m, Assert.Single(voucher.Entries, entry => entry.BusinessEvent == "TENDER_CASH").Debit);
    }

    [Fact]
    public void Each_payment_mode_uses_its_own_tender_ledger()
    {
        var voucher = One(G01(tenders: [new("PHONEPE", "UPI", 1180m)]));
        Assert.Equal(("TENDER_UPI", "UPI receivable - PhonePe"), (voucher.Entries[0].BusinessEvent, voucher.Entries[0].LedgerName));
    }

    private static InvoiceAccountingSource Case(string name) => name switch
    {
        "split payment" => G01(tenders: [new("CASH", "Cash", 500m), new("PHONEPE", "UPI", 680m)]),
        "return" => G01() with { Lines = [new("1", "SAREE1", "SR", -1m, -1180m, -1000m, -180m)] },
        "refund tender" => G01(tenders: [new("CASH_REFUND", "Cash", 1180m)]),
        "two GST rates" => G01() with
        {
            Lines = [new("1", "SAREE1", "INV", 1m, 1180m, 1000m, 180m), new("2", "WATCH1", "INV", 1m, 1050m, 1000m, 50m)],
            TaxRows = [new("SAREE1", 9m, 90m, 9m, 90m, 0m, 0m, 0m), new("WATCH1", 2.5m, 25m, 2.5m, 25m, 0m, 0m, 0m)],
            Tenders = [new("CASH", "Cash", 2230m)]
        },
        "cess" => G01() with { TaxRows = [new("SAREE1", 9m, 90m, 9m, 90m, 0m, 0m, 5m)] },
        "unsafe number" => G01("INV 1"),
        "no GST row" => G01() with { TaxRows = [] },
        "GST rows disagree" => G01() with { TaxRows = [new("SAREE1", 9m, 90m, 9m, 80m, 0m, 0m, 0m)] },
        "empty GST amount" => G01() with { TaxRows = [new("SAREE1", 9m, null, 9m, 90m, 0m, 0m, 0m)] },
        "missing net" => G01() with { Lines = [new("1", "SAREE1", "INV", 1m, 1180m, null, 180m)] },
        "unknown payment code" => G01(tenders: [new("NEWWALLET", null, 1180m)]),
        "payment short" => G01(tenders: [new("CASH", "Cash", 1100m)]),
        "no ledger for card" => G01(tenders: [new("CARD", "Card", 1180m)]),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [InlineData("split payment", "NOT_IN_SCOPE_7A: split payment (CASH, PHONEPE).")]
    [InlineData("return", "NOT_IN_SCOPE_7A: only sales invoices (INV)")]
    [InlineData("refund tender", "NOT_IN_SCOPE_7A: refund tender CASH_REFUND")]
    [InlineData("two GST rates", "NOT_IN_SCOPE_7A: the invoice has items at different GST rates.")]
    [InlineData("cess", "CESS_NOT_SUPPORTED")]
    [InlineData("unsafe number", "KEY_UNSAFE")]
    public void Invoices_this_step_does_not_send_are_left_out_without_blocking_the_day(string name, string reason)
    {
        var plan = TallySalesVoucherComposer.Compose(Context(), [G01("INV-0"), Case(name)]);
        var left = Assert.Single(plan.Vouchers, voucher => voucher.DocumentNumber != "INV-0");
        Assert.Equal("BLOCKED", left.Status);
        Assert.StartsWith(reason, left.BlockedReason, StringComparison.Ordinal);
        Assert.True(left.LeftOutByScope);
        Assert.Empty(left.Entries);
        Assert.Null(plan.BlockingReason);
        Assert.Equal(1180m, plan.DebitTotal);
    }

    [Theory]
    [InlineData("no GST row", "TAX_ROW_MISSING: no GST row for item SAREE1.")]
    [InlineData("GST rows disagree", "TAX_SPLIT_MISMATCH: the GST rows add up to 170.00 but the sales lines carry 180.00.")]
    [InlineData("empty GST amount", "TAX_AMOUNT_MISSING")]
    [InlineData("missing net", "SOURCE_AMOUNT_MISSING")]
    [InlineData("unknown payment code", "TENDER_MODE_UNKNOWN: payment code NEWWALLET")]
    [InlineData("payment short", "UNBALANCED: payment and round-off (1100.00) do not equal revenue and GST (1180.00).")]
    [InlineData("no ledger for card", "MAPPING_MISSING: no approved ledger for TENDER_CARD on 25-Aug-2026.")]
    public void A_fault_blocks_the_invoice_and_the_day_until_it_is_fixed(string name, string reason)
    {
        var plan = TallySalesVoucherComposer.Compose(Context(), [G01("INV-0"), Case(name)]);
        var blocked = Assert.Single(plan.Vouchers, voucher => voucher.DocumentNumber != "INV-0");
        Assert.Equal("BLOCKED", blocked.Status);
        Assert.StartsWith(reason, blocked.BlockedReason, StringComparison.Ordinal);
        Assert.False(blocked.LeftOutByScope);
        Assert.StartsWith("1 invoice(s) need fixing before this day can be approved: INV-1 ", plan.BlockingReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_company_shared_by_stores_needs_each_stores_cost_centre()
    {
        var plan = TallySalesVoucherComposer.Compose(Context(costCentre: null), [G01()]);
        Assert.StartsWith("COST_CENTRE_MISSING", plan.Vouchers[0].BlockedReason, StringComparison.Ordinal);
        Assert.NotNull(plan.BlockingReason);

        var single = Assert.Single(TallySalesVoucherComposer.Compose(Context(costCentre: null, required: false), [G01()]).Vouchers);
        Assert.Equal("PLANNED", single.Status);
        Assert.All(single.Entries, entry => Assert.Null(entry.CostCentre));
    }

    [Fact]
    public void A_day_with_nothing_to_send_cannot_be_approved()
    {
        Assert.Equal("No invoices exist for this day.", TallySalesVoucherComposer.Compose(Context(), []).BlockingReason);
        Assert.Equal("No invoice of this day can be sent yet; every invoice is left out.",
            TallySalesVoucherComposer.Compose(Context(), [G01(tenders: [new("CASH", "Cash", 500m), new("PHONEPE", "UPI", 680m)])]).BlockingReason);
    }

    [Fact]
    public void Hashes_follow_the_facts_not_their_order()
    {
        var invoice = G01() with { Lines = [new("2", "WATCH1", "INV", 1m, 590m, 500m, 90m), new("1", "SAREE1", "INV", 1m, 590m, 500m, 90m)] };
        var reordered = invoice with { Lines = invoice.Lines.Reverse().ToArray() };
        Assert.Equal(TallySalesVoucherComposer.SourceHash(invoice), TallySalesVoucherComposer.SourceHash(reordered));
        Assert.NotEqual(TallySalesVoucherComposer.SourceHash(invoice),
            TallySalesVoucherComposer.SourceHash(invoice with { Tenders = [new("CASH", "Cash", 1180.01m)] }));

        var renamed = Mappings.Select(mapping => mapping.BusinessEvent == "SALES_REVENUE" ? mapping with { CreditLedger = "Sales - Retail" } : mapping).ToArray();
        Assert.NotEqual(One(G01()).PlanSha256, Assert.Single(TallySalesVoucherComposer.Compose(Context(mappings: renamed), [G01()]).Vouchers).PlanSha256);
        Assert.Equal(One(G01()).PlanSha256, One(G01()).PlanSha256);
    }

    [Fact]
    public void Vouchers_are_numbered_in_invoice_order_and_the_newest_mapping_version_wins()
    {
        var newer = Mappings.Append(new("TENDER_CASH", 7, 2, "Cash in hand", "Cash in hand")).ToArray();
        var plan = TallySalesVoucherComposer.Compose(Context(mappings: newer), [G01("INV-2"), G01("INV-10"), G01("INV-1")]);
        Assert.Equal(new[] { "INV-1:1", "INV-10:2", "INV-2:3" }, plan.Vouchers.Select(voucher => $"{voucher.DocumentNumber}:{voucher.Sequence}"));
        Assert.All(plan.Vouchers, voucher => Assert.Equal("Cash in hand", voucher.Entries[0].LedgerName));
        Assert.Equal(2, Assert.Single(plan.MappingsUsed, mapping => mapping.BusinessEvent == "TENDER_CASH").Version);
    }

    [Theory]
    [InlineData("DAILY_SUMMARY", "SINGLE_LEDGER", "IN_VOUCHER", "ACCOUNTING_ONLY", "one daily summary voucher")]
    [InlineData("PER_INVOICE", "NAMED_LEDGERS", "CLEARING_LEDGER", "ACCOUNTING_ONLY", "named customer ledgers, a clearing ledger for payments")]
    [InlineData("PER_INVOICE", "SINGLE_LEDGER", "IN_VOUCHER", "INVENTORY", "stock items")]
    public void Other_decisions_are_refused_in_plain_words(string granularity, string party, string tender, string posting, string named)
    {
        var profile = TallyProfile.NewTest("OTHER", "TEST - Other", new[] { "WLMHW" }) with
            { VoucherGranularity = granularity, PartyPolicy = party, TenderModel = tender, PostingModel = posting };
        var error = Assert.Throws<InvalidOperationException>(() => TallySalesVoucherComposer.Compose(new(profile, null, false, Mappings), [G01()]));
        Assert.Contains(named, error.Message, StringComparison.Ordinal);
        Assert.Null(TallySalesVoucherComposer.Unsupported(TallyProfile.NewTest("OK", "TEST - Ok", new[] { "WLMHW" })));
    }

    [Theory]
    [InlineData("Cash", "TENDER_CASH")]
    [InlineData("Service UPI", "TENDER_SERVICE_UPI")]
    public void Tender_events_follow_the_tender_master_mode(string mode, string expected) =>
        Assert.Equal(expected, TallySalesVoucherComposer.TenderEvent(mode));

    [Theory]
    [InlineData("CGST", 9, "OUTPUT_CGST_9")]
    [InlineData("SGST", 1.5, "OUTPUT_SGST_1.5")]
    [InlineData("IGST", 18.00, "OUTPUT_IGST_18")]
    public void Tax_events_name_the_component_and_rate(string component, double rate, string expected) =>
        Assert.Equal(expected, TallySalesVoucherComposer.TaxEvent(component, (decimal)rate));
}
