namespace Etp.Reporting.Application.Accounting;

/// <summary>
/// One same-day bill-cancellation (BC) document's share in a bill (decision 74): its BC lines that take back items of the
/// bill, and, when the BC document cancels this bill only, its GST rows (R018 'BC') and refund tenders.
/// </summary>
/// <param name="SharedWithOtherBills">The BC document also cancels another bill; its GST rows and tenders cannot be split
/// between the bills, so they are not used (the GST is split by the bill's own rates and the refund taken as paid back).</param>
public sealed record SameDayCancellationPart(
    string DocumentNumber,
    IReadOnlyList<InvoiceSourceLine> Lines,
    IReadOnlyList<InvoiceTaxRow> TaxRows,
    IReadOnlyList<InvoiceSourceTender> Tenders,
    bool SharedWithOtherBills = false);

/// <summary>What netting did to a partly cancelled bill: the BC documents taken off it, the gross they took back, and the
/// reason it could not be netted (code and sentence), if any.</summary>
public sealed record SameDayCancellationNetting(
    IReadOnlyList<string> CancellationDocuments,
    decimal CancelledGross,
    string? BlockCode = null,
    string? BlockDetail = null);

/// <summary>
/// Decision 74 (1.12.1, review M-1): a bill partly cancelled the same day goes to Tally <b>net</b> of the cancelled items -
/// value, GST and quantity - so its voucher equals the bill's DSR VALUE (INV lines plus its BC lines). A bill cancelled
/// whole stays out (decision 41, <c>CancelledSameDay</c>). Pure: the loader hands in the bill and its BC parts.
/// <list type="bullet">
/// <item>Lines: per item, quantity and gross/net/tax less the BC lines' (taken as reductions whatever their sign). An item
/// cancelled completely leaves the voucher; a partly cancelled item becomes one net line.</item>
/// <item>GST: per item, the bill's R018 rows less the BC document's R018 'BC' rows; without those rows (or when the BC
/// document is shared) the cancelled tax of the BC lines is split by the bill's own component amounts, to the paisa.</item>
/// <item>Payment: the bill's one payment less the BC document's refunds, which must be in the same payment mode (a refund
/// in another mode is left out for a later step); with no refund rows, the cancelled gross is taken as paid back.</item>
/// </list>
/// Anything that does not add up (a BC line for an item the bill does not hold, more taken back than sold) is a fault the
/// composer shows as <see cref="MismatchCode"/>; it never guesses a balancing amount.
/// </summary>
public static class TallySameDayCancellation
{
    public const string MismatchCode = "CANCELLATION_MISMATCH";

    public static InvoiceAccountingSource Net(InvoiceAccountingSource bill, IReadOnlyList<SameDayCancellationPart> parts)
    {
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentNullException.ThrowIfNull(parts);
        var cancelling = parts.Where(part => part.Lines.Count > 0).ToArray();
        if (bill.CancelledSameDay || cancelling.Length == 0) return bill;
        var documents = cancelling.Select(part => part.DocumentNumber).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var named = string.Join(", ", documents);
        var bcLines = cancelling.SelectMany(part => part.Lines).ToArray();
        InvoiceAccountingSource Fault(string code, string detail) => bill with { PartCancellation = new(documents, 0m, code, detail) };

        // The composer reports a bill line without amounts itself (SOURCE_AMOUNT_MISSING).
        if (bill.Lines.Any(line => line.Gross is null || line.Net is null || line.Tax is null)) return bill with { PartCancellation = new(documents, 0m) };
        if (bcLines.Any(line => line.Gross is null || line.Net is null || line.Tax is null))
            return Fault("SOURCE_AMOUNT_MISSING", $"a line of cancellation {named} has no gross, net or tax amount. Import the sales export again.");

        static decimal Back(decimal? value) => -Math.Abs(value ?? 0m);
        var lines = new List<InvoiceSourceLine>();
        var touched = new Dictionary<string, (decimal Quantity, decimal Gross, decimal Net, decimal Tax, bool Gone)>(StringComparer.Ordinal);
        foreach (var item in bcLines.GroupBy(line => line.ProductCode, StringComparer.Ordinal))
        {
            var sold = bill.Lines.Where(line => string.Equals(line.ProductCode, item.Key, StringComparison.Ordinal)).ToArray();
            if (sold.Length == 0)
                return Fault(MismatchCode, $"cancellation {named} takes back item {item.Key}, which bill {bill.DocumentNumber} does not hold. Check the sales export.");
            var quantity = sold.Sum(line => line.Quantity) + item.Sum(line => Back(line.Quantity));
            var gross = sold.Sum(line => line.Gross!.Value) + item.Sum(line => Back(line.Gross));
            var net = sold.Sum(line => line.Net!.Value) + item.Sum(line => Back(line.Net));
            var tax = sold.Sum(line => line.Tax!.Value) + item.Sum(line => Back(line.Tax));
            if (quantity < 0 || gross < 0 || net < 0 || tax < 0)
                return Fault(MismatchCode, $"cancellation {named} takes back more of item {item.Key} than bill {bill.DocumentNumber} sold. Check the sales export.");
            var gone = quantity == 0 && gross == 0 && net == 0 && tax == 0;
            if (!gone && (quantity == 0 || gross == 0))
                return Fault(MismatchCode, $"after cancellation {named}, item {item.Key} of bill {bill.DocumentNumber} has quantity {quantity:0.##} but value {gross:0.00}. Check the sales export.");
            touched[item.Key] = (quantity, gross, net, tax, gone);
        }
        foreach (var line in bill.Lines)
        {
            if (!touched.TryGetValue(line.ProductCode, out var change)) { lines.Add(line); continue; }
            if (change.Gone || lines.Any(existing => string.Equals(existing.ProductCode, line.ProductCode, StringComparison.Ordinal))) continue;
            var ids = string.Join("+", bill.Lines.Where(other => string.Equals(other.ProductCode, line.ProductCode, StringComparison.Ordinal))
                .Select(other => other.LineIdentifier).Order(StringComparer.Ordinal));
            lines.Add(new($"{ids}-{string.Join("+", documents)}", line.ProductCode, "INV", change.Quantity, change.Gross, change.Net, change.Tax));
        }
        var cancelledGross = Money(-bcLines.Sum(line => Back(line.Gross)));
        if (lines.Count == 0)
            // Every item taken back: the same as a whole-bill cancellation.
            return bill with { CancelledSameDay = true, PartCancellation = new(documents, cancelledGross) };

        // GST rows, per item.
        var ownRows = cancelling.All(part => !part.SharedWithOtherBills);
        var taxRows = new List<InvoiceTaxRow>();
        foreach (var group in bill.TaxRows.GroupBy(row => row.ItemNumber, StringComparer.Ordinal))
        {
            if (!touched.TryGetValue(group.Key, out var change)) { taxRows.AddRange(group); continue; }
            if (change.Gone) continue;
            var sold = group.ToArray();
            decimal Sum(Func<InvoiceTaxRow, decimal?> amount) => sold.Sum(row => amount(row) ?? 0m);
            decimal? Rate(Func<InvoiceTaxRow, decimal?> rate) => sold.Select(rate).FirstOrDefault(value => value is not null);
            bool Has(Func<InvoiceTaxRow, decimal?> amount) => sold.Any(row => amount(row) is not null);
            var bcRows = ownRows ? cancelling.SelectMany(part => part.TaxRows).Where(row => string.Equals(row.ItemNumber, group.Key, StringComparison.Ordinal)).ToArray() : [];
            decimal cgst, sgst, igst, cess;
            if (bcRows.Length > 0)
            {
                cgst = bcRows.Sum(row => Math.Abs(row.CgstAmount ?? 0m)); sgst = bcRows.Sum(row => Math.Abs(row.SgstAmount ?? 0m));
                igst = bcRows.Sum(row => Math.Abs(row.IgstAmount ?? 0m)); cess = bcRows.Sum(row => Math.Abs(row.CessAmount ?? 0m));
            }
            else
            {
                // The cancelled tax of this item's BC lines, split by the bill's own component amounts; the last takes the paisa.
                var cancelledTax = Money(bcLines.Where(line => string.Equals(line.ProductCode, group.Key, StringComparison.Ordinal)).Sum(line => Math.Abs(line.Tax!.Value)));
                var parts3 = new[] { Sum(row => row.CgstAmount), Sum(row => row.SgstAmount), Sum(row => row.IgstAmount) };
                var total = parts3.Sum();
                if (total == 0 && cancelledTax != 0)
                    return Fault(MismatchCode, $"cancellation {named} takes back GST {cancelledTax:0.00} on item {group.Key}, which carries no GST on bill {bill.DocumentNumber}.");
                var shares = new decimal[3];
                var last = Array.FindLastIndex(parts3, value => value != 0);
                for (var i = 0; i < 3; i++)
                    shares[i] = total == 0 || parts3[i] == 0 ? 0m : i == last ? cancelledTax - shares.Take(i).Sum() : Money(cancelledTax * parts3[i] / total);
                (cgst, sgst, igst, cess) = (shares[0], shares[1], shares[2], 0m);
            }
            taxRows.Add(new(group.Key,
                Rate(row => row.CgstRate), Has(row => row.CgstAmount) || cgst != 0 ? Sum(row => row.CgstAmount) - cgst : null,
                Rate(row => row.SgstRate), Has(row => row.SgstAmount) || sgst != 0 ? Sum(row => row.SgstAmount) - sgst : null,
                Rate(row => row.IgstRate), Has(row => row.IgstAmount) || igst != 0 ? Sum(row => row.IgstAmount) - igst : null,
                Has(row => row.CessAmount) || cess != 0 ? Sum(row => row.CessAmount) - cess : null));
        }

        // Payment: the bill's one payment less the refund.
        var netting = new SameDayCancellationNetting(documents, cancelledGross);
        var tenders = bill.Tenders.ToList();
        var payments = bill.Tenders.Where(tender => tender.Amount != 0 && !IsRoundOff(tender)).ToArray();
        if (payments.Length == 1)
        {
            var refunds = ownRows ? cancelling.SelectMany(part => part.Tenders).Where(tender => tender.Amount != 0).ToArray() : [];
            var refundPayments = refunds.Where(tender => !IsRoundOff(tender)).ToArray();
            if (refundPayments.FirstOrDefault(tender => !string.Equals(tender.Mode, payments[0].Mode, StringComparison.OrdinalIgnoreCase)) is { } other)
                return bill with
                {
                    PartCancellation = netting with
                    {
                        BlockCode = "NOT_IN_SCOPE_7A",
                        BlockDetail = $"the part cancelled by {named} was refunded as {other.SourceTenderCode}, not as the bill's {payments[0].SourceTenderCode}; one voucher cannot carry two payments yet."
                    }
                };
            var paidBack = refundPayments.Length == 0 ? cancelledGross : refundPayments.Sum(tender => Math.Abs(tender.Amount));
            var roundOffBack = refunds.Where(IsRoundOff).Sum(tender => tender.Amount);
            tenders = bill.Tenders.Where(tender => tender != payments[0] && !IsRoundOff(tender)).ToList();
            tenders.Insert(0, payments[0] with { Amount = payments[0].Amount - paidBack });
            var roundOff = bill.Tenders.Where(IsRoundOff).Sum(tender => tender.Amount) + roundOffBack;
            if (roundOff != 0) tenders.Add(new(TallySalesVoucherComposer.RoundOffCode, null, roundOff));
        }
        return bill with { Lines = lines, TaxRows = taxRows, Tenders = tenders, PartCancellation = netting };
    }

    private static bool IsRoundOff(InvoiceSourceTender tender) =>
        string.Equals(tender.SourceTenderCode, TallySalesVoucherComposer.RoundOffCode, StringComparison.OrdinalIgnoreCase);

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
