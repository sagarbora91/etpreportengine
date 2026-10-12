using System.Text;
using System.Xml.Linq;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer.Tally;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

// 1.12 D1, decision 49 (3): the masters of a brand-new Tally company, made from ETP data. Synthetic stores only.
public sealed class TallyMastersExportTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static TallyMastersSource Source(
        IReadOnlyList<TallyMastersStore>? stores = null,
        IReadOnlyList<TallyLedgerMappingRow>? mappings = null,
        IReadOnlyList<string>? modes = null,
        IReadOnlyList<TallyMastersGstRate>? rates = null)
    {
        stores ??= [new TallyMastersStore("TTONE", "Titan Synthetic One", "Store One"), new TallyMastersStore("TTTWO", "Titan Synthetic Two", null)];
        var profile = TallyProfile.NewTest("TTSAAGAR", TallyTestCompany.Name, stores.Select(store => store.StoreCode).ToArray());
        return new(profile, stores, modes ?? ["Cash", "Card", "UPI", "CN", "TC", "Service Cash", "Service UPI"],
            rates ?? [new TallyMastersGstRate("CGST", 9m), new TallyMastersGstRate("SGST", 9m), new TallyMastersGstRate("CGST", 2.5m), new TallyMastersGstRate("SGST", 2.5m)],
            mappings ?? [], Today);
    }

    private static TallyLedgerMappingRow Mapping(string businessEvent, string? store, string ledger, bool active = true, DateOnly? to = null) =>
        new(businessEvent, store, ledger, ledger, new(2026, 8, 1), to, 1, active, "SHOP\\owner", new(2026, 8, 1));

    [Fact]
    public void Two_stores_get_cost_centres_per_store_tender_ledgers_and_shared_sales_and_GST_ledgers()
    {
        var plan = TallyMastersPlanner.Plan(Source());

        Assert.Equal(TallyTestCompany.Name, plan.CompanyName);
        Assert.Equal(new[] { "Store One|TTONE|False", "Titan Synthetic Two (TTTWO)|TTTWO|True" },
            plan.CostCentres.Select(centre => $"{centre.Name}|{centre.StoreCode}|{centre.Proposed}"));
        string Ledger(string name) { var ledger = plan.Ledgers.Single(item => item.Name == name); return $"{ledger.Parent}|{string.Join(",", ledger.StoreCodes)}|{ledger.BusinessEvent}"; }
        Assert.Equal("Sales Accounts|TTONE,TTTWO|SALES_REVENUE", Ledger("Sales - GST Retail"));
        Assert.Equal("Duties & Taxes|TTONE,TTTWO|OUTPUT_CGST_9", Ledger("Output CGST 9%"));
        Assert.Equal("Duties & Taxes|TTONE,TTTWO|OUTPUT_SGST_2.5", Ledger("Output SGST 2.5%"));
        Assert.Equal("Indirect Expenses|TTONE,TTTWO|ROUND_OFF", Ledger("Round Off"));
        Assert.Equal("Cash-in-Hand|TTONE|TENDER_CASH", Ledger("Cash - TTONE"));
        Assert.Equal("Cash-in-Hand|TTTWO|TENDER_CASH", Ledger("Cash - TTTWO"));
        Assert.Equal($"{TallyMastersPlanner.CardUpiGroup}|TTONE|TENDER_CARD", Ledger("Card Settlement - TTONE"));
        Assert.Equal($"{TallyMastersPlanner.CardUpiGroup}|TTTWO|TENDER_UPI", Ledger("UPI Settlement - TTTWO"));
        Assert.Equal($"{TallyMastersPlanner.CreditNoteGroup}|TTONE|TENDER_CN", Ledger("Credit Note Redeemed - TTONE"));
        Assert.Equal($"{TallyMastersPlanner.OtherTenderGroup}|TTONE|TENDER_TC", Ledger("Other Tender - TTONE"));
        Assert.Equal("Bank Accounts|TTONE|", Ledger("Bank - TTONE"));
        Assert.Equal("Sundry Debtors|TTONE,TTTWO|", Ledger("Cash Sales"));
        Assert.DoesNotContain(plan.Ledgers, ledger => ledger.Name.Contains("Service", StringComparison.Ordinal));
        var cgst = plan.Ledgers.Single(ledger => ledger.Name == "Output CGST 9%");
        Assert.Equal(("Central Tax", 9m), (cgst.GstDutyHead!, cgst.GstRate!.Value));
        Assert.Equal("State Tax", plan.Ledgers.Single(ledger => ledger.Name == "Output SGST 2.5%").GstDutyHead);
        Assert.All(plan.Ledgers.Where(ledger => ledger.BusinessEvent is not null), ledger => Assert.True(ledger.CostCentresOn));
        Assert.Equal(new[] { TallyMastersPlanner.CardUpiGroup, TallyMastersPlanner.CreditNoteGroup, TallyMastersPlanner.OtherTenderGroup }.Order(),
            plan.Groups.Select(group => group.Name).Order());
        Assert.Equal(plan.Ledgers.Count, plan.Ledgers.Select(ledger => ledger.Name.ToUpperInvariant()).Distinct().Count());
        Assert.Contains(plan.ReviewPoints, point => point.Contains("Titan Synthetic Two (TTTWO)", StringComparison.Ordinal) && point.Contains("COST_CENTRE_MISSING", StringComparison.Ordinal));
        var sales = Assert.Single(plan.VoucherTypes);
        Assert.Equal(("Sales", true), (sales.Name, sales.BuiltIn));
    }

    [Fact]
    public void An_approved_mapping_names_the_ledger_and_only_proposals_need_approval()
    {
        var plan = TallyMastersPlanner.Plan(Source(mappings:
        [
            Mapping("TENDER_CASH", "TTONE", "Cash in hand - One"),
            Mapping("SALES_REVENUE", null, "Sales @ GST"),
            Mapping("TENDER_CASH", "TTTWO", "Old cash", active: false),
            Mapping("ROUND_OFF", null, "Expired round off", to: new(2026, 9, 30))
        ]));

        var cash = plan.Ledgers.Single(ledger => ledger.Name == "Cash in hand - One");
        Assert.True(cash.FromApprovedMapping);
        Assert.True(plan.Ledgers.Single(ledger => ledger.Name == "Sales @ GST").FromApprovedMapping);
        Assert.DoesNotContain(plan.Ledgers, ledger => ledger.Name is "Old cash" or "Expired round off" or "Sales - GST Retail");
        Assert.False(plan.Ledgers.Single(ledger => ledger.Name == "Cash - TTTWO").FromApprovedMapping);
        Assert.DoesNotContain(plan.MappingsToApprove, mapping => mapping is { StoreCode: "TTONE", BusinessEvent: "TENDER_CASH" });
        Assert.DoesNotContain(plan.MappingsToApprove, mapping => mapping.BusinessEvent == "SALES_REVENUE");
        Assert.Contains(new ProposedLedgerMapping("TTTWO", "TENDER_CASH", "Cash - TTTWO"), plan.MappingsToApprove);
        Assert.Contains(new ProposedLedgerMapping("TTONE", "ROUND_OFF", "Round Off"), plan.MappingsToApprove);
    }

    [Fact]
    public void One_store_without_a_cost_centre_needs_none()
    {
        var plan = TallyMastersPlanner.Plan(Source(stores: [new("TTONE", "Titan Synthetic One", null)]));
        Assert.Empty(plan.CostCentres);
        Assert.All(plan.Ledgers, ledger => Assert.False(ledger.CostCentresOn));
        Assert.Contains(plan.SetupSteps, step => step.StartsWith("Cost centres are not needed", StringComparison.Ordinal));
        var xml = XDocument.Parse(Encoding.UTF8.GetString(TallyMastersWriter.Xml(plan)));
        Assert.Empty(xml.Descendants("COSTCENTRE"));
        Assert.All(xml.Descendants("LEDGER"), ledger => Assert.Equal("No", ledger.Element("ISCOSTCENTRESON")!.Value));
    }

    [Fact]
    public void The_import_file_creates_groups_then_cost_centres_then_ledgers_with_GST_details()
    {
        var plan = TallyMastersPlanner.Plan(Source(mappings: [Mapping("TENDER_CARD", "TTONE", "Card & UPI - One")]));
        var bytes = TallyMastersWriter.Xml(plan);
        var text = Encoding.UTF8.GetString(bytes);
        var xml = XDocument.Parse(text);

        Assert.Equal("All Masters", xml.Descendants("REPORTNAME").Single().Value);
        Assert.Equal(TallyTestCompany.Name, xml.Descendants("SVCURRENTCOMPANY").Single().Value);
        var kinds = xml.Descendants("TALLYMESSAGE").Select(message => message.Elements().Single().Name.LocalName).ToArray();
        Assert.Equal(kinds.OrderBy(kind => kind switch { "GROUP" => 0, "COSTCENTRE" => 1, _ => 2 }), kinds);
        Assert.Equal(plan.Groups.Count + plan.CostCentres.Count + plan.Ledgers.Count, kinds.Length);
        Assert.All(xml.Descendants("TALLYMESSAGE").Select(message => message.Elements().Single()), master =>
        {
            Assert.Equal("Create", master.Attribute("ACTION")!.Value);
            Assert.Equal(master.Attribute("NAME")!.Value, master.Descendants("NAME").First().Value);
        });
        Assert.Contains("NAME=\"Card &amp; UPI - One\"", text, StringComparison.Ordinal);
        Assert.Contains("<PARENT>Duties &amp; Taxes</PARENT>", text, StringComparison.Ordinal);
        var cgst = xml.Descendants("LEDGER").Single(ledger => ledger.Attribute("NAME")!.Value == "Output CGST 2.5%");
        Assert.Equal(("GST", "Central Tax", "2.5"), (cgst.Element("TAXTYPE")!.Value, cgst.Element("GSTDUTYHEAD")!.Value, cgst.Element("RATEOFTAXCALCULATION")!.Value));
        Assert.Null(xml.Descendants("LEDGER").Single(ledger => ledger.Attribute("NAME")!.Value == "Cash - TTONE").Element("TAXTYPE"));
        Assert.Equal("Primary Cost Category", xml.Descendants("COSTCENTRE").First().Element("CATEGORY")!.Value);
        Assert.Equal(TallyMastersPlanner.CardUpiGroup, xml.Descendants("LEDGER").Single(ledger => ledger.Attribute("NAME")!.Value == "Card & UPI - One").Element("PARENT")!.Value);
    }

    [Fact]
    public void The_accountant_s_list_names_every_master_and_what_is_only_a_proposal()
    {
        var plan = TallyMastersPlanner.Plan(Source(mappings: [Mapping("TENDER_CASH", "TTONE", "Cash in hand - One")]));
        var review = TallyMastersWriter.Review(plan, new DateTime(2026, 10, 12, 21, 5, 0));

        Assert.StartsWith($"Tally masters for '{TallyTestCompany.Name}' (TEST company)", review, StringComparison.Ordinal);
        Assert.Contains("12-Oct-2026 21:05", review, StringComparison.Ordinal);
        foreach (var heading in new[] { "1. Company setup in Tally", "2. Ledger groups ETP creates", "3. Cost centres (one per store)", "4. Ledgers", "5. Voucher types", "6. Points to agree with the accountant", "7. Ledger names to approve in ETP" })
            Assert.Contains(heading, review, StringComparison.Ordinal);
        foreach (var ledger in plan.Ledgers) Assert.Contains(ledger.Name, review, StringComparison.Ordinal);
        Assert.Contains("Cash in hand - One  (under Cash-in-Hand; stores TTONE)  [approved in ETP]", review, StringComparison.Ordinal);
        Assert.Contains("Cash - TTTWO  (under Cash-in-Hand; stores TTTWO)  [ETP's proposal]", review, StringComparison.Ordinal);
        Assert.Contains("Output CGST 9%  (under Duties & Taxes, GST Central Tax 9%", review, StringComparison.Ordinal);
        Assert.Contains("TTTWO  TENDER_CASH  →  Cash - TTTWO", review, StringComparison.Ordinal);
        Assert.Contains("port 9000", review, StringComparison.Ordinal);
        Assert.Contains("Educational mode", review, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", review.Replace("\r", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_GST_imports_the_list_says_so_and_a_company_without_stores_is_refused()
    {
        var plan = TallyMastersPlanner.Plan(Source(rates: []));
        Assert.DoesNotContain(plan.Ledgers, ledger => ledger.GstDutyHead is not null);
        Assert.Contains(plan.ReviewPoints, point => point.Contains("Import the GST detail report (R018) first", StringComparison.Ordinal));

        var source = Source();
        var empty = source with { Profile = source.Profile with { StoreCodes = [] } };
        Assert.Contains("No store is linked", Assert.Throws<InvalidOperationException>(() => TallyMastersPlanner.Plan(empty)).Message, StringComparison.Ordinal);
    }

    // The masters cover every ledger the synthetic test vouchers use, so a test day imports into the new company without a
    // "ledger does not exist" rejection.
    [Fact]
    public void Masters_cover_every_ledger_of_the_generated_test_vouchers()
    {
        var plan = TallyMastersPlanner.Plan(Source(stores: [new("TTGOLD", "Titan Synthetic Golden", null)], modes: ["Cash", "Card", "UPI"],
            rates: [new("CGST", 2.5m), new("SGST", 2.5m), new("CGST", 6m), new("SGST", 6m), new("CGST", 9m), new("SGST", 9m)]));
        var names = plan.Ledgers.Select(ledger => ledger.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var context = new SalesVoucherContext(TallyProfile.NewTest("TTGOLD", TallyTestCompany.Name, ["TTGOLD"]), null, false, TallyTestDataGenerator.Mappings("TTGOLD"));
        var day = TallyTestDataGenerator.PickDates(TallyEducationalCalendar.Default, new(2026, 9, 1), 1)[0];
        var used = TallySalesVoucherComposer.Compose(context, TallyTestDataGenerator.Invoices("TTGOLD", day, 12)).Vouchers
            .SelectMany(voucher => voucher.Entries).Select(entry => entry.LedgerName).Distinct().ToArray();
        Assert.NotEmpty(used);
        Assert.All(used, ledger => Assert.Contains(ledger, names));
        Assert.Equal(TallyTestDataGenerator.Mappings("TTGOLD").Select(mapping => $"{mapping.BusinessEvent}={mapping.DebitLedger}").Order(),
            plan.MappingsToApprove.Where(mapping => mapping.StoreCode == "TTGOLD").Select(mapping => $"{mapping.BusinessEvent}={mapping.LedgerName}").Order());
    }
}
