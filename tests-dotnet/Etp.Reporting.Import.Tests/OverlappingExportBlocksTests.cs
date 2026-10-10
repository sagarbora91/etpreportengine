using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// IF-021 (1.9.8 triage): a consolidated workbook holding the same sales line in two overlapping export blocks (the
/// 17 Aug 2026 case: a 17 Aug export and a 6 Sep export both covering 17 Aug) must yield one sales line, from the latest
/// export, without a hand-made "minus the 6 Sep copy" file, also when a descriptive field (the customer name) differs
/// between the two copies. Runs the shipped R025 catalogue roles through the real canonicaliser, projector and in-source
/// resolver. All values are synthetic.
/// </summary>
public sealed class OverlappingExportBlocksTests
{
    private static readonly EtpReportFamily R025 = EtpReportFamilyRegistry.Resolve("R025");
    private static readonly ExportTime Aug17 = ExportTime.AtMinute(new DateTime(2026, 8, 17, 18, 30, 0));
    private static readonly ExportTime Sep06 = ExportTime.AtMinute(new DateTime(2026, 9, 6, 10, 15, 0));

    [Fact]
    public void Customer_name_is_descriptive_in_the_shipped_catalogue()
    {
        Assert.Equal(ColumnRole.Descriptive, R025.Columns.Single(column => column.CanonicalField == "customer_name").Role);
        Assert.NotNull(R025.Identity);
    }

    [Fact]
    public void Timed_blocks_one_line_in_both_with_a_descriptive_difference_yield_one_line_from_the_latest_export()
    {
        var resolution = Resolve(SourceKind.Consolidated, BlockCompleteness.Complete, BlockOrigin.Contract,
            ("Synthetic customer A", 1000m), ("SYNTHETIC CUSTOMER A.", 1000m));

        var document = Assert.Single(resolution.Authoritative);
        Assert.Null(document.HoldCode);
        Assert.Equal(1, document.RowCount);
        Assert.Equal(2, document.BlockNo);
        Assert.Equal(Sep06, document.ExportTime);
        // The facts are equal, so the 17 Aug copy is attested by the 6 Sep one, not added.
        Assert.Equal([new InSourceDecision(document.Key, 1, DocumentDecision.AttestedInSource)], resolution.EarlierBlocks);
        Assert.Empty(resolution.Diagnostics);
    }

    [Fact]
    public void Timed_blocks_whose_line_was_restated_take_the_latest_export()
    {
        var resolution = Resolve(SourceKind.Consolidated, BlockCompleteness.Complete, BlockOrigin.Contract,
            ("Synthetic customer A", 1000m), ("Synthetic customer A", 900m));

        var document = Assert.Single(resolution.Authoritative);
        Assert.Null(document.HoldCode);
        Assert.Equal(1, document.RowCount);
        Assert.Equal(2, document.BlockNo);
        Assert.Equal(900m, decimal.Parse(Assert.Single(document.Rows).Canonical.Facts["source_net_amount"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal([new InSourceDecision(document.Key, 1, DocumentDecision.RestatedInSource)], resolution.EarlierBlocks);
    }

    [Fact]
    public void Legacy_blocks_one_line_in_both_with_a_descriptive_difference_yield_one_line()
    {
        // A legacy consolidated workbook (Info block table without the contract, as the 17 Aug workbook was built).
        var resolution = Resolve(SourceKind.ConsolidatedLegacy, BlockCompleteness.Legacy, BlockOrigin.InfoLegacy,
            ("Synthetic customer A", 1000m), ("SYNTHETIC CUSTOMER A.", 1000m));

        var document = Assert.Single(resolution.Authoritative);
        Assert.Null(document.HoldCode);
        Assert.Equal(1, document.RowCount);
        Assert.Empty(resolution.Diagnostics);
    }

    private static SourceResolution Resolve(SourceKind kind, BlockCompleteness completeness, BlockOrigin origin,
        (string Customer, decimal Amount) first, (string Customer, decimal Amount) second)
    {
        SourceBlock Block(int blockNo, ExportTime time) => new(blockNo, "Data", blockNo * 10, blockNo * 10, 1, completeness, origin, time);
        var blocks = new[] { Block(1, Aug17), Block(2, Sep06) };
        var projector = new DocumentProjector(FactCanonicalizer.Instance);
        var projections = blocks.Zip([first, second], (block, line) => projector.Project(new ProjectionRequest(R025, ProjectorTestCatalogue.Store, block,
        [
            ProjectorTestCatalogue.Row(R025, new RowLocator(block.BlockNo, "Data", block.FirstRow!.Value),
                ("invoice_number", "INV-0817"), ("transaction_date", new DateOnly(2026, 8, 17)), ("customer_name", line.Customer),
                ("source_net_amount", line.Amount), ("source_store_timestamp", $"2026-08-17 1{block.BlockNo}:00:00"))
        ]))).ToArray();
        Assert.All(projections, projection => Assert.Single(projection.Documents));
        return new InSourceResolver(FactCanonicalizer.Instance).Resolve(new SourceDescription(kind, blocks, [], []), projections);
    }
}
