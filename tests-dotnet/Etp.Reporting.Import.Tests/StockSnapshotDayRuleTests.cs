using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

// IF-032 (decision 66): one dating rule for Closing Stock (R011) and BinWise (R010) exports. Synthetic values only.
public sealed class StockSnapshotDayRuleTests
{
    [Theory]
    [InlineData("202610031800_Closing Stock.xlsx", "2026-10-03")] // evening: that day's closing
    [InlineData("202610031745_BinWise-Stock - BinWise-Stock.xlsx", "2026-10-03")]
    [InlineData("202610031200_Closing Stock.xlsx", "2026-10-03")] // noon counts as the day
    [InlineData("202610031159_Closing Stock.xlsx", "2026-10-02")] // before noon: the previous day's closing
    [InlineData("202610040930_BinWise-Stock - BinWise-Stock.xlsx", "2026-10-03")]
    [InlineData("S004_20261003183000.xlsx", "2026-10-03")] // a seconds-precise export name counts too
    public void An_export_time_gives_its_business_day(string fileName, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), StockSnapshotDayRule.ClosingDayOf(fileName));

    [Theory]
    [InlineData("R011_Closing_Stock.xlsx")] // no time in the name
    [InlineData("Closing Stock 03.10.2026.xlsx")] // a date without a time of day
    [InlineData(null)]
    public void Without_a_time_of_day_the_rule_says_nothing(string? fileName) =>
        Assert.Null(StockSnapshotDayRule.ClosingDayOf(fileName));

    [Theory]
    [InlineData("2026-10-02", "202610031800_Closing Stock.xlsx", "2026-10-03")] // the 3 Oct Titan file
    [InlineData("2026-10-03", "202610031800_Closing Stock.xlsx", null)] // the column already says the day
    [InlineData("2026-10-02", "202610030930_Closing Stock.xlsx", null)] // morning export, column the previous day
    [InlineData("2026-10-03", "202610030930_Closing Stock.xlsx", "2026-10-02")] // morning export dated the day itself
    [InlineData("2026-09-25", "202610031800_Closing Stock.xlsx", null)] // an older snapshot keeps its own date
    [InlineData("2026-10-02", "R011_Closing_Stock.xlsx", null)] // no export time: the column stands
    public void Closing_stock_column_is_replaced_only_by_the_rule(string column, string fileName, string? expected) =>
        Assert.Equal(expected is null ? null : DateOnly.Parse(expected), StockSnapshotDayRule.ClosingStockDay(DateOnly.Parse(column), fileName));

    [Fact]
    public void Only_the_two_stock_snapshot_families_are_dated_by_the_rule()
    {
        Assert.True(StockSnapshotDayRule.Applies("CLOSING_STOCK"));
        Assert.True(StockSnapshotDayRule.Applies("R011"));
        Assert.True(StockSnapshotDayRule.Applies("R010"));
        Assert.False(StockSnapshotDayRule.Applies("R023"));
        Assert.False(StockSnapshotDayRule.Applies("STOCK_LEDGER"));
        Assert.False(StockSnapshotDayRule.Applies(null));
    }

    [Fact]
    public void Evening_closing_stock_is_scoped_to_its_export_day_and_says_why()
    {
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(ClosingStock("202610031800_Closing Stock.xlsx", new(2026, 10, 2)));

        Assert.Equal((new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 3)), (accepted.Scope.PeriodStart!.Value, accepted.Scope.PeriodEnd!.Value));
        Assert.Equal(new DateOnly(2026, 10, 2), accepted.Scope.ClosingStockColumnDate);
        Assert.Equal(new DateOnly(2026, 10, 3), accepted.Scope.ClosingStockDayOf(new(2026, 10, 2)));
        var note = Assert.Single(accepted.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.SnapshotDatedByExportTime);
        Assert.Equal(ImportDiagnosticSeverity.Information, note.Severity);
        // History keeps exactly this text: it is the catalogue's template for the code.
        Assert.Equal(note.Message, ImportDiagnosticCatalogue.SafeMessage(note.Code, note.Message));
        Assert.Equal(ImportIssueSeverity.Information, ImportCodes.DefaultSeverity(note.Code));
    }

    [Fact]
    public void Closing_stock_with_its_own_day_in_the_column_is_unchanged()
    {
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(ClosingStock("202610031800_Closing Stock.xlsx", new(2026, 10, 3)));

        Assert.Equal(new DateOnly(2026, 10, 3), accepted.Scope.PeriodEnd);
        Assert.Null(accepted.Scope.ClosingStockColumnDate);
        Assert.DoesNotContain(accepted.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.SnapshotDatedByExportTime);
    }

    [Fact]
    public void Next_morning_binwise_is_the_previous_days_closing()
    {
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(BinWise("202610040930_BinWise-Stock - BinWise-Stock.xlsx"));

        Assert.Equal((new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 3)), (accepted.Scope.PeriodStart!.Value, accepted.Scope.PeriodEnd!.Value));
        Assert.All(accepted.Scope.SnapshotBlocks, block => Assert.Equal(new DateOnly(2026, 10, 3), block.SnapshotDate));
        Assert.Equal(new DateOnly(2026, 10, 3), accepted.Scope.SnapshotDateOf("Data", 2, null));
        Assert.Contains(accepted.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.SnapshotDatedByExportTime);
    }

    [Fact]
    public void Evening_binwise_keeps_its_export_day_without_a_note()
    {
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(BinWise("202610031745_BinWise-Stock - BinWise-Stock.xlsx"));

        Assert.Equal(new DateOnly(2026, 10, 3), accepted.Scope.PeriodEnd);
        Assert.DoesNotContain(accepted.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.SnapshotDatedByExportTime);
    }

    private static WorkbookSnapshot ClosingStock(string fileName, DateOnly column) =>
        new(fileName, 10, new string('7', 64),
        [new("Sheet0", 1, StockImportProfiles.ClosingStockHeaders, [new WorkbookRow(2, new object?[]
        {
            "SYN1", "Synthetic", "Retail", "Store", "Region", "State", "City", column.ToDateTime(TimeOnly.MinValue), "ITEM-1", "HSN", "Description",
            "EAN-1", "BR", "Cluster", "U", 2m, 10m, 20m, "LOT-1", null
        }.Select(value => new WorkbookCell(value)).ToArray())])]);

    private static WorkbookSnapshot BinWise(string fileName)
    {
        var family = EtpReportFamilyRegistry.Resolve("R010");
        var columns = family.Headers.Select(header => family.Columns.Single(column => column.SourceHeader == header)).ToArray();
        var row = new WorkbookRow(2, columns.Select(column => new WorkbookCell(column.CanonicalField switch
        {
            "store_code" => "SYN1",
            "itemnumber" => "ITEM-1",
            "lotnumber" => "LOT-1",
            "uid" => null,
            "retailbin" => 2m,
            "servicebin" => 1m,
            "closingbalance" => 3m,
            "ucp" => 10m,
            "totalucp" => 30m,
            _ => column.DataType == Etp.Reporting.Domain.Imports.CanonicalDataType.Decimal ? (object)0m : "SYNTHETIC"
        })).ToArray());
        return new(fileName, 10, new string('8', 64), [new("Data", 1, family.Headers, [row])]);
    }
}
