using Etp.Reporting.Application.Collections;
using Etp.Reporting.Infrastructure.SqlServer.Collections;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12 lane D2, Appendix C test 6 (cross-layer, C# meets SQL): a synthetic bank CSV is imported through the C# services and
/// dbo.collections_import_statement; re-import adds nothing and a superset adds only its new lines; the deposit the daily
/// sheet says was sent on a LOCKED day gets an R-DEP-1 proposal, the Owner accepts it and the statement shows "credited";
/// provider settlement totals reach the statement as "totals only"; narration, payer, VPA and raw cells are denied to
/// Store Manager and Viewer, who still import through the procedure. The test login is the Owner (as in every SQL test).
/// Needs lane A3's 0062 (the 1.12 audit types in CK_operational_audit_type); the object views are applied from
/// database/objects here when A3's runner has not applied them.
/// </summary>
public sealed class CollectionsSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string TwoColumns = """
        {"value_date":"Value Dt","posting_date":"Date","narration":"Narration","reference":"Chq./Ref.No.",
         "debit":"Withdrawal Amt.","credit":"Deposit Amt.","running_balance":"Closing Balance","date_format":"dd/MM/yyyy"}
        """;

    private const string Card = """
        {"txn_datetime":"Txn Date Time","merchant_id":"MID","terminal_id":"TID","txn_reference":"RRN","invoice_reference":"Invoice",
         "line_kind_column":"Type","line_kind_values":{"SALE":"SALE","REFUND":"REFUND"},"status":"Status","status_failed_values":["FAILED"],
         "gross":"Gross Amount","fee":"MDR","tax_on_fee":"GST on MDR","net":"Net Amount","settlement_date":"Settlement Date",
         "payer_name":"Customer Name","payer_vpa":"VPA","datetime_format":"dd/MM/yyyy HH:mm:ss","date_format":"dd/MM/yyyy"}
        """;

    private static readonly DateOnly Day1 = new(2026, 8, 1);

    [SqlFact]
    public async Task A_bank_csv_imports_idempotently_and_an_accepted_deposit_proposal_shows_as_credited_on_a_locked_day()
    {
        var evidence = await PrepareAsync("COLA");
        try
        {
            var connections = new TestConnections(database.ConnectionString);
            var setup = new SqlServerCollectionsSetupService(connections);
            var import = new SqlServerCollectionsImportService(connections);
            var deposits = new SqlServerCashDepositService(connections);
            var statement = new SqlServerCollectionsStatementQuery(connections);

            var account = await setup.SaveBankAccountAsync(new(null, "SYN_COLA", "Synthetic COLA account", "Synthetic Bank", "0001", "COLA", true, true), "Synthetic test account");
            var header = await setup.ReadSampleHeaderAsync(Fixture("bank-two-columns.csv"), null, 3);
            var draft = new StatementLayoutDraft(null, "SYN_COLA_CSV", "Synthetic COLA CSV", "BANK", account, null, header.HeaderSignatureSha256, null, 3, 1, TwoColumns, true);
            var test = await setup.TestLayoutAsync(Fixture("bank-two-columns.csv"), draft);
            Assert.True(test.SignatureMatches);
            Assert.Equal(5, test.ReadableLines);
            var layout = await setup.SaveLayoutAsync(draft, "Synthetic layout");

            var preview = await import.PreviewAsync(Fixture("bank-two-columns.csv"));
            Assert.Equal(layout, Assert.Single(preview.Layouts).LayoutId);

            var importAudits = await IntAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type='CollectionsImport' AND outcome='Succeeded'");
            var first = await import.ImportAsync(new(Fixture("bank-two-columns.csv"), layout));
            Assert.Equal(CollectionsImportOutcome.Imported, first.Outcome);
            Assert.Equal(5, first.NewLines);
            var ids = await TextAsync($"SELECT STRING_AGG(CONVERT(varchar(20),bank_line_id),',') WITHIN GROUP(ORDER BY bank_line_id) FROM dbo.bank_statement_lines WHERE bank_account_id={account}");
            var credits = await TextAsync($"SELECT CONVERT(varchar(40),SUM(credit_amount)) FROM dbo.bank_statement_lines WHERE bank_account_id={account}");
            Assert.Equal("143956.7800", credits);

            // The same file twice adds nothing; the same lines in another export add nothing.
            Assert.Equal(CollectionsImportOutcome.AlreadyImported, (await import.ImportAsync(new(Fixture("bank-two-columns.csv"), layout))).Outcome);
            var again = await import.ImportAsync(new(Fixture("bank-two-columns-reexport.csv"), layout));
            Assert.Equal(CollectionsImportOutcome.DuplicateContent, again.Outcome);
            Assert.Equal(5, await IntAsync($"SELECT COUNT(*) FROM dbo.bank_statement_lines WHERE bank_account_id={account}"));

            // A superset adds exactly its three new lines, keeps the earlier ids and supersedes the first file.
            var superset = await import.ImportAsync(new(Fixture("bank-two-columns-superset.csv"), layout));
            Assert.Equal(CollectionsImportOutcome.Imported, superset.Outcome);
            Assert.Equal(3, superset.NewLines);
            Assert.Equal(5, superset.AlreadyPresentLines);
            Assert.Equal(1, superset.SupersededFiles);
            Assert.Equal(8, await IntAsync($"SELECT COUNT(*) FROM dbo.bank_statement_lines WHERE bank_account_id={account}"));
            Assert.StartsWith(ids + ",", await TextAsync($"SELECT STRING_AGG(CONVERT(varchar(20),bank_line_id),',') WITHIN GROUP(ORDER BY bank_line_id) FROM dbo.bank_statement_lines WHERE bank_account_id={account}"), StringComparison.Ordinal);
            Assert.Equal(1, await IntAsync($"SELECT COUNT(*) FROM dbo.import_files WHERE report_code='BANK_STATEMENT' AND is_superseded=1 AND superseded_by_import_file_id={superset.ImportFileId} AND store_code IS NULL"));
            Assert.Equal(0, await IntAsync("SELECT COUNT(*) FROM dbo.bank_statement_lines WHERE classification<>'UNCLASSIFIED'"));
            Assert.Equal(importAudits + 2, await IntAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type='CollectionsImport' AND outcome='Succeeded'"));
            Assert.True(Directory.EnumerateFiles(Path.Combine(evidence, "BankSources", "SYN_COLA"), "*.csv", SearchOption.AllDirectories).Any());

            // The daily sheet says 12,500 went to the bank on 1 Aug, and the day is then finalised.
            await database.ExecuteAsync("""
                INSERT dbo.manual_operational_inputs(store_code,business_date,field_code,numeric_value,entered_by,modified_by,change_reason)
                VALUES('COLA','2026-08-01','CASH_DEPOSIT',12500,N'test',N'test',N'Synthetic deposit');
                INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc)
                VALUES('COLA','2026-08-01','LOCKED',N'test',SYSUTCDATETIME());
                """);
            var transit = Assert.Single(await deposits.LoadInTransitAsync(["COLA"]));
            Assert.Equal(12500m, transit.Amount);

            var proposal = Assert.Single(await deposits.LoadProposalsAsync(["COLA"]));
            Assert.False(proposal.IsAmbiguous);
            Assert.Equal("DEP0001", proposal.Line.BankReference);
            Assert.Equal(Day1, proposal.Line.LineDate);

            var confirmation = await deposits.AcceptProposalAsync(new("COLA", Day1, proposal.Line.BankLineId, "Seen on the bank statement"));
            var twice = await Assert.ThrowsAsync<InvalidOperationException>(() => deposits.AcceptProposalAsync(new("COLA", Day1, proposal.Line.BankLineId, "Again")));
            Assert.Contains("already", twice.Message, StringComparison.Ordinal);
            Assert.Equal("CASH_DEPOSIT", await TextAsync($"SELECT classification FROM dbo.bank_statement_lines WHERE bank_line_id={proposal.Line.BankLineId}"));
            Assert.Empty(await deposits.LoadInTransitAsync(["COLA"]));

            var request = new CollectionsStatementRequest(["COLA"], Day1, new(2026, 8, 5), new(2026, 8, 6));
            var cash = Assert.Single((await statement.LoadStatementAsync(request)).Lines, line => line.Mode == "Cash" && line.BusinessDate == Day1);
            Assert.Equal(CollectionsLineState.DepositCredited, cash.State);
            Assert.Equal(Day1, cash.CreditedDate);
            Assert.Equal(0m, cash.Difference);

            // A mistake is withdrawn, never deleted; the deposit is in transit again and the line is free.
            await deposits.WithdrawAsync(confirmation, "Wrong line chosen");
            Assert.Single(await deposits.LoadInTransitAsync(["COLA"]));
            Assert.Equal("UNCLASSIFIED", await TextAsync($"SELECT classification FROM dbo.bank_statement_lines WHERE bank_line_id={proposal.Line.BankLineId}"));
            Assert.Equal(2, await IntAsync($"SELECT COUNT(*) FROM dbo.cash_deposit_confirmation_history WHERE cash_deposit_confirmation_id={confirmation}"));
            var delete = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"DELETE dbo.cash_deposit_confirmations WHERE cash_deposit_confirmation_id={confirmation}"));
            Assert.Equal(51955, delete.Number);
        }
        finally { if (Directory.Exists(evidence)) Directory.Delete(evidence, true); }
    }

    [SqlFact]
    public async Task Provider_settlement_totals_reach_the_statement_as_totals_only_next_to_the_tenders()
    {
        var evidence = await PrepareAsync("COLB");
        try
        {
            var connections = new TestConnections(database.ConnectionString);
            var setup = new SqlServerCollectionsSetupService(connections);
            await setup.SaveMerchantIdAsync(new(null, "CARD_ACQUIRER", "MID-SYN-1", null, "COLB", true), "Synthetic merchant id");
            var header = await setup.ReadSampleHeaderAsync(Fixture("provider-card-gross.csv"), null, 1);
            var layout = await setup.SaveLayoutAsync(new(null, "SYN_CARD_CSV", "Synthetic card CSV", "PROVIDER", null, "CARD_ACQUIRER",
                header.HeaderSignatureSha256, null, 1, 0, Card, true), "Synthetic provider layout");

            var result = await new SqlServerCollectionsImportService(connections).ImportAsync(new(Fixture("provider-card-gross.csv"), layout));
            Assert.Equal(4, result.NewLines);
            Assert.Equal(1, await IntAsync("SELECT COUNT(*) FROM dbo.provider_settlement_lines WHERE store_code IS NULL AND merchant_id=N'MID-SYN-2'"));
            Assert.Equal(1, await IntAsync("SELECT COUNT(*) FROM dbo.provider_settlement_lines WHERE is_failed=1"));

            // Tenders of 1 Aug at COLB: card 900, PhonePe 300, cash 1,000, gift card 50 and an unmapped code.
            await database.ExecuteAsync("""
                DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@invoice bigint;
                INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
                INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end)
                VALUES(@batch,N'synthetic-r022.xlsx',REPLICATE('c',64),1,'R022','COLB','2026-08-01','2026-08-01','2026-08-01');
                SET @file=SCOPE_IDENTITY();
                INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('COLB',N'SYN-1',2026,'2026-08-01');
                SET @invoice=SCOPE_IDENTITY();
                INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
                SELECT @file,N'R022',n,'TENDER' FROM (VALUES(1),(2),(3),(4),(5)) v(n);
                INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id)
                SELECT @invoice,t.code,t.amount,'INR',s.source_lineage_id
                FROM (VALUES(1,N'CARD',900),(2,N'PHONEPE',300),(3,N'CASH',1000),(4,N'GIFTCARD',50),(5,N'SYNTHPAY',20)) t(n,code,amount)
                JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.source_row_number=t.n;
                """);

            var lines = (await new SqlServerCollectionsStatementQuery(connections).LoadStatementAsync(new(["COLB"], Day1, Day1, new(2026, 8, 3)))).Lines;

            var card = Assert.Single(lines, line => line.ProviderKey == "CARD_ACQUIRER");
            Assert.Equal(CollectionsLineState.TotalsOnly, card.State);
            Assert.Equal(900m, card.Expected);
            Assert.Equal(800m, card.Evidence);
            Assert.Equal(-100m, card.Difference);
            Assert.Equal(CollectionsLineState.NoSettlementLinesYet, Assert.Single(lines, line => line.ProviderKey == "PHONEPE").State);
            Assert.Equal(CollectionsLineState.NoOutsideEvidenceExpected, Assert.Single(lines, line => line.Mode == "Gift Card").State);
            Assert.Equal(CollectionsLineState.TenderCodeNotMapped, Assert.Single(lines, line => line.Mode == "Unmapped").State);
            var cash = Assert.Single(lines, line => line.Mode == "Cash");
            Assert.Equal(CollectionsLineState.NoDepositRecorded, cash.State);
            Assert.Equal(1000m, cash.TenderTotal);
        }
        finally { if (Directory.Exists(evidence)) Directory.Delete(evidence, true); }
    }

    [SqlFact]
    public async Task Collections_pii_is_denied_to_store_manager_and_viewer_who_still_import_through_the_procedure()
    {
        await EnsureObjectsAsync();
        await database.ExecuteAsync("""
            IF DATABASE_PRINCIPAL_ID(N'col_manager') IS NULL BEGIN CREATE USER col_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER col_manager; END;
            IF DATABASE_PRINCIPAL_ID(N'col_viewer') IS NULL BEGIN CREATE USER col_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER col_viewer; END;
            INSERT dbo.bank_accounts(account_key,display_name,bank_name,change_reason) VALUES('SYN_PII',N'Synthetic PII account',N'Synthetic Bank',N'Synthetic');
            INSERT dbo.statement_layouts(layout_key,display_name,layout_kind,bank_account_id,header_signature_sha256,mapping_json,change_reason)
            SELECT 'SYN_PII_CSV',N'Synthetic PII layout','BANK',bank_account_id,REPLICATE('b',64),N'{}',N'Synthetic' FROM dbo.bank_accounts WHERE account_key='SYN_PII';
            """);
        var layout = await IntAsync("SELECT layout_id FROM dbo.statement_layouts WHERE layout_key='SYN_PII_CSV'");

        // The scan: every narration, payer, VPA or raw column of a collections table is denied to both roles.
        var unprotected = await IntAsync("""
            SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id
            CROSS JOIN (VALUES(N'etp_store_manager'),(N'etp_viewer')) r(role_name)
            WHERE t.name IN(N'bank_statement_lines',N'provider_settlement_lines')
              AND (c.name LIKE N'%narration%' OR c.name LIKE N'%payer%' OR c.name LIKE N'%vpa%' OR c.name LIKE N'raw[_]%')
              AND NOT EXISTS(SELECT 1 FROM sys.database_permissions p
                WHERE p.major_id=t.object_id AND p.minor_id=c.column_id AND p.state='D' AND p.permission_name='SELECT'
                  AND p.grantee_principal_id=DATABASE_PRINCIPAL_ID(r.role_name))
            """);
        Assert.Equal(0, unprotected);

        // A Store Manager imports through the procedure (ownership chain), on a non-pooled connection, reverting in the batch.
        const string lines = """[{"row":2,"identity":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","value_date":"2026-08-01","narration":"SYNTHETIC NAME","credit":10,"raw":{"Narration":"SYNTHETIC NAME"}}]""";
        var outcome = await TextAsync($"""
            DECLARE @r TABLE(outcome varchar(20),import_file_id bigint,new_lines int,present_lines int,invalid_lines int,possible_duplicates int,superseded_files int);
            EXECUTE AS USER='col_manager';
            BEGIN TRY
              INSERT @r EXEC dbo.collections_import_statement {layout},N'synthetic.csv','{new string('d', 64)}',10,N'synthetic',N'{lines}';
            END TRY BEGIN CATCH REVERT; THROW; END CATCH;
            REVERT;
            SELECT outcome FROM @r;
            """, pooling: false);
        Assert.Equal("IMPORTED", outcome);

        foreach (var user in new[] { "col_manager", "col_viewer" })
        {
            // 230: column denied; 229: the whole table denied (the Viewer, review m-4).
            foreach (var probe in new[] { "SELECT TOP(1) narration FROM dbo.bank_statement_lines", "SELECT TOP(1) raw_json FROM dbo.bank_statement_lines",
                         "SELECT TOP(1) payer_vpa FROM dbo.provider_settlement_lines", "SELECT TOP(1) payer_name FROM dbo.provider_settlement_lines" })
                Assert.Contains(await ProbeAsync(user, probe), new[] { 229, 230 });
            Assert.Equal(user == "col_viewer" ? 229 : 0, await ProbeAsync(user, "SELECT TOP(1) bank_reference,credit_amount,line_date FROM dbo.bank_statement_lines"));
            Assert.Equal(user == "col_viewer" ? 229 : 0, await ProbeAsync(user, "SELECT TOP(1) merchant_id FROM dbo.provider_settlement_lines"));
            Assert.Equal(user == "col_viewer" ? 229 : 0, await ProbeAsync(user, "SELECT TOP(1) amount FROM dbo.cash_deposit_confirmations"));
            Assert.Equal(0, await ProbeAsync(user, "SELECT TOP(1) * FROM dbo.v_collection_evidence"));
            Assert.Equal(0, await ProbeAsync(user, "SELECT TOP(1) * FROM dbo.v_cash_deposit_status"));
            Assert.NotEqual(0, await ProbeAsync(user, "INSERT dbo.bank_accounts(account_key,display_name,bank_name,change_reason) VALUES('SYN_X',N'x',N'x',N'x')"));
        }
        Assert.NotEqual(0, await ProbeAsync("col_viewer", "SELECT TOP(1) * FROM dbo.v_cash_deposits_in_transit"));
        Assert.Equal(0, await ProbeAsync("col_manager", "SELECT TOP(1) * FROM dbo.v_cash_deposits_in_transit"));
    }

    [SqlFact]
    public async Task Collections_contracts_refuse_bad_keys_and_mismatched_providers_and_ship_no_settlement_rule()
    {
        var lower = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
            "INSERT dbo.bank_accounts(account_key,display_name,bank_name,change_reason) VALUES('syn_lower',N'x',N'x',N'x')"));
        Assert.Contains("CK_bank_accounts_key", lower.Message, StringComparison.Ordinal);
        var digits = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
            "INSERT dbo.bank_accounts(account_key,display_name,bank_name,change_reason) VALUES('SYN_DIGITS',N'Account 50100012345678',N'x',N'x')"));
        Assert.Contains("CK_bank_accounts_names", digits.Message, StringComparison.Ordinal);

        var mismatch = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
            "UPDATE dbo.tender_modes SET provider_key='CARD_ACQUIRER' WHERE source_tender_code=N'PHONEPE'"));
        Assert.Equal(51950, mismatch.Number);
        // Service Card settles through the card acquirer (the Service prefix is ignored).
        Assert.Equal("CARD_ACQUIRER", await TextAsync("SELECT provider_key FROM dbo.tender_modes WHERE source_tender_code=N'SERVICE_CARD'"));
        Assert.Equal("AIRPAY", await TextAsync("SELECT provider_key FROM dbo.tender_modes WHERE source_tender_code=N'PAYMENTTYPE25'"));
        Assert.Equal(1, await IntAsync("SELECT CONVERT(int,is_till_cash) FROM dbo.tender_modes WHERE source_tender_code=N'CASH'"));

        Assert.Equal(0, await IntAsync("SELECT COUNT(*) FROM dbo.collection_policies"));
        Assert.Equal(6, await IntAsync("SELECT COUNT(*) FROM dbo.collection_providers"));
        Assert.Equal(6, await IntAsync("SELECT COUNT(*) FROM dbo.collections_setup_history WHERE table_name='collection_providers' AND action='I'"));
        var history = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("DELETE dbo.collections_setup_history"));
        Assert.Equal(51957, history.Number);
    }

    private async Task<string> PrepareAsync(string store)
    {
        await EnsureObjectsAsync();
        var evidence = Path.Combine(Path.GetTempPath(), "EtpCollections_" + Guid.NewGuid().ToString("N"));
        await database.ExecuteAsync($"""
            IF NOT EXISTS(SELECT 1 FROM dbo.stores WHERE store_code='{store}') INSERT dbo.stores(store_code,store_name) VALUES('{store}',N'Synthetic collections store');
            UPDATE dbo.product_settings SET collections_evidence_root=N'{evidence}',modified_by=N'test',change_reason=N'Synthetic evidence folder' WHERE product_setting_id=1;
            """);
        return evidence;
    }

    /// <summary>
    /// Review m-8 (1.12 integration): the fixture's bootstrap runs lane A3's objects step from the shipped database\objects folder, so
    /// every collections object must already exist and be journaled. This asserts it, rather than applying the files itself and
    /// hiding a runner failure.
    /// </summary>
    private async Task EnsureObjectsAsync()
    {
        var folder = Repository("database", "objects");
        foreach (var file in Directory.GetFiles(folder, "*.sql").Where(path => Path.GetFileName(path).Contains("collection", StringComparison.Ordinal)
                     || Path.GetFileName(path).Contains("cash_deposit", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file)[3..];
            Assert.True(await IntAsync($"SELECT CASE WHEN OBJECT_ID(N'{name}') IS NULL THEN 0 ELSE 1 END") == 1, $"{name} was not created by the objects step.");
            Assert.True(await IntAsync($"SELECT COUNT(*) FROM dbo.schema_objects WHERE object_name=N'{name}'") == 1, $"{name} is not in dbo.schema_objects.");
        }
    }

    private async Task<int> ProbeAsync(string user, string sql)
    {
        var escaped = sql.Replace("'", "''", StringComparison.Ordinal);
        var batch = $"""
            SET NOCOUNT ON; DECLARE @r int=0;
            EXECUTE AS USER='{user}';
            BEGIN TRY BEGIN TRANSACTION; EXEC sys.sp_executesql N'{escaped}'; ROLLBACK; END TRY
            BEGIN CATCH SET @r=ERROR_NUMBER(); IF @@TRANCOUNT>0 ROLLBACK; END CATCH;
            REVERT;
            SELECT @r AS probe_result;
            """;
        // A probe that succeeds returns its own result set first; the answer is the last one.
        await using var connection = new SqlConnection(TestSqlConnections.ForDatabase(database.Name, pooling: false));
        await connection.OpenAsync();
        await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
        int? result = null;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                if (reader.FieldCount == 1 && reader.GetName(0) == "probe_result" && await reader.ReadAsync()) result = reader.GetInt32(0);
            } while (await reader.NextResultAsync());
        }
        Assert.NotNull(result);
        return result.Value;
    }

    private async Task<int> IntAsync(string sql, bool pooling = true) => Convert.ToInt32(await ScalarAsync(sql, pooling), System.Globalization.CultureInfo.InvariantCulture);

    private async Task<string> TextAsync(string sql, bool pooling = true) => Convert.ToString(await ScalarAsync(sql, pooling), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private async Task<object?> ScalarAsync(string sql, bool pooling)
    {
        await using var connection = new SqlConnection(TestSqlConnections.ForDatabase(database.Name, pooling));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        return await command.ExecuteScalarAsync();
    }

    private static string Fixture(string name) => Path.Combine(Repository("tests-dotnet", "fixtures", "collections-synthetic"), name);

    private static string Repository(params string[] parts)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine([folder.FullName, .. parts]);
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(folder.FullName, "Etp.Reporting.slnx"))) return candidate;
        }
        throw new DirectoryNotFoundException(string.Join('/', parts));
    }

    /// <summary>The 1.12 connection factory contract (A1) over the test database.</summary>
    private sealed class TestConnections(string connectionString) : IEtpConnectionFactory
    {
        public string DatabaseName => new SqlConnectionStringBuilder(connectionString).InitialCatalog;

        public SqlConnection Create(EtpConnectionPurpose purpose = EtpConnectionPurpose.Default) => new(connectionString);

        public async Task<SqlConnection> OpenAsync(EtpConnectionPurpose purpose = EtpConnectionPurpose.Default, CancellationToken ct = default)
        {
            var connection = Create(purpose);
            try { await connection.OpenAsync(ct); return connection; }
            catch { await connection.DisposeAsync(); throw; }
        }
    }
}
