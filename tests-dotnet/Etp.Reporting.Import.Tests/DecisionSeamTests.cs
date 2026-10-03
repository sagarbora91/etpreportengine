using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;
using static Etp.Reporting.Import.Tests.ProjectorTestCatalogue;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// The seams between the source reader, the projector, the resolver and the decision engine (review 1.9.3,
/// contract-decision findings 1, 5 and 6), run on the shapes those parts really produce: rows from
/// <see cref="DocumentProjector"/>, blocks from <see cref="SourceDescriptionReader"/> and <see cref="InSourceResolver"/>, and
/// the shipped catalogue where the finding is about it. All values are synthetic.
/// </summary>
public sealed class DecisionSeamTests
{
    private static readonly DocumentProjector Projector = new();
    private static readonly DocumentDecisionEngine Engine = new();

    [Fact]
    public void A_raw_closing_stock_export_in_a_pack_folder_covers_only_its_own_snapshot()
    {
        // The package layout: a raw CLOSING_STOCK export of 25 Aug 18:00 in "... 01 JULY 2026 TO 25 AUG 2026". Its block
        // carries the pack's declared period, but it read one snapshot (25 Aug), not every day of the pack.
        var family = ClosingStock;
        var exportTime = ExportTime.AtMinute(new DateTime(2026, 8, 25, 18, 0, 0));
        var snapshot = new DateOnly(2026, 8, 25);
        var sheet = new WorkbookSheet("Sheet1", 1, family.Headers,
            [new WorkbookRow(2, [new WorkbookCell("ITEM-1")]), new WorkbookRow(3, [new WorkbookCell("ITEM-2")])]);
        var workbook = new WorkbookSnapshot("202608251800_ClosingStock.xlsx", 1000, new string('c', 64), [sheet],
            @"C:\Imports\TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026\202608251800_ClosingStock.xlsx");
        var source = TestSources.Reader().Describe(new SourceDescriptionRequest(workbook, family, sheet));
        var block = Assert.Single(source.Blocks);
        Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 25)), (block.PeriodFrom!.Value, block.PeriodTo!.Value));
        Assert.Equal(exportTime, block.ExportTime);
        SourceRow[] rows =
        [
            Row(family, 2, ("snapshot_date", snapshot), ("product_code", "ITEM-1")),
            Row(family, 3, ("snapshot_date", snapshot), ("product_code", "ITEM-2"))
        ];
        var projection = Projector.Project(new(family, Store, block, rows));
        var resolution = new InSourceResolver(FactCanonicalizer.Instance).Resolve(source, [projection]);

        var coverage = Assert.Single(InSourceResolver.CoverageBlocks(source, [projection], family.Identity!.Scope));

        Assert.True(coverage.Covers(snapshot));
        Assert.False(coverage.Covers(new DateOnly(2026, 8, 7)));
        Assert.False(coverage.Covers(new DateOnly(2026, 7, 2)));

        // Older first: the 2 Jul and 7 Aug snapshots are CURRENT. The 25 Aug export raises no RETIRE item for them.
        var stored = new[] { new DateOnly(2026, 7, 2), new DateOnly(2026, 8, 7) }
            .Select(date => Snapshot(family, date, ExportTime.AtMinute(date.ToDateTime(new TimeOnly(18, 0)))))
            .ToArray();
        var decisions = Engine.Decide(Request(family, resolution.Authoritative, stored, [coverage])).Decisions;
        Assert.Equal([DocumentDecision.New], decisions.Select(decision => decision.Decision));

        // Newer first: the 25 Aug block is registered; an older 7 Aug export of its own snapshot is new, not NOT_ADDED.
        var older = Projector.Project(new(family, Store, Block(1, ExportTime.AtMinute(new DateTime(2026, 8, 7, 18, 0, 0))),
            [Row(family, 2, ("snapshot_date", new DateOnly(2026, 8, 7)))]));
        var registered = coverage with { ImportFileId = 41 };
        var olderDecision = Assert.Single(Engine.Decide(Request(family, older.Documents, [], [registered])).Decisions);
        Assert.Equal(DocumentDecision.New, olderDecision.Decision);
    }

    [Fact]
    public void A_snapshot_block_still_shows_its_own_snapshot_missing()
    {
        // A complete raw R011 export dated 25 Aug by its rows that no longer holds a stored 25 Aug snapshot cannot exist,
        // but a block dated by its export (R010) with no rows of its snapshot can: that snapshot is missing from it.
        var family = R010;
        var exportTime = ExportTime.AtMinute(new DateTime(2026, 8, 26, 10, 0, 0));
        var day = new DateOnly(2026, 8, 25);
        var block = Block(0, exportTime) with { SnapshotDate = day, FirstRow = null, LastRow = null };
        var source = new SourceDescription(SourceKind.Raw, [block], [], []);
        var projection = Projector.Project(new(family, Store, block, []));

        var coverage = Assert.Single(InSourceResolver.CoverageBlocks(source, [projection], DocumentScope.Snapshot));
        var stored = Snapshot(family, day, ExportTime.AtMinute(new DateTime(2026, 8, 25, 21, 0, 0)));
        var retire = Assert.Single(Engine.Decide(Request(family, [], [stored], [coverage])).Decisions);

        Assert.Equal(DocumentDecision.MissingFromLater, retire.Decision);
    }

    [Fact]
    public void Held_rows_never_make_their_documents_missing()
    {
        // A complete R022 export of 29 Sep covering September. INV-1's row lacks NETVALUE (held, key known); a row of
        // 12 Sep has no invoice number (held, day known). Neither may raise a RETIRE item for a document it may hold.
        var family = R022;
        var exportTime = ExportTime.AtMinute(new DateTime(2026, 9, 29, 18, 0, 0));
        var block = Block(4, exportTime) with { PeriodFrom = new DateOnly(2026, 9, 1), PeriodTo = new DateOnly(2026, 9, 28), PeriodBasis = PeriodBasis.Declared };
        var source = new SourceDescription(SourceKind.Raw, [block], [], []);
        SourceRow[] rows =
        [
            Row(family, 2, ("invoice_number", "INV-1"), ("transaction_date", new DateOnly(2026, 9, 10)), ("source_net_value", null)),
            Row(family, 3, ("invoice_number", "INV-2"), ("transaction_date", new DateOnly(2026, 9, 11))),
            Row(family, 4, ("invoice_number", " "), ("transaction_date", new DateOnly(2026, 9, 12))),
            Row(family, 5, ("invoice_number", "INV-5"), ("transaction_date", new DateOnly(2026, 9, 13)))
        ];
        var projection = Projector.Project(new(family, Store, block, rows));
        var resolution = new InSourceResolver(FactCanonicalizer.Instance).Resolve(source, [projection]);
        var coverage = Assert.Single(InSourceResolver.CoverageBlocks(source, [projection], family.Identity!.Scope));
        var attested = ExportTime.AtMinute(new DateTime(2026, 9, 20, 10, 0, 0));
        StoredDocument Invoice(string number, int day) =>
            Stored(DocumentKey.ForDocument("R022", Store, new DateOnly(2026, 9, day), number), new DateOnly(2026, 9, day), attested);
        StoredDocument[] stored = [Invoice("INV-1", 10), Invoice("INV-3", 12), Invoice("INV-4", 14), Invoice("INV-5", 13)];

        var decisions = Engine.Decide(Request(family, resolution.Authoritative, stored, [coverage])).Decisions;

        // INV-1 is in the export (its row is held) and INV-3 may be the undated-number row of 12 Sep: no item for either.
        Assert.True(coverage.Observed(stored[0].Key));
        Assert.False(coverage.Covers(new DateOnly(2026, 9, 12)));
        var retired = decisions.Where(decision => decision.Decision == DocumentDecision.MissingFromLater).Select(decision => decision.Key.KeyText);
        Assert.Equal(["2027|INV-4"], retired);

        // A row with no usable date could be any document: the block takes no part in the absence check at all.
        var undated = Projector.Project(new(family, Store, block, [.. rows, Row(family, 6, ("invoice_number", "INV-6"), ("transaction_date", null))]));
        var undatedCoverage = Assert.Single(InSourceResolver.CoverageBlocks(source, [undated], family.Identity!.Scope));
        Assert.False(undatedCoverage.HasCoverage);
        Assert.DoesNotContain(Engine.Decide(Request(family, undated.Documents, stored, [undatedCoverage])).Decisions,
            decision => decision.Decision == DocumentDecision.MissingFromLater);
    }

    [Fact]
    public void Real_v0_invoices_are_filled_from_the_shipped_catalogue()
    {
        // The shipped R025 identity and a row as DocumentProjector projects it, against the v0 sales_lines row the
        // upgrade reads (fact-table names, gross and tax NULL). Spec 7.3: FILL, not a review item.
        var family = EtpReportFamilyRegistry.Resolve("R025");
        Assert.Equal(["source_gross_amount", "source_tax_amount"], CanonicalFactProjection.LegacyNullableColumns(family.Identity!).Order());
        var exportTime = ExportTime.AtMinute(new DateTime(2026, 9, 29, 14, 49, 0));
        var incoming = Assert.Single(Projector.Project(new(family, Store, Block(1, exportTime), [Row(family, 2)])).Documents);
        var date = new DateOnly(2026, 8, 29);
        Assert.Equal(DocumentKey.ForDocument("R025", Store, date, "INV-0001"), incoming.Key);

        DocumentDecision Decide(decimal quantity, decimal? tax)
        {
            var v0 = CanonicalFactRow.Create(FactCanonicalizer.Instance, new Dictionary<string, object?>
            {
                [CanonicalFactProjection.FactTable] = "sales_lines", ["store_code"] = Store, ["document_number"] = "INV-0001",
                ["transaction_date"] = date, ["product_code"] = "ITEM-1", ["source_transaction_type"] = "INV",
                ["source_quantity"] = quantity, ["source_gross_amount"] = null, ["source_net_amount"] = 847.46m, ["source_tax_amount"] = tax
            });
            var storedRow = new FactRow(new RowLocator(0, "sales_lines", 1),
                new CanonicalRow("", "", "", "", new Dictionary<string, string>(), new Dictionary<string, string>())) { FactTableRows = [v0] };
            var version = new StoredVersion(1, 1, VersionBasis.CanonicalOnly, null, FactCanonicalizer.Instance.MultisetHash([v0.Hash]), null, 1,
                ExportTime.Unknown, false) { Rows = [storedRow] };
            var stored = new StoredDocument(incoming.Key, 7, DocumentStatus.Current, date, version);
            return Assert.Single(Engine.Decide(Request(family, [incoming], [stored], [])).Decisions).Decision;
        }

        Assert.Equal(DocumentDecision.Filled, Decide(1m, null));
        Assert.Equal(DocumentDecision.PendingChange, Decide(2m, null));      // a fact changed too
        Assert.Equal(DocumentDecision.PendingChange, Decide(1m, 12m));       // tax was not NULL
    }

    private static StoredDocument Snapshot(EtpReportFamily family, DateOnly date, ExportTime attested) =>
        Stored(DocumentKey.ForSnapshot(family.ReportCode, Store, date), date, attested);

    private static StoredDocument Stored(DocumentKey key, DateOnly date, ExportTime attested) =>
        new(key, 1, DocumentStatus.Current, date,
            new StoredVersion(1, 1, VersionBasis.SourceRows, new string('a', 64), null, new string('b', 64), 1, attested, false) { ExportTime = attested })
        {
            RowsPointerTime = attested
        };

    private static DecisionRequest Request(EtpReportFamily family, IReadOnlyList<DocumentObservation> incoming,
        IReadOnlyList<StoredDocument> stored, IReadOnlyList<CoverageBlock> blocks) =>
        new(Store, family.ReportCode, family.Identity!, incoming, stored.ToDictionary(document => document.Key.Hash), blocks,
            new HashSet<DateOnly>(), ImporterRole.Owner);
}
