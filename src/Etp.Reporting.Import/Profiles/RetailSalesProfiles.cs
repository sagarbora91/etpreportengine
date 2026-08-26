using Etp.Reporting.Domain.Imports;

namespace Etp.Reporting.Import.Profiles;

public static class RetailSalesProfiles
{
    public static readonly IReadOnlyList<string> R003Headers =
    [
        "TRANS_TYPE", "STORE CODE", "STORE NAME", "STORE TYPE", "CHANNEL", "REGION", "CITY",
        "INVOICE NUMBER", "INVOICE DATE", "ITEMNUMBER", "BRAND", "BRAND NAME", "CLUSTER", "GENDER",
        "QTY", "UCP", "GROSSUCP", "SCH_DISCOUNTS", "NETGROSS", "ACTIVATION DETAILS", "USER DISCOUNTS",
        "OTHERCHARG", "NETAMOUNT", "USER DISCOUNT DETAILS", "TAX", "NETVALUE", "INVOICE REF NO",
        "INVOICE REF DATE", "CUSTOMERNUMBER", "CUSTOMERNAME", "CONTACTNO", "ULP NO", "EASTIMESTAMP", "STORETIMESTAMP"
    ];

    public static readonly IReadOnlyList<string> R013Headers =
    [
        "TRANS_TYPE", "STORE CODE", "STORE NAME", "STORE TYPE", "CHANNEL", "REGION", "CITY",
        "ITEMNUMBER", "BRAND", "BRANDNAME", "CLUSTER", "GENDER", "CRO NUMBER", "CRO NAME", "CUSTOMERNAME", "CONTACTNO",
        "INVNUMBER", "INVDATE", "QTY", "UCP", "GROSSUCP", "SCH_DISCOUNTS", "NETGROSS", "PRE_DISCOUNTS", "NETAMOUNT", "NETVALUE",
        "INVREFNO", "INVREFDATE"
    ];

    public static readonly IReadOnlyList<string> R025Headers =
    [
        "TRANS_TYPE", "STORE CODE", "STORENAME", "STORETYPE", "CHANNEL", "REGION", "CITY",
        "ITEMNUMBER", "HSNCODE", "BRAND", "BRANDNAME", "CLUSTER", "GENDER", "INVNUMBER",
        "INVDATE", "QTY", "UCP", "GROSSUCP", "SCH_DISCOUNTS", "USER_DISCOUNTS",
        "HELIOS_CREDITNOTE", "PROMO_GC", "NETGROSS", "PRE_DISCOUNTS", "NETAMOUNT",
        "SGST/UTGST %", "SGST/UTGST VALUE", "CSGT %", "CSGT VALUE", "IGST %", "IGST VALUE",
        "CESS %", "CESS VALUE", "TAX", "NETVALUE", "INVREFNO", "INVREFDATE", "CUSTOMERNAME",
        "CONTACTNO", "ULPNUMBER", "STORETIMESTAMP"
    ];

    public static readonly IReadOnlyList<string> R022Headers =
    [
        "TRANS_TYPE", "STORE CODE", "STORE NAME", "STORE TYPE", "CHANNEL", "REGION", "STATE", "CITY",
        "INVNUMBER", "CUSTOMERNAME", "ContactNo", "InvoiceQuantity", "INVOICEDATE", "INVOICEYEAR",
        "CASH", "CARD", "CHEQUE", "LOYALTY_POINTS", "GV", "CREDITNOTE REDEEM", "EXCESS GV",
        "ROUND OFF", "NO REFUND", "OTHERS", "TATA GV", "GIFTCARD", "TataCliQ", "GYFTR", "PAYTM",
        "HELIOSOMNI", "ADVANCERDEEM", "BHIMUPI", "PHONEPE", "BHARATPE", "BAJAJFIN", "RAZORPAY",
        "PAYMENTTYPE24", "PAYMENTTYPE25", "ISSUED CREDITNOTE", "CASH REFUND", "Cheque/RTGS REFUND",
        "NetValue", "ENCIRCLE", "STORETIMESTAMP", "REFERENCENUMBER", "REFERENCEYEAR"
    ];

    public static ImportProfile R025 { get; } = Create(
        "R025",
        R025Headers,
        new("TRANS_TYPE", "source_transaction_type", CanonicalDataType.Text, true),
        new("STORE CODE", "store_code", CanonicalDataType.Identifier, true),
        new("ITEMNUMBER", "product_code", CanonicalDataType.Identifier, true),
        new("HSNCODE", "hsn_code", CanonicalDataType.Identifier, false),
        new("BRAND", "source_brand_code", CanonicalDataType.Identifier, false),
        new("BRANDNAME", "source_brand_name", CanonicalDataType.Text, false),
        new("CLUSTER", "brand_segment_code", CanonicalDataType.Identifier, false),
        new("GENDER", "gender_code", CanonicalDataType.Text, false),
        new("INVNUMBER", "invoice_number", CanonicalDataType.Identifier, true),
        new("INVDATE", "transaction_date", CanonicalDataType.Date, true),
        new("QTY", "source_quantity", CanonicalDataType.Decimal, true),
        new("UCP", "source_ucp", CanonicalDataType.Decimal, false),
        new("GROSSUCP", "source_gross_ucp", CanonicalDataType.Decimal, false),
        new("SCH_DISCOUNTS", "scheme_discount", CanonicalDataType.Decimal, false),
        new("USER_DISCOUNTS", "user_discount", CanonicalDataType.Decimal, false),
        new("PRE_DISCOUNTS", "pre_discount", CanonicalDataType.Decimal, false),
        new("NETAMOUNT", "source_net_amount", CanonicalDataType.Decimal, true),
        new("TAX", "source_tax_amount", CanonicalDataType.Decimal, false),
        new("NETVALUE", "source_net_value", CanonicalDataType.Decimal, true),
        new("INVREFNO", "reference_invoice_number", CanonicalDataType.Identifier, false),
        new("INVREFDATE", "reference_invoice_date", CanonicalDataType.Date, false),
        new("STORETIMESTAMP", "source_store_timestamp", CanonicalDataType.Text, false));

    public static ImportProfile R003 { get; } = Create(
        "R003",
        R003Headers,
        new("TRANS_TYPE", "source_transaction_type", CanonicalDataType.Text, true),
        new("STORE CODE", "store_code", CanonicalDataType.Identifier, true),
        new("INVOICE NUMBER", "invoice_number", CanonicalDataType.Identifier, true),
        new("INVOICE DATE", "transaction_date", CanonicalDataType.Date, true),
        new("ITEMNUMBER", "product_code", CanonicalDataType.Identifier, true),
        new("QTY", "source_quantity", CanonicalDataType.Decimal, true),
        new("SCH_DISCOUNTS", "scheme_discount", CanonicalDataType.Decimal, false),
        new("USER DISCOUNTS", "user_discount", CanonicalDataType.Decimal, false),
        new("OTHERCHARG", "other_charges", CanonicalDataType.Decimal, false),
        new("NETAMOUNT", "source_net_amount", CanonicalDataType.Decimal, true),
        new("NETVALUE", "source_net_value", CanonicalDataType.Decimal, true));

    public static ImportProfile R013 { get; } = Create(
        "R013",
        R013Headers,
        new("TRANS_TYPE", "source_transaction_type", CanonicalDataType.Text, true),
        new("STORE CODE", "store_code", CanonicalDataType.Identifier, true),
        new("ITEMNUMBER", "product_code", CanonicalDataType.Identifier, true),
        new("CRO NUMBER", "cro_number", CanonicalDataType.Identifier, true),
        new("INVNUMBER", "invoice_number", CanonicalDataType.Identifier, true),
        new("INVDATE", "transaction_date", CanonicalDataType.Date, true),
        new("QTY", "source_quantity", CanonicalDataType.Decimal, true),
        new("SCH_DISCOUNTS", "scheme_discount", CanonicalDataType.Decimal, false),
        new("PRE_DISCOUNTS", "pre_discount", CanonicalDataType.Decimal, false),
        new("NETAMOUNT", "source_net_amount", CanonicalDataType.Decimal, true),
        new("NETVALUE", "source_net_value", CanonicalDataType.Decimal, true));

    public static ImportProfile R022 { get; } = Create(
        "R022",
        R022Headers,
        new("TRANS_TYPE", "source_transaction_type", CanonicalDataType.Text, true),
        new("STORE CODE", "store_code", CanonicalDataType.Identifier, true),
        new("INVNUMBER", "invoice_number", CanonicalDataType.Identifier, true),
        new("InvoiceQuantity", "source_invoice_quantity", CanonicalDataType.Decimal, true),
        new("INVOICEDATE", "transaction_date", CanonicalDataType.Date, true),
        new("CASH", "tender_cash", CanonicalDataType.Decimal, false),
        new("CARD", "tender_card", CanonicalDataType.Decimal, false),
        new("CHEQUE", "tender_cheque", CanonicalDataType.Decimal, false),
        new("LOYALTY_POINTS", "tender_loyalty_points", CanonicalDataType.Decimal, false),
        new("GV", "tender_gift_voucher", CanonicalDataType.Decimal, false),
        new("CREDITNOTE REDEEM", "tender_credit_note_redeemed", CanonicalDataType.Decimal, false),
        new("EXCESS GV", "tender_excess_gv", CanonicalDataType.Decimal, false),
        new("ROUND OFF", "tender_round_off", CanonicalDataType.Decimal, false),
        new("NO REFUND", "tender_no_refund", CanonicalDataType.Decimal, false),
        new("OTHERS", "tender_others", CanonicalDataType.Decimal, false),
        new("TATA GV", "tender_tata_gv", CanonicalDataType.Decimal, false),
        new("GIFTCARD", "tender_gift_card", CanonicalDataType.Decimal, false),
        new("TataCliQ", "tender_tatacliq", CanonicalDataType.Decimal, false),
        new("GYFTR", "tender_gyftr", CanonicalDataType.Decimal, false),
        new("PAYTM", "tender_paytm", CanonicalDataType.Decimal, false),
        new("HELIOSOMNI", "tender_helios_omni", CanonicalDataType.Decimal, false),
        new("ADVANCERDEEM", "tender_advance_redeem", CanonicalDataType.Decimal, false),
        new("BHIMUPI", "tender_bhim_upi", CanonicalDataType.Decimal, false),
        new("PHONEPE", "tender_phonepe", CanonicalDataType.Decimal, false),
        new("BHARATPE", "tender_bharatpe", CanonicalDataType.Decimal, false),
        new("BAJAJFIN", "tender_bajaj_finance", CanonicalDataType.Decimal, false),
        new("RAZORPAY", "tender_razorpay", CanonicalDataType.Decimal, false),
        new("PAYMENTTYPE24", "tender_payment_type24", CanonicalDataType.Decimal, false),
        new("PAYMENTTYPE25", "tender_payment_type25", CanonicalDataType.Decimal, false),
        new("ISSUED CREDITNOTE", "tender_issued_credit_note", CanonicalDataType.Decimal, false),
        new("CASH REFUND", "tender_cash_refund", CanonicalDataType.Decimal, false),
        new("Cheque/RTGS REFUND", "tender_cheque_rtgs_refund", CanonicalDataType.Decimal, false),
        new("NetValue", "source_net_value", CanonicalDataType.Decimal, true),
        new("STORETIMESTAMP", "source_store_timestamp", CanonicalDataType.Text, false),
        new("REFERENCENUMBER", "reference_invoice_number", CanonicalDataType.Identifier, false));

    public static IReadOnlyList<ImportProfile> FirstSalesSlice { get; } = [R025, R022, R013, R003];

    private static ImportProfile Create(string reportCode, IReadOnlyList<string> headers, params ImportFieldMapping[] fields) =>
        new(reportCode, "ETP_2026_08", "1", ImportProfileMatcher.CreateHeaderSignature(headers), fields, headers);
}
