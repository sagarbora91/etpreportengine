using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer.Tally;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

// Decision 49 (1): TallyPrime in Educational mode accepts vouchers only on some days of a month (assumed 1, 2 and 31 until the
// VM visit checks it). The test data and the export both respect it.
public sealed class TallyEducationalExportTests
{
    [Fact]
    public void The_default_days_are_the_1st_2nd_and_31st()
    {
        var calendar = TallyEducationalCalendar.Default;
        Assert.Equal(new[] { 1, 2, 31 }, calendar.AllowedDays);
        Assert.True(calendar.IsAllowed(new(2026, 8, 1)));
        Assert.True(calendar.IsAllowed(new(2026, 8, 31)));
        Assert.False(calendar.IsAllowed(new(2026, 8, 25)));
        Assert.Equal(new DateOnly(2026, 10, 1), calendar.NextAllowed(new(2026, 9, 3)));   // September has no 31st
        Assert.Equal(new DateOnly(2026, 8, 31), calendar.NextAllowed(new(2026, 8, 3)));
        Assert.Equal(new DateOnly(2027, 1, 1), calendar.NextAllowed(new DateOnly(2026, 12, 31).AddDays(1)));
        Assert.Equal(new[] { new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 2), new DateOnly(2027, 3, 1) },
            calendar.AllowedBetween(new(2027, 1, 3), new(2027, 3, 1)));
    }

    [Fact]
    public void The_allowed_days_are_a_setting_the_VM_visit_can_correct()
    {
        var verified = new TallyEducationalCalendar([2, 1, 1]);
        Assert.Equal(new[] { 1, 2 }, verified.AllowedDays);
        Assert.False(verified.IsAllowed(new(2026, 8, 31)));
        Assert.Throws<ArgumentException>(() => new TallyEducationalCalendar([]));
        Assert.Throws<ArgumentException>(() => new TallyEducationalCalendar([0]));
        Assert.Throws<ArgumentException>(() => new TallyEducationalCalendar([32]));
    }

    [Fact]
    public void A_date_Tally_would_refuse_is_named_before_anything_is_written()
    {
        var calendar = TallyEducationalCalendar.Default;
        Assert.Null(calendar.Refusal([new(2026, 8, 1), new(2026, 8, 2)]));
        var refusal = calendar.Refusal([new(2026, 8, 25), new(2026, 8, 1), new(2026, 8, 25)]);
        Assert.Equal("TallyPrime in Educational mode accepts vouchers only on day(s) 1, 2, 31 of a month, so it would refuse 25-Aug-2026. Use test data on an allowed day.", refusal);
    }

    [Fact]
    public void Only_a_company_labelled_Educational_is_limited()
    {
        var profile = TallyProfile.NewTest("TTSAAGAR", TallyTestCompany.Name, ["TTGOLD"]);
        Assert.False(TallyEducationalCalendar.AppliesTo(profile));
        Assert.True(TallyEducationalCalendar.AppliesTo(profile with { TallyBuildLabel = "TallyPrime 6.1 Educational" }));
        Assert.True(TallyEducationalCalendar.AppliesTo(profile with { TallyBuildLabel = "EDUCATIONAL" }));
        Assert.False(TallyEducationalCalendar.AppliesTo(profile with { TallyBuildLabel = "TallyPrime 6.1 Silver" }));
    }

    [Fact]
    public void Test_data_picks_allowed_days_and_is_the_same_for_the_same_seed()
    {
        var dates = TallyTestDataGenerator.PickDates(TallyEducationalCalendar.Default, new(2026, 8, 3), 5);
        Assert.Equal(new[] { new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2) }, dates);

        var first = TallyTestDataGenerator.Invoices("ttgold", dates[0], 5, seed: 7);
        var again = TallyTestDataGenerator.Invoices("TTGOLD", dates[0], 5, seed: 7);
        Assert.Equal(first.Select(TallySalesVoucherComposer.SourceHash), again.Select(TallySalesVoucherComposer.SourceHash));
        Assert.All(first, invoice =>
        {
            Assert.Equal("TTGOLD", invoice.StoreCode);
            Assert.Equal(2027, invoice.InvoiceYear);
            Assert.True(TallyCorrespondenceKey.IsDocumentNumberSafe(invoice.DocumentNumber));
            var line = Assert.Single(invoice.Lines);
            Assert.Equal(line.Gross, line.Net + line.Tax);
            var tax = Assert.Single(invoice.TaxRows);
            Assert.Equal(line.Tax, tax.CgstAmount + tax.SgstAmount);
            Assert.Equal(line.Gross, invoice.Tenders.Sum(tender => tender.Amount));
            Assert.Contains(tax.CgstRate!.Value * 2, new[] { 5m, 12m, 18m });
        });
        Assert.Equal(5, first.Select(invoice => invoice.DocumentNumber).Distinct().Count());
        Assert.Equal(2026, TallyTestDataGenerator.InvoiceYear(new(2026, 3, 31)));
        Assert.Equal(2027, TallyTestDataGenerator.InvoiceYear(new(2026, 4, 1)));
    }
}
