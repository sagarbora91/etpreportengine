using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer.Tally;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

// Decision 74 (1.12.1, review M-1): a bill partly cancelled the same day goes to Tally net of the cancelled items.
// Bill P1, intra-state GST 18% (CGST 9% + SGST 9%), paid 2,006.00 cash:
//   ITEMA 1 x 1,180.00 = 1,000.00 + 90.00 + 90.00
//   ITEMB 1 x   590.00 =   500.00 + 45.00 + 45.00   <- cancelled the same day by BC document PB1 (refund 590.00 cash)
//   ITEMC 2 x   236.00 =   200.00 + 18.00 + 18.00
// Hand-computed net voucher: Dr Cash 1,416.00 = Cr Sales 1,200.00 + CGST 9% 108.00 + SGST 9% 108.00.
// DSR VALUE of P1 = 1,180 + 590 + 236 - 590 = 1,416.00: the voucher total.
public sealed class TallyPartCancellationTests
{
    private static readonly DateOnly Day = new(2026, 8, 25);

    private static readonly TallyLedgerMapping[] Mappings =
    [
        new("TENDER_CASH", 1, 1, "Cash", "Cash"), new("ROUND_OFF", 2, 1, "Round Off", "Round Off"),
        new("SALES_REVENUE", 3, 1, "Sales", "Sales"),
        new("OUTPUT_CGST_9", 4, 1, "Output CGST 9%", "Output CGST 9%"), new("OUTPUT_SGST_9", 5, 1, "Output SGST 9%", "Output SGST 9%")
    ];

    private static readonly TallyProfile Profile = TallyProfile.NewTest("GOLDEN", "TEST - ETP Golden", new[] { "WLMHW" });

    private static InvoiceAccountingSource P1() => new(
        10, "WLMHW", 2027, "P1", Day,
        [
            new("1", "ITEMA", "INV", 1m, 1180m, 1000m, 180m),
            new("2", "ITEMB", "INV", 1m, 590m, 500m, 90m),
            new("3", "ITEMC", "INV", 2m, 236m, 200m, 36m)
        ],
        [
            new("ITEMA", 9m, 90m, 9m, 90m, 0m, 0m, 0m),
            new("ITEMB", 9m, 45m, 9m, 45m, 0m, 0m, 0m),
            new("ITEMC", 9m, 18m, 9m, 18m, 0m, 0m, 0m)
        ],
        [new("CASH", "Cash", 2006m)]);

    private static InvoiceAccountingSource PB1() => new(
        11, "WLMHW", 2027, "PB1", Day,
        [new("1", "ITEMB", "BC", -1m, -590m, -500m, -90m)], [], [new("CASH_REFUND", "Cash", -590m)]);

    private static SameDayCancellationPart Part(bool withTax = true, bool withRefund = true, params InvoiceSourceLine[] lines) => new("PB1",
        lines.Length == 0 ? PB1().Lines : lines,
        withTax ? [new("ITEMB", 9m, -45m, 9m, -45m, 0m, 0m, 0m)] : [],
        withRefund ? PB1().Tenders : []);

    private static PlannedVoucher Compose(InvoiceAccountingSource bill) =>
        TallySalesVoucherComposer.Compose(new(Profile, null, false, Mappings), [bill]).Vouchers.Single();

    private static string[] Entries(PlannedVoucher voucher) =>
        voucher.Entries.Select(entry => $"{entry.BusinessEvent}|{entry.Debit:0.00}|{entry.Credit:0.00}").ToArray();

    [Fact]
    public void A_bill_with_one_of_three_lines_cancelled_the_same_day_goes_to_tally_net_value_gst_and_quantity()
    {
        var net = TallySameDayCancellation.Net(P1(), [Part()]);

        Assert.Equal(new[] { "1:ITEMA:1:1180:1000:180", "3:ITEMC:2:236:200:36" },
            net.Lines.Select(line => $"{line.LineIdentifier}:{line.ProductCode}:{line.Quantity:0.##}:{line.Gross:0.##}:{line.Net:0.##}:{line.Tax:0.##}"));
        Assert.Equal(new[] { "ITEMA:90:90", "ITEMC:18:18" }, net.TaxRows.Select(row => $"{row.ItemNumber}:{row.CgstAmount:0.##}:{row.SgstAmount:0.##}"));
        Assert.Equal(new[] { "CASH:1416" }, net.Tenders.Select(tender => $"{tender.SourceTenderCode}:{tender.Amount:0.##}"));
        Assert.Equal(new[] { "PB1" }, net.PartCancellation!.CancellationDocuments);
        Assert.Equal(590m, net.PartCancellation.CancelledGross);

        var voucher = Compose(net);
        Assert.Equal(TallyVoucherStatus.Planned, voucher.Status);
        Assert.Equal(new[] { "TENDER_CASH|1416.00|0.00", "SALES_REVENUE|0.00|1200.00", "OUTPUT_CGST_9|0.00|108.00", "OUTPUT_SGST_9|0.00|108.00" }, Entries(voucher));
        Assert.Equal(1416m, voucher.ExpectedTotal);
        Assert.EndsWith("net of same-day cancellation PB1", voucher.Narration, StringComparison.Ordinal);
        // The net bill hashes differently from the raw bill, so a batch prepared before the cancellation was imported is not mistaken for it.
        Assert.NotEqual(TallySalesVoucherComposer.SourceHash(P1()), TallySalesVoucherComposer.SourceHash(net));
        // Every other invoice keeps its hash.
        Assert.Equal(TallySalesVoucherComposer.SourceHash(P1()), TallySalesVoucherComposer.SourceHash(P1() with { PartCancellation = null }));

        // Plan task 7 judges the net bill: nothing fails.
        var (plan, findings) = TallySalesVoucherValidation.Apply(TallySalesVoucherComposer.Compose(new(Profile, null, false, Mappings), [net]), [net], Profile, Day.AddDays(1));
        Assert.Equal(TallyVoucherStatus.Planned, plan.Vouchers.Single().Status);
        Assert.DoesNotContain(findings, finding => finding.Severity == "FAIL");
        Assert.Equal((1416m, 1416m), (plan.DebitTotal, plan.CreditTotal));
    }

    [Fact]
    public void Without_gst_rows_or_refund_rows_for_the_cancellation_the_bills_own_split_and_the_cancelled_gross_are_used()
    {
        // Half of ITEMC (1 of 2) is also taken back: 118.00 = 100.00 + 9.00 + 9.00, split by ITEMC's own 18/18.
        var net = TallySameDayCancellation.Net(P1(), [Part(withTax: false, withRefund: false,
            new("1", "ITEMB", "BC", -1m, -590m, -500m, -90m), new("2", "ITEMC", "BC", -1m, -118m, -100m, -18m))]);

        Assert.Equal(new[] { "ITEMA:1:1180", "ITEMC:1:118" }, net.Lines.Select(line => $"{line.ProductCode}:{line.Quantity:0.##}:{line.Gross:0.##}"));
        Assert.Equal("3-PB1", net.Lines[1].LineIdentifier);
        Assert.Equal(new[] { "ITEMA:90:90", "ITEMC:9:9" }, net.TaxRows.Select(row => $"{row.ItemNumber}:{row.CgstAmount:0.##}:{row.SgstAmount:0.##}"));
        // No refund row: the cancelled gross 708.00 is taken as paid back. 2,006.00 - 708.00 = 1,298.00.
        Assert.Equal(1298m, Assert.Single(net.Tenders).Amount);
        Assert.Equal(new[] { "TENDER_CASH|1298.00|0.00", "SALES_REVENUE|0.00|1100.00", "OUTPUT_CGST_9|0.00|99.00", "OUTPUT_SGST_9|0.00|99.00" }, Entries(Compose(net)));
    }

    [Fact]
    public void An_odd_paisa_of_cancelled_gst_is_split_to_the_paisa_and_the_voucher_still_balances()
    {
        // ITEMB 2 units carry CGST 90.01 + SGST 90.00; one unit is cancelled with tax 90.01 and no GST rows. Its split by
        // the bill's own amounts: CGST 90.01 x 90.01 / 180.01 = 45.0075 -> 45.01, SGST takes the rest, 45.00.
        // Net ITEMB: CGST 45.00, SGST 45.00, tax 90.00. Paid 2,360.01 less the cancelled 590.01 = 1,770.00.
        var bill = P1() with { Lines = [P1().Lines[0], new("2", "ITEMB", "INV", 2m, 1180.01m, 1000m, 180.01m)],
            TaxRows = [P1().TaxRows[0], new("ITEMB", 9m, 90.01m, 9m, 90m, 0m, 0m, 0m)], Tenders = [new("CASH", "Cash", 2360.01m)] };
        var net = TallySameDayCancellation.Net(bill, [Part(withTax: false, withRefund: false, new InvoiceSourceLine("1", "ITEMB", "BC", -1m, -590.01m, -500m, -90.01m))]);
        var itemB = net.TaxRows.Single(row => row.ItemNumber == "ITEMB");
        Assert.Equal((45.00m, 45.00m), (itemB.CgstAmount!.Value, itemB.SgstAmount!.Value));
        Assert.Equal(1770m, Assert.Single(net.Tenders).Amount);
        var voucher = Compose(net);
        Assert.Equal(TallyVoucherStatus.Planned, voucher.Status);
        Assert.Equal(new[] { "TENDER_CASH|1770.00|0.00", "SALES_REVENUE|0.00|1500.00", "OUTPUT_CGST_9|0.00|135.00", "OUTPUT_SGST_9|0.00|135.00" }, Entries(voucher));
    }

    [Fact]
    public void Taking_back_every_item_is_a_whole_bill_cancellation_and_the_bill_stays_out()
    {
        var net = TallySameDayCancellation.Net(P1(), [Part(withTax: false, withRefund: false,
            new("1", "ITEMA", "BC", -1m, -1180m, -1000m, -180m), new("2", "ITEMB", "BC", -1m, -590m, -500m, -90m), new("3", "ITEMC", "BC", -2m, -236m, -200m, -36m))]);
        Assert.True(net.CancelledSameDay);
        Assert.Equal(TallySalesVoucherComposer.CancelledSameDayCode, Compose(net).BlockCode);
    }

    [Fact]
    public void A_cancellation_that_does_not_fit_the_bill_is_a_fault_and_a_refund_in_another_mode_is_left_out()
    {
        var foreign = TallySameDayCancellation.Net(P1(), [Part(withTax: false, withRefund: false, new InvoiceSourceLine("1", "ITEMZ", "BC", -1m, -10m, -8m, -2m))]);
        var blocked = Compose(foreign);
        Assert.Equal((TallyVoucherStatus.Blocked, TallySameDayCancellation.MismatchCode, false), (blocked.Status, blocked.BlockCode!, blocked.LeftOutByScope));
        Assert.Contains("ITEMZ", blocked.BlockedReason, StringComparison.Ordinal);

        var tooMuch = Compose(TallySameDayCancellation.Net(P1(), [Part(withTax: false, withRefund: false, new InvoiceSourceLine("1", "ITEMB", "BC", -2m, -1180m, -1000m, -180m))]));
        Assert.Equal(TallySameDayCancellation.MismatchCode, tooMuch.BlockCode);

        var card = TallySameDayCancellation.Net(P1(), [Part() with { Tenders = [new("CARD_REFUND", "Card", -590m)] }]);
        var left = Compose(card);
        Assert.Equal(("NOT_IN_SCOPE_7A", true), (left.BlockCode!, left.LeftOutByScope));
        Assert.Contains("CARD_REFUND", left.BlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_loader_nets_the_bill_marks_its_bc_document_and_leaves_a_whole_cancellation_as_it_was()
    {
        var whole = P1() with { SalesInvoiceId = 20, DocumentNumber = "P2", CancelledSameDay = true };
        var wholeBc = PB1() with { SalesInvoiceId = 21, DocumentNumber = "PB2" };
        var invoices = new[] { P1(), PB1(), whole, wholeBc };
        var bcLines = new List<(long, long, string, bool, InvoiceSourceLine)>
        {
            (10, 11, "PB1", false, PB1().Lines[0]),
            (20, 21, "PB2", false, new("1", "ITEMA", "BC", -1m, -1180m, -1000m, -180m))
        };
        var assembled = SqlServerTallySalesBatchService.Assemble(invoices, bcLines,
            new Dictionary<long, List<InvoiceTaxRow>> { [11] = [new("ITEMB", 9m, -45m, 9m, -45m, 0m, 0m, 0m)] },
            new Dictionary<long, List<InvoiceSourceTender>> { [11] = [.. PB1().Tenders] });

        var plan = TallySalesVoucherComposer.Compose(new(Profile, null, false, Mappings), assembled);
        Assert.Null(plan.BlockingReason);
        Assert.Equal(new[] { "P1:PLANNED:", "P2:BLOCKED:CANCELLED_SAME_DAY", "PB1:BLOCKED:CANCELLATION_NETTED", "PB2:BLOCKED:NOT_IN_SCOPE_7A" },
            plan.Vouchers.Select(voucher => $"{voucher.DocumentNumber}:{voucher.Status}:{voucher.BlockCode}"));
        Assert.Contains("bill P1", plan.Vouchers.Single(voucher => voucher.DocumentNumber == "PB1").BlockedReason, StringComparison.Ordinal);
        Assert.Equal((1416m, 1416m), (plan.DebitTotal, plan.CreditTotal));
    }
}
