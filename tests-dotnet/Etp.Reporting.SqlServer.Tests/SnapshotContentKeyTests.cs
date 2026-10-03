using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using static Etp.Reporting.TestSupport.StackedBinWiseWorkbooks;
using Store = Etp.Reporting.Infrastructure.SqlServer.SqlServerTransactionalImportStore;

namespace Etp.Reporting.SqlServer.Tests;

// Review 1.9.3 (dating-restatement-fy finding 1): an undated family's row is a reading of its snapshot date, so the
// date is part of its planner-1 content key, and a stacked file overlaps another file only on the dates it holds.
public sealed class SnapshotContentKeyTests
{
    private static readonly DateOnly July = new(2026, 7, 2), August = new(2026, 8, 7), September = new(2026, 9, 29);

    [Fact]
    public void Unit_unchanged_in_every_block_of_a_stacked_R010_has_one_key_per_snapshot_date()
    {
        var stacked = Accept(Stacked("WLMHW", "d1"));

        var keys = Store.ContentKeys(stacked, stacked.Scope.PeriodEnd);

        // Rows 2, 4 and 6 are the same UNIT-A reading in the 2 Jul, 7 Aug and 29 Sep blocks.
        Assert.Equal([July, August, September], new[] { 2, 4, 6 }.Select(row => Store.SnapshotDateOfKey(keys[row])));
        Assert.Equal(3, new[] { keys[2], keys[4], keys[6] }.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys.Values, key => Assert.EndsWith(":1", key));
        Assert.All(keys.Values, key => Assert.InRange(key.Length, 1, 80));
        // The content hash itself is unchanged, so the outcome's content_sha256 (key[..64]) still names the row's values.
        Assert.Equal(keys[2][..64], keys[4][..64]);

        // A single-date export of 7 Aug keys its rows exactly as the stacked file's 7 Aug block does, and only so.
        var single = Accept(Single("WLMHW", "202608071848", "d2", Units));
        var singleKeys = Store.ContentKeys(single, single.Scope.PeriodEnd);
        Assert.Equal(new[] { keys[4], keys[5] }, new[] { singleKeys[2], singleKeys[3] });
        Assert.DoesNotContain(singleKeys[2], new[] { keys[2], keys[6] });
    }

    [Fact]
    public void Single_date_file_shares_the_plan_only_with_files_holding_its_date()
    {
        var stacked = Accept(Stacked("WLMHW", "d3"));
        var held = File(1, July, September, Store.ContentKeys(stacked, stacked.Scope.PeriodEnd).Values);
        // Imported before keys carried their date: matched by its period, as before.
        var legacy = File(2, new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 15), ["0123:1"]);

        var september = Accept(Single("WLMHW", "202609151200", "d4", Units));
        var plan = Store.SharingSnapshotDates([held, legacy], Store.ContentKeys(september, september.Scope.PeriodEnd).Values, null);
        Assert.Equal([2L], plan.Select(file => file.Id));

        var august = Accept(Single("WLMHW", "202608071848", "d5", Units));
        Assert.Equal([1L], Store.SharingSnapshotDates([held, legacy], Store.ContentKeys(august, august.Scope.PeriodEnd).Values, null)
            .Select(file => file.Id));
        // An explicit restatement target always stays in the plan, so the database decides whether it is covered.
        Assert.Equal([1L, 2L], Store.SharingSnapshotDates([held, legacy], Store.ContentKeys(august, august.Scope.PeriodEnd).Values, 2)
            .Select(file => file.Id));
    }

    [Fact]
    public void Dated_family_keys_and_plan_are_unchanged()
    {
        Assert.True(ImportScope.IsUndatedFamily("R010"));
        Assert.True(ImportScope.IsUndatedFamily("R023"));
        Assert.True(ImportScope.IsUndatedFamily("SOR_AGEING"));
        Assert.False(ImportScope.IsUndatedFamily("R025"));
        Assert.False(ImportScope.IsUndatedFamily("CLOSING_STOCK"));

        var closing = Accept(new WorkbookSnapshot("R011_Closing_Stock.xlsx", 1, Hash("d6"),
        [
            new("Closing Stock", 1, StockImportProfiles.ClosingStockHeaders, [new WorkbookRow(2,
                new object?[] { "WLMHW", "Store", "Retail", "Store", "Region", "State", "City", new DateTime(2026, 9, 29), "ITEM-1", "HSN",
                    "Description", "EAN", "BR", "Cluster", "U", 3m, 10m, 30m, null, null }.Select(value => new WorkbookCell(value)).ToArray())])
        ]));
        var key = Assert.Single(Store.ContentKeys(closing, closing.Scope.PeriodEnd).Values);
        Assert.Matches("^[0-9a-f]{64}:1$", key);
        Assert.Null(Store.SnapshotDateOfKey(key));
        var previous = File(3, September, September, ["0123:1"]);
        Assert.Equal([previous], Store.SharingSnapshotDates([previous], [key], null));
    }

    private static MatchedImportEnvelope Accept(WorkbookSnapshot workbook) =>
        new MatchedImportEnvelopeFactory(["WLMHW"]).RequireAccepted(workbook);

    private static Store.PreviousFile File(long id, DateOnly start, DateOnly end, IEnumerable<string> keys) =>
        new(id, Hash("ee"), start, end, new DateTime(2026, 10, 1), keys.ToHashSet(StringComparer.Ordinal), 1);
}
