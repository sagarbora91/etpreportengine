using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;

namespace Etp.Reporting.Import.Tests;

public sealed class DocumentDecisionEngineTests
{
    private const string Store = "WLMHW";
    private static readonly DateOnly Day = new(2026, 9, 1);
    private static readonly DocumentDecisionEngine Engine = new();

    // Families of spec 7.3-7.4, reduced to what the engine reads.
    private static readonly EtpFamilyIdentity Sales = new()
    {
        Scope = DocumentScope.Document, YearRule = YearRule.FinancialYearOfPrimaryDate, Route = FamilyRoute.Sales,
        LegacyNullable = ["source_gross_amount", "source_tax_amount"]
    };
    private static readonly EtpFamilyIdentity Revenue = Sales with { Route = FamilyRoute.Revenue, LegacyNullable = [] };
    private static readonly EtpFamilyIdentity LandingDay = new() { Scope = DocumentScope.Date };
    private static readonly EtpFamilyIdentity ClosingStock = new()
    {
        Scope = DocumentScope.Snapshot, RowRule = RowRule.SnapshotItems, ChangePolicy = ChangePolicy.LatestReadingWins,
        Route = FamilyRoute.StockSnapshot, SnapshotDate = "Column:snapshot_date"
    };
    private static readonly EtpFamilyIdentity LandingSnapshot = new()
    {
        Scope = DocumentScope.Snapshot, RowRule = RowRule.SnapshotItems, ChangePolicy = ChangePolicy.LatestReadingWins, SnapshotDate = "Block"
    };
    private static readonly EtpFamilyIdentity LandingPeriod = new() { Scope = DocumentScope.Period, ChangePolicy = ChangePolicy.LatestReadingWins };

    // The stored version was last attested by the 6 Sep export; the document's day (1 Sep) was long over.
    private const string StoredTime = "2026-09-06T21:07";

    // ------------------------------------------------------------------ rules 9-15 by order

    public static TheoryData<string, string, DocumentDecision, ChangeReason?, ChangeMode?> RulesByOrder()
    {
        var data = new TheoryData<string, string, DocumentDecision, ChangeReason?, ChangeMode?>();
        void Add(string scenario, string order, DocumentDecision decision, ChangeReason? reason = null, ChangeMode? mode = null) =>
            data.Add(scenario, order, decision, reason, mode);

        // Rule 9: present. The rows pointer moves only for a newer export, or when the stored pointer is unknown (9b).
        Add("present", "OLDER", DocumentDecision.Present);
        Add("present", "SAME", DocumentDecision.Present);
        Add("present", "NEWER", DocumentDecision.Refreshed);
        Add("present", "UNKNOWN", DocumentDecision.Present);
        Add("present", "DATE_SAME", DocumentDecision.Present);
        Add("present", "DATE_NEWER", DocumentDecision.Refreshed);
        Add("present", "STORED_UNKNOWN", DocumentDecision.Refreshed);

        // Rule 9a: attributes follow a newer export, or a known export over an unknown one (OD-3).
        Add("attributes", "OLDER", DocumentDecision.Present);
        Add("attributes", "SAME", DocumentDecision.Present);
        Add("attributes", "NEWER", DocumentDecision.AttributeUpdated, ChangeReason.Attribute, ChangeMode.Auto);
        Add("attributes", "UNKNOWN", DocumentDecision.Present);
        Add("attributes", "DATE_SAME", DocumentDecision.Present);
        Add("attributes", "DATE_NEWER", DocumentDecision.AttributeUpdated, ChangeReason.Attribute, ChangeMode.Auto);
        Add("attributes", "STORED_UNKNOWN", DocumentDecision.AttributeUpdated, ChangeReason.Attribute, ChangeMode.Auto);

        // Rules 11 and 15 for a typed Review family: older is stale, everything else is reviewed with its reason.
        Add("typed-change", "OLDER", DocumentDecision.Stale);
        Add("typed-change", "SAME", DocumentDecision.PendingChange, ChangeReason.SameExportDiffers, ChangeMode.Review);
        Add("typed-change", "NEWER", DocumentDecision.PendingChange, ChangeReason.LaterExport, ChangeMode.Review);
        Add("typed-change", "UNKNOWN", DocumentDecision.PendingChange, ChangeReason.UnknownProvenance, ChangeMode.Review);
        Add("typed-change", "DATE_SAME", DocumentDecision.PendingChange, ChangeReason.UnknownProvenance, ChangeMode.Review);
        Add("typed-change", "DATE_NEWER", DocumentDecision.PendingChange, ChangeReason.LaterExport, ChangeMode.Review);
        Add("typed-change", "STORED_UNKNOWN", DocumentDecision.PendingChange, ChangeReason.UnknownProvenance, ChangeMode.Review);

        // Rule 12: late rows on a landing-only day grow it, from a newer export or a known one over an unknown one.
        Add("landing-grow", "OLDER", DocumentDecision.Stale);
        Add("landing-grow", "SAME", DocumentDecision.PendingChange, ChangeReason.SameExportDiffers, ChangeMode.Review);
        Add("landing-grow", "NEWER", DocumentDecision.Grown, ChangeReason.Grow, ChangeMode.Auto);
        Add("landing-grow", "UNKNOWN", DocumentDecision.PendingChange, ChangeReason.UnknownProvenance, ChangeMode.Review);
        Add("landing-grow", "DATE_SAME", DocumentDecision.PendingChange, ChangeReason.UnknownProvenance, ChangeMode.Review);
        Add("landing-grow", "DATE_NEWER", DocumentDecision.Grown, ChangeReason.Grow, ChangeMode.Auto);
        Add("landing-grow", "STORED_UNKNOWN", DocumentDecision.Grown, ChangeReason.Grow, ChangeMode.Auto);

        // A landing-only day that lost a row is a genuine change, never a growth.
        Add("landing-changed", "NEWER", DocumentDecision.PendingChange, ChangeReason.LaterExport, ChangeMode.Review);
        Add("landing-changed", "STORED_UNKNOWN", DocumentDecision.PendingChange, ChangeReason.UnknownProvenance, ChangeMode.Review);

        // Rule 14 (typed snapshot, Owner importing): a newer reading with nothing removed applies; only NEWER qualifies.
        Add("reading", "OLDER", DocumentDecision.Stale);
        Add("reading", "SAME", DocumentDecision.PendingChange, ChangeReason.SameExportDiffers, ChangeMode.Review);
        Add("reading", "NEWER", DocumentDecision.ReadingUpdated, ChangeReason.Reading, ChangeMode.Auto);
        Add("reading", "UNKNOWN", DocumentDecision.PendingChange, ChangeReason.UnknownProvenance, ChangeMode.Review);
        Add("reading", "DATE_SAME", DocumentDecision.PendingChange, ChangeReason.UnknownProvenance, ChangeMode.Review);
        Add("reading", "DATE_NEWER", DocumentDecision.ReadingUpdated, ChangeReason.Reading, ChangeMode.Auto);
        Add("reading", "STORED_UNKNOWN", DocumentDecision.PendingChange, ChangeReason.UnknownProvenance, ChangeMode.Review);

        // Rule 14: a reading that removes a row key always needs the Owner.
        Add("shrink", "OLDER", DocumentDecision.Stale);
        Add("shrink", "NEWER", DocumentDecision.PendingChange, ChangeReason.SnapshotShrink, ChangeMode.Review);
        Add("shrink", "DATE_NEWER", DocumentDecision.PendingChange, ChangeReason.SnapshotShrink, ChangeMode.Review);
        Add("shrink", "UNKNOWN", DocumentDecision.PendingChange, ChangeReason.UnknownProvenance, ChangeMode.Review);

        // Rule 10 comes before rule 11: a NULL fill of a v0 document applies whatever the order.
        foreach (var order in Orders) Add("fill", order, DocumentDecision.Filled, ChangeReason.Fill, ChangeMode.Auto);
        return data;
    }

    private static readonly string[] Orders = ["OLDER", "SAME", "NEWER", "UNKNOWN", "DATE_SAME", "DATE_NEWER", "STORED_UNKNOWN"];

    [Theory]
    [MemberData(nameof(RulesByOrder))]
    public void Rules_follow_the_order_of_exports(
        string scenario, string order, DocumentDecision expected, ChangeReason? reason, ChangeMode? mode)
    {
        var storedTime = order == "STORED_UNKNOWN" ? ExportTime.Unknown : T(StoredTime);
        var incomingTime = order switch
        {
            "OLDER" => T("2026-08-25T10:00"),
            "SAME" => T(StoredTime),
            "NEWER" or "STORED_UNKNOWN" => T("2026-09-29T14:49"),
            "UNKNOWN" => ExportTime.Unknown,
            "DATE_SAME" => T("2026-09-06"),
            "DATE_NEWER" => T("2026-09-29"),
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };
        (EtpFamilyIdentity, DocumentKey, FactRow[], FactRow[], VersionBasis) setup = scenario switch
        {
            "present" => (Sales, SalesKey(), Rows("p1", "p2"), Rows("p1", "p2"), VersionBasis.SourceRows),
            "attributes" => (Sales, SalesKey(), Rows("p1", "p2"), [Row("p1", "b2"), Row("p2", "b2")], VersionBasis.SourceRows),
            "typed-change" => (Sales, SalesKey(), Rows("p1", "p2"), Rows("p1", "p3"), VersionBasis.SourceRows),
            "landing-grow" => (LandingDay, DayKey(), Rows("p1", "p2"), Rows("p1", "p2", "p2"), VersionBasis.SourceRows),
            "landing-changed" => (LandingDay, DayKey(), Rows("p1", "p2"), Rows("p1", "p3"), VersionBasis.SourceRows),
            "reading" => (ClosingStock, SnapshotKey(), Items(("k1", "q1"), ("k2", "q1")), Items(("k1", "q2"), ("k2", "q1"), ("k3", "q1")), VersionBasis.SourceRows),
            "shrink" => (ClosingStock, SnapshotKey(), Items(("k1", "q1"), ("k2", "q1")), Items(("k1", "q1")), VersionBasis.SourceRows),
            "fill" => (Sales, SalesKey(), [Filled("p1", tax: "")], [Filled("p1", tax: "18")], VersionBasis.CanonicalOnly),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var (identity, key, storedRows, incomingRows, basis) = setup;
        var stored = Stored(key, Day, Version(storedTime, storedRows, basis: basis)) with { RowsPointerTime = storedTime };

        var result = Single(Request(identity, [Obs(key, incomingTime, Day, incomingRows)], [stored]));

        Assert.Equal(expected, result.Decision);
        Assert.Equal(reason, result.Change?.Reason);
        Assert.Equal(mode, result.Change?.Mode);
        Assert.False(result.Change?.Held ?? false);
        if (expected is DocumentDecision.Present or DocumentDecision.Refreshed or DocumentDecision.AttributeUpdated or DocumentDecision.Filled)
            Assert.True(result.Attest || expected == DocumentDecision.Filled);
        if (scenario == "attributes" && expected == DocumentDecision.Present)
            Assert.Equal(ImportCodes.AttributeNotApplied, result.DetailCode);
        if (expected == DocumentDecision.Stale) Assert.Null(result.Change);
    }

    [Fact]
    public void Change_items_carry_the_version_they_create()
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T(StoredTime), Rows("p1")));
        var later = Single(Request(Sales, [Obs(key, T("2026-09-29T14:49"), Day, Rows("p2"))], [stored]));
        Assert.Equal(new ChangeProposal(ChangeMode.Review, ChangeReason.LaterExport, ChangeAction.Replace, VersionChangeKind.Replace), later.Change);
        Assert.Equal(ChangeItemStatus.Pending, later.Change!.ItemStatus);

        var attribute = Single(Request(Sales, [Obs(key, T("2026-09-29T14:49"), Day, [Row("p1", "b2")])], [stored]));
        Assert.Equal(new ChangeProposal(ChangeMode.Auto, ChangeReason.Attribute, ChangeAction.Update, VersionChangeKind.Attribute), attribute.Change);
        Assert.Equal(ChangeItemStatus.Applied, attribute.Change!.ItemStatus);
    }

    [Fact]
    public void Attribute_not_applied_is_reported_as_information_without_customer_values()
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T(StoredTime), Rows("p1")));
        var outcome = Engine.Decide(Request(Sales, [Obs(key, T("2026-08-25T10:00"), Day, [Row("p1", "b2")])], [stored]));

        var diagnostic = Assert.Single(outcome.Diagnostics);
        Assert.Equal(ImportCodes.AttributeNotApplied, diagnostic.Code);
        Assert.Equal(Diagnostics.ImportDiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Equal(key.KeyText, diagnostic.DocumentRef);
        Assert.Equal(1, diagnostic.BlockNo);
    }

    // ------------------------------------------------------------------ rules 1-2

    [Fact]
    public void Facts_already_pending_are_attested_not_queued_again()
    {
        var key = SalesKey();
        var incoming = Obs(key, T("2026-09-29T14:49"), Day, Rows("p9"));
        var stored = Stored(key, Day, Version(T(StoredTime), Rows("p1"))) with { PendingFactSha256 = [incoming.FactSha256.ToUpperInvariant()] };

        var result = Single(Request(Sales, [incoming], [stored]));

        Assert.Equal(DocumentDecision.PendingExists, result.Decision);
        Assert.True(result.Attest);
        Assert.Null(result.Change);
    }

    [Fact]
    public void Facts_the_owner_rejected_are_never_raised_again()
    {
        var key = SalesKey();
        var incoming = Obs(key, T("2026-09-29T14:49"), Day, Rows("p9"));
        var stored = Stored(key, Day, Version(T(StoredTime), Rows("p1"))) with { RejectedFactSha256 = [incoming.FactSha256] };

        var result = Single(Request(Sales, [incoming], [stored]));

        Assert.Equal(DocumentDecision.RejectedBefore, result.Decision);
        Assert.False(result.Attest);
        Assert.Null(result.Change);
    }

    // ------------------------------------------------------------------ rules 3-8: no current facts

    [Fact]
    public void A_new_document_is_inserted_and_obsoletes_a_pending_insert()
    {
        var key = SalesKey();
        var fresh = Single(Request(Sales, [Obs(key, T("2026-09-29T14:49"), Day, Rows("p1"))], []));
        Assert.Equal(DocumentDecision.New, fresh.Decision);
        Assert.Null(fresh.Change);
        Assert.False(fresh.ObsoletePendingInsert);
        Assert.False(fresh.NewVersionProvisional);

        var notAdded = new StoredDocument(key, 7, DocumentStatus.NotAdded, Day, null) { HasPendingInsert = true };
        var later = Single(Request(Sales, [Obs(key, T("2026-09-29T14:49"), Day, Rows("p1"))], [notAdded]));
        Assert.Equal(DocumentDecision.New, later.Decision);
        Assert.True(later.ObsoletePendingInsert);

        var pending = notAdded with { Status = DocumentStatus.Pending };
        Assert.Equal(DocumentDecision.New, Single(Request(Sales, [Obs(key, ExportTime.Unknown, Day, Rows("p1"))], [pending])).Decision);
    }

    [Theory]
    [InlineData(false, false, DocumentDecision.HeldHeaderDate)]
    [InlineData(true, false, DocumentDecision.New)]   // an orphan header is archived in the AUTO set, never in the way
    [InlineData(false, true, DocumentDecision.New)]   // the header carries the same date
    public void A_new_invoice_never_re_dates_a_shared_header(bool orphan, bool sameDate, DocumentDecision expected)
    {
        foreach (var identity in new[] { Sales, Revenue })
        {
            var key = SalesKey();
            var header = new InvoiceHeaderState(key, sameDate ? Day : Day.AddDays(-3), orphan);
            var request = Request(identity, [Obs(key, T("2026-09-29T14:49"), Day, Rows("p1"))], []) with
            {
                Headers = new Dictionary<string, InvoiceHeaderState> { [key.Hash] = header }
            };

            var outcome = Engine.Decide(request);
            var result = Assert.Single(outcome.Decisions);

            Assert.Equal(expected, result.Decision);
            if (expected != DocumentDecision.HeldHeaderDate) continue;
            Assert.Equal(new ChangeProposal(ChangeMode.Review, ChangeReason.HeaderDateMismatch, ChangeAction.None, null, Held: true), result.Change);
            Assert.Equal(ImportCodes.HeaderDateMismatch, Assert.Single(outcome.Diagnostics).Code);
        }
    }

    [Fact]
    public void Header_dates_only_matter_to_sales_and_revenue()
    {
        var key = DayKey();
        var request = Request(LandingDay, [Obs(key, T("2026-09-29T14:49"), Day, Rows("p1"))], []) with
        {
            Headers = new Dictionary<string, InvoiceHeaderState> { [key.Hash] = new(key, Day.AddDays(-3), false) }
        };
        Assert.Equal(DocumentDecision.New, Single(request).Decision);
    }

    [Fact]
    public void A_new_document_on_a_locked_day_is_held()
    {
        var key = SalesKey();
        var result = Single(Request(Sales, [Obs(key, T("2026-09-29T14:49"), Day, Rows("p1"))], [], locked: [Day]));

        Assert.Equal(DocumentDecision.HeldLocked, result.Decision);
        Assert.Equal(new ChangeProposal(ChangeMode.Review, ChangeReason.NewOnLockedDay, ChangeAction.Insert, VersionChangeKind.New, Held: true), result.Change);
        Assert.Equal(ChangeItemStatus.Held, result.Change!.ItemStatus);
    }

    [Theory]
    [InlineData("2026-09-29T14:49", DocumentDecision.PendingChange)]
    [InlineData("2026-09-06T21:07", DocumentDecision.Stale)]
    [InlineData("2026-08-25T10:00", DocumentDecision.Stale)]
    [InlineData("", DocumentDecision.Stale)]
    public void A_retired_document_reappears_only_from_a_newer_export(string time, DocumentDecision expected)
    {
        var key = SalesKey();
        var retired = Stored(key, Day, Version(T(StoredTime), Rows("p1"))) with { Status = DocumentStatus.Retired };

        var result = Single(Request(Sales, [Obs(key, T(time), Day, Rows("p1"))], [retired]));

        Assert.Equal(expected, result.Decision);
        if (expected == DocumentDecision.PendingChange)
            Assert.Equal(new ChangeProposal(ChangeMode.Review, ChangeReason.Reappeared, ChangeAction.Insert, VersionChangeKind.New), result.Change);
        else
            Assert.Null(result.Change);
    }

    [Fact]
    public void A_retired_document_missing_from_a_later_export_is_not_added_rather_than_reappearing()
    {
        // Retired after the 29 Sep export lacked it; an export of 20 Sep (newer than the one that attested it) arrives.
        var key = SalesKey();
        var retired = Stored(key, Day, Version(T("2026-09-06T21:07"), Rows("p1"))) with { Status = DocumentStatus.Retired };

        var result = Single(Request(Sales, [Obs(key, T("2026-09-20T10:00"), Day, Rows("p2"))], [retired],
            blocks: [Registered(T("2026-09-29T14:49"), Day, Day.AddDays(27))]));

        Assert.Equal(DocumentDecision.NotAdded, result.Decision);
        Assert.Equal(ChangeReason.MissingFromLater, result.Change!.Reason);
    }

    // ------------------------------------------------------------------ 8.4 absence

    [Fact]
    public void Not_added_document_raises_review_item_in_either_order()
    {
        var key = SalesKey();
        var older = T("2026-09-06T21:07");
        var newer = T("2026-09-29T14:49");
        var other = SalesKey("100000099");

        // Older then newer: the document is stored, the newer complete export lacks it -> RETIRE item, nothing deleted.
        var stored = Stored(key, Day, Version(older, Rows("p1")));
        var newerImport = Engine.Decide(Request(Sales, [Obs(other, newer, Day, Rows("p5"))], [stored],
            blocks: [Incoming(newer, Day, Day.AddDays(27), other)]));
        var retire = Assert.Single(newerImport.Decisions, decision => decision.Key == key);
        Assert.Equal(DocumentDecision.MissingFromLater, retire.Decision);
        Assert.Equal(new ChangeProposal(ChangeMode.Review, ChangeReason.MissingFromLater, ChangeAction.Retire, null), retire.Change);
        Assert.Equal(1, retire.BlockNo);
        Assert.Equal(DocumentDecision.New, Assert.Single(newerImport.Decisions, decision => decision.Key == other).Decision);

        // Newer then older: the newer complete export is registered and lacks it -> NOT_ADDED with an INSERT item.
        var olderImport = Single(Request(Sales, [Obs(key, older, Day, Rows("p1"))], [],
            blocks: [Registered(newer, Day, Day.AddDays(27), other), Incoming(older, Day, Day.AddDays(5), key)]));
        Assert.Equal(DocumentDecision.NotAdded, olderImport.Decision);
        Assert.Equal(new ChangeProposal(ChangeMode.Review, ChangeReason.MissingFromLater, ChangeAction.Insert, VersionChangeKind.New), olderImport.Change);
        Assert.False(olderImport.Attest);

        // The same question either way: is the document real? Both are review items about the same document.
        Assert.Equal(retire.Key, olderImport.Key);
        Assert.Equal(ChangeItemStatus.Pending, retire.Change!.ItemStatus);
        Assert.Equal(ChangeItemStatus.Pending, olderImport.Change!.ItemStatus);
    }

    [Fact]
    public void Absence_inside_one_source_uses_the_later_block()
    {
        var key = SalesKey();
        var other = SalesKey("100000099");
        var early = T("2026-09-06T21:07");
        var late = T("2026-09-29T14:49");
        var request = Request(Sales, [Obs(key, early, Day, Rows("p1")), Obs(other, late, Day, Rows("p2")) with { BlockNo = 2 }], [],
            blocks: [Incoming(early, Day, Day.AddDays(5), key), Incoming(late, Day, Day.AddDays(27), other) with { BlockNo = 2 }]);

        var decisions = Engine.Decide(request).Decisions;

        Assert.Equal(DocumentDecision.NotAdded, Assert.Single(decisions, d => d.Key == key).Decision);
        Assert.Equal(DocumentDecision.New, Assert.Single(decisions, d => d.Key == other).Decision);
    }

    public static TheoryData<string, bool> AbsenceConditions() => new()
    {
        { "complete", true },
        { "delta", true },
        { "trimmed", false },          // never rebuilt in full: no absence check
        { "legacy", false },
        { "empty", false },
        { "unknown-time", false },
        { "day-not-over", false },     // the block was exported on the document's own day
        { "outside-coverage", false },
        { "observed", false },
        { "older-block", false },
        { "same-time-block", false },
        { "no-coverage", false }
    };

    [Theory]
    [MemberData(nameof(AbsenceConditions))]
    public void Absence_needs_a_newer_complete_block_that_covers_the_finished_day(string condition, bool absent)
    {
        var key = SalesKey();
        var other = SalesKey("100000099");
        var docTime = T("2026-09-06T21:07");
        var block = Registered(T("2026-09-29T14:49"), Day, Day.AddDays(27), other);
        block = condition switch
        {
            "delta" => block with { Completeness = BlockCompleteness.Delta },
            "trimmed" => block with { Completeness = BlockCompleteness.Trimmed },
            "legacy" => block with { Completeness = BlockCompleteness.Legacy },
            "empty" => block with { Completeness = BlockCompleteness.Empty },
            "unknown-time" => block with { ExportTime = ExportTime.Unknown },
            "day-not-over" => block with { ExportTime = T("2026-09-01T18:00") },
            "outside-coverage" => block with { CoverageFrom = Day.AddDays(1) },
            "observed" => block with { ObservedDocuments = new HashSet<string> { key.Hash } },
            "older-block" => block with { ExportTime = T("2026-08-25T10:00") },
            "same-time-block" => block with { ExportTime = docTime },
            "no-coverage" => block with { CoverageFrom = null, CoverageTo = null },
            _ => block
        };
        var docObservation = Obs(key, condition == "day-not-over" ? T("2026-09-01T12:00") : docTime, Day, Rows("p1"));

        // Newer then older: rule 3.
        var notAdded = Single(Request(Sales, [docObservation], [], blocks: [block]));
        Assert.Equal(absent ? DocumentDecision.NotAdded : DocumentDecision.New, notAdded.Decision);

        // Older then newer: the same block arriving later raises (or not) the RETIRE item.
        var stored = Stored(key, Day, Version(docObservation.ExportTime, Rows("p1")));
        var incomingBlock = block with { ImportFileId = null };
        var outcome = Engine.Decide(Request(Sales, [], [stored], blocks: [incomingBlock]));
        Assert.Equal(absent, outcome.Decisions.Any(d => d.Decision == DocumentDecision.MissingFromLater));
    }

    [Fact]
    public void A_retire_item_needs_an_export_newer_than_every_attesting_one()
    {
        var key = SalesKey();
        var other = SalesKey("100000099");
        var block = Incoming(T("2026-09-29T14:49"), Day, Day.AddDays(27), other);

        Assert.Empty(Engine.Decide(Request(Sales, [], [Stored(key, Day, Version(T("2026-09-30T09:00"), Rows("p1")))], blocks: [block])).Decisions);
        var unknown = Engine.Decide(Request(Sales, [], [Stored(key, Day, Version(ExportTime.Unknown, Rows("p1")))], blocks: [block]));
        Assert.Equal(DocumentDecision.MissingFromLater, Assert.Single(unknown.Decisions).Decision);

        // Documents of other stores or reports are never retired by this source.
        var foreign = Stored(DocumentKey.ForDocument("R025", "HEMW", Day, "100000068"), Day, Version(T(StoredTime), Rows("p1")));
        var request = Request(Sales, [], [], blocks: [block]) with
        {
            Stored = new Dictionary<string, StoredDocument> { [foreign.Key.Hash] = foreign }
        };
        Assert.Empty(Engine.Decide(request).Decisions);
    }

    [Fact]
    public void A_document_missing_from_two_blocks_gets_one_retire_item_from_the_newest()
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T(StoredTime), Rows("p1")));
        var blocks = new[]
        {
            Incoming(T("2026-09-20T10:00"), Day, Day.AddDays(20)),
            Incoming(T("2026-09-29T14:49"), Day, Day.AddDays(27)) with { BlockNo = 2 }
        };

        var retire = Assert.Single(Engine.Decide(Request(Sales, [], [stored], blocks: blocks)).Decisions);

        Assert.Equal(2, retire.BlockNo);
    }

    [Fact]
    public void Absence_covers_every_day_of_a_period()
    {
        var key = DocumentKey.ForPeriod("S002", Store, Day, Day.AddDays(29));
        var observation = Obs(key, T("2026-10-02T10:00"), Day, Rows("j1")) with { PeriodTo = Day.AddDays(29) };
        var covering = Registered(T("2026-10-05T10:00"), Day, Day.AddDays(29));
        var partial = Registered(T("2026-10-05T10:00"), Day, Day.AddDays(10));

        Assert.Equal(DocumentDecision.NotAdded, Single(Request(LandingPeriod, [observation], [], blocks: [covering], report: "S002")).Decision);
        Assert.Equal(DocumentDecision.New, Single(Request(LandingPeriod, [observation], [], blocks: [partial], report: "S002")).Decision);
    }

    // ------------------------------------------------------------------ 8.3 provisional days

    [Theory]
    [InlineData("2026-09-01T18:30", true)]   // later the same day
    [InlineData("2026-09-02T09:15", true)]   // the next morning
    [InlineData("2026-09-02T23:59", true)]
    [InlineData("2026-09-02", true)]         // a date-only export of the next day
    [InlineData("2026-09-03T00:00", false)]  // two days on: a change to a settled day
    [InlineData("2026-09-29T14:49", false)]
    public void Provisional_window_is_next_day_only(string time, bool automatic)
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T("2026-09-01T14:49"), Rows("p1"), provisional: true));

        var result = Single(Request(Sales, [Obs(key, T(time), Day, Rows("p2"))], [stored]));

        if (automatic)
        {
            Assert.Equal(DocumentDecision.ProvisionalUpdated, result.Decision);
            Assert.Equal(new ChangeProposal(ChangeMode.Auto, ChangeReason.Provisional, ChangeAction.Replace, VersionChangeKind.Provisional), result.Change);
        }
        else
        {
            Assert.Equal(DocumentDecision.PendingChange, result.Decision);
            Assert.Equal(ChangeReason.LaterExport, result.Change!.Reason);
        }
        // The new version is provisional only while its export was taken on the business day itself.
        Assert.Equal(T(time).ExportDate <= Day, result.NewVersionProvisional);
    }

    [Fact]
    public void A_settled_version_is_never_updated_as_provisional()
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T("2026-09-02T09:00"), Rows("p1"), provisional: false));

        var result = Single(Request(Sales, [Obs(key, T("2026-09-02T18:00"), Day, Rows("p2"))], [stored]));

        Assert.Equal(DocumentDecision.PendingChange, result.Decision);
        Assert.Equal(ChangeReason.LaterExport, result.Change!.Reason);
    }

    [Theory]
    [InlineData("2026-09-02T09:00", true)]
    [InlineData("2026-09-02", true)]
    [InlineData("2026-09-01T23:00", false)]
    [InlineData("", false)]
    public void Present_settles_a_provisional_version_once_the_day_is_over(string time, bool settles)
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T("2026-09-01T14:49"), Rows("p1"), provisional: true));

        var result = Single(Request(Sales, [Obs(key, T(time), Day, Rows("p1"))], [stored]));

        Assert.True(result.Attest);
        Assert.Equal(settles, result.SettleProvisional);
    }

    // Rules 13 and 14 under OD-7: policy x importer role x typed family.
    public static TheoryData<ProvisionalPolicy, ImporterRole, bool, bool> PolicyMatrix()
    {
        var data = new TheoryData<ProvisionalPolicy, ImporterRole, bool, bool>();
        foreach (var policy in Enum.GetValues<ProvisionalPolicy>())
        foreach (var role in Enum.GetValues<ImporterRole>())
        foreach (var typed in new[] { true, false })
        {
            var automatic = policy switch
            {
                ProvisionalPolicy.Anyone => true,
                ProvisionalPolicy.AlwaysReview => false,
                _ => !typed || role == ImporterRole.Owner
            };
            data.Add(policy, role, typed, automatic);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(PolicyMatrix))]
    public void Provisional_updates_follow_the_policy_and_the_importer(ProvisionalPolicy policy, ImporterRole role, bool typed, bool automatic)
    {
        var (identity, key) = typed ? (Sales, SalesKey()) : (LandingDay, DayKey());
        var stored = Stored(key, Day, Version(T("2026-09-01T14:49"), Rows("p1", "p2"), provisional: true));
        var request = Request(identity, [Obs(key, T("2026-09-02T09:00"), Day, Rows("p1", "p3"))], [stored], role: role) with
        {
            Policy = new DecisionPolicy(policy)
        };

        var result = Single(request);

        Assert.Equal(automatic ? DocumentDecision.ProvisionalUpdated : DocumentDecision.PendingChange, result.Decision);
        Assert.Equal(new ChangeProposal(automatic ? ChangeMode.Auto : ChangeMode.Review, ChangeReason.Provisional,
            ChangeAction.Replace, VersionChangeKind.Provisional), result.Change);
    }

    [Theory]
    [MemberData(nameof(PolicyMatrix))]
    public void Snapshot_readings_follow_the_policy_and_the_importer(ProvisionalPolicy policy, ImporterRole role, bool typed, bool automatic)
    {
        var (identity, report) = typed ? (ClosingStock, "CLOSING_STOCK") : (LandingSnapshot, "R023");
        var key = DocumentKey.ForSnapshot(report, Store, Day);
        var stored = Stored(key, Day, Version(T(StoredTime), Items(("k1", "q1"), ("k2", "q1"))));
        var reading = Obs(key, T("2026-09-29T14:49"), Day, Items(("k1", "q1"), ("k2", "q2")));
        var shrink = Obs(key, T("2026-09-29T14:49"), Day, Items(("k1", "q1")));
        DecisionRequest Make(DocumentObservation o) =>
            Request(identity, [o], [stored], role: role, report: report) with { Policy = new DecisionPolicy(policy) };

        var updated = Single(Make(reading));
        Assert.Equal(automatic ? DocumentDecision.ReadingUpdated : DocumentDecision.PendingChange, updated.Decision);
        Assert.Equal(new ChangeProposal(automatic ? ChangeMode.Auto : ChangeMode.Review, ChangeReason.Reading,
            ChangeAction.Replace, VersionChangeKind.Reading), updated.Change);

        // A removed row always needs the Owner, whoever imports and whatever the policy.
        var shrunk = Single(Make(shrink));
        Assert.Equal(DocumentDecision.PendingChange, shrunk.Decision);
        Assert.Equal(new ChangeProposal(ChangeMode.Review, ChangeReason.SnapshotShrink, ChangeAction.Replace, VersionChangeKind.Reading), shrunk.Change);
    }

    [Fact]
    public void A_reading_without_the_stored_rows_is_treated_as_a_shrink()
    {
        var key = SnapshotKey();
        var stored = Stored(key, Day, Version(T(StoredTime), Items(("k1", "q1"))) with { Rows = null });

        var result = Single(Request(ClosingStock, [Obs(key, T("2026-09-29T14:49"), Day, Items(("k1", "q2")))], [stored]));

        Assert.Equal(ChangeReason.SnapshotShrink, result.Change!.Reason);
    }

    // ------------------------------------------------------------------ 8.6 locked days

    public static TheoryData<string, DocumentDecision, ChangeReason> LockedChanges() => new()
    {
        { "attributes", DocumentDecision.HeldLocked, ChangeReason.Attribute },
        { "fill", DocumentDecision.HeldLocked, ChangeReason.Fill },
        { "grow", DocumentDecision.HeldLocked, ChangeReason.Grow },
        { "provisional", DocumentDecision.HeldLocked, ChangeReason.Provisional },
        { "reading", DocumentDecision.HeldLocked, ChangeReason.Reading },
        { "later", DocumentDecision.HeldLocked, ChangeReason.LaterExport },
        { "legacy", DocumentDecision.LegacyBlocksDiffer, ChangeReason.LegacyBlocksDiffer },
        { "reappeared", DocumentDecision.HeldLocked, ChangeReason.NewOnLockedDay },
        { "not-added", DocumentDecision.NotAdded, ChangeReason.MissingFromLater }
    };

    [Theory]
    [MemberData(nameof(LockedChanges))]
    public void Every_change_on_a_locked_day_is_held_with_its_reason(string change, DocumentDecision expected, ChangeReason reason)
    {
        var newer = T("2026-09-29T14:49");
        (EtpFamilyIdentity, DocumentKey, StoredVersion?, DocumentObservation, CoverageBlock[]) setup = change switch
        {
            "attributes" => (Sales, SalesKey(), Version(T(StoredTime), Rows("p1")), Obs(SalesKey(), newer, Day, [Row("p1", "b2")]), []),
            "fill" => (Sales, SalesKey(), Version(T(StoredTime), [Filled("p1", tax: "")], basis: VersionBasis.CanonicalOnly), Obs(SalesKey(), newer, Day, [Filled("p1", tax: "18")]), []),
            "grow" => (LandingDay, DayKey(), Version(T(StoredTime), Rows("p1")), Obs(DayKey(), newer, Day, Rows("p1", "p2")), []),
            "provisional" => (Sales, SalesKey(), Version(T("2026-09-01T14:49"), Rows("p1"), provisional: true), Obs(SalesKey(), T("2026-09-02T09:00"), Day, Rows("p2")), []),
            "reading" => (ClosingStock, SnapshotKey(), Version(T(StoredTime), Items(("k1", "q1"))), Obs(SnapshotKey(), newer, Day, Items(("k1", "q2"))), []),
            "later" => (Sales, SalesKey(), Version(T(StoredTime), Rows("p1")), Obs(SalesKey(), newer, Day, Rows("p2")), []),
            "legacy" => (Sales, SalesKey(), Version(T(StoredTime), Rows("p1")), Obs(SalesKey(), ExportTime.Unknown, Day, Rows("p2")) with { IsLegacyMerge = true, HoldCode = ImportCodes.LegacyBlocksDiffer }, []),
            "reappeared" => (Sales, SalesKey(), Version(T(StoredTime), Rows("p1")), Obs(SalesKey(), newer, Day, Rows("p1")), []),
            "not-added" => (Sales, SalesKey(), null, Obs(SalesKey(), T(StoredTime), Day, Rows("p1")), [Registered(newer, Day, Day.AddDays(27))]),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        var (identity, key, stored, incoming, blocks) = setup;
        var document = stored is null ? null : Stored(key, Day, stored) with
        {
            RowsPointerTime = T(StoredTime),
            Status = change == "reappeared" ? DocumentStatus.Retired : DocumentStatus.Current
        };

        var result = Single(Request(identity, [incoming], document is null ? [] : [document], blocks: blocks, locked: [Day],
            report: key.ReportCode));

        Assert.Equal(expected, result.Decision);
        Assert.Equal(reason, result.Change!.Reason);
        Assert.True(result.Change.Held);
        Assert.Equal(ChangeMode.Review, result.Change.Mode);
        Assert.Equal(ChangeItemStatus.Held, result.Change.ItemStatus);
        if (change == "grow") Assert.False(result.MoveRowsPointer);
        if (change == "attributes")
        {
            Assert.True(result.Attest);
            Assert.True(result.MoveRowsPointer);   // a descriptive refresh is allowed on a locked day
        }
    }

    [Fact]
    public void Present_refreshed_and_stale_are_allowed_on_a_locked_day()
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T(StoredTime), Rows("p1"))) with { RowsPointerTime = T(StoredTime) };
        DocumentDecisionResult Decide(string time, params FactRow[] rows) =>
            Single(Request(Sales, [Obs(key, T(time), Day, rows)], [stored], locked: [Day]));

        Assert.Equal(DocumentDecision.Present, Decide(StoredTime, Rows("p1")).Decision);
        var refreshed = Decide("2026-09-29T14:49", Rows("p1"));
        Assert.Equal(DocumentDecision.Refreshed, refreshed.Decision);
        Assert.Null(refreshed.Change);
        Assert.Equal(DocumentDecision.Stale, Decide("2026-08-25T10:00", Rows("p2")).Decision);
    }

    [Fact]
    public void A_locked_day_inside_a_period_holds_the_period()
    {
        var key = DocumentKey.ForPeriod("S002", Store, Day, Day.AddDays(29));
        var stored = Stored(key, Day, Version(T(StoredTime), Rows("j1"))) with { PeriodTo = Day.AddDays(29) };
        var incoming = Obs(key, T("2026-10-05T10:00"), Day, Rows("j1", "j2")) with { PeriodTo = Day.AddDays(29) };

        var held = Single(Request(LandingPeriod, [incoming], [stored], locked: [Day.AddDays(15)], report: "S002"));
        var open = Single(Request(LandingPeriod, [incoming], [stored], locked: [Day.AddDays(30)], report: "S002"));

        Assert.Equal(DocumentDecision.HeldLocked, held.Decision);
        Assert.True(held.Change!.Held);
        Assert.Equal(DocumentDecision.Grown, open.Decision);
        Assert.Equal(ChangeReason.Grow, held.Change.Reason);
    }

    [Fact]
    public void A_retire_item_on_a_locked_day_is_held()
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T(StoredTime), Rows("p1")));

        var retire = Assert.Single(Engine.Decide(Request(Sales, [], [stored],
            blocks: [Incoming(T("2026-09-29T14:49"), Day, Day.AddDays(27))], locked: [Day])).Decisions);

        Assert.Equal(DocumentDecision.MissingFromLater, retire.Decision);
        Assert.Equal(ChangeItemStatus.Held, retire.Change!.ItemStatus);
        Assert.Equal(ChangeReason.MissingFromLater, retire.Change.Reason);
    }

    // ------------------------------------------------------------------ basis

    [Theory]
    [InlineData(VersionBasis.CanonicalOnly)]
    [InlineData(VersionBasis.Unverified)]
    public void Unverified_versions_compare_canonical_facts(VersionBasis basis)
    {
        var key = SalesKey();
        var version = Version(T(StoredTime), Rows("x1"), basis: basis) with { CanonicalSha256 = "c1" };
        var stored = Stored(key, Day, version);
        DocumentDecisionResult Decide(string? canonical) =>
            Single(Request(Sales, [Obs(key, T("2026-09-29T14:49"), Day, Rows("p1")) with { CanonicalSha256 = canonical }], [stored]));

        Assert.Equal(DocumentDecision.Refreshed, Decide("C1").Decision);
        Assert.Equal(DocumentDecision.PendingChange, Decide("c2").Decision);
        Assert.Equal(DocumentDecision.PendingChange, Decide(null).Decision);
    }

    [Fact]
    public void Source_row_versions_compare_fact_hashes_only()
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T(StoredTime), Rows("p1")) with { CanonicalSha256 = "c1" });

        var result = Single(Request(Sales, [Obs(key, T("2026-09-29T14:49"), Day, Rows("p1")) with { CanonicalSha256 = "c9" }], [stored]));

        Assert.Equal(DocumentDecision.Refreshed, result.Decision);
    }

    [Theory]
    [InlineData(VersionBasis.CanonicalOnly, "", "", "18", "500", DocumentDecision.Filled)]            // tax filled
    [InlineData(VersionBasis.CanonicalOnly, "", "", "18", "501", DocumentDecision.PendingChange)]     // and a fact changed
    [InlineData(VersionBasis.CanonicalOnly, "12", "", "18", "500", DocumentDecision.PendingChange)]   // tax was not NULL
    [InlineData(VersionBasis.SourceRows, "", "", "18", "500", DocumentDecision.PendingChange)]        // only v0 versions fill
    [InlineData(VersionBasis.CanonicalOnly, "", "", "", "501", DocumentDecision.PendingChange)]       // nothing filled, a fact changed
    public void Fill_only_adds_legacy_nulls(VersionBasis basis, string storedTax, string storedGross, string incomingTax, string incomingNet, DocumentDecision expected)
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T(StoredTime), [Filled("p1", tax: storedTax, gross: storedGross)], basis: basis));
        var incomingRow = Filled("p1", tax: incomingTax, gross: incomingTax == "" ? "" : "590", net: incomingNet);

        var result = Single(Request(Sales, [Obs(key, T("2026-09-29T14:49"), Day, [incomingRow])], [stored]));

        Assert.Equal(expected, result.Decision);
        if (expected == DocumentDecision.Filled)
            Assert.Equal(new ChangeProposal(ChangeMode.Auto, ChangeReason.Fill, ChangeAction.Update, VersionChangeKind.Fill), result.Change);
    }

    [Fact]
    public void Fill_pairs_rows_with_multiplicity()
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T(StoredTime), [Filled("p1", tax: ""), Filled("p1", tax: ""), Filled("p2", tax: "")],
            basis: VersionBasis.CanonicalOnly));
        DocumentDecisionResult Decide(params FactRow[] rows) => Single(Request(Sales, [Obs(key, T(StoredTime), Day, rows)], [stored]));

        Assert.Equal(DocumentDecision.Filled, Decide(Filled("p1", tax: "18"), Filled("p2", tax: "18"), Filled("p1", tax: "")).Decision);
        Assert.NotEqual(DocumentDecision.Filled, Decide(Filled("p1", tax: "18"), Filled("p1", tax: "18"), Filled("p1", tax: "18")).Decision);
    }

    // ------------------------------------------------------------------ holds from the source (6.6-6.7)

    [Fact]
    public void An_in_source_conflict_is_held_and_never_applied()
    {
        var key = SalesKey();
        var o = Obs(key, T("2026-09-29T14:49"), Day, Rows("p1")) with { HoldCode = ImportCodes.InSourceConflict };

        foreach (var stored in new[] { null, Stored(key, Day, Version(T(StoredTime), Rows("p1"))) })
        {
            var result = Single(Request(Sales, [o], stored is null ? [] : [stored]));
            Assert.Equal(DocumentDecision.InSourceConflict, result.Decision);
            Assert.Equal(new ChangeProposal(ChangeMode.Review, ChangeReason.InSourceConflict, ChangeAction.None, null, Held: true), result.Change);
            Assert.False(result.Attest);
        }
    }

    [Fact]
    public void Legacy_blocks_that_differ_are_held()
    {
        var key = SalesKey();
        var merged = Obs(key, ExportTime.Unknown, Day, Rows("p1", "p2")) with { IsLegacyMerge = true, HoldCode = ImportCodes.LegacyBlocksDiffer };

        // Not yet stored: an INSERT item proposing the merged observation.
        var insert = Single(Request(Sales, [merged], []));
        Assert.Equal(DocumentDecision.LegacyBlocksDiffer, insert.Decision);
        Assert.Equal(new ChangeProposal(ChangeMode.Review, ChangeReason.LegacyBlocksDiffer, ChangeAction.Insert, VersionChangeKind.New), insert.Change);

        // Stored with other facts: a REPLACE item.
        var differs = Single(Request(Sales, [merged], [Stored(key, Day, Version(T(StoredTime), Rows("p1")))]));
        Assert.Equal(DocumentDecision.LegacyBlocksDiffer, differs.Decision);
        Assert.Equal(new ChangeProposal(ChangeMode.Review, ChangeReason.LegacyBlocksDiffer, ChangeAction.Replace, VersionChangeKind.Replace), differs.Change);

        // Stored with the merged facts: present, whatever the blocks said (6.6 step 5).
        var storedMerged = Stored(key, Day, Version(T(StoredTime), Rows("p1", "p2"))) with { RowsPointerTime = T(StoredTime) };
        var present = Single(Request(Sales, [merged], [storedMerged]));
        Assert.Equal(DocumentDecision.Present, present.Decision);
        Assert.Null(present.Change);
    }

    // ------------------------------------------------------------------ 8.5 reinterpretation

    [Fact]
    public void A_re_read_legacy_workbook_retires_what_the_old_reading_introduced()
    {
        // HEMW R010, file 26: stored as 29 Sep, re-reads as 7 Sep.
        var identity = LandingSnapshot with { Route = FamilyRoute.StockSnapshot };
        var oldReading = DocumentKey.ForSnapshot("R010", "HEMW", new DateOnly(2026, 9, 29));
        var newReading = DocumentKey.ForSnapshot("R010", "HEMW", new DateOnly(2026, 9, 7));
        var stored = Stored(oldReading, new DateOnly(2026, 9, 29), Version(ExportTime.Unknown, Items(("i1", "q1"))));
        var request = new DecisionRequest("HEMW", "R010", identity,
            [Obs(newReading, T("2026-09-07T14:57"), new DateOnly(2026, 9, 7), Items(("i1", "q1")))],
            new Dictionary<string, StoredDocument> { [oldReading.Hash] = stored },
            [], new HashSet<DateOnly>(), ImporterRole.Owner)
        {
            EarlierReadingDocuments = [oldReading, newReading]
        };

        var decisions = Engine.Decide(request).Decisions;

        Assert.Equal(2, decisions.Count);
        Assert.Equal(DocumentDecision.New, Assert.Single(decisions, d => d.Key == newReading).Decision);
        var retire = Assert.Single(decisions, d => d.Key == oldReading);
        Assert.Equal(DocumentDecision.Reinterpreted, retire.Decision);
        Assert.Equal(new ChangeProposal(ChangeMode.Review, ChangeReason.Reinterpretation, ChangeAction.Retire, null), retire.Change);
        Assert.Null(retire.BlockNo);
    }

    [Fact]
    public void A_reinterpreted_document_gets_one_retire_item_even_when_also_missing()
    {
        var key = SalesKey();
        var stored = Stored(key, Day, Version(T(StoredTime), Rows("p1")));
        var request = Request(Sales, [], [stored], blocks: [Incoming(T("2026-09-29T14:49"), Day, Day.AddDays(27))]) with
        {
            EarlierReadingDocuments = [key]
        };

        var retire = Assert.Single(Engine.Decide(request).Decisions);

        Assert.Equal(DocumentDecision.Reinterpreted, retire.Decision);
    }

    // ------------------------------------------------------------------ request checks

    [Fact]
    public void A_document_has_one_authoritative_observation_of_its_own_store_and_report()
    {
        var key = SalesKey();
        var o = Obs(key, T(StoredTime), Day, Rows("p1"));

        Assert.Throws<ArgumentException>(() => Engine.Decide(Request(Sales, [o, o with { BlockNo = 2 }], [])));
        Assert.Throws<ArgumentException>(() => Engine.Decide(Request(Sales, [o], [], report: "R022")));
        Assert.Throws<ArgumentException>(() => Engine.Decide(Request(Sales, [o], []) with { StoreCode = "HEMW" }));
        Assert.Single(Engine.Decide(Request(Sales, [o], []) with { StoreCode = " wlmhw ", ReportCode = "r025" }).Decisions);
    }

    // ------------------------------------------------------------------ 8.7 order independence

    public static TheoryData<string, ImporterRole, int> ConvergenceCases()
    {
        var data = new TheoryData<string, ImporterRole, int>();
        foreach (var family in new[] { "sales", "landing", "stock" })
        foreach (var role in new[] { ImporterRole.Owner, ImporterRole.Automation })
        foreach (var seed in new[] { 11, 23, 37, 41, 59 })
            data.Add(family, role, seed);
        return data;
    }

    /// <summary>
    /// Spec 8.7: with known export times, once the Owner has answered every item, each document holds the
    /// observation of the latest export that contains it, or is absent when the latest export that covers its
    /// finished day lacks it; whatever the import order, and with exports imported again.
    /// </summary>
    [Theory]
    [MemberData(nameof(ConvergenceCases))]
    public void Permuting_the_import_order_gives_the_same_final_state(string family, ImporterRole role, int seed)
    {
        var random = new Random(seed);
        var (identity, report) = family switch
        {
            "sales" => (Sales, "R025"),
            "landing" => (LandingDay, "R001"),
            _ => (ClosingStock, "CLOSING_STOCK")
        };
        var exports = RandomExports(random, identity, report);
        var expected = Expected(exports);

        for (var permutation = 0; permutation < 40; permutation++)
        {
            var order = exports.OrderBy(_ => random.Next()).ToList();
            // Some exports are imported twice: re-processing must not change the result.
            order.AddRange(exports.Where(_ => random.Next(3) == 0).OrderBy(_ => random.Next()));
            var ledger = new Ledger(identity, report, role);
            foreach (var export in order) ledger.Import(export);

            Assert.Equal(expected, ledger.FinalState());
        }
    }

    private sealed record Export(int Id, ExportTime Time, DateOnly CoverageFrom, DateOnly CoverageTo, IReadOnlyList<DocumentObservation> Documents);

    private static List<Export> RandomExports(Random random, EtpFamilyIdentity identity, string report)
    {
        var days = Enumerable.Range(0, 4).Select(offset => Day.AddDays(offset)).ToArray();
        var keys = days.SelectMany(day => (identity.Scope switch
        {
            DocumentScope.Document => new[] { DocumentKey.ForDocument(report, Store, day, $"1000{day.Day}1"), DocumentKey.ForDocument(report, Store, day, $"1000{day.Day}2") },
            DocumentScope.Date => new[] { DocumentKey.ForDate(report, Store, day) },
            _ => new[] { DocumentKey.ForSnapshot(report, Store, day) }
        }).Select(key => (Key: key, Day: day))).ToArray();
        FactRow[][] variants = identity.ChangePolicy == ChangePolicy.LatestReadingWins
            ? [Items(("k1", "q1"), ("k2", "q1")), Items(("k1", "q2"), ("k2", "q1")), Items(("k1", "q1")), Items(("k1", "q1"), ("k2", "q1"), ("k3", "q1"))]
            : [Rows("p1", "p2"), Rows("p1", "p3"), Rows("p1", "p2", "p4"), Rows("p1", "p2", "p2")];

        // Distinct minutes from the first business day to well after the last; some on a document's own day.
        var minutes = Enumerable.Range(0, 6).Select(_ => random.Next(10 * 60, 9 * 24 * 60)).Distinct().ToArray();
        return minutes.Select((minute, index) =>
        {
            var time = ExportTime.AtMinute(Day.ToDateTime(TimeOnly.MinValue).AddMinutes(minute));
            var documents = keys
                .Where(doc => doc.Day <= time.ExportDate && random.Next(5) != 0)
                .Select(doc =>
                {
                    var attribute = random.Next(2) == 0 ? "a1" : "a2";
                    var rows = variants[random.Next(variants.Length)].Select(row => row with
                    {
                        Canonical = row.Canonical with { AttributeHash = attribute }
                    }).ToArray();
                    return Obs(doc.Key, time, doc.Day, rows);
                })
                .ToList();
            return new Export(index, time, days[0], days[^1], documents);
        }).ToList();
    }

    private static IReadOnlyDictionary<string, string> Expected(IReadOnlyList<Export> exports)
    {
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var keys = exports.SelectMany(export => export.Documents).Select(o => (o.Key, Date: o.DocumentDate!.Value)).Distinct();
        foreach (var (key, date) in keys)
        {
            // The latest export that either holds the document or covers its finished day.
            var latest = exports
                .Where(export => export.Documents.Any(o => o.Key == key)
                    || export.CoverageFrom <= date && date <= export.CoverageTo && date < export.Time.ExportDate)
                .MaxBy(export => export.Time.Instant)!;
            var o = latest.Documents.SingleOrDefault(o => o.Key == key);
            expected[key.Hash] = o is null ? "absent" : State(o.FactSha256, o.AttributeSha256, latest.Time, o.ExportTime.ExportDate <= date);
        }
        return expected;
    }

    private static string State(string? facts, string? attributes, ExportTime attested, bool provisional) =>
        $"{facts}|{attributes}|{attested}|{(provisional ? "provisional" : "settled")}";

    /// <summary>
    /// Applies decisions as the import and the Owner would: AUTO changes at once; review items answered at once,
    /// approving every proposed version and every retire item, and keeping NOT_ADDED documents out.
    /// </summary>
    private sealed class Ledger(EtpFamilyIdentity identity, string report, ImporterRole role)
    {
        private readonly Dictionary<string, StoredDocument> _documents = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DocumentObservation> _proposals = new(StringComparer.Ordinal);
        private readonly List<CoverageBlock> _blocks = [];
        private long _nextId = 1;

        public void Import(Export export)
        {
            var block = new CoverageBlock(null, 1, export.Time, BlockCompleteness.Complete, export.CoverageFrom, export.CoverageTo,
                export.Documents.Select(o => o.Key.Hash).ToHashSet(StringComparer.Ordinal));
            var request = new DecisionRequest(Store, report, identity, export.Documents,
                new Dictionary<string, StoredDocument>(_documents), [.. _blocks, block], new HashSet<DateOnly>(), role);
            var observations = export.Documents.ToDictionary(o => o.Key.Hash);
            foreach (var decision in Engine.Decide(request).Decisions)
            {
                observations.TryGetValue(decision.Key.Hash, out var o);
                _documents.TryGetValue(decision.Key.Hash, out var stored);
                Apply(decision, o, stored);
            }
            _blocks.Add(block with { ImportFileId = _nextId++ });
        }

        private void Apply(DocumentDecisionResult decision, DocumentObservation? o, StoredDocument? stored)
        {
            switch (decision.Decision)
            {
                case DocumentDecision.New:
                case DocumentDecision.Grown:
                case DocumentDecision.ProvisionalUpdated:
                case DocumentDecision.ReadingUpdated:
                case DocumentDecision.PendingChange:   // the Owner approves the proposed version
                    Replace(o!, decision.NewVersionProvisional);
                    break;
                case DocumentDecision.Present:
                case DocumentDecision.Refreshed:
                case DocumentDecision.AttributeUpdated:
                    var cur = stored!.Current!;
                    var attested = ExportOrder.IsNewer(o!.ExportTime, cur.LastAttested) || !cur.LastAttested.IsKnown ? o.ExportTime : cur.LastAttested;
                    var version = cur with
                    {
                        LastAttested = attested,
                        Provisional = decision.Decision == DocumentDecision.AttributeUpdated ? decision.NewVersionProvisional : cur.Provisional && !decision.SettleProvisional,
                        AttributeSha256 = decision.Decision == DocumentDecision.AttributeUpdated ? o.AttributeSha256 : cur.AttributeSha256
                    };
                    _documents[o.Key.Hash] = stored with { Current = version, RowsPointerTime = decision.MoveRowsPointer ? o.ExportTime : stored.RowsPointerTime };
                    break;
                case DocumentDecision.NotAdded:        // "Keep current": the document stays out (a retired one stays retired)
                    if (stored?.Status != DocumentStatus.Retired)
                        _documents[o!.Key.Hash] = new StoredDocument(o.Key, _nextId++, DocumentStatus.NotAdded, o.DocumentDate, null);
                    break;
                case DocumentDecision.MissingFromLater: // approved: retired, its last version kept for rule 6
                    _documents[decision.Key.Hash] = stored! with { Status = DocumentStatus.Retired };
                    break;
                case DocumentDecision.Stale:
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected decision {decision.Decision} for {decision.Key}.");
            }
        }

        private void Replace(DocumentObservation o, bool provisional)
        {
            var version = new StoredVersion(_nextId++, 1, VersionBasis.SourceRows, o.FactSha256, null, o.AttributeSha256, o.RowCount,
                o.ExportTime, provisional) { ExportTime = o.ExportTime, Rows = o.Rows };
            _documents[o.Key.Hash] = new StoredDocument(o.Key, _nextId++, DocumentStatus.Current, o.DocumentDate, version)
            {
                RowsPointerTime = o.ExportTime
            };
            _proposals[o.Key.Hash] = o;
        }

        public IReadOnlyDictionary<string, string> FinalState()
        {
            var state = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (hash, document) in _documents)
                state[hash] = document is { Status: DocumentStatus.Current, Current: { } v }
                    ? State(v.FactSha256, v.AttributeSha256, v.LastAttested, v.Provisional)
                    : "absent";
            return state;
        }
    }

    // ------------------------------------------------------------------ builders

    private static DocumentDecisionResult Single(DecisionRequest request) => Assert.Single(Engine.Decide(request).Decisions);

    private static DecisionRequest Request(
        EtpFamilyIdentity identity, IReadOnlyList<DocumentObservation> incoming, IReadOnlyList<StoredDocument> stored,
        IReadOnlyList<CoverageBlock>? blocks = null, IReadOnlyList<DateOnly>? locked = null, ImporterRole role = ImporterRole.Owner,
        string? report = null) =>
        new(Store, report ?? incoming.Concat<object>(stored).Select(item => item switch
            {
                DocumentObservation o => o.Key.ReportCode,
                StoredDocument s => s.Key.ReportCode,
                _ => null
            }).FirstOrDefault() ?? "R025",
            identity, incoming, stored.ToDictionary(document => document.Key.Hash), blocks ?? [], (locked ?? []).ToHashSet(), role);

    private static DocumentKey SalesKey(string number = "100000068") => DocumentKey.ForDocument("R025", Store, Day, number);
    private static DocumentKey DayKey() => DocumentKey.ForDate("R001", Store, Day);
    private static DocumentKey SnapshotKey() => DocumentKey.ForSnapshot("CLOSING_STOCK", Store, Day);

    private static ExportTime T(string text) =>
        text.Length == 0 ? ExportTime.Unknown
        : ExportTime.TryParseContract(text, out var time) ? time : throw new ArgumentException(text, nameof(text));

    private static FactRow Row(string fact, string attribute = "a1", string? rowKey = null, IReadOnlyDictionary<string, string>? facts = null) =>
        new(new RowLocator(1, "Sheet1", 2),
            new CanonicalRow(fact, attribute, "d", fact, facts ?? new Dictionary<string, string> { ["product_code"] = fact }, new Dictionary<string, string>()))
        {
            RowKey = rowKey
        };

    private static FactRow[] Rows(params string[] facts) => facts.Select(fact => Row(fact)).ToArray();

    /// <summary>Snapshot items: a row key and a quantity; the fact hash covers both.</summary>
    private static FactRow[] Items(params (string Key, string Quantity)[] items) =>
        items.Select(item => Row($"{item.Key}:{item.Quantity}", rowKey: item.Key)).ToArray();

    /// <summary>A sales row with the v0 nullable fields (spec 7.3 tax note).</summary>
    private static FactRow Filled(string product, string tax, string? gross = null, string net = "500")
    {
        gross ??= tax;
        var facts = new Dictionary<string, string>
        {
            ["product_code"] = product,
            ["source_net_amount"] = net,
            ["source_tax_amount"] = tax,
            ["source_gross_amount"] = gross
        };
        return Row(Hash(string.Join('|', facts.OrderBy(pair => pair.Key, StringComparer.Ordinal))), facts: facts);
    }

    private static DocumentObservation Obs(DocumentKey key, ExportTime time, DateOnly date, IReadOnlyList<FactRow> rows) =>
        new(key, 1, time, date, rows, Multiset(rows.Select(row => row.Canonical.FactRowHash)),
            Multiset(rows.Select(row => row.Canonical.AttributeHash)));

    private static StoredVersion Version(ExportTime attested, IReadOnlyList<FactRow> rows, bool provisional = false,
        VersionBasis basis = VersionBasis.SourceRows) =>
        new(1, 1, basis, Multiset(rows.Select(row => row.Canonical.FactRowHash)), null,
            Multiset(rows.Select(row => row.Canonical.AttributeHash)), rows.Count, attested, provisional)
        {
            ExportTime = attested,
            Rows = rows
        };

    private static StoredDocument Stored(DocumentKey key, DateOnly date, StoredVersion version) =>
        new(key, 1, DocumentStatus.Current, date, version);

    private static CoverageBlock Incoming(ExportTime time, DateOnly from, DateOnly to, params DocumentKey[] observed) =>
        new(null, 1, time, BlockCompleteness.Complete, from, to, observed.Select(key => key.Hash).ToHashSet(StringComparer.Ordinal));

    private static CoverageBlock Registered(ExportTime time, DateOnly from, DateOnly to, params DocumentKey[] observed) =>
        Incoming(time, from, to, observed) with { ImportFileId = 41 };

    private static string Multiset(IEnumerable<string> hashes) =>
        Hash(string.Join('\n', hashes.Order(StringComparer.Ordinal)));

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
