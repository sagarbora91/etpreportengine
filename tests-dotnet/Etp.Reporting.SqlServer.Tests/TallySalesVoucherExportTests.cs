using System.Text;
using System.Xml.Linq;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer.Tally;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

// 1.12 D1, master plan 7a task 8: the Sales voucher file. G01 is a cash sale of 1,180.00 (1,000.00 sales, CGST 9% 90.00,
// SGST 9% 90.00); the voucher total is GST-inclusive, as the customer paid it.
public sealed class TallySalesVoucherExportTests
{
    private static readonly DateOnly Day = new(2026, 8, 1);
    private const string Key = "ETP:TTGOLD:2027:T260801-001:SALES:1";

    private static TallyProfile Profile(string company = TallyTestCompany.Name) => TallyProfile.NewTest("TTGOLD", company, ["TTGOLD"]);

    private static AccountingVoucherDraft G01(string cash = "Cash - TTGOLD", string? centre = "Golden Store", string key = Key, string document = "T260801-001") => new(
        1, key, "Sales", Day, document, $"{key} | ETP TTGOLD invoice {document} dated 01-Aug-2026",
        [
            new(cash, 1180m, 0m, centre),
            new("Sales - GST Retail", 0m, 1000m, centre),
            new("Output CGST 9%", 0m, 90m, centre),
            new("Output SGST 9%", 0m, 90m, centre)
        ]);

    [Fact]
    public void G01_is_one_sales_voucher_with_Tally_signs_party_and_cost_centre()
    {
        var bytes = TallySalesVoucherXmlWriter.Write(Profile(), [G01()]);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(bytes));

        Assert.Equal("Import Data", xml.Root!.Element("HEADER")!.Element("TALLYREQUEST")!.Value);
        Assert.Equal("Vouchers", xml.Descendants("REPORTNAME").Single().Value);
        Assert.Equal(TallyTestCompany.Name, xml.Descendants("SVCURRENTCOMPANY").Single().Value);
        var voucher = xml.Descendants("VOUCHER").Single();
        Assert.Equal(("Sales", "Create", "Accounting Voucher View"), (voucher.Attribute("VCHTYPE")!.Value, voucher.Attribute("ACTION")!.Value, voucher.Attribute("OBJVIEW")!.Value));
        Assert.Equal("20260801", voucher.Element("DATE")!.Value);
        Assert.Equal("T260801-001", voucher.Element("VOUCHERNUMBER")!.Value);
        Assert.Equal("T260801-001", voucher.Element("REFERENCE")!.Value);
        Assert.Equal("Cash - TTGOLD", voucher.Element("PARTYLEDGERNAME")!.Value);
        Assert.StartsWith(Key, voucher.Element("NARRATION")!.Value, StringComparison.Ordinal);
        Assert.Equal(new[] { "Cash - TTGOLD|Yes|Yes|-1180.00", "Sales - GST Retail|No|No|1000.00", "Output CGST 9%|No|No|90.00", "Output SGST 9%|No|No|90.00" },
            voucher.Elements("ALLLEDGERENTRIES.LIST").Select(line =>
                $"{line.Element("LEDGERNAME")!.Value}|{line.Element("ISDEEMEDPOSITIVE")!.Value}|{line.Element("ISPARTYLEDGER")!.Value}|{line.Element("AMOUNT")!.Value}"));
        var allocations = voucher.Descendants("COSTCENTREALLOCATIONS.LIST").ToArray();
        Assert.Equal(4, allocations.Length);
        Assert.All(allocations, allocation => Assert.Equal("Golden Store", allocation.Element("NAME")!.Value));
        Assert.Equal("-1180.00", allocations[0].Element("AMOUNT")!.Value);
        Assert.Equal("Primary Cost Category", voucher.Descendants("CATEGORY").First().Value);
        Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public void Without_a_cost_centre_no_allocation_is_written()
    {
        var xml = XDocument.Parse(Encoding.UTF8.GetString(TallySalesVoucherXmlWriter.Write(Profile(), [G01(centre: null)])));
        Assert.Empty(xml.Descendants("CATEGORYALLOCATIONS.LIST"));
    }

    [Fact]
    public void An_ampersand_in_a_ledger_name_is_escaped_and_read_back_unchanged()
    {
        var voucher = G01(cash: "Cash & Card - TTGOLD");
        var bytes = TallySalesVoucherXmlWriter.Write(Profile(), [voucher]);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("<LEDGERNAME>Cash &amp; Card - TTGOLD</LEDGERNAME>", text, StringComparison.Ordinal);
        Assert.Contains("<PARTYLEDGERNAME>Cash &amp; Card - TTGOLD</PARTYLEDGERNAME>", text, StringComparison.Ordinal);
        var parsed = Assert.Single(TallyVoucherXmlReader.Read(bytes).Vouchers);
        Assert.Equal("Cash & Card - TTGOLD", parsed.Lines[0].LedgerName);
        Assert.Null(TallySalesVoucherXmlWriter.Verify(bytes, TallyTestCompany.Name, [voucher]));
    }

    [Fact]
    public void The_file_reads_back_with_the_same_company_keys_dates_and_signed_amounts()
    {
        var second = G01(key: "ETP:TTGOLD:2027:T260801-002:SALES:1", document: "T260801-002") with { Sequence = 2 };
        var vouchers = new[] { G01(), second };
        var bytes = TallySalesVoucherXmlWriter.Write(Profile(), vouchers);

        Assert.Null(TallySalesVoucherXmlWriter.Verify(bytes, TallyTestCompany.Name, vouchers));
        var document = TallyVoucherXmlReader.Read(bytes);
        Assert.Null(document.FailureReason);
        Assert.Equal(TallyTestCompany.Name, document.CompanyNameReported);
        Assert.Equal(new[] { Key, "ETP:TTGOLD:2027:T260801-002:SALES:1" }, document.Vouchers.Select(voucher => voucher.Keys.Single()));
        Assert.All(document.Vouchers, voucher => Assert.Equal((Day, "Sales"), (voucher.VoucherDate!.Value, voucher.VoucherType!)));
        Assert.Equal(new decimal?[] { -1180m, 1000m, 90m, 90m }, document.Vouchers[0].Lines.Select(line => line.Amount));
        Assert.Equal(1180m, vouchers.Sum(voucher => voucher.Total) / 2);
    }

    [Fact]
    public void A_changed_file_or_another_company_fails_RULE_PAY_001()
    {
        var bytes = TallySalesVoucherXmlWriter.Write(Profile(), [G01()]);
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("<AMOUNT>1000.00</AMOUNT>", "<AMOUNT>999.00</AMOUNT>", StringComparison.Ordinal));

        Assert.Contains("ledger lines", TallySalesVoucherXmlWriter.Verify(tampered, TallyTestCompany.Name, [G01()]), StringComparison.Ordinal);
        Assert.Contains("names company", TallySalesVoucherXmlWriter.Verify(bytes, "TEST - Somebody Else", [G01()]), StringComparison.Ordinal);
        Assert.Contains("1 voucher(s) instead of 2", TallySalesVoucherXmlWriter.Verify(bytes, TallyTestCompany.Name,
            [G01(), G01(key: "ETP:TTGOLD:2027:T260801-002:SALES:1", document: "T260801-002")]), StringComparison.Ordinal);
        Assert.Contains("cannot be read back", TallySalesVoucherXmlWriter.Verify("not xml"u8.ToArray(), TallyTestCompany.Name, [G01()]), StringComparison.Ordinal);
    }

    [Fact]
    public void Anything_Tally_could_misread_is_refused_before_a_byte_is_written()
    {
        var unbalanced = G01() with { Lines = [new("Cash - TTGOLD", 1180m, 0m, null), new("Sales - GST Retail", 0m, 1000m, null)] };
        var threeDecimals = G01() with { Lines = [new("Cash - TTGOLD", 10.005m, 0m, null), new("Sales - GST Retail", 0m, 10.005m, null)] };
        var bothSides = G01() with { Lines = [new("Cash - TTGOLD", 10m, 10m, null), new("Sales - GST Retail", 0m, 0m, null)] };
        var noKey = G01() with { Narration = "ETP TTGOLD invoice T260801-001" };
        var unsafeNumber = G01(key: "ETP:TTGOLD:2027:T 1:SALES:1", document: "T 1");

        Assert.Contains("do not balance", Refused(unbalanced), StringComparison.Ordinal);
        Assert.Contains("more than two decimals", Refused(threeDecimals), StringComparison.Ordinal);
        Assert.Contains("either a debit or a credit", Refused(bothSides), StringComparison.Ordinal);
        Assert.Contains("key exactly once", Refused(noKey), StringComparison.Ordinal);
        Assert.Contains("KEY_UNSAFE", Refused(unsafeNumber), StringComparison.Ordinal);
        Assert.Contains("appears twice", Assert.Throws<InvalidOperationException>(() => TallySalesVoucherXmlWriter.Write(Profile(), [G01(), G01()])).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => TallySalesVoucherXmlWriter.Write(Profile(), []));
        Assert.Contains("accounting voucher view", Assert.Throws<InvalidOperationException>(() =>
            TallySalesVoucherXmlWriter.Write(Profile() with { VoucherView = "INVOICE" }, [G01()])).Message, StringComparison.Ordinal);

        static string Refused(AccountingVoucherDraft voucher) =>
            Assert.Throws<InvalidOperationException>(() => TallySalesVoucherXmlWriter.Write(Profile(), [voucher])).Message;
    }

    [Fact]
    public async Task WriteAsync_writes_the_same_bytes_as_Write()
    {
        using var stream = new MemoryStream();
        await TallySalesVoucherXmlWriter.WriteAsync(stream, Profile(), [G01()]);
        Assert.Equal(TallySalesVoucherXmlWriter.Write(Profile(), [G01()]), stream.ToArray());
    }

    // The test-data generator, the composer and the writer agree: every synthetic invoice on an Educational day is planned,
    // its voucher total is the GST-inclusive bill, and the file reads back.
    [Fact]
    public void Generated_test_days_compose_and_write_cleanly()
    {
        var dates = TallyTestDataGenerator.PickDates(TallyEducationalCalendar.Default, new(2026, 8, 3), 3);
        var context = new SalesVoucherContext(Profile(), "Golden Store", false, TallyTestDataGenerator.Mappings("TTGOLD"));
        foreach (var date in dates)
        {
            var invoices = TallyTestDataGenerator.Invoices("TTGOLD", date, 7, seed: date.Day);
            var plan = TallySalesVoucherComposer.Compose(context, invoices);
            Assert.Null(plan.BlockingReason);
            Assert.All(plan.Vouchers, voucher => Assert.Equal(TallyVoucherStatus.Planned, voucher.Status));
            Assert.All(plan.Vouchers, voucher => Assert.Equal(
                invoices.Single(invoice => invoice.DocumentNumber == voucher.DocumentNumber).Lines.Sum(line => line.Gross!.Value), voucher.ExpectedTotal));

            var drafts = plan.Vouchers.Select(voucher => new AccountingVoucherDraft(voucher.Sequence, voucher.CorrespondenceKey, voucher.VoucherType,
                voucher.VoucherDate, voucher.DocumentNumber, voucher.Narration,
                voucher.Entries.Select(entry => new AccountingVoucherLine(entry.LedgerName, entry.Debit, entry.Credit, entry.CostCentre)).ToArray())).ToArray();
            Assert.All(drafts, draft => Assert.Equal(plan.Vouchers.Single(voucher => voucher.Sequence == draft.Sequence).ExpectedTotal,
                draft.Lines.Where(line => line.LedgerName.StartsWith("Cash", StringComparison.Ordinal) || line.LedgerName.Contains("Settlement", StringComparison.Ordinal))
                    .Sum(line => line.Debit) + draft.Lines.Where(line => line.LedgerName == "Round Off").Sum(line => line.Debit - line.Credit)));
            var bytes = TallySalesVoucherXmlWriter.Write(Profile(), drafts);
            Assert.Null(TallySalesVoucherXmlWriter.Verify(bytes, TallyTestCompany.Name, drafts));
            Assert.All(TallyVoucherXmlReader.Read(bytes).Vouchers, voucher => Assert.True(TallyEducationalCalendar.Default.IsAllowed(voucher.VoucherDate!.Value)));
        }
    }

    // Decision 41: a bill cancelled the same day is left out like the DSR leaves it out of the invoice count; it does not
    // block the day, and the source hash of every other invoice stays what it was before the rule.
    [Fact]
    public void A_bill_cancelled_the_same_day_is_left_out_without_blocking_the_day()
    {
        var invoices = TallyTestDataGenerator.Invoices("TTGOLD", Day, 2);
        var cancelled = invoices[1] with { CancelledSameDay = true };
        var context = new SalesVoucherContext(Profile(), null, false, TallyTestDataGenerator.Mappings("TTGOLD"));

        var plan = TallySalesVoucherComposer.Compose(context, [invoices[0], cancelled]);

        Assert.Null(plan.BlockingReason);
        var left = plan.Vouchers.Single(voucher => voucher.DocumentNumber == cancelled.DocumentNumber);
        Assert.Equal((TallyVoucherStatus.Blocked, TallySalesVoucherComposer.CancelledSameDayCode, true), (left.Status, left.BlockCode!, left.LeftOutByScope));
        Assert.Contains("decision 41", left.BlockedReason, StringComparison.Ordinal);
        Assert.Equal(TallyVoucherStatus.Planned, plan.Vouchers.Single(voucher => voucher.DocumentNumber == invoices[0].DocumentNumber).Status);
        Assert.Equal(plan.Vouchers.Single(voucher => voucher.DocumentNumber == invoices[0].DocumentNumber).ExpectedTotal, plan.DebitTotal);

        Assert.Equal(TallySalesVoucherComposer.SourceHash(invoices[1]), TallySalesVoucherComposer.SourceHash(invoices[1] with { CancelledSameDay = false }));
        Assert.NotEqual(TallySalesVoucherComposer.SourceHash(invoices[1]), TallySalesVoucherComposer.SourceHash(cancelled));

        var onlyCancelled = TallySalesVoucherComposer.Compose(context, [cancelled]);
        Assert.Equal("No invoice of this day can be sent yet; every invoice is left out.", onlyCancelled.BlockingReason);
    }

    [Fact]
    public void Export_is_refused_for_live_books_and_for_a_test_company_that_could_pass_for_the_real_one()
    {
        Assert.Null(SqlServerTallySalesBatchService.ExportRefusal(Profile()));
        Assert.Null(SqlServerTallySalesBatchService.ExportRefusal(Profile("TEST - ETP Golden")));
        Assert.Contains("TEST in its name", SqlServerTallySalesBatchService.ExportRefusal(Profile("Saagar Traders")), StringComparison.Ordinal);
        Assert.Contains("Live books", SqlServerTallySalesBatchService.ExportRefusal(Profile() with { Environment = "PRODUCTION" }), StringComparison.Ordinal);
        Assert.Contains("switched off", SqlServerTallySalesBatchService.ExportRefusal(Profile() with { IsEnabled = false }), StringComparison.Ordinal);
        Assert.Contains("stock items", SqlServerTallySalesBatchService.ExportRefusal(Profile() with { PostingModel = "INVENTORY" }), StringComparison.Ordinal);
    }
}
