using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Import.Service;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class DailyReadinessQueryTests
{
    private static readonly DateOnly Day = new(2026, 10, 9);

    private static DailyReadinessFacts Facts(
        IEnumerable<(string, string)>? exports = null,
        IEnumerable<(string, string)>? inputs = null,
        IEnumerable<string>? monthly = null,
        IEnumerable<string>? staff = null,
        IEnumerable<string>? stores = null,
        IEnumerable<string>? serviceCentres = null,
        IEnumerable<string>? noSale = null) =>
        new((stores ?? ["WLMHW", "HEMW"]).ToArray(), (exports ?? []).ToArray(), (inputs ?? []).ToArray(),
            (monthly ?? []).ToArray(), (staff ?? []).ToArray())
        {
            // 1.12: the Service Centres come from the store catalogue; the legacy one is the default fixture here.
            ServiceCentres = (serviceCentres ?? ["AW330"]).ToArray(),
            NoSaleStores = (noSale ?? []).ToArray()
        };

    private static IEnumerable<(string, string)> AllRetail(string store) =>
        [(store, "R025"), (store, "R022"), (store, "CLOSING_STOCK"), (store, "STOCK_LEDGER")];

    private static IEnumerable<(string, string)> AllInputs(string store) =>
        [(store, "WALK_INS"), (store, "OPENING_CASH"), (store, "EXPENSES"), (store, "CASH_DEPOSIT")];

    [Fact]
    public void Nothing_held_lists_every_export_input_and_target_per_store_then_the_service_pack()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, []), Facts());

        Assert.False(result.IsComplete);
        Assert.Equal(["HEMW", "WLMHW"], result.StoreCodes);
        Assert.Equal(2 * 10 + 1, result.Missing.Count);
        Assert.Equal(
            ["R025", "R022", "R011", "R030", "WALK_INS", "OPENING_CASH", "EXPENSES", "CASH_DEPOSIT", "MONTHLY_TARGET", "STAFF_TARGETS"],
            result.Missing.Where(x => x.StoreCode == "HEMW").Select(x => x.Code));
        var last = result.Missing[^1];
        Assert.Equal(new DailyReadinessItem(DailyReadinessItemKind.MissingExport, "AW330", "SERVICE_RAW",
            "The Service raw pack for Service Centre (AW330) on 09 Oct 2026 has not been imported.", DailyReadinessDestination.ImportIntake), last);
    }

    [Fact]
    public void Everything_held_is_complete()
    {
        var facts = Facts(
            AllRetail("WLMHW").Concat(AllRetail("HEMW")).Append(("AW330", "S004")),
            AllInputs("WLMHW").Concat(AllInputs("HEMW")),
            ["WLMHW", "HEMW"], ["WLMHW", "HEMW"]);

        var result = DailyReadinessEvaluator.Evaluate(new(Day, []), facts);

        Assert.True(result.IsComplete);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public void Each_item_names_its_destination_and_a_plain_message_without_customer_data()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, ["HEMW"], IncludeServiceCentre: false), Facts());

        Assert.All(result.Missing, x => Assert.Equal("HEMW", x.StoreCode));
        Assert.Equal(DailyReadinessDestination.ImportIntake, Find(result, "R025").Destination);
        Assert.Equal("R025 sales lines for HEMW on 09 Oct 2026 has not been imported.", Find(result, "R025").Message);
        Assert.Equal("R011 closing stock for HEMW on 09 Oct 2026 has not been imported.", Find(result, "R011").Message);
        Assert.Equal(DailyReadinessDestination.TodayWalkIns, Find(result, "WALK_INS").Destination);
        Assert.Equal("Walk-ins for HEMW on 09 Oct 2026 has not been entered.", Find(result, "WALK_INS").Message);
        Assert.Equal(DailyReadinessDestination.TodayCashEntries, Find(result, "OPENING_CASH").Destination);
        Assert.Equal(DailyReadinessDestination.TodayCashEntries, Find(result, "EXPENSES").Destination);
        Assert.Equal(DailyReadinessDestination.TodayCashEntries, Find(result, "CASH_DEPOSIT").Destination);
        Assert.Equal(DailyReadinessItemKind.MissingMonthlyTarget, Find(result, "MONTHLY_TARGET").Kind);
        Assert.Equal(DailyReadinessDestination.SettingsMonthlyTargets, Find(result, "MONTHLY_TARGET").Destination);
        Assert.Equal("The monthly sales target for HEMW for Oct 2026 has not been set.", Find(result, "MONTHLY_TARGET").Message);
        Assert.Equal(DailyReadinessItemKind.MissingStaffTargets, Find(result, "STAFF_TARGETS").Kind);
        Assert.Equal(DailyReadinessDestination.SettingsStaffTargets, Find(result, "STAFF_TARGETS").Destination);
    }

    [Fact]
    public void R011_and_R030_count_under_their_stored_report_codes_and_their_own_codes()
    {
        var stored = DailyReadinessEvaluator.Evaluate(new(Day, ["WLMHW"], false),
            Facts([("WLMHW", "CLOSING_STOCK"), ("WLMHW", "STOCK_LEDGER")]));
        var raw = DailyReadinessEvaluator.Evaluate(new(Day, ["WLMHW"], false),
            Facts([("WLMHW", "R011"), ("WLMHW", "R030")]));

        foreach (var result in new[] { stored, raw })
        {
            Assert.DoesNotContain(result.Missing, x => x.Code is "R011" or "R030");
            Assert.Contains(result.Missing, x => x.Code == "R025");
        }
    }

    [Fact]
    public void A_file_of_another_store_or_report_does_not_count()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, ["HEMW"], false),
            Facts([("WLMHW", "R025"), ("HEMW", "R024"), ("AW330", "R022")], [("WLMHW", "WALK_INS")], ["WLMHW"], ["WLMHW"]));

        Assert.Equal(10, result.Missing.Count);
    }

    [Fact]
    public void Store_and_code_matching_ignores_case_and_spaces()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, [" hemw "], false),
            Facts([("hemw", "r025")], [("Hemw", "walk_ins")], ["hemw"], ["HEMW"]));

        Assert.Equal(["HEMW"], result.StoreCodes);
        Assert.DoesNotContain(result.Missing, x => x.Code is "R025" or "WALK_INS" or "MONTHLY_TARGET" or "STAFF_TARGETS");
    }

    [Fact]
    public void Requested_codes_that_are_not_active_Retail_stores_are_not_checked()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, ["AW330", "OLD01", "WLMHW"], false), Facts());

        Assert.Equal(["WLMHW"], result.StoreCodes);
        Assert.All(result.Missing, x => Assert.Equal("WLMHW", x.StoreCode));
    }

    [Fact]
    public void A_store_set_with_no_active_Retail_store_checks_only_the_service_pack()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, ["AW330"]), Facts());

        Assert.Empty(result.StoreCodes);
        Assert.Equal("SERVICE_RAW", Assert.Single(result.Missing).Code);
    }

    [Fact]
    public void Any_landed_service_family_for_AW330_counts_as_the_raw_pack()
    {
        foreach (var family in ServiceInterimFamilies.Importable)
        {
            var result = DailyReadinessEvaluator.Evaluate(new(Day, [], true), Facts([("AW330", family)], stores: []));
            Assert.True(result.IsComplete, family);
        }
        Assert.False(DailyReadinessEvaluator.Evaluate(new(Day, []), Facts([("AW330", "S005"), ("AW330", "S001"), ("WLMHW", "S004")], stores: [])).IsComplete);
    }

    [Fact]
    public void Service_pack_is_left_out_when_the_request_excludes_the_service_centre()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, [], IncludeServiceCentre: false), Facts(stores: []));

        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Service_raw_pack_families_match_the_importer()
    {
        Assert.Equal(ServiceInterimFamilies.Importable.Order(StringComparer.Ordinal),
            DailyReadinessExpectations.ServiceRawPackFamilies.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Expected_export_list_is_R025_R022_R011_R030_per_Retail_store_and_the_service_pack()
    {
        Assert.Equal(["R025", "R022", "R011", "R030"],
            DailyReadinessExpectations.Exports.Where(x => x.Scope == ExpectedExportScope.RetailStore).Select(x => x.Code));
        var service = Assert.Single(DailyReadinessExpectations.Exports, x => x.Scope == ExpectedExportScope.ServiceCentre);
        Assert.Equal("SERVICE_RAW", service.Code);
        Assert.Equal(["WALK_INS", "OPENING_CASH", "EXPENSES", "CASH_DEPOSIT"], DailyReadinessExpectations.ManualInputs.Select(x => x.FieldCode));
        Assert.Contains("CLOSING_STOCK", DailyReadinessExpectations.AllStoredReportCodes);
        Assert.Contains("STOCK_LEDGER", DailyReadinessExpectations.AllStoredReportCodes);
        Assert.Equal(DailyReadinessExpectations.AllStoredReportCodes.Count,
            DailyReadinessExpectations.AllStoredReportCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(string.Join(',', DailyReadinessExpectations.AllStoredReportCodes).Length < 4000);
    }

    [Fact]
    public async Task Query_loads_facts_for_the_requested_date_and_evaluates_them()
    {
        DateOnly? observed = null;
        var query = new SqlServerDailyReadinessQuery((date, _) =>
        {
            observed = date;
            return Task.FromResult(Facts(AllRetail("WLMHW"), AllInputs("WLMHW"), ["WLMHW"], ["WLMHW"], ["WLMHW"]));
        }, () => ShopLiveSchedule.Empty);

        var result = await query.LoadAsync(new(Day, ["WLMHW"]));

        Assert.Equal(Day, observed);
        Assert.Equal(Day, result.BusinessDate);
        Assert.Equal("SERVICE_RAW", Assert.Single(result.Missing).Code);
    }

    [Fact]
    public void Facts_sql_reads_only_current_completed_files_covering_the_date_and_writes_nothing()
    {
        var sql = SqlServerDailyReadinessQuery.FactsSql;
        Assert.Contains("b.status='Completed' AND f.is_superseded=0", sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(f.period_start,f.business_date)<=@date", sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(f.period_end,f.business_date)>=@date", sql, StringComparison.Ordinal);
        // 1.12: Service Centres from the catalogue (unit_kind, COL_LENGTH-guarded with the legacy SERVICE unit as fallback).
        Assert.Contains("COL_LENGTH(N'dbo.business_units',N'unit_kind') IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("u.unit_kind=''SERVICE''", sql, StringComparison.Ordinal);
        Assert.Contains("u.business_unit_code=''SERVICE''", sql, StringComparison.Ordinal);
        Assert.Contains("t.target_month=@month", sql, StringComparison.Ordinal);
        // 1.12 N1: the no-sale marks, guarded for a database before 0068 (an empty result set keeps the reader in step).
        Assert.Contains("IF OBJECT_ID(N'dbo.v_no_sale_days_current',N'V') IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT CAST(NULL AS varchar(30)) store_code WHERE 1=0;", sql, StringComparison.Ordinal);
        foreach (var verb in new[] { "INSERT", "UPDATE", "DELETE", "MERGE" })
            Assert.DoesNotContain(verb, sql, StringComparison.OrdinalIgnoreCase);
        // The only EXECUTE is the catalogue read through sp_executesql (a missing column cannot be compiled).
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sql, @"\bEXEC(UTE)?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        Assert.Contains("EXECUTE sys.sp_executesql @catalogue;", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Constructor_refuses_a_missing_loader_and_a_null_request()
    {
        Assert.Throws<ArgumentNullException>(() => new SqlServerDailyReadinessQuery((Func<DateOnly, CancellationToken, Task<DailyReadinessFacts>>)null!));
        var query = new SqlServerDailyReadinessQuery((_, _) => Task.FromResult(Facts()), () => ShopLiveSchedule.Empty);
        await Assert.ThrowsAsync<ArgumentNullException>(() => query.LoadAsync(null!));
    }

    // ---- 1.12 lane N1: no-sale day (decision 52; decision 55 = Q1 option a) and Service Centres from the catalogue ----------

    [Fact]
    public void A_no_sale_day_waives_the_exports_and_daily_entries_of_that_store_and_keeps_its_targets()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, [], IncludeServiceCentre: false), Facts(noSale: ["HEMW"]));

        Assert.Equal(["HEMW"], result.NoSaleStoreCodes);
        Assert.Equal(["MONTHLY_TARGET", "STAFF_TARGETS"], result.Missing.Where(x => x.StoreCode == "HEMW").Select(x => x.Code));
        // The other store is unchanged: every export, input and target is still asked for.
        Assert.Equal(10, result.Missing.Count(x => x.StoreCode == "WLMHW"));
    }

    [Fact]
    public void A_no_sale_day_with_targets_set_is_complete()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, ["HEMW"], false),
            Facts(monthly: ["HEMW"], staff: ["HEMW"], noSale: [" hemw "]));

        Assert.True(result.IsComplete);
        Assert.Equal(["HEMW"], result.NoSaleStoreCodes);
    }

    [Fact]
    public void A_mark_for_a_store_outside_the_request_is_not_reported()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, ["WLMHW"], false), Facts(noSale: ["HEMW"]));

        Assert.Empty(result.NoSaleStoreCodes);
        Assert.Equal(10, result.Missing.Count);
    }

    [Fact]
    public void A_marked_service_centre_is_not_asked_for_its_raw_pack_and_another_one_still_is()
    {
        var result = DailyReadinessEvaluator.Evaluate(new(Day, [], true),
            Facts(stores: [], serviceCentres: ["AW330", "SC999"], noSale: ["SC999"]));

        Assert.Equal(["SC999"], result.NoSaleStoreCodes);
        var item = Assert.Single(result.Missing);
        Assert.Equal(("AW330", "SERVICE_RAW"), (item.StoreCode, item.Code));
    }

    [Fact]
    public void Every_service_centre_of_the_catalogue_is_checked_and_none_when_the_catalogue_has_none()
    {
        var two = DailyReadinessEvaluator.Evaluate(new(Day, [], true), Facts(stores: [], serviceCentres: ["SC999", "AW330", "sc999"]));
        Assert.Equal(["AW330", "SC999"], two.Missing.Select(x => x.StoreCode));
        Assert.Equal("The Service raw pack for Service Centre (SC999) on 09 Oct 2026 has not been imported.", two.Missing[1].Message);

        var none = DailyReadinessEvaluator.Evaluate(new(Day, [], true), Facts(stores: [], serviceCentres: []));
        Assert.True(none.IsComplete);
    }

    [Fact]
    public void The_result_has_no_no_sale_stores_by_default_so_older_callers_keep_working()
    {
        var result = new DailyReadinessResult(Day, ["HEMW"], []);
        Assert.Empty(result.NoSaleStoreCodes);
        Assert.Empty(new DailyReadinessFacts([], [], [], [], []).NoSaleStores);
        Assert.Empty(new DailyReadinessFacts([], [], [], [], []).ServiceCentres);
    }

    [Fact]
    public void Readiness_code_names_no_service_centre_store_code()
    {
        // The Service Centre is store-catalogue data (A4); the AW330 literal left the readiness code in 1.12 (lane N1).
        foreach (var file in new[] { "DailyReadinessContracts.cs", "DailyReadinessEvaluator.cs" })
        {
            var text = File.ReadAllText(Path.Combine(Src(), "Etp.Reporting.Application", "DailyReadiness", file));
            Assert.DoesNotContain("AW330", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("AW330", SqlServerDailyReadinessQuery.FactsSql, StringComparison.OrdinalIgnoreCase);
    }

    private static string Src()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "Etp.Reporting.slnx"))) return Path.Combine(folder.FullName, "src");
        throw new DirectoryNotFoundException("src");
    }

    private static DailyReadinessItem Find(DailyReadinessResult result, string code) => Assert.Single(result.Missing, x => x.Code == code);
}
