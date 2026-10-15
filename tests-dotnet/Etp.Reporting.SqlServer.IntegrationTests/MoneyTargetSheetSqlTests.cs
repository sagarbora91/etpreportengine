using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12.1 lane M2 (money test gap #8, decision 68 point 5): DAY TGT, MTD BLA and REQ ADS on the evening sheet.
/// REQ ADS divides the balance by the days left AFTER today (25 Aug: 31 - 25 = 6), because today's sales are already in MTD.
/// Synthetic MTD facts are passed straight to <see cref="OperationalReportRepository.LoadEveningSheetsAsync"/>; targets are real rows.
/// Months used (Feb 2028, Aug 2033) are not used by any other test. Hand arithmetic is in the comments.
/// </summary>
public sealed class MoneyTargetSheetSqlTests(SqlDatabaseFixture db) : IClassFixture<SqlDatabaseFixture>
{
    private static DsrManagementRow Mtd(string store, DateOnly day, decimal sales) =>
        new("MTD", store, new(day.Year, day.Month, 1), day, sales, null, null, "MissingSource", null, null, null, null, null, null, null, null, "TEST");

    private async Task<IReadOnlyList<string>> ActiveStoresAsync() =>
        (await new StoreCatalogRepository(db.ConnectionString).LoadAsync()).Where(x => x.IsActive).Select(x => x.Code).ToArray();

    private async Task<IReadOnlyList<EveningStoreSheet>> SheetsAsync(DateOnly day, IReadOnlyDictionary<string, decimal> mtd)
    {
        var stores = await ActiveStoresAsync();
        var facts = stores.Select(x => Mtd(x, day, mtd.GetValueOrDefault(x))).Append(Mtd("COMBINED", day, mtd.Values.Sum())).ToArray();
        return await new OperationalReportRepository(db.ConnectionString).LoadEveningSheetsAsync(day, facts);
    }

    private async Task SetTargetsAsync(DateOnly month, IReadOnlyDictionary<string, decimal> targets)
    {
        var masters = new DataTruthMasterRepository(db.ConnectionString);
        // Every active store needs a target for COMBINED to have one; a store the test does not name gets a target of 0.
        foreach (var store in await ActiveStoresAsync())
            await masters.SaveMonthlyTargetAsync(new(store, month, targets.GetValueOrDefault(store)));
    }

    [SqlFact]
    public async Task Leap_february_target_divides_by_29_and_required_ads_by_the_19_days_left_after_the_10th()
    {
        var month = new DateOnly(2028, 2, 1);
        await SetTargetsAsync(month, new Dictionary<string, decimal> { ["WLMHW"] = 290_000m });

        var sheets = await SheetsAsync(new(2028, 2, 10), new Dictionary<string, decimal> { ["WLMHW"] = 100_000m });
        var sheet = sheets.Single(x => x.StoreCode == "WLMHW");

        Assert.Equal(290_000m, sheet.StoreTarget);
        Assert.Equal(10_000m, sheet.DayTarget);        // 2,90,000 / 29 days (leap February)
        Assert.Equal(190_000m, sheet.Balance);         // 2,90,000 - 1,00,000
        Assert.Equal(10_000m, sheet.RequiredAds);      // 1,90,000 / (29 - 10 = 19 days left after today)
    }

    [SqlFact]
    public async Task Over_achieved_target_shows_a_negative_balance_and_required_ads_the_day_before_month_end()
    {
        var month = new DateOnly(2028, 2, 1);
        await SetTargetsAsync(month, new Dictionary<string, decimal> { ["WLMHW"] = 290_000m });

        // 28 Feb 2028: 29 - 28 = 1 day left. MTD 3,00,000 is 10,000 over target.
        var sheet = (await SheetsAsync(new(2028, 2, 28), new Dictionary<string, decimal> { ["WLMHW"] = 300_000m })).Single(x => x.StoreCode == "WLMHW");

        Assert.Equal(-10_000m, sheet.Balance);         // 2,90,000 - 3,00,000
        Assert.Equal(-10_000m, sheet.RequiredAds);     // -10,000 / 1 day left
    }

    [SqlFact]
    public async Task Required_ads_on_the_last_day_of_the_month_has_no_days_left_and_reads_blank()
    {
        var month = new DateOnly(2028, 2, 1);
        await SetTargetsAsync(month, new Dictionary<string, decimal> { ["WLMHW"] = 290_000m });

        // 29 Feb 2028: 29 - 29 = 0 days left after today, so there is nothing to divide by: blank, never a division error or a zero.
        var sheet = (await SheetsAsync(new(2028, 2, 29), new Dictionary<string, decimal> { ["WLMHW"] = 300_000m })).Single(x => x.StoreCode == "WLMHW");

        Assert.Equal(-10_000m, sheet.Balance);
        Assert.Equal(10_000m, sheet.DayTarget);
        Assert.Null(sheet.RequiredAds);
    }

    [SqlFact]
    public async Task The_25_aug_tie_out_store_and_combined_required_ads_divide_by_the_6_days_left()
    {
        // Aug 2033 stands in for Aug 2026 (the same 31-day month; only the day number counts).
        var month = new DateOnly(2033, 8, 1);
        await SetTargetsAsync(month, new Dictionary<string, decimal> { ["WLMHW"] = 1_600_000m, ["HEMW"] = 1_300_000m });
        var day = new DateOnly(2033, 8, 25);

        var sheets = await SheetsAsync(day, new Dictionary<string, decimal> { ["WLMHW"] = 973_862m, ["HEMW"] = 821_668.60m });
        var titan = sheets.Single(x => x.StoreCode == "WLMHW");
        var helios = sheets.Single(x => x.StoreCode == "HEMW");
        var combined = sheets.Single(x => x.StoreCode == "COMBINED");

        // Titan: 16,00,000 / 31 = 51,612.9032 ; balance 16,00,000 - 9,73,862 = 6,26,138 ; 6,26,138 / 6 = 1,04,356.3333
        Assert.Equal(51_612.90m, Math.Round(titan.DayTarget!.Value, 2));
        Assert.Equal(626_138m, titan.Balance);
        Assert.Equal(104_356.33m, Math.Round(titan.RequiredAds!.Value, 2));
        // Helios: 13,00,000 / 31 = 41,935.4839 ; balance 13,00,000 - 8,21,668.60 = 4,78,331.40 ; / 6 = 79,721.90
        Assert.Equal(41_935.48m, Math.Round(helios.DayTarget!.Value, 2));
        Assert.Equal(478_331.40m, helios.Balance);
        Assert.Equal(79_721.90m, Math.Round(helios.RequiredAds!.Value, 2));
        // Combined: 29,00,000 / 31 = 93,548.3871 ; balance 29,00,000 - 17,95,530.60 = 11,04,469.40 ; / 6 = 1,84,078.2333
        Assert.Equal(2_900_000m, combined.StoreTarget);
        Assert.Equal(93_548.39m, Math.Round(combined.DayTarget!.Value, 2));
        Assert.Equal(1_104_469.40m, combined.Balance);
        Assert.Equal(184_078.23m, Math.Round(combined.RequiredAds!.Value, 2));
    }

    [SqlFact]
    public async Task The_25_aug_tie_out_targets_balance_and_day_target_do_not_depend_on_the_required_ads_rule()
    {
        var month = new DateOnly(2033, 8, 1);
        await SetTargetsAsync(month, new Dictionary<string, decimal> { ["WLMHW"] = 1_600_000m, ["HEMW"] = 1_300_000m });

        var sheets = await SheetsAsync(new(2033, 8, 25), new Dictionary<string, decimal> { ["WLMHW"] = 973_862m, ["HEMW"] = 821_668.60m });
        var combined = sheets.Single(x => x.StoreCode == "COMBINED");

        Assert.Equal(626_138m, sheets.Single(x => x.StoreCode == "WLMHW").Balance);
        Assert.Equal(478_331.40m, sheets.Single(x => x.StoreCode == "HEMW").Balance);
        Assert.Equal(2_900_000m, combined.StoreTarget);
        Assert.Equal(1_104_469.40m, combined.Balance);  // 29,00,000 - 17,95,530.60
    }

    [SqlFact]
    public async Task Combined_has_no_target_when_one_active_store_has_none()
    {
        var month = new DateOnly(2033, 9, 1);
        // Only WLMHW is given a target in September 2033: any other active store has none, so COMBINED has none.
        await new DataTruthMasterRepository(db.ConnectionString).SaveMonthlyTargetAsync(new("WLMHW", month, 500_000m));
        var stores = await ActiveStoresAsync();
        Assert.True(stores.Count >= 2, "the test database must hold at least two active stores");

        var sheets = await SheetsAsync(new(2033, 9, 10), new Dictionary<string, decimal> { ["WLMHW"] = 100_000m });

        Assert.Null(sheets.Single(x => x.StoreCode == "COMBINED").StoreTarget);
        Assert.Null(sheets.Single(x => x.StoreCode == "COMBINED").RequiredAds);
        Assert.Equal(500_000m, sheets.Single(x => x.StoreCode == "WLMHW").StoreTarget);
    }
}
