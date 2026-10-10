using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// Migration 0038: the Phase 7 Tally foundation. Every test uses its own store and profile codes,
// so the shared fixture never carries one test's rows into another's assertions.
public sealed class TallyFoundationSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Theory]
    [InlineData("N'http://192.168.1.20:9000/'")]
    [InlineData("N'http://tally.example.com:9000/'")]
    [InlineData("N'https://127.0.0.1:9000/'")]
    [InlineData("N'http://127.0.0.1:9000'")]
    [InlineData("N'http://127.0.0.1:/'")]
    [InlineData("N'http://127.0.0.1:90a0/'")]
    [InlineData("N'http://localhost'")]
    public async Task Only_this_PCs_Tally_endpoint_is_accepted(string endpoint)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(Profile("REMOTE", endpoint: endpoint)));
        Assert.Equal(547, error.Number);
        Assert.Contains("CK_tally_profiles_endpoint", error.Message);
        Assert.NotNull(await database.ExecuteAsync(Profile("LOOPA", endpoint: "N'http://127.0.0.1:9000/'")));
        Assert.NotNull(await database.ExecuteAsync(Profile("LOOPB", endpoint: "N'http://localhost:9999/'")));
        await database.ExecuteAsync("DELETE dbo.tally_profiles WHERE profile_code IN('LOOPA','LOOPB')");
    }

    [Theory]
    [InlineData("party_policy='NAMED_LEDGERS'", "CK_tally_profiles_party_tender")]
    [InlineData("single_party_ledger=NULL", "CK_tally_profiles_single_party")]
    [InlineData("single_party_ledger=N'  '", "CK_tally_profiles_single_party")]
    [InlineData("production_enabled_utc=SYSUTCDATETIME()", "CK_tally_profiles_production")]
    [InlineData("profile_code='bad code'", "CK_tally_profiles_code")]
    [InlineData("change_reason=N' '", "CK_tally_profiles_reason")]
    public async Task Profile_policy_constraints_hold(string change, string constraint)
    {
        var id = await database.ExecuteAsync(Profile("POLICY"));
        try
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"UPDATE dbo.tally_profiles SET {change} WHERE tally_profile_id={id}"));
            Assert.Equal(547, error.Number);
            Assert.Contains(constraint, error.Message);
        }
        finally { await database.ExecuteAsync($"DELETE dbo.tally_profiles WHERE tally_profile_id={id}"); }
    }

    [Fact]
    public async Task A_store_binds_to_one_test_and_one_production_company()
    {
        await database.ExecuteAsync("INSERT dbo.stores(store_code,store_name) VALUES('TBIND',N'Synthetic bind store')");
        var first = await database.ExecuteAsync(Profile("BINDA"));
        var second = await database.ExecuteAsync(Profile("BINDB"));
        var live = await database.ExecuteAsync(Profile("BINDLIVE", environment: "PRODUCTION"));
        await database.ExecuteAsync($"INSERT dbo.tally_profile_stores(tally_profile_id,environment,store_code) VALUES({first},'TEST','TBIND')");
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"INSERT dbo.tally_profile_stores(tally_profile_id,environment,store_code) VALUES({second},'TEST','TBIND')"));
        Assert.Equal(2627, duplicate.Number);
        Assert.Contains("UQ_tally_profile_stores_store", duplicate.Message);
        var mismatched = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"INSERT dbo.tally_profile_stores(tally_profile_id,environment,store_code) VALUES({second},'PRODUCTION','TBIND')"));
        Assert.Equal(547, mismatched.Number);
        Assert.Contains("FK_tally_profile_stores_profile", mismatched.Message);
        await database.ExecuteAsync($"INSERT dbo.tally_profile_stores(tally_profile_id,environment,store_code) VALUES({live},'PRODUCTION','TBIND')");
        Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.tally_profile_stores WHERE store_code='TBIND'"));
    }

    [Fact]
    public async Task Existing_day_journal_batches_keep_working_and_record_history()
    {
        var batch = await database.ExecuteAsync(Day("TJOURNAL") + Batch("TJOURNAL"));
        Assert.Equal("DAY_JOURNAL", await database.ExecuteAsync($"SELECT batch_kind FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
        await database.ExecuteAsync($"UPDATE dbo.accounting_batches SET status='APPROVED_READY',approval_reason=N'Checked synthetic journal' WHERE accounting_batch_id={batch}");
        Assert.Equal("|DRAFT|;DRAFT|APPROVED_READY|Checked synthetic journal", await database.ExecuteAsync(History("BATCH", batch!)));

        var sales = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(Day("TNOPROF") + Batch("TNOPROF", kind: "SALES_VOUCHERS")));
        Assert.Equal(547, sales.Number);
        Assert.Contains("CK_accounting_batches_kind_profile", sales.Message);
    }

    [Fact]
    public async Task Status_history_cannot_be_changed_or_deleted()
    {
        var batch = await database.ExecuteAsync(Day("THIST") + Batch("THIST"));
        foreach (var sql in new[]
        {
            $"UPDATE dbo.accounting_status_history SET reason=N'Rewritten' WHERE subject_type='BATCH' AND subject_id={batch}",
            $"DELETE dbo.accounting_status_history WHERE subject_type='BATCH' AND subject_id={batch}"
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(sql));
            Assert.Equal(51573, error.Number);
        }
        Assert.Equal("|DRAFT|", await database.ExecuteAsync(History("BATCH", batch!)));
    }

    [Fact]
    public async Task An_invoice_is_reserved_once_per_Tally_company_until_its_batch_is_rejected_unsent()
    {
        var profile = await database.ExecuteAsync(Profile("RESERVE"));
        var first = await database.ExecuteAsync(Day("TRES") + Batch("TRES", kind: "SALES_VOUCHERS", profile: profile));
        var firstVoucher = await database.ExecuteAsync(Voucher(first!, "TRES"));
        await database.ExecuteAsync(Reservation(profile!, "TRES", firstVoucher!));

        var later = await database.ExecuteAsync(Day("TRES", "20260826") + Batch("TRES", date: "20260826", kind: "SALES_VOUCHERS", profile: profile));
        var laterVoucher = await database.ExecuteAsync(Voucher(later!, "TRES"));
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(Reservation(profile!, "TRES", laterVoucher!)));
        Assert.Equal(2601, duplicate.Number);
        Assert.Contains("UX_accounting_voucher_reservations_active", duplicate.Message);

        await database.ExecuteAsync($"EXEC dbo.reject_accounting_batch {first},N'Prepare corrected batch';");
        Assert.Equal("Batch rejected before anything was sent to Tally.",
            await database.ExecuteAsync($"SELECT release_reason FROM dbo.accounting_voucher_reservations WHERE accounting_voucher_id={firstVoucher}"));
        await database.ExecuteAsync(Reservation(profile!, "TRES", laterVoucher!));
        Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_voucher_reservations WHERE store_code='TRES' AND released_utc IS NULL"));
    }

    [Fact]
    public async Task A_rejected_batch_with_an_attempt_keeps_its_reservation()
    {
        var profile = await database.ExecuteAsync(Profile("ATTEMPT"));
        var batch = await database.ExecuteAsync(Day("TATT") + Batch("TATT", kind: "SALES_VOUCHERS", profile: profile));
        var voucher = await database.ExecuteAsync(Voucher(batch!, "TATT"));
        await database.ExecuteAsync(Reservation(profile!, "TATT", voucher!));
        var artifact = await database.ExecuteAsync(Artifact(batch!));
        await database.ExecuteAsync(Attempt(batch!, profile!, artifact!));
        await database.ExecuteAsync($"EXEC dbo.reject_accounting_batch {batch},N'Synthetic rejection after a written file';");
        Assert.Equal(DBNull.Value, await database.ExecuteAsync($"SELECT released_utc FROM dbo.accounting_voucher_reservations WHERE accounting_voucher_id={voucher}"));
    }

    [Fact]
    public async Task Blocking_a_voucher_before_any_attempt_releases_its_invoice_with_the_reason()
    {
        var profile = await database.ExecuteAsync(Profile("BLOCK"));
        var batch = await database.ExecuteAsync(Day("TBLK") + Batch("TBLK", kind: "SALES_VOUCHERS", profile: profile));
        var voucher = await database.ExecuteAsync(Voucher(batch!, "TBLK"));
        await database.ExecuteAsync(Reservation(profile!, "TBLK", voucher!));
        await database.ExecuteAsync($"UPDATE dbo.accounting_vouchers SET voucher_status='BLOCKED',blocked_reason=N'NOT_IN_SCOPE_7A: split tender' WHERE accounting_voucher_id={voucher}");
        Assert.Equal("NOT_IN_SCOPE_7A: split tender",
            await database.ExecuteAsync($"SELECT release_reason FROM dbo.accounting_voucher_reservations WHERE accounting_voucher_id={voucher}"));
        Assert.Equal("|PLANNED|;PLANNED|BLOCKED|NOT_IN_SCOPE_7A: split tender", await database.ExecuteAsync(History("VOUCHER", voucher!)));

        var notPlanned = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(Reservation(profile!, "TBLK", voucher!)));
        Assert.Equal(51579, notPlanned.Number);
        var released = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"UPDATE dbo.accounting_voucher_reservations SET released_utc=SYSUTCDATETIME(),release_reason=N'Again' WHERE accounting_voucher_id={voucher}"));
        Assert.Equal(51579, released.Number);
        var deleted = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"DELETE dbo.accounting_voucher_reservations WHERE accounting_voucher_id={voucher}"));
        Assert.Equal(51573, deleted.Number);
    }

    [Fact]
    public async Task A_reservation_must_match_its_voucher_and_company()
    {
        var profile = await database.ExecuteAsync(Profile("MATCH"));
        var other = await database.ExecuteAsync(Profile("MATCHOTHER"));
        var batch = await database.ExecuteAsync(Day("TMAT") + Batch("TMAT", kind: "SALES_VOUCHERS", profile: profile));
        var voucher = await database.ExecuteAsync(Voucher(batch!, "TMAT"));
        foreach (var sql in new[]
        {
            Reservation(other!, "TMAT", voucher!),
            Reservation(profile!, "TMAT", voucher!).Replace("N'INV-1'", "N'INV-2'", StringComparison.Ordinal)
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(sql));
            Assert.Equal(51579, error.Number);
        }
    }

    [Theory]
    [InlineData("TKA", "INV-9", "INV-1", "PLANNED", "CK_accounting_vouchers_key")]
    [InlineData("TKB", "A:1", "A:1", "PLANNED", "CK_accounting_vouchers_key_safe")]
    [InlineData("TKC", "A 1", "A 1", "PLANNED", "CK_accounting_vouchers_key_safe")]
    [InlineData("TKD", "INV-1", "INV-1", "BLOCKED", "CK_accounting_vouchers_blocked")]
    public async Task Voucher_keys_must_match_their_parts_and_be_safe_to_send(string store, string keyNumber, string number, string status, string constraint)
    {
        var profile = await database.ExecuteAsync(Profile("KEY" + store));
        var batch = await database.ExecuteAsync(Day(store) + Batch(store, kind: "SALES_VOUCHERS", profile: profile));
        var sql = Voucher(batch!, store).Replace($"N'ETP:{store}:2027:INV-1:SALES:1'", $"N'ETP:{store}:2027:{keyNumber}:SALES:1'", StringComparison.Ordinal)
            .Replace("N'INV-1'", $"N'{number}'", StringComparison.Ordinal).Replace("'PLANNED'", $"'{status}'", StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(sql));
        Assert.Equal(547, error.Number);
        Assert.Contains(constraint, error.Message);
    }

    [Fact]
    public async Task An_unsafe_number_may_be_stored_as_a_blocked_voucher()
    {
        var profile = await database.ExecuteAsync(Profile("UNSAFE"));
        var batch = await database.ExecuteAsync(Day("TUNS") + Batch("TUNS", kind: "SALES_VOUCHERS", profile: profile));
        var sql = Voucher(batch!, "TUNS").Replace("INV-1", "A|1", StringComparison.Ordinal)
            .Replace("'PLANNED',NULL", "'BLOCKED',N'KEY_UNSAFE'", StringComparison.Ordinal);
        Assert.NotNull(await database.ExecuteAsync(sql));
    }

    [Fact]
    public async Task Vouchers_of_a_decided_batch_keep_their_plan_but_can_record_reconciliation()
    {
        var profile = await database.ExecuteAsync(Profile("DECIDED"));
        var batch = await database.ExecuteAsync(Day("TDEC") + Batch("TDEC", kind: "SALES_VOUCHERS", profile: profile));
        var voucher = await database.ExecuteAsync(Voucher(batch!, "TDEC"));
        await database.ExecuteAsync($"UPDATE dbo.accounting_batches SET status='APPROVED_READY',approval_reason=N'Checked synthetic vouchers' WHERE accounting_batch_id={batch}");

        var changed = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"UPDATE dbo.accounting_vouchers SET expected_total=1 WHERE accounting_voucher_id={voucher}"));
        Assert.Equal(51212, changed.Number);
        var added = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(Voucher(batch!, "TDEC").Replace("VALUES(" + batch + ",1,", "VALUES(" + batch + ",2,", StringComparison.Ordinal).Replace("INV-1", "INV-2", StringComparison.Ordinal)));
        Assert.Equal(51212, added.Number);

        await database.ExecuteAsync($"UPDATE dbo.accounting_vouchers SET voucher_status='EXPORTED' WHERE accounting_voucher_id={voucher}");
        Assert.Equal("EXPORTED", await database.ExecuteAsync($"SELECT voucher_status FROM dbo.accounting_vouchers WHERE accounting_voucher_id={voucher}"));
        // 0040: never back to planned, blocked or excluded once the batch is decided.
        var back = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"UPDATE dbo.accounting_vouchers SET voucher_status='PLANNED' WHERE accounting_voucher_id={voucher}"));
        Assert.Equal(51212, back.Number);
    }

    [Fact]
    public async Task A_blocked_voucher_of_a_decided_batch_stays_blocked()
    {
        var profile = await database.ExecuteAsync(Profile("DECBLK"));
        var batch = await database.ExecuteAsync(Day("TDBL") + Batch("TDBL", kind: "SALES_VOUCHERS", profile: profile));
        var voucher = await database.ExecuteAsync(Voucher(batch!, "TDBL"));
        await database.ExecuteAsync($"UPDATE dbo.accounting_vouchers SET voucher_status='BLOCKED',blocked_reason=N'NOT_IN_SCOPE_7A: split tender' WHERE accounting_voucher_id={voucher}");
        await database.ExecuteAsync($"UPDATE dbo.accounting_batches SET status='APPROVED_READY',approval_reason=N'Checked synthetic vouchers' WHERE accounting_batch_id={batch}");

        foreach (var status in new[] { "PLANNED", "EXPORTED" })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"UPDATE dbo.accounting_vouchers SET voucher_status='{status}' WHERE accounting_voucher_id={voucher}"));
            Assert.Equal(51212, refused.Number);
        }
        Assert.Equal("BLOCKED", await database.ExecuteAsync($"SELECT voucher_status FROM dbo.accounting_vouchers WHERE accounting_voucher_id={voucher}"));
    }

    [Fact]
    public async Task An_entry_cannot_point_at_a_voucher_of_another_batch()
    {
        var profile = await database.ExecuteAsync(Profile("CROSS"));
        var first = await database.ExecuteAsync(Day("TCRA") + Batch("TCRA", kind: "SALES_VOUCHERS", profile: profile));
        var voucher = await database.ExecuteAsync(Voucher(first!, "TCRA"));
        var second = await database.ExecuteAsync(Day("TCRB") + Batch("TCRB"));
        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"""
            INSERT dbo.accounting_entries(accounting_batch_id,line_number,business_event,ledger_name,debit_amount,credit_amount,narration,source_reference,accounting_voucher_id)
            VALUES({second},1,'SALES_REVENUE',N'Sales',0,100,N'Synthetic',N'fixture',{voucher})
            """));
        Assert.Equal(547, error.Number);
        Assert.Contains("FK_accounting_entries_voucher", error.Message);
    }

    [Fact]
    public async Task Evidence_records_are_append_only_and_attempts_only_advance()
    {
        var profile = await database.ExecuteAsync(Profile("EVIDENCE"));
        var batch = await database.ExecuteAsync(Day("TEVI") + Batch("TEVI", kind: "SALES_VOUCHERS", profile: profile));
        var artifact = await database.ExecuteAsync(Artifact(batch!));
        foreach (var sql in new[]
        {
            $"UPDATE dbo.tally_artifacts SET byte_length=1 WHERE tally_artifact_id={artifact}",
            $"DELETE dbo.tally_artifacts WHERE tally_artifact_id={artifact}"
        })
            Assert.Equal(51573, (await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(sql))).Number);

        var unsafePath = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(Artifact(batch!).Replace(@"payload.xml", @"..\..\payload.xml", StringComparison.Ordinal)));
        Assert.Equal(547, unsafePath.Number);

        var attempt = await database.ExecuteAsync(Attempt(batch!, profile!, artifact!, "SENT", "HTTP"));
        await database.ExecuteAsync($"UPDATE dbo.tally_attempts SET attempt_status='RESPONDED',http_status=200,completed_utc=SYSUTCDATETIME() WHERE tally_attempt_id={attempt}");
        var finished = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"UPDATE dbo.tally_attempts SET attempt_status='TIMED_OUT' WHERE tally_attempt_id={attempt}"));
        Assert.Equal(51573, finished.Number);
        var deleted = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"DELETE dbo.tally_attempts WHERE tally_attempt_id={attempt}"));
        Assert.Equal(51573, deleted.Number);

        var other = await database.ExecuteAsync(Profile("EVIDENCEOTHER"));
        var wrongCompany = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(Attempt(batch!, other!, artifact!)));
        Assert.Equal(51579, wrongCompany.Number);
    }

    [Fact]
    public async Task New_tables_are_denied_to_store_managers_and_viewers()
    {
        string[] tables = ["tally_profiles", "tally_profile_stores", "accounting_vouchers", "accounting_voucher_reservations", "accounting_status_history", "tally_artifacts", "tally_attempts"];
        var list = string.Join(',', tables.Select(table => $"'{table}'"));
        Assert.Equal(tables.Length * 2 * 4, await database.ExecuteAsync($"""
            SELECT COUNT(*) FROM sys.database_permissions p
            JOIN sys.database_principals r ON r.principal_id=p.grantee_principal_id
            JOIN sys.objects o ON o.object_id=p.major_id
            WHERE p.state_desc='DENY' AND p.permission_name IN('SELECT','INSERT','UPDATE','DELETE')
              AND r.name IN('etp_store_manager','etp_viewer') AND SCHEMA_NAME(o.schema_id)='dbo' AND o.name IN({list})
            """));
    }

    [Fact]
    public async Task Upgrade_gives_every_existing_batch_one_starting_history_row()
    {
        await using var upgrade = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(upgrade.ConnectionString, new BeforeTally(source)).BootstrapAsync();
        await upgrade.ExecuteAsync(Day("TUPG") + """
            INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,created_by)
            SELECT store_code,business_date,daily_report_generation_id,1,118,118,'DRAFT',SUSER_SNAME() FROM dbo.daily_report_generations WHERE store_code='TUPG';
            """);
        await upgrade.ExecuteAsync("UPDATE dbo.accounting_batches SET status='APPROVED_READY',approval_reason=N'Checked before upgrade' WHERE store_code='TUPG'");

        Assert.Contains("0038_tally_transfer_foundation", await new MigrationRunner(source, new SqlServerMigrationStore(upgrade.ConnectionString)).RunAsync());

        Assert.Equal("|APPROVED_READY|database update 0038", await upgrade.ExecuteAsync(
            "SELECT STRING_AGG(CONCAT(h.from_status,'|',h.to_status,'|',h.actor),';') FROM dbo.accounting_status_history h JOIN dbo.accounting_batches b ON b.accounting_batch_id=h.subject_id AND h.subject_type='BATCH' WHERE b.store_code='TUPG'"));
        Assert.Equal("DAY_JOURNAL", await upgrade.ExecuteAsync("SELECT batch_kind FROM dbo.accounting_batches WHERE store_code='TUPG'"));
        Assert.Equal(@"C:\ProgramData\EtpReporting\TallyEvidence", await upgrade.ExecuteAsync("SELECT TOP(1) tally_evidence_root FROM dbo.product_settings"));
    }

    private static string Profile(string code, string environment = "TEST", string endpoint = "NULL") => $"""
        INSERT dbo.tally_profiles(profile_code,company_name,environment,endpoint_url,voucher_granularity,party_policy,single_party_ledger,tender_model,posting_model,voucher_view,change_reason)
        VALUES('{code}',N'TEST - ETP Golden {code}','{environment}',{endpoint},'PER_INVOICE','SINGLE_LEDGER',N'Cash Sales','IN_VOUCHER','ACCOUNTING_ONLY','ACCOUNTING',N'Synthetic test profile');
        SELECT CONVERT(int,SCOPE_IDENTITY());
        """;

    private static string Day(string store, string date = "20260825") => $"""
        INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
        VALUES('{store}','{date}',1,REPLICATE('a',64),N'{"{}"}',SUSER_SNAME(),1);
        """;

    private static string Batch(string store, string date = "20260825", string kind = "DAY_JOURNAL", object? profile = null) => $"""
        INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,created_by,batch_kind,tally_profile_id)
        SELECT store_code,business_date,daily_report_generation_id,1,118,118,'DRAFT',SUSER_SNAME(),'{kind}',{profile ?? "NULL"}
        FROM dbo.daily_report_generations WHERE store_code='{store}' AND business_date='{date}';
        SELECT CONVERT(bigint,SCOPE_IDENTITY());
        """;

    private static string Voucher(object batch, string store) => $"""
        INSERT dbo.accounting_vouchers(accounting_batch_id,voucher_sequence,component_role,voucher_type,store_code,invoice_year,document_number,voucher_date,expected_total,correspondence_key,source_sha256,plan_sha256,voucher_status,blocked_reason)
        VALUES({batch},1,'SALES',N'Sales','{store}',2027,N'INV-1','20260825',118,N'ETP:{store}:2027:INV-1:SALES:1',REPLICATE('a',64),REPLICATE('b',64),'PLANNED',NULL);
        SELECT CONVERT(bigint,SCOPE_IDENTITY());
        """;

    private static string Reservation(object profile, string store, object voucher) => $"""
        INSERT dbo.accounting_voucher_reservations(tally_profile_id,store_code,invoice_year,document_number,component_role,accounting_voucher_id)
        VALUES({profile},'{store}',2027,N'INV-1','SALES',{voucher});
        """;

    private static string Artifact(object batch) => $"""
        INSERT dbo.tally_artifacts(accounting_batch_id,artifact_kind,relative_path,sha256,byte_length)
        VALUES({batch},'PAYLOAD_XML',N'GOLDEN\TEVI\2026-08\batch-{batch}\payload.xml',REPLICATE('c',64),42);
        SELECT CONVERT(bigint,SCOPE_IDENTITY());
        """;

    private static string Attempt(object batch, object profile, object artifact, string status = "FILE_WRITTEN", string delivery = "FILE") => $"""
        INSERT dbo.tally_attempts(accounting_batch_id,tally_profile_id,payload_artifact_id,delivery_mode,payload_format,attempt_status)
        VALUES({batch},{profile},{artifact},'{delivery}','XML','{status}');
        SELECT CONVERT(bigint,SCOPE_IDENTITY());
        """;

    private static string History(string subject, object id) =>
        $"SELECT STRING_AGG(CONCAT(from_status,'|',to_status,'|',reason),';') WITHIN GROUP(ORDER BY history_id) FROM dbo.accounting_status_history WHERE subject_type='{subject}' AND subject_id={id}";

    private sealed class BeforeTally(IMigrationSource source) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken token = default) =>
            (await source.DiscoverAsync(token)).Where(migration => string.CompareOrdinal(migration.Id, "0038") < 0).ToArray();
    }

    private sealed class UpgradeDatabase : IAsyncDisposable
    {
        private readonly string name = "EtpPhase0Test_TallyUpgrade_" + Guid.NewGuid().ToString("N");
        public string ConnectionString { get; }
        public UpgradeDatabase() => ConnectionString = TestSqlConnections.ForDatabase(name, pooling: false);

        public async Task<object?> ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return await command.ExecuteScalarAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (!name.StartsWith("EtpPhase0Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture name.");
            var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
