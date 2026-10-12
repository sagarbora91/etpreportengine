using System.Globalization;
using Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <summary>Synthetic Tally test data for the VM test company (decision 49): invoices on days Educational mode accepts, with
/// GST at 5, 12 and 18 per cent, cash, card and UPI payments and paise rounded off, plus the ledger mappings that match the
/// masters ETP proposes. Deterministic for a seed. No customer data: item codes and amounts only.</summary>
public static class TallyTestDataGenerator
{
    private static readonly decimal[] NetAmounts = [1000.00m, 2499.50m, 847.46m, 12999.00m, 355.93m, 5084.75m, 1999.99m];
    private static readonly decimal[] GstRates = [5m, 12m, 18m];
    private static readonly (string Code, string Mode)[] Payments = [("CASH", "Cash"), ("CARD", "Card"), ("PHONEPE", "UPI")];

    /// <summary><paramref name="count"/> allowed dates from <paramref name="from"/> on, oldest first.</summary>
    public static IReadOnlyList<DateOnly> PickDates(TallyEducationalCalendar calendar, DateOnly from, int count)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var result = new List<DateOnly>();
        for (var date = calendar.NextAllowed(from); result.Count < count; date = calendar.NextAllowed(date.AddDays(1)))
            result.Add(date);
        return result;
    }

    /// <summary>ETP's invoice year: the year in which the financial year ends (April to March).</summary>
    public static int InvoiceYear(DateOnly date) => date.Month >= 4 ? date.Year + 1 : date.Year;

    /// <summary><paramref name="count"/> sales invoices of <paramref name="storeCode"/> on <paramref name="date"/>, each one item,
    /// one payment and, when the bill has paise, a round-off row.</summary>
    public static IReadOnlyList<InvoiceAccountingSource> Invoices(string storeCode, DateOnly date, int count, int seed = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var store = storeCode.Trim().ToUpperInvariant();
        var result = new List<InvoiceAccountingSource>();
        for (var number = 1; number <= count; number++)
        {
            var pick = Math.Abs(seed * 31 + number * 7);
            var net = NetAmounts[pick % NetAmounts.Length];
            var rate = GstRates[pick % GstRates.Length];
            var tax = decimal.Round(net * rate / 100m, 2, MidpointRounding.AwayFromZero);
            var cgst = decimal.Round(tax / 2m, 2, MidpointRounding.AwayFromZero);
            var sgst = tax - cgst;
            var gross = net + tax;
            var paid = decimal.Round(gross, 0, MidpointRounding.AwayFromZero);
            var (code, mode) = Payments[pick % Payments.Length];
            var document = string.Create(CultureInfo.InvariantCulture, $"T{date:yyMMdd}-{number:000}");
            var item = string.Create(CultureInfo.InvariantCulture, $"TESTITEM{pick % 9 + 1}");
            var tenders = new List<InvoiceSourceTender> { new(code, mode, paid) };
            if (gross != paid) tenders.Add(new(TallySalesVoucherComposer.RoundOffCode, null, gross - paid));
            result.Add(new(-number, store, InvoiceYear(date), document, date,
                [new InvoiceSourceLine("1", item, "INV", 1m, gross, net, tax)],
                [new InvoiceTaxRow(item, rate / 2m, cgst, rate / 2m, sgst, null, null, null)],
                tenders));
        }
        return result;
    }

    /// <summary>The ledger mappings that match <see cref="TallyMastersPlanner"/>'s proposed names for one store.</summary>
    public static IReadOnlyList<TallyLedgerMapping> Mappings(string storeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        var store = storeCode.Trim().ToUpperInvariant();
        var result = new List<TallyLedgerMapping>();
        void Add(string businessEvent, string ledger) => result.Add(new(businessEvent, result.Count + 1, 1, ledger, ledger));
        Add("TENDER_CASH", $"Cash - {store}");
        Add("TENDER_CARD", $"Card Settlement - {store}");
        Add("TENDER_UPI", $"UPI Settlement - {store}");
        Add("SALES_REVENUE", TallyMastersPlanner.DefaultSalesLedger);
        Add(TallySalesVoucherComposer.RoundOffCode, TallyMastersPlanner.DefaultRoundOffLedger);
        foreach (var rate in GstRates)
        {
            var half = rate / 2m;
            var percent = half.ToString("0.##", CultureInfo.InvariantCulture);
            Add(TallySalesVoucherComposer.TaxEvent("CGST", half), $"Output CGST {percent}%");
            Add(TallySalesVoucherComposer.TaxEvent("SGST", half), $"Output SGST {percent}%");
        }
        return result;
    }
}
