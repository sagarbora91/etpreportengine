using System.Data;
using Etp.Reporting.Application.Collections;
using Etp.Reporting.Import.Collections;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;
using static Etp.Reporting.Infrastructure.SqlServer.Collections.CollectionsAccess;

namespace Etp.Reporting.Infrastructure.SqlServer.Collections;

/// <summary>Settings > Collections setup (master plan 8a tasks 1-2). Every role reads; every change is Owner-only, carries a
/// reason, is copied to collections_setup_history by trigger and audited as CollectionsSetup. Store Manager and Viewer also
/// hold no write permission on these tables in SQL (0063).</summary>
public sealed class SqlServerCollectionsSetupService : ICollectionsSetupService
{
    private const int PreviewLines = 20;
    private readonly ICollectionsSession session;

    public SqlServerCollectionsSetupService(IEtpConnectionFactory connections) : this(new SqlCollectionsSession(connections)) { }

    internal SqlServerCollectionsSetupService(ICollectionsSession session) => this.session = session ?? throw new ArgumentNullException(nameof(session));

    public async Task<CollectionsSetup> LoadAsync(CancellationToken cancellationToken = default)
    {
        await RequireAsync(session, CanView, SetupAudit, ViewRequired, cancellationToken);
        const string sql = """
            SELECT bank_account_id,account_key,display_name,bank_name,account_last4,store_code,takes_cash_deposits,active,modified_by,modified_utc
            FROM dbo.bank_accounts ORDER BY account_key;
            SELECT provider_key,display_name,mode,settles_via,statement_kind,granularity,bank_account_id,rounding_tolerance,active,modified_by,modified_utc
            FROM dbo.collection_providers ORDER BY provider_key;
            SELECT provider_merchant_id,provider_key,merchant_id,terminal_id,store_code,active FROM dbo.provider_merchant_ids ORDER BY provider_key,merchant_id,terminal_id;
            SELECT policy_id,store_code,mode,provider_key,settlement_window_days,overdue_after_days,cash_tolerance,effective_from,effective_to
            FROM dbo.collection_policies ORDER BY mode,store_code,provider_key,effective_from;
            SELECT layout_id,layout_key,display_name,layout_kind,bank_account_id,provider_key,header_signature_sha256,sheet_name,header_row,
                   skip_footer_rows,mapping_json,version,active
            FROM dbo.statement_layouts ORDER BY layout_key;
            SELECT source_tender_code,mode,provider_key,is_till_cash,active FROM dbo.tender_modes ORDER BY source_tender_code;
            SELECT collections_evidence_root FROM dbo.product_settings WHERE product_setting_id=1;
            """;
        await using var connection = await session.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var accounts = new List<BankAccountRow>();
        while (await reader.ReadAsync(cancellationToken))
            accounts.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), OptionalString(reader, 4),
                OptionalString(reader, 5), reader.GetBoolean(6), reader.GetBoolean(7), reader.GetString(8), reader.GetDateTime(9)));
        await reader.NextResultAsync(cancellationToken);
        var providers = new List<CollectionProviderRow>();
        while (await reader.ReadAsync(cancellationToken))
            providers.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                OptionalInt(reader, 6), OptionalDecimal(reader, 7), reader.GetBoolean(8), reader.GetString(9), reader.GetDateTime(10)));
        await reader.NextResultAsync(cancellationToken);
        var merchants = new List<MerchantIdRow>();
        while (await reader.ReadAsync(cancellationToken))
            merchants.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), OptionalString(reader, 3), reader.GetString(4), reader.GetBoolean(5)));
        await reader.NextResultAsync(cancellationToken);
        var policies = new List<CollectionPolicyRow>();
        while (await reader.ReadAsync(cancellationToken))
            policies.Add(new(reader.GetInt32(0), OptionalString(reader, 1), reader.GetString(2), OptionalString(reader, 3), OptionalInt(reader, 4),
                OptionalInt(reader, 5), OptionalDecimal(reader, 6), RequiredDate(reader, 7), OptionalDate(reader, 8)));
        await reader.NextResultAsync(cancellationToken);
        var layouts = new List<StatementLayoutRow>();
        while (await reader.ReadAsync(cancellationToken))
            layouts.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), OptionalInt(reader, 4), OptionalString(reader, 5),
                reader.GetString(6), OptionalString(reader, 7), reader.GetInt32(8), reader.GetInt32(9), reader.GetString(10), reader.GetInt32(11), reader.GetBoolean(12)));
        await reader.NextResultAsync(cancellationToken);
        var tenders = new List<TenderProviderRow>();
        while (await reader.ReadAsync(cancellationToken))
            tenders.Add(new(reader.GetString(0), reader.GetString(1), OptionalString(reader, 2), reader.GetBoolean(3), reader.GetBoolean(4)));
        await reader.NextResultAsync(cancellationToken);
        var root = await reader.ReadAsync(cancellationToken) ? reader.GetString(0) : string.Empty;
        return new(accounts, providers, merchants, policies, layouts, tenders, root);
    }

    public async Task<int> SaveBankAccountAsync(BankAccountDraft draft, string reason, CancellationToken cancellationToken = default)
    {
        var value = CollectionsSetupRules.Normalise(draft);
        var why = CollectionsSetupRules.Reason(reason);
        await RequireAsync(session, CanAdminister, SetupAudit, OwnerRequired, cancellationToken);
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @saved int=@id;
            IF @saved IS NULL
            BEGIN
              INSERT dbo.bank_accounts(account_key,display_name,bank_name,account_last4,store_code,takes_cash_deposits,active,change_reason)
              VALUES(@key,@name,@bank,@last4,@store,@cash,@active,@reason);
              SET @saved=CONVERT(int,SCOPE_IDENTITY());
            END
            ELSE
            BEGIN
              -- The key names the evidence folder of every statement already kept, so it is fixed once lines exist.
              IF EXISTS(SELECT 1 FROM dbo.bank_accounts a WHERE a.bank_account_id=@saved AND a.account_key<>@key)
                 AND EXISTS(SELECT 1 FROM dbo.bank_statement_lines b WHERE b.bank_account_id=@saved)
                THROW 51958,'This account already has statement lines, so its key cannot change. Add a new account instead.',1;
              UPDATE dbo.bank_accounts SET account_key=@key,display_name=@name,bank_name=@bank,account_last4=@last4,store_code=@store,
                takes_cash_deposits=@cash,active=@active,change_reason=@reason,modified_by=ORIGINAL_LOGIN(),modified_utc=SYSUTCDATETIME()
              WHERE bank_account_id=@saved;
              IF @@ROWCOUNT<>1 THROW 51958,'This bank account no longer exists. Refresh the list.',1;
            END;
            EXEC dbo.record_operational_audit 'CollectionsSetup','Succeeded',N'Collections setup changed',N'database';
            COMMIT TRANSACTION;
            SELECT @saved;
            """;
        return await SaveAsync(sql, command =>
        {
            Add(command, "@id", SqlDbType.Int, value.Id);
            Add(command, "@key", SqlDbType.VarChar, value.AccountKey, 30);
            Add(command, "@name", SqlDbType.NVarChar, value.DisplayName, 100);
            Add(command, "@bank", SqlDbType.NVarChar, value.BankName, 100);
            Add(command, "@last4", SqlDbType.Char, value.AccountLast4, 4);
            Add(command, "@store", SqlDbType.VarChar, value.StoreCode, 30);
            Add(command, "@cash", SqlDbType.Bit, value.TakesCashDeposits);
            Add(command, "@active", SqlDbType.Bit, value.Active);
            Add(command, "@reason", SqlDbType.NVarChar, why, 500);
        }, cancellationToken);
    }

    public async Task SaveProviderAsync(ProviderDraft draft, string reason, CancellationToken cancellationToken = default)
    {
        var value = CollectionsSetupRules.Normalise(draft);
        var why = CollectionsSetupRules.Reason(reason);
        await RequireAsync(session, CanAdminister, SetupAudit, OwnerRequired, cancellationToken);
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            MERGE dbo.collection_providers WITH(HOLDLOCK) t USING(SELECT @key provider_key) s ON t.provider_key=s.provider_key
            WHEN MATCHED THEN UPDATE SET display_name=@name,mode=@mode,settles_via=@settles,statement_kind=@kind,granularity=@granularity,
              bank_account_id=@account,rounding_tolerance=@tolerance,active=@active,change_reason=@reason,modified_by=ORIGINAL_LOGIN(),modified_utc=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT(provider_key,display_name,mode,settles_via,statement_kind,granularity,bank_account_id,rounding_tolerance,active,change_reason)
              VALUES(@key,@name,@mode,@settles,@kind,@granularity,@account,@tolerance,@active,@reason);
            EXEC dbo.record_operational_audit 'CollectionsSetup','Succeeded',N'Collections setup changed',N'database';
            COMMIT TRANSACTION;
            SELECT 0;
            """;
        await SaveAsync(sql, command =>
        {
            Add(command, "@key", SqlDbType.VarChar, value.ProviderKey, 30);
            Add(command, "@name", SqlDbType.NVarChar, value.DisplayName, 100);
            Add(command, "@mode", SqlDbType.VarChar, value.Mode, 30);
            Add(command, "@settles", SqlDbType.VarChar, value.SettlesVia, 10);
            Add(command, "@kind", SqlDbType.VarChar, value.StatementKind, 25);
            Add(command, "@granularity", SqlDbType.VarChar, value.Granularity, 20);
            Add(command, "@account", SqlDbType.Int, value.BankAccountId);
            Add(command, "@tolerance", SqlDbType.Decimal, value.RoundingTolerance);
            command.Parameters["@tolerance"].Precision = 19; command.Parameters["@tolerance"].Scale = 4;
            Add(command, "@active", SqlDbType.Bit, value.Active);
            Add(command, "@reason", SqlDbType.NVarChar, why, 500);
        }, cancellationToken);
    }

    public async Task<int> SaveMerchantIdAsync(MerchantIdDraft draft, string reason, CancellationToken cancellationToken = default)
    {
        var value = CollectionsSetupRules.Normalise(draft);
        var why = CollectionsSetupRules.Reason(reason);
        await RequireAsync(session, CanAdminister, SetupAudit, OwnerRequired, cancellationToken);
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @saved int=@id;
            IF @saved IS NULL
            BEGIN
              INSERT dbo.provider_merchant_ids(provider_key,merchant_id,terminal_id,store_code,active,change_reason)
              VALUES(@provider,@merchant,@terminal,@store,@active,@reason);
              SET @saved=CONVERT(int,SCOPE_IDENTITY());
            END
            ELSE
            BEGIN
              UPDATE dbo.provider_merchant_ids SET provider_key=@provider,merchant_id=@merchant,terminal_id=@terminal,store_code=@store,active=@active,
                change_reason=@reason,modified_by=ORIGINAL_LOGIN(),modified_utc=SYSUTCDATETIME()
              WHERE provider_merchant_id=@saved;
              IF @@ROWCOUNT<>1 THROW 51958,'This merchant id no longer exists. Refresh the list.',1;
            END;
            EXEC dbo.record_operational_audit 'CollectionsSetup','Succeeded',N'Collections setup changed',N'database';
            COMMIT TRANSACTION;
            SELECT @saved;
            """;
        return await SaveAsync(sql, command =>
        {
            Add(command, "@id", SqlDbType.Int, value.Id);
            Add(command, "@provider", SqlDbType.VarChar, value.ProviderKey, 30);
            Add(command, "@merchant", SqlDbType.NVarChar, value.MerchantId, 50);
            Add(command, "@terminal", SqlDbType.NVarChar, value.TerminalId, 50);
            Add(command, "@store", SqlDbType.VarChar, value.StoreCode, 30);
            Add(command, "@active", SqlDbType.Bit, value.Active);
            Add(command, "@reason", SqlDbType.NVarChar, why, 500);
        }, cancellationToken);
    }

    public async Task<int> SavePolicyAsync(CollectionPolicyDraft draft, string reason, CancellationToken cancellationToken = default)
    {
        var value = CollectionsSetupRules.Normalise(draft);
        var why = CollectionsSetupRules.Reason(reason);
        await RequireAsync(session, CanAdminister, SetupAudit, OwnerRequired, cancellationToken);
        // 1.12.1 P2: two rules of the same scope never cover the same day (CollectionPolicyResolver.Overlaps says the same
        // first), and a provider rule names a provider of that mode (Service prefix ignored, as trg_tender_modes_provider_mode).
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @saved int=@id;
            IF @provider IS NOT NULL
               AND NOT EXISTS(SELECT 1 FROM dbo.collection_providers p WHERE p.provider_key=@provider
                              AND p.mode=CASE WHEN @mode LIKE 'Service %' THEN STUFF(@mode,1,8,'') ELSE @mode END)
              THROW 52001,'This provider settles a different payment mode. Choose a provider of this mode, or leave it blank.',1;
            IF EXISTS(SELECT 1 FROM dbo.collection_policies p WITH(UPDLOCK,HOLDLOCK)
                      WHERE (@saved IS NULL OR p.policy_id<>@saved) AND p.mode=@mode
                        AND ISNULL(p.store_code,'')=ISNULL(@store,'') AND ISNULL(p.provider_key,'')=ISNULL(@provider,'')
                        AND p.effective_from<=ISNULL(@to,'99991231') AND ISNULL(p.effective_to,'99991231')>=@from)
              THROW 52000,'Another settlement rule for the same mode, store and provider covers some of these dates. Give the other rule an end date first.',1;
            IF @saved IS NULL
            BEGIN
              INSERT dbo.collection_policies(store_code,mode,provider_key,settlement_window_days,overdue_after_days,cash_tolerance,effective_from,effective_to,change_reason)
              VALUES(@store,@mode,@provider,@window,@overdue,@tolerance,@from,@to,@reason);
              SET @saved=CONVERT(int,SCOPE_IDENTITY());
            END
            ELSE
            BEGIN
              UPDATE dbo.collection_policies SET store_code=@store,mode=@mode,provider_key=@provider,settlement_window_days=@window,
                overdue_after_days=@overdue,cash_tolerance=@tolerance,effective_from=@from,effective_to=@to,change_reason=@reason,
                modified_by=ORIGINAL_LOGIN(),modified_utc=SYSUTCDATETIME()
              WHERE policy_id=@saved;
              IF @@ROWCOUNT<>1 THROW 51958,'This settlement rule no longer exists. Refresh the list.',1;
            END;
            EXEC dbo.record_operational_audit 'CollectionsSetup','Succeeded',N'Collections setup changed',N'database';
            COMMIT TRANSACTION;
            SELECT @saved;
            """;
        return await SaveAsync(sql, command =>
        {
            Add(command, "@id", SqlDbType.Int, value.Id);
            Add(command, "@store", SqlDbType.VarChar, value.StoreCode, 30);
            Add(command, "@mode", SqlDbType.VarChar, value.Mode, 30);
            Add(command, "@provider", SqlDbType.VarChar, value.ProviderKey, 30);
            Add(command, "@window", SqlDbType.Int, value.SettlementWindowDays);
            Add(command, "@overdue", SqlDbType.Int, value.OverdueAfterDays);
            Add(command, "@tolerance", SqlDbType.Decimal, value.CashTolerance);
            command.Parameters["@tolerance"].Precision = 19; command.Parameters["@tolerance"].Scale = 4;
            Add(command, "@from", SqlDbType.Date, Date(value.EffectiveFrom));
            Add(command, "@to", SqlDbType.Date, Date(value.EffectiveTo));
            Add(command, "@reason", SqlDbType.NVarChar, why, 500);
        }, cancellationToken);
    }

    public async Task<int> SaveLayoutAsync(StatementLayoutDraft draft, string reason, CancellationToken cancellationToken = default)
    {
        var value = CollectionsSetupRules.Normalise(draft);
        var why = CollectionsSetupRules.Reason(reason);
        ParseLayout(value);
        await RequireAsync(session, CanAdminister, SetupAudit, OwnerRequired, cancellationToken);
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @saved int=@id;
            IF @saved IS NULL
            BEGIN
              INSERT dbo.statement_layouts(layout_key,display_name,layout_kind,bank_account_id,provider_key,header_signature_sha256,sheet_name,
                header_row,skip_footer_rows,mapping_json,active,change_reason)
              VALUES(@key,@name,@kind,@account,@provider,@signature,@sheet,@header,@footer,@mapping,@active,@reason);
              SET @saved=CONVERT(int,SCOPE_IDENTITY());
            END
            ELSE
            BEGIN
              -- Lines keep the layout they were read with, so a used layout keeps its account or provider.
              IF EXISTS(SELECT 1 FROM dbo.statement_layouts l WHERE l.layout_id=@saved
                    AND (l.layout_kind<>@kind OR ISNULL(l.bank_account_id,-1)<>ISNULL(@account,-1) OR ISNULL(l.provider_key,'')<>ISNULL(@provider,'')))
                 AND (EXISTS(SELECT 1 FROM dbo.bank_statement_lines WHERE layout_id=@saved) OR EXISTS(SELECT 1 FROM dbo.provider_settlement_lines WHERE layout_id=@saved))
                THROW 51958,'Lines were already imported with this layout, so its account or provider cannot change. Add a new layout instead.',1;
              UPDATE dbo.statement_layouts SET layout_key=@key,display_name=@name,layout_kind=@kind,bank_account_id=@account,provider_key=@provider,
                header_signature_sha256=@signature,sheet_name=@sheet,header_row=@header,skip_footer_rows=@footer,mapping_json=@mapping,active=@active,
                version=version+1,change_reason=@reason,modified_by=ORIGINAL_LOGIN(),modified_utc=SYSUTCDATETIME()
              WHERE layout_id=@saved;
              IF @@ROWCOUNT<>1 THROW 51958,'This file layout no longer exists. Refresh the list.',1;
            END;
            EXEC dbo.record_operational_audit 'CollectionsSetup','Succeeded',N'Collections setup changed',N'database';
            COMMIT TRANSACTION;
            SELECT @saved;
            """;
        return await SaveAsync(sql, command =>
        {
            Add(command, "@id", SqlDbType.Int, value.Id);
            Add(command, "@key", SqlDbType.VarChar, value.LayoutKey, 30);
            Add(command, "@name", SqlDbType.NVarChar, value.DisplayName, 100);
            Add(command, "@kind", SqlDbType.VarChar, value.LayoutKind, 10);
            Add(command, "@account", SqlDbType.Int, value.BankAccountId);
            Add(command, "@provider", SqlDbType.VarChar, value.ProviderKey, 30);
            Add(command, "@signature", SqlDbType.Char, value.HeaderSignatureSha256, 64);
            Add(command, "@sheet", SqlDbType.NVarChar, value.SheetName, 100);
            Add(command, "@header", SqlDbType.Int, value.HeaderRow);
            Add(command, "@footer", SqlDbType.Int, value.SkipFooterRows);
            Add(command, "@mapping", SqlDbType.NVarChar, value.MappingJson.Trim(), -1);
            Add(command, "@active", SqlDbType.Bit, value.Active);
            Add(command, "@reason", SqlDbType.NVarChar, why, 500);
        }, cancellationToken);
    }

    public async Task SaveTenderProviderAsync(TenderProviderDraft draft, string reason, CancellationToken cancellationToken = default)
    {
        var value = CollectionsSetupRules.Normalise(draft);
        CollectionsSetupRules.Reason(reason);
        await RequireAsync(session, CanAdminister, SetupAudit, OwnerRequired, cancellationToken);
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            UPDATE dbo.tender_modes SET provider_key=@provider,is_till_cash=@cash,modified_by=ORIGINAL_LOGIN(),modified_utc=SYSUTCDATETIME()
            WHERE source_tender_code=@code;
            IF @@ROWCOUNT<>1 THROW 51958,'This payment code is not in the tender modes list.',1;
            EXEC dbo.record_operational_audit 'CollectionsSetup','Succeeded',N'Collections setup changed',N'database';
            COMMIT TRANSACTION;
            SELECT 0;
            """;
        await SaveAsync(sql, command =>
        {
            Add(command, "@code", SqlDbType.NVarChar, value.SourceTenderCode, 80);
            Add(command, "@provider", SqlDbType.VarChar, value.ProviderKey, 30);
            Add(command, "@cash", SqlDbType.Bit, value.IsTillCash);
        }, cancellationToken);
    }

    public async Task SaveEvidenceRootAsync(string path, string reason, CancellationToken cancellationToken = default)
    {
        var root = CollectionsSetupRules.EvidenceRoot(path);
        var why = CollectionsSetupRules.Reason(reason);
        await RequireAsync(session, CanAdminister, SetupAudit, OwnerRequired, cancellationToken);
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            UPDATE dbo.product_settings SET collections_evidence_root=@root,modified_by=ORIGINAL_LOGIN(),modified_utc=SYSUTCDATETIME(),change_reason=@reason
            WHERE product_setting_id=1;
            EXEC dbo.record_operational_audit 'CollectionsSetup','Succeeded',N'Collections setup changed',N'database';
            COMMIT TRANSACTION;
            SELECT 0;
            """;
        await SaveAsync(sql, command =>
        {
            Add(command, "@root", SqlDbType.NVarChar, root, 400);
            Add(command, "@reason", SqlDbType.NVarChar, why, 500);
        }, cancellationToken);
    }

    public async Task<SampleFileHeader> ReadSampleHeaderAsync(string samplePath, string? sheetName, int headerRow, CancellationToken cancellationToken = default)
    {
        if (headerRow is < 1 or > 1000) throw new ArgumentException("The header row must be 1 to 1000.");
        await RequireAsync(session, CanAdminister, SetupAudit, OwnerRequired, cancellationToken);
        var snapshot = await ReadFileAsync(samplePath, cancellationToken);
        var header = StatementLayoutReader.ReadHeader(snapshot, string.IsNullOrWhiteSpace(sheetName) ? null : sheetName.Trim(), headerRow, out var problem)
            ?? throw new InvalidOperationException(problem);
        return new(header.SheetNames, header.SheetName, header.HeaderRow, header.Headers, header.HeaderSignature);
    }

    public async Task<LayoutTestResult> TestLayoutAsync(string samplePath, StatementLayoutDraft draft, CancellationToken cancellationToken = default)
    {
        await RequireAsync(session, CanAdminister, SetupAudit, OwnerRequired, cancellationToken);
        StatementLayoutDraft value;
        StatementLayout layout;
        try
        {
            value = CollectionsSetupRules.Normalise(draft);
            layout = ParseLayout(value);
        }
        catch (ArgumentException exception)
        {
            return new(false, [exception.Message], 0, 0, []);
        }
        var snapshot = await ReadFileAsync(samplePath, cancellationToken);
        var result = StatementLayoutReader.Read(snapshot, layout, value.SheetName, value.HeaderRow, value.SkipFooterRows, "TEST");
        var matches = result.Header is not null && string.Equals(result.Header.HeaderSignature, value.HeaderSignatureSha256, StringComparison.Ordinal);
        var problems = result.Problems.ToList();
        if (result.Header is not null && !matches) problems.Insert(0, "This file's header row is not the one the layout was made from.");
        var first = result.Lines.Take(PreviewLines).Select(line => new LayoutTestLine(line.RowNumber, line.LineDate, line.Reference ?? line.TxnReference,
            line.Credit, line.Debit, line.LineKind, line.Gross, line.Net, line.MerchantId, line.ParseError)).ToList();
        return new(matches, problems, result.ReadableLines, result.UnreadableLines, first);
    }

    internal static StatementLayout ParseLayout(StatementLayoutDraft value)
    {
        try { return StatementLayout.Parse(value.MappingJson, value.LayoutKind == "BANK" ? StatementLayoutKind.Bank : StatementLayoutKind.Provider); }
        catch (StatementLayoutException exception) { throw new ArgumentException(exception.Message, exception); }
    }

    private async Task<int> SaveAsync(string sql, Action<SqlCommand> parameters, CancellationToken cancellationToken)
    {
        await using var connection = await session.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        parameters(command);
        try
        {
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (SqlException exception) when (Plain(exception) is { } message)
        {
            throw new InvalidOperationException(message, exception);
        }
    }
}
