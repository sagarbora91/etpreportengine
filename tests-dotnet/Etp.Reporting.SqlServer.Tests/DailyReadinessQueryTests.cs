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
        IEnumerable<string>? stores = null) =>
        new((stores ?? ["WLMHW", "HEMW"]).ToArray(), (exports ?? []).ToArray(), (inputs ?? []).ToArray(),
            (monthly ?? []).ToArray(), (staff ?? []).ToArray());

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
    public void Expected_export_list_is_R025_R022_R011_R030_per_Retail_store_and_the_AW330_service_pack()
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
        });

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
        Assert.Contains("u.business_unit_code<>'SERVICE'", sql, StringComparison.Ordinal);
        Assert.Contains("t.target_month=@month", sql, StringComparison.Ordinal);
        foreach (var verb in new[] { "INSERT", "UPDATE", "DELETE", "MERGE", "EXEC" })
            Assert.DoesNotContain(verb, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Constructor_refuses_a_missing_loader_and_a_null_request()
    {
        Assert.Throws<ArgumentNullException>(() => new SqlServerDailyReadinessQuery((Func<DateOnly, CancellationToken, Task<DailyReadinessFacts>>)null!));
        var query = new SqlServerDailyReadinessQuery((_, _) => Task.FromResult(Facts()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => query.LoadAsync(null!));
    }

    private static DailyReadinessItem Find(DailyReadinessResult result, string code) => Assert.Single(result.Missing, x => x.Code == code);
}
