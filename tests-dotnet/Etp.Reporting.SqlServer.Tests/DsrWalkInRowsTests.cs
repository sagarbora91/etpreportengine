using Etp.Reporting.Domain.Periods;
using Etp.Reporting.Reporting;
using Facts = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.DsrFacts;
using WalkIns = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.WalkInFacts;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

/// <summary>
/// HEMW FIX-03 / WLMHW FIX-06 (report audit 3 Oct 2026): a store with no WALK_INS entry must give
/// walk-ins null (not entered), never 0, and COMBINED is the sum only when every store has a value.
/// </summary>
public sealed class DsrWalkInRowsTests
{
    private static readonly string[] Stores = ["WLMHW", "HEMW"];
    private static readonly DateOnly Date = new(2026, 9, 28);

    private static readonly Dictionary<string, Facts> SalesFacts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WLMHW"] = new(TySales: 69_880m, TyUnits: 8m, TyInvoices: 8),
        ["HEMW"] = new(TySales: 76_090m, TyUnits: 3m, TyInvoices: 3)
    };

    private static IReadOnlyList<DsrManagementRow> Rows(ReportingPeriodKind kind, Dictionary<string, WalkIns> walkIns) =>
        OperationalReportRepository.BuildDsrPeriodRows(kind.ToString().ToUpperInvariant(), Stores,
            new IndianFinancialYearPeriodPolicy().Resolve(Date, kind), SalesFacts, walkIns, new ManagementMetricEngine());

    [Fact]
    public void No_walk_in_entries_give_null_walk_ins_and_conversion_for_every_store_and_combined()
    {
        var rows = Rows(ReportingPeriodKind.Ftd, new(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["WLMHW", "HEMW", "COMBINED"], rows.Select(x => x.Store));
        Assert.All(rows, row =>
        {
            Assert.Null(row.WalkIns);
            Assert.Null(row.ConversionPercent);
        });
        Assert.Equal([1, 1, 2], rows.Select(x => x.WalkInMissingDays));
        Assert.Equal(145_970m, rows.Single(x => x.Store == "COMBINED").TySales);
    }

    [Fact]
    public void No_walk_in_entries_in_a_month_give_null_walk_ins_not_zero()
    {
        var rows = Rows(ReportingPeriodKind.Mtd, new(StringComparer.OrdinalIgnoreCase));

        Assert.All(rows, row => Assert.Null(row.WalkIns));
        Assert.Equal([28, 28, 56], rows.Select(x => x.WalkInMissingDays));
    }

    [Fact]
    public void One_store_entered_and_one_not_keeps_the_store_value_and_makes_combined_null()
    {
        var rows = Rows(ReportingPeriodKind.Ftd, new(StringComparer.OrdinalIgnoreCase) { ["WLMHW"] = new(10m, 0) });

        var titan = rows.Single(x => x.Store == "WLMHW");
        Assert.Equal(10m, titan.WalkIns);
        Assert.Equal(80m, titan.ConversionPercent);
        var helios = rows.Single(x => x.Store == "HEMW");
        Assert.Null(helios.WalkIns);
        Assert.Null(helios.ConversionPercent);
        var combined = rows.Single(x => x.Store == "COMBINED");
        Assert.Null(combined.WalkIns);
        Assert.Null(combined.ConversionPercent);
        Assert.Equal(1, combined.WalkInMissingDays);
    }

    [Fact]
    public void Every_store_entered_gives_the_combined_sum_and_conversion()
    {
        var rows = Rows(ReportingPeriodKind.Ftd, new(StringComparer.OrdinalIgnoreCase) { ["WLMHW"] = new(10m, 0), ["HEMW"] = new(4m, 0) });

        var combined = rows.Single(x => x.Store == "COMBINED");
        Assert.Equal(14m, combined.WalkIns);
        Assert.Equal(78.6m, decimal.Round(combined.ConversionPercent!.Value, 1));
        Assert.Equal(0, combined.WalkInMissingDays);
    }

    [Fact]
    public void Entered_zero_walk_ins_stay_zero_and_partial_months_keep_the_available_sum_without_conversion()
    {
        var rows = Rows(ReportingPeriodKind.Mtd, new(StringComparer.OrdinalIgnoreCase) { ["WLMHW"] = new(0m, 27), ["HEMW"] = new(40m, 0) });

        Assert.Equal(0m, rows.Single(x => x.Store == "WLMHW").WalkIns);
        var combined = rows.Single(x => x.Store == "COMBINED");
        Assert.Equal(40m, combined.WalkIns);
        Assert.Equal(27, combined.WalkInMissingDays);
        Assert.Null(combined.ConversionPercent);
    }
}
