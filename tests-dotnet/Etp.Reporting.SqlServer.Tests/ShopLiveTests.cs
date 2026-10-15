using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.12.1 lane P7 (decision 51): only LIVE shops are expected. The shop-live file, the schedule it parses to, the readiness
/// evaluator that Today, DATA-01 and Morning all use, the query that applies the file, and the DATA-01 wording. Synthetic
/// store codes only. The Workpc receive script reads the same file (scripts/onedrive/tests/Test-OneDriveScripts.ps1).
/// </summary>
public sealed class ShopLiveTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 22);
    private readonly string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpShopLive_" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private const string Json = """
        { "shops": [
          { "role": "TITAN",   "storeCodes": ["ZZ001"], "sendingFrom": "2026-10-20" },
          { "role": "HELIOS",  "storeCodes": ["ZZ002"], "sendingFrom": null },
          { "role": "SERVICE", "storeCodes": ["ZZ900"], "sendingFrom": "2026-11-15" } ] }
        """;

    private static ShopLiveSchedule Parse(string json)
    {
        Assert.True(ShopLiveSchedule.TryParse(json, out var schedule, out var error), error);
        return schedule;
    }

    private static DailyReadinessFacts Facts(ShopLiveSchedule live, IEnumerable<string>? stores = null) =>
        new((stores ?? ["ZZ001", "ZZ002", "ZZ003"]).ToArray(), [], [], [], [])
        {
            ServiceCentres = ["ZZ900"],
            ShopLive = live
        };

    // ---- the schedule -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_store_is_expected_from_its_sending_day_and_never_before_or_when_not_live()
    {
        var live = Parse(Json);

        Assert.False(live.IsSending("ZZ001", new DateOnly(2026, 10, 19)));
        Assert.True(live.IsSending("ZZ001", new DateOnly(2026, 10, 20)));   // the sending day itself
        Assert.True(live.IsSending("ZZ001", Day));
        Assert.False(live.IsSending("ZZ002", Day));                          // listed, no sendingFrom: not live yet
        Assert.False(live.IsSending("zz900", Day));                          // case does not matter
        Assert.True(live.IsSending("ZZ900", new DateOnly(2026, 11, 15)));
        Assert.True(live.IsSending("ZZ777", Day));                           // not listed: expected
        Assert.True(live.IsSending("", Day));
        Assert.Null(live.SendingFrom("ZZ002"));
        Assert.Equal(new DateOnly(2026, 10, 20), live.SendingFrom("ZZ001"));
    }

    [Fact]
    public void No_shops_and_an_empty_list_expect_every_store()
    {
        Assert.True(ShopLiveSchedule.Empty.IsSending("ZZ001", Day));
        Assert.True(Parse("""{ "shops": [] }""").IsSending("ZZ001", Day));
        Assert.True(Parse("{}").IsSending("ZZ001", Day));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "shops": {} }""")]
    [InlineData("""{ "shops": [ { "role": "NOSUCH", "storeCodes": ["ZZ001"], "sendingFrom": "2026-10-20" } ] }""")]
    [InlineData("""{ "shops": [ { "storeCodes": ["ZZ001"] } ] }""")]
    [InlineData("""{ "shops": [ { "role": "TITAN", "sendingFrom": "20/10/2026" } ] }""")]
    [InlineData("""{ "shops": [ { "role": "TITAN", "sendingFrom": 20261020 } ] }""")]
    [InlineData("""{ "shops": [ { "role": "TITAN" }, { "role": "titan" } ] }""")]
    [InlineData("""{ "shops": [ { "role": "TITAN", "storeCodes": ["ZZ001"] }, { "role": "HELIOS", "storeCodes": ["zz001"] } ] }""")]
    [InlineData("""{ "shops": [ { "role": "TITAN", "storeCodes": [""] } ] }""")]
    public void An_invalid_file_is_refused_with_a_reason_and_waives_nothing(string json)
    {
        Assert.False(ShopLiveSchedule.TryParse(json, out var schedule, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(schedule.Entries);
        Assert.True(schedule.IsSending("ZZ001", Day));
    }

    [Fact]
    public void The_example_file_the_receive_script_ships_parses_and_waives_only_what_it_names()
    {
        var path = Path.Combine(RepositoryRoot(), "scripts", "onedrive", "shop-live.example.json");

        var live = Parse(File.ReadAllText(path));

        Assert.Equal(["TITAN", "HELIOS", "SERVICE"], live.Entries.Select(e => e.Role));
        Assert.True(live.IsSending("WLMHW", new DateOnly(2026, 10, 20)));
        Assert.False(live.IsSending("HEMW", new DateOnly(2026, 12, 31)));
        Assert.False(live.IsSending("AW330", new DateOnly(2026, 12, 31)));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }

    // ---- the file reader -----------------------------------------------------------------------------------------------

    [Fact]
    public void The_reader_gives_the_schedule_of_a_good_file_and_expects_everything_for_a_missing_or_broken_one()
    {
        var good = Path.Combine(folder, "good.json");
        File.WriteAllText(good, Json);
        var broken = Path.Combine(folder, "broken.json");
        File.WriteAllText(broken, "{ nope");

        Assert.Equal(3, ShopLiveFile.Load(good).Entries.Count);
        Assert.Empty(ShopLiveFile.Load(Path.Combine(folder, "absent.json")).Entries);
        Assert.Empty(ShopLiveFile.Load(broken).Entries);
        Assert.Equal(Path.Combine("EtpReporting", "Operations", "shop-live.json"),
            Path.GetRelativePath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ShopLiveFile.DefaultPath()));
    }

    // ---- the evaluator (Today, DATA-01 and Morning all use it) ---------------------------------------------------------

    [Fact]
    public void A_shop_that_is_not_live_is_asked_for_nothing_not_even_targets_and_is_named_as_not_sending()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, []), Facts(Parse(Json)));

        Assert.Equal(["ZZ001", "ZZ003"], result.StoreCodes);
        Assert.DoesNotContain(result.Missing, x => x.StoreCode == "ZZ002");
        Assert.Equal(10, result.Missing.Count(x => x.StoreCode == "ZZ001"));
        Assert.Equal(10, result.Missing.Count(x => x.StoreCode == "ZZ003"));   // not listed: still asked for everything
        Assert.DoesNotContain(result.Missing, x => x.Code == "SERVICE_RAW");  // the Service Centre starts 15 Nov
        Assert.Equal(["ZZ002", "ZZ900"], result.NotYetSendingStoreCodes);
    }

    [Fact]
    public void A_shop_becomes_expected_on_its_sending_day()
    {
        var live = Parse(Json);

        var before = DailyReadinessEvaluator.Evaluate(new(new DateOnly(2026, 11, 14), []), Facts(live));
        var on = DailyReadinessEvaluator.Evaluate(new(new DateOnly(2026, 11, 15), []), Facts(live));

        Assert.DoesNotContain(before.Missing, x => x.Code == "SERVICE_RAW");
        Assert.Contains(on.Missing, x => x.Code == "SERVICE_RAW" && x.StoreCode == "ZZ900");
        Assert.DoesNotContain("ZZ900", on.NotYetSendingStoreCodes);
    }

    [Fact]
    public void Without_a_schedule_the_evaluator_behaves_as_before()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, []), Facts(ShopLiveSchedule.Empty));

        Assert.Equal(["ZZ001", "ZZ002", "ZZ003"], result.StoreCodes);
        Assert.Equal(3 * 10 + 1, result.Missing.Count);
        Assert.Empty(result.NotYetSendingStoreCodes);
    }

    [Fact]
    public void Asking_for_one_shop_that_is_not_live_finds_nothing_missing()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, ["ZZ002"], IncludeServiceCentre: false), Facts(Parse(Json)));

        Assert.True(result.IsComplete);
        Assert.Equal(["ZZ002"], result.NotYetSendingStoreCodes);
    }

    [Fact]
    public void A_not_live_shop_with_a_no_sale_mark_is_reported_once_as_not_sending()
    {
        var facts = Facts(Parse(Json)) with { NoSaleStores = ["ZZ002", "ZZ001"] };

        var result = DailyReadinessEvaluator.Evaluate(new(Day, [], IncludeServiceCentre: false), facts);

        Assert.Equal(["ZZ001"], result.NoSaleStoreCodes);
        Assert.Equal(["ZZ002"], result.NotYetSendingStoreCodes);
    }

    // ---- the query applies the file ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_query_applies_the_shop_live_schedule_to_the_loaded_facts()
    {
        var loaded = 0;
        var query = new SqlServerDailyReadinessQuery((_, _) => Task.FromResult(Facts(ShopLiveSchedule.Empty)), () => { loaded++; return Parse(Json); });

        var result = await query.LoadAsync(new(Day, [], IncludeServiceCentre: true));

        Assert.Equal(1, loaded);
        Assert.Equal(["ZZ002", "ZZ900"], result.NotYetSendingStoreCodes);
        Assert.DoesNotContain(result.Missing, x => x.StoreCode == "ZZ002" || x.Code == "SERVICE_RAW");
    }

    // ---- DATA-01 -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Data01_says_which_shops_are_not_sending_yet_on_a_complete_day_and_nothing_extra_otherwise()
    {
        var done = new DailyReadinessResult(Day, ["ZZ001"], []) { NotYetSendingStoreCodes = ["ZZ002", "ZZ900"] };
        var plain = new DailyReadinessResult(Day, ["ZZ001"], []);

        var withNote = DataProbes.EvaluateCompleteness(Day, done, TimeSpan.Zero);
        var without = DataProbes.EvaluateCompleteness(Day, plain, TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Pass, withNote.Outcome);
        Assert.Contains("targets. (not sending yet: ZZ002, ZZ900)", withNote.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("not sending yet", without.Explanation, StringComparison.Ordinal);
        Assert.Equal("", DataProbes.NotYetSendingNote(null));
        Assert.Equal("", DataProbes.NotYetSendingNote([]));
    }
}
