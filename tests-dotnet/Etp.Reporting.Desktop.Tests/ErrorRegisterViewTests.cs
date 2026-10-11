using System.Threading;
using Etp.Reporting.Application.ErrorRegister;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// The 1.11.0 Error register screen against a fake IErrorRegisterStore (lane L8, design 9.2, decision 33): Owner only,
/// Find Ref, the status filter, the status actions with their prompts, merge and Copy summary. All values are synthetic:
/// event codes, source and site names of the product, Refs made of hex digits, tickets FT-xxx, no names or row data.
/// The fakes complete synchronously, so the view's awaits never leave the STA thread.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ErrorRegisterViewTests
{
    private const string KnownRef = "1A2B3C4D5E6F";

    // ---- roles ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_user_who_is_not_the_Owner_sees_nothing_and_every_action_is_refused()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            var prompts = new FakePrompts { ReasonAnswer = "Expected on the old PC", TicketAnswer = "FT-007" };
            var view = NewView(store, isOwner: () => false, prompts: prompts);

            view.LoadAsync().GetAwaiter().GetResult();
            Assert.Equal(ErrorRegisterView.OwnerOnlyText, view.StatusText);
            Assert.Empty(view.Rows);
            Assert.False(view.FindAsync(KnownRef).GetAwaiter().GetResult());
            Assert.False(view.MarkSeenAsync().GetAwaiter().GetResult());
            Assert.False(view.MarkExpectedAsync().GetAwaiter().GetResult());
            Assert.False(view.LinkTicketAsync().GetAwaiter().GetResult());
            Assert.False(view.MarkFixedAsync().GetAwaiter().GetResult());
            Assert.False(view.MergeAsync().GetAwaiter().GetResult());
            Assert.False(view.CopySummary());
            view.RefreshAsync().GetAwaiter().GetResult();

            Assert.Equal(0, store.LoadRegisterCalls);
            Assert.Empty(store.Changes);
            Assert.Equal(0, prompts.Asked);
            Assert.False(view.ActionsEnabled);
        });
    }

    // ---- the list ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_default_filter_shows_the_open_entries_and_All_shows_every_one()
    {
        RunSta(() =>
        {
            var view = NewView(new FakeStore());

            view.LoadAsync().GetAwaiter().GetResult();

            Assert.Equal("Open (New, Seen, Recurred)", view.SelectedFilter);
            Assert.Equal(["ER-0006", "ER-0001", "ER-0002"], view.Rows.Select(row => row.Id));
            Assert.Equal("3 of 5 entries shown.", view.StatusText);

            view.SelectedFilter = "All";
            Assert.Equal(5, view.Rows.Count);

            view.SelectedFilter = "Fixed";
            Assert.Equal(["ER-0003"], view.Rows.Select(row => row.Id));

            view.SelectedFilter = "Expected";
            Assert.Equal(["ER-0004"], view.Rows.Select(row => row.Id));
        });
    }

    [Fact]
    public void A_row_shows_the_status_with_the_fix_version_the_cause_the_count_and_the_versions_seen()
    {
        RunSta(() =>
        {
            var view = NewView(new FakeStore());
            view.LoadAsync().GetAwaiter().GetResult();
            view.SelectedFilter = "All";

            var fresh = view.Rows.Single(row => row.Id == "ER-0006");
            Assert.Equal("New", fresh.Status);
            Assert.Equal("STAFF_REPORT_FAILED", fresh.Event);
            Assert.Equal("Reports.Workspace / ReportsWorkspaceView.LoadStaffPerformanceAsync", fresh.Where);
            Assert.Equal("SqlException -2", fresh.Error);
            Assert.Equal("3", fresh.Count);
            Assert.Equal("1.11.0", fresh.Versions);
            Assert.Equal("-", fresh.Ticket);

            var fixedRow = view.Rows.Single(row => row.Id == "ER-0003");
            Assert.Equal("Fixed 1.9.7", fixedRow.Status);
            Assert.Equal("1,200", view.Rows.Single(row => row.Id == "ER-0001").Count);
            Assert.Equal("1.9.2 to 1.9.6", fixedRow.Versions);
            Assert.Equal("FT-003", fixedRow.Ticket);
            Assert.Equal("InvalidOperationException", fixedRow.Error);

            var recurred = view.Rows.Single(row => row.Id == "ER-0001");
            Assert.Equal("Recurred 1.9.8", recurred.Status);
            Assert.Equal("REPORT_STORE_REQUIRED / STORE_MISSING", recurred.Event);
        });
    }

    [Fact]
    public void An_empty_register_and_a_load_failure_say_what_to_do_and_a_failure_carries_a_Ref()
    {
        RunSta(() =>
        {
            var empty = NewView(new FakeStore { Entries = [] });
            empty.LoadAsync().GetAwaiter().GetResult();
            Assert.Contains("press Refresh", empty.StatusText, StringComparison.Ordinal);

            var broken = NewView(new FakeStore { FailLoad = true });
            broken.LoadAsync().GetAwaiter().GetResult();
            Assert.StartsWith("The error register could not be loaded.", broken.StatusText, StringComparison.Ordinal);
            Assert.DoesNotContain("private detail", broken.StatusText, StringComparison.Ordinal);
            Assert.Matches(@"Ref: [0-9A-Fa-f]{12}$", broken.StatusText);
        });
    }

    [Fact]
    public void Refresh_reads_this_accounts_log_first_and_a_failed_read_still_reloads_the_register()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            var ingested = 0;
            var view = new ErrorRegisterView(() => store, _ => { ingested++; return Task.FromResult<ErrorIngestResult?>(new ErrorIngestResult(4, 0, 0, 1, 0)); }, () => true, new FakePrompts(), _ => { });

            view.RefreshAsync().GetAwaiter().GetResult();

            Assert.Equal(1, ingested);
            Assert.StartsWith("4 new log line(s) added from this account's log.", view.StatusText, StringComparison.Ordinal);
            Assert.Equal(3, view.Rows.Count);

            var failing = new ErrorRegisterView(() => store, _ => throw new IOException("private detail"), () => true, new FakePrompts(), _ => { });
            failing.RefreshAsync().GetAwaiter().GetResult();
            Assert.StartsWith("This account's log could not be read.", failing.StatusText, StringComparison.Ordinal);
            Assert.Matches(@"Ref: [0-9A-Fa-f]{12} ", failing.StatusText);
            Assert.DoesNotContain("private detail", failing.StatusText, StringComparison.Ordinal);
            Assert.Equal(3, failing.Rows.Count);
        });
    }

    // ---- Find Ref ------------------------------------------------------------------------------------------------

    [Fact]
    public void Find_Ref_accepts_the_twelve_characters_with_or_without_the_Ref_label_in_any_case()
    {
        Assert.True(ErrorRegisterView.TryParseReference("1a2b3c4d5e6f", out var lower));
        Assert.Equal(KnownRef, lower);
        Assert.True(ErrorRegisterView.TryParseReference("  Ref: 1A2B3C4D5E6F  ", out var labelled));
        Assert.Equal(KnownRef, labelled);
        Assert.False(ErrorRegisterView.TryParseReference("1A2B3C4D5E6", out _));
        Assert.False(ErrorRegisterView.TryParseReference("1A2B3C4D5E6FF", out _));
        Assert.False(ErrorRegisterView.TryParseReference("1A2B3C4D5E6G", out _));
        Assert.False(ErrorRegisterView.TryParseReference("", out _));
        Assert.False(ErrorRegisterView.TryParseReference(null, out _));
        Assert.False(ErrorRegisterView.TryParseReference("%' OR 1=1 --", out _));
    }

    [Fact]
    public void Find_Ref_shows_the_entry_that_holds_the_occurrence_with_its_details()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            var view = NewView(store);
            view.LoadAsync().GetAwaiter().GetResult();

            Assert.True(view.FindAsync("Ref: 1a2b3c4d5e6f").GetAwaiter().GetResult());
            view.ShowSelectedDetailAsync().GetAwaiter().GetResult();

            Assert.Equal(KnownRef, store.LastReference);
            Assert.Equal(["ER-0006"], view.Rows.Select(row => row.Id));
            Assert.Equal("ER-0006", view.SelectedRow!.Id);
            Assert.Contains($"Ref {KnownRef} is in ER-0006", view.StatusText, StringComparison.Ordinal);
            Assert.Equal("Details (ER-0006)", view.DetailHeaderText);
            Assert.Contains("ReportsWorkspaceView.LoadStaffPerformanceAsync", view.DetailText, StringComparison.Ordinal);
            Assert.Equal(2, view.Occurrences.Count);
            Assert.Equal(KnownRef, ((ErrorOccurrenceRow)view.OccurrenceTable.SelectedItem).Ref);
        });
    }

    [Fact]
    public void Find_Ref_says_so_when_the_Ref_is_not_in_the_register_and_when_it_is_not_a_Ref()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            var view = NewView(store);
            view.LoadAsync().GetAwaiter().GetResult();
            var before = store.LoadRegisterCalls;

            Assert.False(view.FindAsync("FFFFFFFFFFFF").GetAwaiter().GetResult());
            Assert.Equal(ErrorRegisterView.RefNotFoundText, view.StatusText);
            Assert.Contains("press Refresh", view.StatusText, StringComparison.Ordinal);
            Assert.Equal(3, view.Rows.Count);

            Assert.False(view.FindAsync("not a ref").GetAwaiter().GetResult());
            Assert.Equal(ErrorRegisterView.BadRefText, view.StatusText);
            Assert.Equal(before + 1, store.LoadRegisterCalls);
        });
    }

    // ---- actions -------------------------------------------------------------------------------------------------

    [Fact]
    public void Mark_seen_sets_the_status_through_the_store_and_reloads_with_the_entry_still_selected()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            var view = NewView(store);
            view.LoadAsync().GetAwaiter().GetResult();
            view.Select(6);
            Assert.True(view.ActionsEnabled);

            Assert.True(view.MarkSeenAsync().GetAwaiter().GetResult());

            Assert.Equal([new Change(6, ErrorStatus.Seen, null, null, null)], store.Changes);
            Assert.Equal("ER-0006 marked seen.", view.StatusText);
            Assert.Equal("ER-0006", view.SelectedRow!.Id);
            Assert.Equal("Seen", view.SelectedRow.Status);
        });
    }

    [Fact]
    public void Actions_need_a_selected_entry()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            var view = NewView(store);
            view.LoadAsync().GetAwaiter().GetResult();

            Assert.False(view.ActionsEnabled);
            Assert.False(view.MarkSeenAsync().GetAwaiter().GetResult());
            Assert.Equal("Select an entry first.", view.StatusText);
            Assert.False(view.MarkFixedAsync().GetAwaiter().GetResult());
            Assert.False(view.CopySummary());
            Assert.Empty(store.Changes);
        });
    }

    [Fact]
    public void Mark_expected_needs_a_reason_in_plain_words()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            var prompts = new FakePrompts { ReasonAnswer = "Seen only on the retired PC" };
            var view = NewView(store, prompts: prompts);
            view.LoadAsync().GetAwaiter().GetResult();
            view.Select(2);

            Assert.True(view.MarkExpectedAsync().GetAwaiter().GetResult());
            Assert.Equal([new Change(2, ErrorStatus.Expected, null, null, "Seen only on the retired PC")], store.Changes);

            store.Changes.Clear();
            view.SelectedFilter = "All";
            view.Select(2);
            prompts.ReasonAnswer = "mail someone@example.invalid";
            Assert.False(view.MarkExpectedAsync().GetAwaiter().GetResult());
            prompts.ReasonAnswer = null;
            Assert.False(view.MarkExpectedAsync().GetAwaiter().GetResult());
            Assert.Empty(store.Changes);
        });
    }

    [Fact]
    public void Link_ticket_takes_an_FT_id_marks_the_entry_seen_and_keeps_the_id_in_capitals()
    {
        Assert.Null(ErrorRegisterView.ValidateTicket("FT-007"));
        Assert.Null(ErrorRegisterView.ValidateTicket(" FT-12345678 "));
        Assert.NotNull(ErrorRegisterView.ValidateTicket("FT-"));
        Assert.NotNull(ErrorRegisterView.ValidateTicket("ft-007"));
        Assert.NotNull(ErrorRegisterView.ValidateTicket("FT-123456789"));
        Assert.NotNull(ErrorRegisterView.ValidateTicket("FT-7; DROP TABLE x"));
        RunSta(() =>
        {
            var store = new FakeStore();
            var prompts = new FakePrompts { TicketAnswer = "FT-007" };
            var view = NewView(store, prompts: prompts);
            view.LoadAsync().GetAwaiter().GetResult();
            view.Select(6);

            Assert.True(view.LinkTicketAsync().GetAwaiter().GetResult());
            Assert.Equal([new Change(6, ErrorStatus.Seen, null, "FT-007", null)], store.Changes);

            store.Changes.Clear();
            prompts.TicketAnswer = "FT-";
            Assert.False(view.LinkTicketAsync().GetAwaiter().GetResult());
            Assert.Contains("FT-007", view.StatusText, StringComparison.Ordinal);
            Assert.Empty(store.Changes);
        });
    }

    [Fact]
    public void Mark_fixed_offers_the_versions_seen_and_sends_the_version_ticket_and_reason()
    {
        Assert.Null(ErrorRegisterView.ValidateVersion("1.11.0"));
        Assert.Null(ErrorRegisterView.ValidateVersion("1.9.7"));
        Assert.NotNull(ErrorRegisterView.ValidateVersion("1"));
        Assert.NotNull(ErrorRegisterView.ValidateVersion("1.9.x"));
        Assert.NotNull(ErrorRegisterView.ValidateVersion("1.2.3.4.5"));
        RunSta(() =>
        {
            var store = new FakeStore();
            var prompts = new FakePrompts { FixedAnswer = ("1.10.1", "FT-009", "Fixed by the retry in setup") };
            var view = NewView(store, prompts: prompts);
            view.LoadAsync().GetAwaiter().GetResult();
            view.Select(6);
            view.ShowSelectedDetailAsync().GetAwaiter().GetResult();

            Assert.True(view.MarkFixedAsync().GetAwaiter().GetResult());

            Assert.Contains("1.11.0", prompts.VersionsOffered);
            Assert.Equal([new Change(6, ErrorStatus.Fixed, "1.10.1", "FT-009", "Fixed by the retry in setup")], store.Changes);
            Assert.Equal("ER-0006 marked fixed in 1.10.1.", view.StatusText);

            store.Changes.Clear();
            view.SelectedFilter = "All";
            view.Select(6);
            prompts.FixedAnswer = ("1.10.1", null, "No ticket for a local fix");
            Assert.True(view.MarkFixedAsync().GetAwaiter().GetResult());
            Assert.Equal([new Change(6, ErrorStatus.Fixed, "1.10.1", null, "No ticket for a local fix")], store.Changes);

            store.Changes.Clear();
            prompts.FixedAnswer = ("one point ten", null, "x");
            Assert.False(view.MarkFixedAsync().GetAwaiter().GetResult());
            prompts.FixedAnswer = ("1.10.1", null, "");
            Assert.False(view.MarkFixedAsync().GetAwaiter().GetResult());
            Assert.Empty(store.Changes);
        });
    }

    [Fact]
    public void Merge_moves_one_entry_into_another_listed_entry_with_a_reason()
    {
        RunSta(() =>
        {
            var store = new FakeStore();
            var prompts = new FakePrompts { MergeAnswer = (2, "Same fault after a rename") };
            var view = NewView(store, prompts: prompts);
            view.LoadAsync().GetAwaiter().GetResult();
            view.Select(6);

            Assert.True(view.MergeAsync().GetAwaiter().GetResult());

            Assert.Equal([(6, 2, "Same fault after a rename")], store.Merges);
            Assert.DoesNotContain(prompts.MergeCandidates, entry => entry.ErrorRegisterId == 6);
            Assert.Equal("ER-0006 was merged into ER-0002.", view.StatusText);
            Assert.DoesNotContain(view.LoadedEntries, entry => entry.ErrorRegisterId == 6);
            Assert.Equal("ER-0002", view.SelectedRow!.Id);

            store.Merges.Clear();
            view.Select(1);
            prompts.MergeAnswer = (99, "Not in the list");
            Assert.False(view.MergeAsync().GetAwaiter().GetResult());
            prompts.MergeAnswer = (2, "");
            Assert.False(view.MergeAsync().GetAwaiter().GetResult());
            Assert.Empty(store.Merges);
        });
    }

    [Fact]
    public void A_store_failure_changes_nothing_on_screen_and_carries_a_Ref()
    {
        RunSta(() =>
        {
            var store = new FakeStore { FailChange = true };
            var view = NewView(store);
            view.LoadAsync().GetAwaiter().GetResult();
            view.Select(6);

            Assert.False(view.MarkSeenAsync().GetAwaiter().GetResult());

            Assert.StartsWith("The entry was not changed.", view.StatusText, StringComparison.Ordinal);
            Assert.Matches(@"Ref: [0-9A-Fa-f]{12}$", view.StatusText);
            Assert.Equal("New", view.SelectedRow!.Status);
        });
    }

    // ---- copy summary --------------------------------------------------------------------------------------------

    [Fact]
    public void Copy_summary_puts_the_privacy_safe_block_on_the_clipboard()
    {
        RunSta(() =>
        {
            var copied = new List<string>();
            var view = NewView(new FakeStore(), copy: copied.Add);
            view.LoadAsync().GetAwaiter().GetResult();
            view.Select(3);
            view.SelectedFilter = "All";
            view.Select(3);
            view.ShowSelectedDetailAsync().GetAwaiter().GetResult();

            Assert.True(view.CopySummary());

            var text = Assert.Single(copied);
            Assert.StartsWith("ER-0003  Fixed 1.9.7", text, StringComparison.Ordinal);
            Assert.Contains("Event REPORT_STORE_REQUIRED", text, StringComparison.Ordinal);
            Assert.Contains("Where Reports.Workspace", text, StringComparison.Ordinal);
            Assert.Contains("Error InvalidOperationException", text, StringComparison.Ordinal);
            Assert.Contains("Seen 21 times", text, StringComparison.Ordinal);
            Assert.Contains("1.9.2 (15)", text, StringComparison.Ordinal);
            Assert.Contains("1.9.6 (6)", text, StringComparison.Ordinal);
            Assert.Contains("Ticket FT-003", text, StringComparison.Ordinal);
            Assert.DoesNotContain("LoadStaffPerformanceAsync", text, StringComparison.Ordinal);
            Assert.DoesNotContain("@", text, StringComparison.Ordinal);
            Assert.Equal($"The summary of ER-0003 was copied. It holds no customer data.", view.StatusText);
        });
    }

    [Fact]
    public void Copy_summary_reports_a_busy_clipboard_instead_of_failing()
    {
        RunSta(() =>
        {
            var view = NewView(new FakeStore(), copy: _ => throw new System.Runtime.InteropServices.COMException("CLIPBRD_E_CANT_OPEN"));
            view.LoadAsync().GetAwaiter().GetResult();
            view.Select(6);
            view.ShowSelectedDetailAsync().GetAwaiter().GetResult();

            Assert.False(view.CopySummary());
            Assert.Equal("The clipboard is in use by another program. Try again.", view.StatusText);
        });
    }

    [Fact]
    public void A_version_older_than_the_fix_is_marked_not_upgraded()
    {
        var detail = FakeStore.DetailOf(FakeStore.Sample().Single(entry => entry.ErrorRegisterId == 3));
        var text = ErrorRegisterView.VersionsText(detail);
        Assert.Contains("1.9.2 (15) also seen on this version (not upgraded)", text, StringComparison.Ordinal);
        Assert.Contains("1.9.6 (6) also seen on this version (not upgraded)", text, StringComparison.Ordinal);

        var open = FakeStore.DetailOf(FakeStore.Sample().Single(entry => entry.ErrorRegisterId == 6));
        Assert.DoesNotContain("not upgraded", ErrorRegisterView.VersionsText(open), StringComparison.Ordinal);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private static ErrorRegisterView NewView(FakeStore store, Func<bool>? isOwner = null, FakePrompts? prompts = null, Action<string>? copy = null) =>
        new(() => store, null, isOwner ?? (() => true), prompts ?? new FakePrompts(), copy ?? (_ => { }));

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private sealed record Change(int Id, ErrorStatus Status, string? FixedInVersion, string? Ticket, string? Reason);

    private sealed class FakePrompts : IErrorRegisterPrompts
    {
        public string? ReasonAnswer { get; set; }
        public string? TicketAnswer { get; set; }
        public (string Version, string? Ticket, string Reason)? FixedAnswer { get; set; }
        public (int IntoId, string Reason)? MergeAnswer { get; set; }
        public IReadOnlyList<string> VersionsOffered { get; private set; } = [];
        public IReadOnlyList<ErrorRegisterEntry> MergeCandidates { get; private set; } = [];
        public int Asked { get; private set; }

        public string? Reason(string title, string intro) { Asked++; return ReasonAnswer; }
        public string? Ticket() { Asked++; return TicketAnswer; }
        public (string Version, string? Ticket, string Reason)? Fixed(IReadOnlyList<string> versionsSeen) { Asked++; VersionsOffered = versionsSeen; return FixedAnswer; }
        public (int IntoId, string Reason)? Merge(ErrorRegisterEntry from, IReadOnlyList<ErrorRegisterEntry> candidates) { Asked++; MergeCandidates = candidates; return MergeAnswer; }
    }

    private sealed class FakeStore : IErrorRegisterStore
    {
        public IReadOnlyList<ErrorRegisterEntry> Entries { get; set; } = Sample();
        public bool FailLoad { get; set; }
        public bool FailChange { get; set; }
        public int LoadRegisterCalls { get; private set; }
        public string? LastReference { get; private set; }
        public List<Change> Changes { get; } = [];
        public List<(int From, int Into, string Reason)> Merges { get; } = [];

        public static IReadOnlyList<ErrorRegisterEntry> Sample() =>
        [
            Entry(6, ErrorStatus.New, "STAFF_REPORT_FAILED", null, "Reports.Workspace", "ReportsWorkspaceView.LoadStaffPerformanceAsync", "Microsoft.Data.SqlClient.SqlException", -2, 3, "1.11.0", "1.11.0",
                lastSeen: new DateTimeOffset(2026, 10, 9, 14, 48, 0, TimeSpan.Zero)),
            Entry(1, ErrorStatus.Recurred, "REPORT_STORE_REQUIRED", "STORE_MISSING", "Reports.Workspace", null, "System.InvalidOperationException", 0, 1200, "1.9.2", "1.9.8",
                fixedIn: "1.9.7", recurredVersion: "1.9.8", lastSeen: new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero)),
            Entry(3, ErrorStatus.Fixed, "REPORT_STORE_REQUIRED", null, "Reports.Workspace", null, "System.InvalidOperationException", 0, 21, "1.9.2", "1.9.6",
                fixedIn: "1.9.7", ticket: "FT-003", lastSeen: new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero)),
            Entry(4, ErrorStatus.Expected, "IMPORT_RETRY", null, "Imports.Folder", null, "System.IO.IOException", 0, 2, "1.9.9", "1.9.9",
                lastSeen: new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero)),
            Entry(2, ErrorStatus.Seen, "EXPORT_FAILED", null, "Reports.Export", null, "System.IO.IOException", 0, 7, "1.9.9", "1.10.0",
                lastSeen: new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero))
        ];

        private static ErrorRegisterEntry Entry(int id, ErrorStatus status, string eventCode, string? subCode, string source, string? site, string exceptionType, int sqlNumber,
            long count, string firstVersion, string lastVersion, string? fixedIn = null, string? ticket = null, string? recurredVersion = null, DateTimeOffset? lastSeen = null) =>
            new(id, new string((char)('a' + id), 64), 1, eventCode, subCode, source, site ?? (id == 6 ? "ReportsWorkspaceView.LoadStaffPerformanceAsync" : null), exceptionType, sqlNumber, ErrorRegisterSeverity.Error, status,
                new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero), lastSeen ?? new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero), count, firstVersion, lastVersion,
                fixedIn, ticket, null, null, recurredVersion, ["Etp.Reporting.Desktop.Modules.Reports.ReportsWorkspaceView.LoadStaffPerformanceAsync", "Etp.Reporting.Infrastructure.SqlServer.SqlServerApplicationReportQuery.LoadAsync"],
                null, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), "SYNTHETIC-OWNER", null);

        public static ErrorRegisterEntryDetail DetailOf(ErrorRegisterEntry entry)
        {
            var when = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
            IReadOnlyList<ErrorRegisterVersionCount> versions = entry.ErrorRegisterId == 3
                ? [new("1.9.6", 6, when, when), new("1.9.2", 15, when, when)]
                : [new(entry.LastVersion, entry.OccurrenceCount, when, when)];
            IReadOnlyList<ErrorOccurrenceRecord> occurrences =
            [
                new(2, when, KnownRef, entry.LastVersion, ErrorRegisterSeverity.Error, "staff-performance", "TST01", new DateOnly(2026, 10, 8), null, "LoadStaffPerformance", "Owner", when),
                new(1, when.AddMinutes(-20), "0A1B2C3D4E5F", entry.LastVersion, ErrorRegisterSeverity.Error, null, null, null, null, null, "StoreManager", when)
            ];
            return new ErrorRegisterEntryDetail(entry, versions, occurrences);
        }

        public Task<ErrorIngestResult> IngestAsync(IReadOnlyList<ErrorOccurrence> items, string appVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ErrorRegisterEntry>> LoadRegisterAsync(ErrorStatus? status = null, string? reference = null, CancellationToken cancellationToken = default)
        {
            LoadRegisterCalls++;
            LastReference = reference;
            if (FailLoad) return Task.FromException<IReadOnlyList<ErrorRegisterEntry>>(new InvalidOperationException("private detail"));
            IReadOnlyList<ErrorRegisterEntry> list = Entries
                .Where(entry => status is null || entry.Status == status)
                .Where(entry => reference is null || (entry.ErrorRegisterId == 6 && reference == KnownRef))
                .OrderByDescending(entry => entry.LastSeenUtc)
                .ToArray();
            return Task.FromResult(list);
        }

        public Task<ErrorRegisterEntryDetail?> LoadEntryAsync(int errorRegisterId, CancellationToken cancellationToken = default)
        {
            var entry = Entries.FirstOrDefault(item => item.ErrorRegisterId == errorRegisterId);
            return Task.FromResult(entry is null ? null : DetailOf(entry));
        }

        public Task SetStatusAsync(int errorRegisterId, ErrorStatus status, string? fixedInVersion, string? ticket, string? reason, CancellationToken cancellationToken = default)
        {
            if (FailChange) return Task.FromException(new InvalidOperationException("private detail"));
            Changes.Add(new Change(errorRegisterId, status, fixedInVersion, ticket, reason));
            Entries = Entries.Select(entry => entry.ErrorRegisterId != errorRegisterId ? entry
                : entry with { Status = status, FixedInVersion = fixedInVersion ?? entry.FixedInVersion, FixedTicket = ticket ?? entry.FixedTicket, StatusReason = reason ?? entry.StatusReason }).ToArray();
            return Task.CompletedTask;
        }

        public Task MergeAsync(int fromId, int intoId, string reason, CancellationToken cancellationToken = default)
        {
            Merges.Add((fromId, intoId, reason));
            Entries = Entries.Where(entry => entry.ErrorRegisterId != fromId).ToArray();
            return Task.CompletedTask;
        }

        public Task<ErrorFixManifestResult> ApplyFixManifestAsync(string manifestJson, string runningVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
