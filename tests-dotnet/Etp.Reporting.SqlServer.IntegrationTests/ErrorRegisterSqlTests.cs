using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.11.0, migration 0054 (lane L1): the error register procedures in schema diag. Grants per role with
/// EXECUTE AS USER, the Owner-only checks, token validation (bad items rejected, malformed batches refused),
/// duplicates, recurrence by version, the fix manifest, merge, the spam cap and occurrence pruning.
/// All data is synthetic: fingerprints are hashes of random GUIDs, Refs and codes are made up.
/// </summary>
public sealed class ErrorRegisterSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string Owner = "diag_er_owner";
    private const string Manager = "diag_er_manager";
    private const string Viewer = "diag_er_viewer";
    private const string Automation = "diag_er_automation";

    [SqlFact]
    public async Task Every_role_can_ingest_and_occurrences_store_the_role_never_the_account()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var fingerprint = DiagSql.NewFingerprint();
        var time = DateTime.UtcNow.AddMinutes(-30);
        var users = new[] { (Owner, "Owner"), (Manager, "StoreManager"), (Viewer, "Viewer"), (Automation, "Automation") };
        for (var i = 0; i < users.Length; i++)
        {
            var result = await IngestAsync(users[i].Item1, [Item(fingerprint, time.AddSeconds(i))]);
            Assert.Equal(1, result["accepted"]);
            Assert.Equal(i == 0 ? 1 : 0, result["new_entries"]);
        }

        var roles = await DiagSql.QueryAsync(database, null,
            "SELECT o.reporter_role FROM diag.error_occurrences o JOIN diag.error_register e ON e.error_register_id=o.error_register_id WHERE e.fingerprint=@f ORDER BY o.occurred_utc",
            ("@f", fingerprint));
        Assert.Equal(users.Select(user => user.Item2), roles[0].Select(row => (string)row["reporter_role"]!));
        Assert.Equal(4L, await ScalarAsync("SELECT occurrence_count FROM diag.error_register WHERE fingerprint=@f", ("@f", fingerprint)));
        Assert.Equal("New", await ScalarAsync("SELECT status FROM diag.error_register WHERE fingerprint=@f", ("@f", fingerprint)));
    }

    [SqlFact]
    public async Task Only_owner_and_automation_read_the_register_and_nobody_reads_the_tables()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var fingerprint = DiagSql.NewFingerprint();
        await IngestAsync(Owner, [Item(fingerprint, DateTime.UtcNow.AddMinutes(-5))]);

        foreach (var user in new[] { Owner, Automation })
            Assert.Contains((await DiagSql.QueryAsync(database, user, "EXEC diag.load_error_register"))[0], row => (string)row["fingerprint"]! == fingerprint);

        foreach (var user in new[] { Manager, Viewer })
        {
            var denied = await Assert.ThrowsAsync<SqlException>(() => DiagSql.QueryAsync(database, user, "EXEC diag.load_error_register"));
            Assert.Equal(229, denied.Number);
        }
        foreach (var user in new[] { Manager, Viewer, Automation })
        {
            foreach (var sql in new[]
                     {
                         "SELECT COUNT(*) FROM diag.error_register",
                         "SELECT COUNT(*) FROM diag.error_occurrences",
                         "SELECT COUNT(*) FROM diag.error_register_versions",
                         "UPDATE diag.error_register SET status='Fixed'",
                         "EXEC diag.load_error_entry 1",
                         "EXEC diag.set_error_status 1,'Seen'",
                         "EXEC diag.merge_error_entries 1,2,N'Reason'",
                     })
            {
                var denied = await Assert.ThrowsAsync<SqlException>(() => DiagSql.QueryAsync(database, user, sql));
                Assert.Equal(229, denied.Number);
            }
        }
        foreach (var user in new[] { Manager, Viewer })
        {
            var denied = await Assert.ThrowsAsync<SqlException>(() => DiagSql.QueryAsync(database, user,
                "EXEC diag.apply_error_fix_manifest N'{\"manifest\":1,\"fixes\":[]}','1.11.0'"));
            Assert.Equal(229, denied.Number);
        }

        // Even an explicit EXECUTE grant does not get past the Owner checks.
        await database.ExecuteAsync($"GRANT EXECUTE ON diag.load_error_entry TO {Manager}; GRANT EXECUTE ON diag.set_error_status TO {Manager}; GRANT EXECUTE ON diag.merge_error_entries TO {Manager};");
        try
        {
            foreach (var sql in new[] { "EXEC diag.load_error_entry 1", "EXEC diag.set_error_status 1,'Seen'", "EXEC diag.merge_error_entries 1,2,N'Reason'" })
            {
                var refused = await Assert.ThrowsAsync<SqlException>(() => DiagSql.QueryAsync(database, Manager, sql));
                Assert.Equal(51801, refused.Number);
            }
        }
        finally
        {
            await database.ExecuteAsync($"REVOKE EXECUTE ON diag.load_error_entry FROM {Manager}; REVOKE EXECUTE ON diag.set_error_status FROM {Manager}; REVOKE EXECUTE ON diag.merge_error_entries FROM {Manager};");
        }
    }

    [SqlFact]
    public async Task A_malformed_batch_is_refused_and_bad_items_are_rejected_one_by_one()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        foreach (var (items, version) in new[]
                 {
                     ("not json", "1.11.0.0"),
                     ("{\"fingerprint\":\"x\"}", "1.11.0.0"),
                     ("[]", "1.11.0.0.1"),
                     ("[]", "1.x"),
                     ("[]", ""),
                     (JsonSerializer.Serialize(Enumerable.Range(0, 501).Select(i => Item(DiagSql.NewFingerprint(), DateTime.UtcNow.AddSeconds(-i)))), "1.11.0.0"),
                 })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => DiagSql.QueryAsync(database, Manager,
                "EXEC diag.ingest_error_occurrences @items,@v", ("@items", items), ("@v", version)));
            Assert.Equal(51800, refused.Number);
        }

        var time = DateTime.UtcNow.AddMinutes(-20);
        var good = DiagSql.NewFingerprint();
        var bad = new object[]
        {
            "a string, not an object",
            Item(good.ToUpperInvariant(), time),                               // fingerprint must be lowercase hex
            Item(good[..63], time),                                            // and 64 characters
            Item(good, time, eventBase: "report_store_required"),              // event codes are A-Z 0-9 _
            Item(good, time, site: "Some Site With Spaces"),                   // not a token
            Item(good, time, reference: "AB@CD"),                              // not a token (gate 2: passed is_token, then aborted the batch on the CHECK)
            Item(good, time, reference: "ABCDEFABCDEF0"),                      // longer than 12
            Item(good, time, severity: "Warning"),                             // warnings are never registered (Q7)
            Item(good, time, applicationVersion: "1.11"),                      // valid version, kept below
            Item(good, time, applicationVersion: "version-one"),
            Item(good, time, timestamp: "yesterday"),
            Item(good, time, timestamp: ""),
            Item(good, time, operation: "C:\\Users\\someone\\file.xlsx"),
            Item(good, time, storeCode: "Store 1"),
            Item(good, time, businessDate: "09/10/2026"),
            Item(good, DateTime.UtcNow.AddDays(3)),                            // more than a day in the future
        };
        var result = await IngestAsync(Manager, bad);
        Assert.Equal(1, result["accepted"]);       // the 1.11 version item
        Assert.Equal(bad.Length - 1, result["rejected"]);
        Assert.Equal(0, result["duplicates"]);
        Assert.Equal(1L, await ScalarAsync("SELECT occurrence_count FROM diag.error_register WHERE fingerprint=@f", ("@f", good)));
        Assert.Equal("1.11", await ScalarAsync("SELECT first_version FROM diag.error_register WHERE fingerprint=@f", ("@f", good)));
    }

    [SqlFact]
    public async Task Duplicates_are_ignored_in_the_batch_and_across_batches()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var fingerprint = DiagSql.NewFingerprint();
        var time = DateTime.UtcNow.AddHours(-2);
        var first = Item(fingerprint, time, reference: "DUP000000001");
        var second = Item(fingerprint, time.AddSeconds(1), reference: "DUP000000002");

        var result = await IngestAsync(Owner, [first, second, first]);
        Assert.Equal((2, 1, 0, 1), (N(result["accepted"]), N(result["duplicates"]), N(result["rejected"]), N(result["new_entries"])));

        // A lost cursor sends the same lines again.
        result = await IngestAsync(Viewer, [first, second]);
        Assert.Equal((0, 2, 0), (N(result["accepted"]), N(result["duplicates"]), N(result["new_entries"])));
        Assert.Equal(2L, await ScalarAsync("SELECT occurrence_count FROM diag.error_register WHERE fingerprint=@f", ("@f", fingerprint)));

        // Find Ref is not case sensitive and finds the entry holding that Ref.
        var found = await DiagSql.QueryAsync(database, Owner, "EXEC diag.load_error_register @reference=@r", ("@r", "dup000000002"));
        Assert.Equal(fingerprint, Assert.Single(found[0])["fingerprint"]);
        Assert.Empty((await DiagSql.QueryAsync(database, Owner, "EXEC diag.load_error_register @reference=@r", ("@r", "NOSUCHREF000")))[0]);
    }

    [SqlFact]
    public async Task Latest_stack_keeps_token_frames_and_is_not_blanked_by_an_occurrence_without_a_stack()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var fingerprint = DiagSql.NewFingerprint();
        var time = DateTime.UtcNow.AddHours(-3);
        await IngestAsync(Owner, [Item(fingerprint, time, stack: ["SqlServerApplicationReportQuery.LoadStaffPerformanceAsync", "C:\\src\\File.cs line 12", "ReportsWorkspaceView.RunAsync"])]);
        const string read = "SELECT latest_stack FROM diag.error_register WHERE fingerprint=@f";
        Assert.Equal("[\"SqlServerApplicationReportQuery.LoadStaffPerformanceAsync\",\"ReportsWorkspaceView.RunAsync\"]", await ScalarAsync(read, ("@f", fingerprint)));

        // L2 (111-L2-NOTE.md): a later occurrence with no stack, or an empty one, keeps the previous stack.
        await IngestAsync(Owner, [Item(fingerprint, time.AddMinutes(1), stack: null), Item(fingerprint, time.AddMinutes(2), stack: [])]);
        Assert.Equal("[\"SqlServerApplicationReportQuery.LoadStaffPerformanceAsync\",\"ReportsWorkspaceView.RunAsync\"]", await ScalarAsync(read, ("@f", fingerprint)));

        // A newer occurrence with a stack replaces it; an older one does not.
        await IngestAsync(Owner, [Item(fingerprint, time.AddMinutes(3), stack: ["MainWindow.StartAsync"])]);
        await IngestAsync(Owner, [Item(fingerprint, time.AddMinutes(-10), stack: ["OlderType.OlderMethod"])]);
        Assert.Equal("[\"MainWindow.StartAsync\"]", await ScalarAsync(read, ("@f", fingerprint)));

        // At most 3400 characters: whole frames only, still valid JSON.
        var frames = Enumerable.Range(0, 40).Select(i => "Type" + i.ToString("00", CultureInfo.InvariantCulture) + "." + new string('M', 150)).ToArray();
        await IngestAsync(Owner, [Item(fingerprint, time.AddMinutes(4), stack: frames)]);
        var stack = (string)(await ScalarAsync(read, ("@f", fingerprint)))!;
        Assert.InRange(stack.Length, 3000, 3400);
        Assert.StartsWith("\"Type00.", JsonDocument.Parse(stack).RootElement[0].GetRawText(), StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task A_fix_turns_into_recurred_only_for_an_occurrence_from_the_fixed_version_or_later()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var fingerprint = DiagSql.NewFingerprint();
        var time = DateTime.UtcNow.AddHours(-6);
        await IngestAsync(Owner, [Item(fingerprint, time, applicationVersion: "1.11.0.0")]);
        var manifest = Manifest((fingerprint, "1.11.1", "FT-007"), (DiagSql.NewFingerprint(), "1.11.1", "FT-008"));

        // An older PC never marks a fix it does not have.
        var applied = await ApplyAsync(Automation, manifest, "1.11.0.0");
        Assert.Equal((2, 0, 0, 0, 2), Counts(applied));
        Assert.Equal("New", await StatusAsync(fingerprint));

        applied = await ApplyAsync(Automation, manifest, "1.11.1.0");
        Assert.Equal((2, 1, 0, 1, 0), Counts(applied));
        Assert.Equal("Fixed", await StatusAsync(fingerprint));
        Assert.Equal("FT-007", await ScalarAsync("SELECT fixed_ticket FROM diag.error_register WHERE fingerprint=@f", ("@f", fingerprint)));
        Assert.Equal((2, 0, 1, 1, 0), Counts(await ApplyAsync(Owner, manifest, "1.11.1.0")));   // idempotent

        // A PC not yet upgraded: counted, still Fixed.
        var result = await IngestAsync(Manager, [Item(fingerprint, time.AddHours(1), applicationVersion: "1.11.0.5")]);
        Assert.Equal(0, result["recurred_entries"]);
        Assert.Equal("Fixed", await StatusAsync(fingerprint));

        // The fixed version itself: Recurred.
        result = await IngestAsync(Manager, [Item(fingerprint, time.AddHours(2), applicationVersion: "1.11.1.0"), Item(fingerprint, time.AddHours(3), applicationVersion: "1.11.1.0")]);
        Assert.Equal(1, result["recurred_entries"]);
        Assert.Equal("Recurred", await StatusAsync(fingerprint));
        Assert.Equal("1.11.1.0", await ScalarAsync("SELECT recurred_version FROM diag.error_register WHERE fingerprint=@f", ("@f", fingerprint)));
        Assert.Equal((2, 0, 1, 1, 0), Counts(await ApplyAsync(Owner, manifest, "1.11.1.0")));   // the same fix again does not hide it

        // A later fix closes it again; versions are kept per version.
        Assert.Equal((1, 1, 0, 0, 0), Counts(await ApplyAsync(Owner, Manifest((fingerprint, "1.11.2", "FT-009")), "1.11.2.0")));
        Assert.Equal("Fixed", await StatusAsync(fingerprint));
        var entryId = Convert.ToInt32(await ScalarAsync("SELECT error_register_id FROM diag.error_register WHERE fingerprint=@f", ("@f", fingerprint)), CultureInfo.InvariantCulture);
        var detail = await DiagSql.QueryAsync(database, Owner, "EXEC diag.load_error_entry @id", ("@id", entryId));
        Assert.Equal(3, detail.Count);
        Assert.Equal(["1.11.1.0", "1.11.0.5", "1.11.0.0"], detail[1].Select(row => (string)row["app_version"]!));
        Assert.Equal(2L, detail[1][0]["occurrence_count"]);
        Assert.Equal(4, detail[2].Count);
        Assert.Equal(new[] { "error_occurrence_id", "occurred_utc", "reference", "app_version", "severity", "report_code", "store_code", "business_date", "date_from", "operation", "reporter_role", "ingested_utc" }, detail[2][0].Keys);

        // A fix manifest line that is not well formed refuses the whole manifest.
        foreach (var bad in new[] { "{\"manifest\":2,\"fixes\":[]}", "{\"manifest\":1}", "[1]", "{\"manifest\":1,\"fixes\":[{\"fingerprint\":\"abc\",\"fixedIn\":\"1.11.1\"}]}",
                     "{\"manifest\":1,\"fixes\":[{\"fingerprint\":\"" + fingerprint + "\",\"fixedIn\":\"soon\"}]}" })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => ApplyAsync(Owner, bad, "1.11.2.0"));
            Assert.Equal(51804, refused.Number);
        }
    }

    [SqlFact]
    public async Task Owner_status_changes_follow_the_lifecycle()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var fingerprint = DiagSql.NewFingerprint();
        await IngestAsync(Owner, [Item(fingerprint, DateTime.UtcNow.AddHours(-1), applicationVersion: "1.9.6.0")]);
        var id = Convert.ToInt32(await ScalarAsync("SELECT error_register_id FROM diag.error_register WHERE fingerprint=@f", ("@f", fingerprint)), CultureInfo.InvariantCulture);

        async Task<int> SetAsync(string status, string? version = null, string? ticket = null, string? reason = null)
        {
            var error = await Record.ExceptionAsync(() => DiagSql.QueryAsync(database, Owner, "EXEC diag.set_error_status @id,@s,@v,@t,@r",
                ("@id", id), ("@s", status), ("@v", version), ("@t", ticket), ("@r", reason)));
            return error is SqlException sql ? sql.Number : error is null ? 0 : throw error;
        }

        Assert.Equal(51802, await SetAsync("Expected"));
        Assert.Equal(51803, await SetAsync("Expected", reason: "Line one\nline two"));
        Assert.Equal(51805, await SetAsync("New"));                   // New -> New is not a change the Owner makes
        Assert.Equal(51805, await SetAsync("Recurred"));
        Assert.Equal(0, await SetAsync("Seen", ticket: "FT-010"));
        Assert.Equal("FT-010", await ScalarAsync("SELECT fixed_ticket FROM diag.error_register WHERE error_register_id=@id", ("@id", id)));
        Assert.Equal(0, await SetAsync("Expected", reason: "Deliberate refusal"));
        Assert.Equal(51805, await SetAsync("Seen"));
        Assert.Equal(0, await SetAsync("New"));                       // undo Expected
        Assert.Equal(51802, await SetAsync("Fixed", version: "1.9.7"));
        Assert.Equal(51803, await SetAsync("Fixed", version: "next", reason: "Shipped"));
        Assert.Equal(0, await SetAsync("Fixed", version: "1.9.7", ticket: "FT-011", reason: "Fixed before the manifest existed"));
        Assert.Equal("Fixed", await StatusAsync(fingerprint));
        Assert.Equal(await ScalarAsync("SELECT ORIGINAL_LOGIN()"), await ScalarAsync("SELECT status_changed_by FROM diag.error_register WHERE error_register_id=@id", ("@id", id)));

        // A manual fix for a version it was already seen on is Recurred at once.
        Assert.Equal(51805, await SetAsync("Fixed", version: "1.9.5", reason: "Again"));   // Fixed -> Fixed is not allowed
        var other = DiagSql.NewFingerprint();
        await IngestAsync(Owner, [Item(other, DateTime.UtcNow.AddHours(-1), applicationVersion: "1.9.6.0")]);
        id = Convert.ToInt32(await ScalarAsync("SELECT error_register_id FROM diag.error_register WHERE fingerprint=@f", ("@f", other)), CultureInfo.InvariantCulture);
        Assert.Equal(0, await SetAsync("Fixed", version: "1.9.5", reason: "Wrong version chosen"));
        Assert.Equal("Recurred", await StatusAsync(other));

        id = 0;
        Assert.Equal(51805, await SetAsync("Seen"));
    }

    [SqlFact]
    public async Task Merge_moves_occurrences_and_counts_and_later_occurrences_follow()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var from = DiagSql.NewFingerprint();
        var into = DiagSql.NewFingerprint();
        var time = DateTime.UtcNow.AddHours(-8);
        await IngestAsync(Owner, [Item(into, time, applicationVersion: "1.10.0.0"), Item(from, time.AddHours(1), applicationVersion: "1.11.0.0", severity: "Critical"), Item(from, time.AddHours(2), applicationVersion: "1.10.0.0")]);
        var fromId = await IdAsync(from);
        var intoId = await IdAsync(into);

        async Task<int> MergeAsync(int a, int b, string? reason)
        {
            var error = await Record.ExceptionAsync(() => DiagSql.QueryAsync(database, Owner, "EXEC diag.merge_error_entries @a,@b,@r", ("@a", a), ("@b", b), ("@r", reason)));
            return error is SqlException sql ? sql.Number : error is null ? 0 : throw error;
        }

        Assert.Equal(51802, await MergeAsync(fromId, intoId, " "));
        Assert.Equal(51805, await MergeAsync(fromId, fromId, "Same method renamed"));
        Assert.Equal(51805, await MergeAsync(fromId, int.MaxValue, "Same method renamed"));
        Assert.Equal(0, await MergeAsync(fromId, intoId, "Same method renamed"));
        Assert.Equal(51805, await MergeAsync(fromId, intoId, "Same method renamed"));     // already merged
        Assert.Equal(51805, await MergeAsync(intoId, fromId, "Into a merged entry"));

        Assert.Equal(3L, await ScalarAsync("SELECT occurrence_count FROM diag.error_register WHERE error_register_id=@id", ("@id", intoId)));
        Assert.Equal("Critical", await ScalarAsync("SELECT max_severity FROM diag.error_register WHERE error_register_id=@id", ("@id", intoId)));
        Assert.Equal(3, await ScalarAsync("SELECT COUNT(*) FROM diag.error_occurrences WHERE error_register_id=@id", ("@id", intoId)));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM diag.error_register_versions WHERE error_register_id=@id", ("@id", fromId)));
        Assert.Equal(2L, await ScalarAsync("SELECT occurrence_count FROM diag.error_register_versions WHERE error_register_id=@id AND app_version='1.10.0.0'", ("@id", intoId)));
        var register = (await DiagSql.QueryAsync(database, Owner, "EXEC diag.load_error_register"))[0];
        Assert.DoesNotContain(register, row => (int)row["error_register_id"]! == fromId);

        // The old fingerprint keeps arriving from PCs that log it: it counts on the entry it was merged into.
        var result = await IngestAsync(Viewer, [Item(from, time.AddHours(3))]);
        Assert.Equal((1, 0), (N(result["accepted"]), N(result["new_entries"])));
        Assert.Equal(4L, await ScalarAsync("SELECT occurrence_count FROM diag.error_register WHERE error_register_id=@id", ("@id", intoId)));
    }

    [SqlFact]
    public async Task More_than_200_a_day_keep_only_the_counter_and_old_occurrences_are_pruned_keeping_20()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var spam = DiagSql.NewFingerprint();
        var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(1);
        var items = Enumerable.Range(0, 205).Select(i => Item(spam, day.AddSeconds(i), reference: "SPAM" + i.ToString("00000000", CultureInfo.InvariantCulture))).ToArray();
        var result = await IngestAsync(Automation, items[..200]);
        Assert.Equal(200, result["accepted"]);
        result = await IngestAsync(Automation, items[200..]);
        Assert.Equal(5, result["accepted"]);
        Assert.Equal(205L, await ScalarAsync("SELECT occurrence_count FROM diag.error_register WHERE fingerprint=@f", ("@f", spam)));
        Assert.Equal(200, await ScalarAsync("SELECT COUNT(*) FROM diag.error_occurrences o JOIN diag.error_register e ON e.error_register_id=o.error_register_id WHERE e.fingerprint=@f", ("@f", spam)));

        // 25 occurrences on 25 days, 300 to 276 days ago: older than 180 days, so only the newest 20 stay.
        var old = DiagSql.NewFingerprint();
        var start = DateTime.UtcNow.AddDays(-300);
        await IngestAsync(Owner, Enumerable.Range(0, 25).Select(i => Item(old, start.AddDays(i))).ToArray());
        const string count = "SELECT COUNT(*) FROM diag.error_occurrences o JOIN diag.error_register e ON e.error_register_id=o.error_register_id WHERE e.fingerprint=@f";
        Assert.Equal(20, await ScalarAsync(count, ("@f", old)));
        Assert.Equal(25L, await ScalarAsync("SELECT occurrence_count FROM diag.error_register WHERE fingerprint=@f", ("@f", old)));
        Assert.Equal(0, await ScalarAsync(
            "SELECT COUNT(*) FROM diag.error_occurrences o JOIN diag.error_register e ON e.error_register_id=o.error_register_id WHERE e.fingerprint=@f AND o.occurred_utc<@cut",
            ("@f", old), ("@cut", start.AddDays(5).AddSeconds(-1))));

        // A recent occurrence is never pruned: it becomes one of the newest 20, and the oldest of the old ones goes.
        await IngestAsync(Owner, [Item(old, DateTime.UtcNow.AddMinutes(-1))]);
        Assert.Equal(20, await ScalarAsync(count, ("@f", old)));
    }

    [SqlFact]
    public async Task A_lost_cursor_resending_a_spam_day_does_not_count_the_overflow_again()
    {
        // 1.11.1 (1.11.0 review minor 7): only 200 per day are stored, so the 5 overflow lines were found by no stored row.
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var spam = DiagSql.NewFingerprint();
        var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(1);
        var items = Enumerable.Range(0, 205).Select(i => Item(spam, day.AddSeconds(i), reference: "OVER" + i.ToString("00000000", CultureInfo.InvariantCulture))).ToArray();
        Assert.Equal(205, (await IngestAsync(Automation, items))["accepted"]);

        var again = await IngestAsync(Automation, items);
        Assert.Equal(0, again["accepted"]);
        Assert.Equal(205, again["duplicates"]);
        Assert.Equal(205L, await ScalarAsync("SELECT occurrence_count FROM diag.error_register WHERE fingerprint=@f", ("@f", spam)));
        Assert.Equal(205L, await ScalarAsync(
            "SELECT CONVERT(bigint,SUM(v.occurrence_count)) FROM diag.error_register_versions v JOIN diag.error_register e ON e.error_register_id=v.error_register_id WHERE e.fingerprint=@f", ("@f", spam)));

        // A genuinely newer line on the same full day is still counted.
        var fresh = await IngestAsync(Automation, [Item(spam, day.AddSeconds(300), reference: "OVER99999999")]);
        Assert.Equal(1, fresh["accepted"]);
        Assert.Equal(206L, await ScalarAsync("SELECT occurrence_count FROM diag.error_register WHERE fingerprint=@f", ("@f", spam)));
    }

    [SqlFact]
    public async Task Result_sets_have_the_contract_columns()
    {
        await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
        var result = await DiagSql.QueryAsync(database, Viewer, "EXEC diag.ingest_error_occurrences @items,@v",
            ("@items", JsonSerializer.Serialize(new[] { Item(DiagSql.NewFingerprint(), DateTime.UtcNow.AddMinutes(-1)) })), ("@v", "1.11.0.0"));
        Assert.Equal(new[] { "accepted", "duplicates", "rejected", "new_entries", "recurred_entries" }, result[0][0].Keys);

        var register = await DiagSql.QueryAsync(database, Automation, "EXEC diag.load_error_register @status=@s", ("@s", "New"));
        Assert.Equal(new[]
        {
            "error_register_id", "fingerprint", "fingerprint_version", "event_code", "sub_code", "source", "site", "exception_type", "sql_number",
            "max_severity", "status", "first_seen_utc", "last_seen_utc", "occurrence_count", "first_version", "last_version", "fixed_in_version",
            "fixed_ticket", "fixed_utc", "recurred_utc", "recurred_version", "latest_stack", "status_reason", "status_changed_utc",
            "status_changed_by", "merged_into_id",
        }, register[0][0].Keys);
        Assert.All(register[0], row => Assert.Equal("New", row["status"]));

        var manifest = await ApplyAsync(Automation, "{\"manifest\":1,\"fixes\":[]}", "1.11.0.0");
        Assert.Equal(new[] { "lines", "entries_marked_fixed", "already_fixed", "not_found", "skipped" }, manifest.Keys);

        var missing = await DiagSql.QueryAsync(database, Owner, "EXEC diag.load_error_entry @id", ("@id", int.MaxValue));
        Assert.Equal(3, missing.Count);
        Assert.Empty(missing[0]);
    }

    // ---- helpers ----

    private static Dictionary<string, object?> Item(string fingerprint, DateTime timestampUtc, string eventBase = "REPORT_STORE_REQUIRED",
        string site = "SqlServerApplicationReportQuery.LoadStaffPerformanceAsync", string? reference = null, string severity = "Error",
        string applicationVersion = "1.11.0.0", string? timestamp = null, string? operation = "RunReport", string? storeCode = "ALL",
        string? businessDate = "2026-10-09", IReadOnlyList<string>? stack = null) => new()
    {
        ["fingerprint"] = fingerprint,
        ["fingerprintVersion"] = 1,
        ["eventBase"] = eventBase,
        ["subCode"] = "",
        ["source"] = "Reports.Workspace",
        ["site"] = site,
        ["exceptionType"] = "System.InvalidOperationException",
        ["sqlNumber"] = 0,
        ["timestampUtc"] = timestamp ?? timestampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        ["severity"] = severity,
        ["reference"] = reference ?? DiagSql.NewReference(),
        ["applicationVersion"] = applicationVersion,
        ["reportCode"] = "staff",
        ["storeCode"] = storeCode,
        ["businessDate"] = businessDate,
        ["dateFrom"] = null,
        ["operation"] = operation,
        ["stack"] = stack,
    };

    private async Task<Dictionary<string, object?>> IngestAsync(string user, object[] items, string version = "1.11.0.0") =>
        (await DiagSql.QueryAsync(database, user, "EXEC diag.ingest_error_occurrences @items,@v",
            ("@items", JsonSerializer.Serialize(items)), ("@v", version)))[0][0];

    private async Task<Dictionary<string, object?>> ApplyAsync(string user, string manifest, string running) =>
        (await DiagSql.QueryAsync(database, user, "EXEC diag.apply_error_fix_manifest @m,@r", ("@m", manifest), ("@r", running)))[0][0];

    private static string Manifest(params (string Fingerprint, string FixedIn, string Ticket)[] fixes) =>
        JsonSerializer.Serialize(new { manifest = 1, fixes = fixes.Select(fix => new { fingerprint = fix.Fingerprint, fixedIn = fix.FixedIn, ticket = fix.Ticket }) });

    private static (int, int, int, int, int) Counts(Dictionary<string, object?> row) =>
        (N(row["lines"]), N(row["entries_marked_fixed"]), N(row["already_fixed"]), N(row["not_found"]), N(row["skipped"]));

    private static int N(object? value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private async Task<object?> StatusAsync(string fingerprint) =>
        await ScalarAsync("SELECT status FROM diag.error_register WHERE fingerprint=@f", ("@f", fingerprint));

    private async Task<int> IdAsync(string fingerprint) =>
        Convert.ToInt32(await ScalarAsync("SELECT error_register_id FROM diag.error_register WHERE fingerprint=@f", ("@f", fingerprint)), CultureInfo.InvariantCulture);

    private async Task<object?> ScalarAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        var rows = (await DiagSql.QueryAsync(database, null, sql, parameters))[0];
        return rows.Count == 0 ? null : rows[0].Values.First();
    }
}

/// <summary>Shared by ErrorRegisterSqlTests and HealthCheckSqlTests: users per role, and reading every result set.</summary>
internal static class DiagSql
{
    /// <summary>Owner, Store Manager, Viewer and the automation account (etp_store_manager + etp_automation), as on Workpc.</summary>
    public static Task EnsureUsersAsync(SqlDatabaseFixture database, string owner, string manager, string viewer, string automation) =>
        database.ExecuteAsync($"""
            IF DATABASE_PRINCIPAL_ID(N'{owner}') IS NULL BEGIN CREATE USER {owner} WITHOUT LOGIN; ALTER ROLE db_owner ADD MEMBER {owner}; ALTER ROLE etp_owner ADD MEMBER {owner}; END;
            IF DATABASE_PRINCIPAL_ID(N'{manager}') IS NULL BEGIN CREATE USER {manager} WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER {manager}; END;
            IF DATABASE_PRINCIPAL_ID(N'{viewer}') IS NULL BEGIN CREATE USER {viewer} WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER {viewer}; END;
            IF DATABASE_PRINCIPAL_ID(N'{automation}') IS NULL BEGIN CREATE USER {automation} WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER {automation}; ALTER ROLE etp_automation ADD MEMBER {automation}; END;
            """);

    public static string NewFingerprint() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"))));

    public static string NewReference() => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    /// <summary>Runs <paramref name="sql"/> (as <paramref name="user"/> when given) and returns every result set, columns in order.</summary>
    public static async Task<List<List<Dictionary<string, object?>>>> QueryAsync(SqlDatabaseFixture database, string? user, string sql,
        params (string Name, object? Value)[] parameters)
    {
        // EXECUTE AS USER is never reverted when the batch fails (and often not at all), so an impersonating query
        // gets its own connection that is never pooled: a pooled one handed back still impersonating is killed at
        // its next reset (SQL error 18059, "the session is in the kill state" in the next test, 1.11.0 gate).
        var connectionString = user is null
            ? database.ConnectionString
            : new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var text = user is null ? sql : $"EXECUTE AS USER='{user}'; {sql};";
        await using var command = new SqlCommand(text, connection) { CommandTimeout = 120 };
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var sets = new List<List<Dictionary<string, object?>>>();
        await using var reader = await command.ExecuteReaderAsync();
        do
        {
            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            // A result set with columns but no rows still counts (load_error_entry for a missing id).
            if (reader.FieldCount > 0) sets.Add(rows);
        } while (await reader.NextResultAsync());
        return sets;
    }
}
