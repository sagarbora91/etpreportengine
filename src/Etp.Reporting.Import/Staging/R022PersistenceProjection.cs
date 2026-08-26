namespace Etp.Reporting.Import.Staging;

public sealed record R022InvoiceControl(
    int SourceRowNumber,
    string StoreCode,
    string InvoiceNumber,
    DateOnly TransactionDate,
    string TransactionTypeRaw,
    decimal InvoiceQuantity,
    decimal NetValue);

public sealed record R022TenderRow(
    int SourceRowNumber,
    string StoreCode,
    string InvoiceNumber,
    DateOnly TransactionDate,
    string TenderCode,
    decimal SourceAmount,
    bool IsQuarantined,
    string? QuarantineReason);

public sealed record R022PersistenceProjectionResult(
    IReadOnlyList<R022InvoiceControl> InvoiceControls,
    IReadOnlyList<R022TenderRow> ClassifiedTenders,
    IReadOnlyList<R022TenderRow> QuarantinedTenders);

public sealed class R022PersistenceProjector
{
    private static readonly IReadOnlyDictionary<string, string> TenderFields = new Dictionary<string, string>
    {
        ["tender_cash"] = "CASH", ["tender_card"] = "CARD", ["tender_cheque"] = "CHEQUE",
        ["tender_loyalty_points"] = "LOYALTY_POINTS", ["tender_gift_voucher"] = "GV",
        ["tender_credit_note_redeemed"] = "CREDITNOTE_REDEEM", ["tender_excess_gv"] = "EXCESS_GV",
        ["tender_round_off"] = "ROUND_OFF", ["tender_no_refund"] = "NO_REFUND",
        ["tender_others"] = "OTHERS", ["tender_tata_gv"] = "TATA_GV", ["tender_gift_card"] = "GIFTCARD",
        ["tender_tatacliq"] = "TATACLIQ", ["tender_gyftr"] = "GYFTR", ["tender_paytm"] = "PAYTM",
        ["tender_helios_omni"] = "HELIOSOMNI", ["tender_advance_redeem"] = "ADVANCERDEEM",
        ["tender_bhim_upi"] = "BHIMUPI", ["tender_phonepe"] = "PHONEPE",
        ["tender_bharatpe"] = "BHARATPE", ["tender_bajaj_finance"] = "BAJAJFIN",
        ["tender_razorpay"] = "RAZORPAY", ["tender_payment_type24"] = "PAYMENTTYPE24",
        ["tender_payment_type25"] = "PAYMENTTYPE25", ["tender_issued_credit_note"] = "ISSUED_CREDITNOTE",
        ["tender_cash_refund"] = "CASH_REFUND", ["tender_cheque_rtgs_refund"] = "CHEQUE_RTGS_REFUND"
    };

    public R022PersistenceProjectionResult Project(IEnumerable<StagedImportRow> stagedRows)
    {
        ArgumentNullException.ThrowIfNull(stagedRows);
        var invoices = new List<R022InvoiceControl>(); var classified = new List<R022TenderRow>(); var quarantined = new List<R022TenderRow>();
        foreach (var row in stagedRows)
        {
            var v = row.Values;
            var store = Required<string>(v, "store_code"); var invoice = Required<string>(v, "invoice_number");
            var date = Required<DateOnly>(v, "transaction_date");
            invoices.Add(new(row.SourceRowNumber, store, invoice, date, Required<string>(v, "source_transaction_type"),
                Required<decimal>(v, "source_invoice_quantity"), Required<decimal>(v, "source_net_value")));
            foreach (var (field, code) in TenderFields)
            {
                if (!v.TryGetValue(field, out var raw) || raw is not decimal amount || amount == 0m) continue;
                var isQuarantined = code == "PAYMENTTYPE25";
                var tender = new R022TenderRow(row.SourceRowNumber, store, invoice, date, code, amount,
                    isQuarantined, isQuarantined ? "UNRESOLVED_PAYMENTTYPE25" : null);
                (isQuarantined ? quarantined : classified).Add(tender);
            }
        }
        return new(invoices, classified, quarantined);
    }

    private static T Required<T>(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) && value is T typed
            ? typed : throw new InvalidOperationException($"Staged R022 field '{key}' is missing or invalid.");
}
