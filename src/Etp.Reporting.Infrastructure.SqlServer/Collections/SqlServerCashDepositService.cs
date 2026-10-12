using System.Data;
using Etp.Reporting.Application.Collections;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;
using static Etp.Reporting.Infrastructure.SqlServer.Collections.CollectionsAccess;

namespace Etp.Reporting.Infrastructure.SqlServer.Collections;

/// <summary>
/// Today > "Bank credited" (decision 44 #7; 112-WAVE-PLAN D-b). "Sent" is the daily sheet's CASH_DEPOSIT (v_cash_deposits_sent);
/// "credited" is a row of cash_deposit_confirmations. R-DEP-1 (<see cref="DepositProposalRule"/>) only proposes; the Owner
/// accepts a proposal, records a credit by hand, or withdraws one with a reason. All three are allowed on a LOCKED day and are
/// audited as CashDepositConfirmed. SQL re-checks the rule inside the accepting transaction, so a stale proposal is refused.
/// </summary>
public sealed class SqlServerCashDepositService : ICashDepositService
{
    private readonly ICollectionsSession session;

    public SqlServerCashDepositService(IEtpConnectionFactory connections) : this(new SqlCollectionsSession(connections)) { }

    internal SqlServerCashDepositService(ICollectionsSession session) => this.session = session ?? throw new ArgumentNullException(nameof(session));

    public async Task<IReadOnlyList<DepositInTransit>> LoadInTransitAsync(IReadOnlyList<string>? storeCodes, CancellationToken cancellationToken = default)
    {
        await RequireAsync(session, CanImport, DepositAudit, ImportRequired, cancellationToken);
        return await LoadInTransitCoreAsync(session, storeCodes, cancellationToken);
    }

    internal static async Task<IReadOnlyList<DepositInTransit>> LoadInTransitCoreAsync(ICollectionsSession session, IReadOnlyList<string>? storeCodes, CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.v_cash_deposits_in_transit',N'V') IS NOT NULL
              SELECT store_code,deposit_business_date,sent_amount,age_days FROM dbo.v_cash_deposits_in_transit
              WHERE @stores IS NULL OR store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores))
              ORDER BY deposit_business_date,store_code;
            """;
        await using var connection = await session.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@stores", SqlDbType.NVarChar, StoresJson(storeCodes), -1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<DepositInTransit>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), RequiredDate(reader, 1), reader.GetDecimal(2), reader.GetInt32(3)));
        return rows;
    }

    public async Task<IReadOnlyList<DepositProposal>> LoadProposalsAsync(IReadOnlyList<string>? storeCodes, CancellationToken cancellationToken = default)
    {
        await RequireAsync(session, CanAdminister, DepositAudit, OwnerRequired, cancellationToken);
        var deposits = await LoadInTransitCoreAsync(session, storeCodes, cancellationToken);
        if (deposits.Count == 0) return [];
        // Credit lines of the accounts that take cash deposits, current, not yet used by an active confirmation and not
        // classified as something else, dated within the window of any deposit in transit.
        const string sql = """
            SELECT b.bank_line_id,b.bank_account_id,a.account_key,a.display_name,a.store_code,b.line_date,b.credit_amount,b.bank_reference,b.narration
            FROM dbo.bank_statement_lines b
            JOIN dbo.bank_accounts a ON a.bank_account_id=b.bank_account_id
            WHERE a.active=1 AND a.takes_cash_deposits=1 AND b.is_current=1 AND b.credit_amount IS NOT NULL
              AND b.classification IN('UNCLASSIFIED','CASH_DEPOSIT') AND b.line_date BETWEEN @from AND @to
              AND NOT EXISTS(SELECT 1 FROM dbo.cash_deposit_confirmations c WHERE c.bank_line_id=b.bank_line_id AND c.withdrawn_utc IS NULL)
            ORDER BY b.line_date,b.bank_line_id;
            """;
        var from = deposits.Min(deposit => deposit.DepositDate);
        var to = deposits.Max(deposit => deposit.DepositDate).AddDays(DepositProposalRule.WindowDays);
        await using var connection = await session.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@from", SqlDbType.Date, Date(from));
        Add(command, "@to", SqlDbType.Date, Date(to));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var lines = new List<DepositCandidateLine>();
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(new(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), OptionalString(reader, 4),
                RequiredDate(reader, 5), reader.GetDecimal(6), OptionalString(reader, 7), OptionalString(reader, 8)));
        return DepositProposalRule.Propose(deposits, lines);
    }

    public async Task<long> AcceptProposalAsync(DepositProposalAcceptance acceptance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acceptance);
        var store = Store(acceptance.StoreCode);
        var reason = CollectionsSetupRules.Reason(acceptance.Reason);
        await RequireAsync(session, CanAdminister, DepositAudit, OwnerRequired, cancellationToken);
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @sent decimal(19,4),@line_date date,@credit decimal(19,4),@account int,@reference nvarchar(100),@id bigint;
            SELECT @sent=sent_amount FROM dbo.v_cash_deposits_sent WHERE store_code=@store AND business_date=@date;
            IF @sent IS NULL THROW 51958,'The daily sheet has no cash deposit for this store and day.',1;
            IF EXISTS(SELECT 1 FROM dbo.cash_deposit_confirmations WITH(UPDLOCK,HOLDLOCK)
                      WHERE store_code=@store AND deposit_business_date=@date AND withdrawn_utc IS NULL)
              THROW 51958,'This deposit is already marked as credited.',1;
            SELECT @line_date=b.line_date,@credit=b.credit_amount,@account=b.bank_account_id,@reference=b.bank_reference
            FROM dbo.bank_statement_lines b WITH(UPDLOCK)
            JOIN dbo.bank_accounts a ON a.bank_account_id=b.bank_account_id
            WHERE b.bank_line_id=@line AND b.is_current=1 AND b.classification IN('UNCLASSIFIED','CASH_DEPOSIT')
              AND a.active=1 AND a.takes_cash_deposits=1 AND (a.store_code IS NULL OR a.store_code=@store);
            -- R-DEP-1 again, inside the transaction (DepositProposalRule.Fits): same amount, credited 0-3 days after the deposit.
            IF @credit IS NULL OR @credit<>@sent OR @line_date<@date OR @line_date>DATEADD(day,3,@date)
              THROW 51959,'This bank line no longer fits the deposit. Refresh the proposals.',1;
            IF EXISTS(SELECT 1 FROM dbo.cash_deposit_confirmations WITH(UPDLOCK,HOLDLOCK) WHERE bank_line_id=@line AND withdrawn_utc IS NULL)
              THROW 51959,'This bank line is already used for another deposit. Refresh the proposals.',1;
            INSERT dbo.cash_deposit_confirmations(store_code,deposit_business_date,credited_date,bank_account_id,bank_line_id,amount,bank_reference,proposal_rule,change_reason)
            VALUES(@store,@date,@line_date,@account,@line,@credit,@reference,'R-DEP-1',@reason);
            SET @id=CONVERT(bigint,SCOPE_IDENTITY());
            UPDATE dbo.bank_statement_lines SET classification='CASH_DEPOSIT',classified_by=ORIGINAL_LOGIN(),classified_utc=SYSUTCDATETIME(),
              classification_reason=N'Cash deposit credited (R-DEP-1)'
            WHERE bank_line_id=@line AND classification='UNCLASSIFIED';
            EXEC dbo.record_operational_audit 'CashDepositConfirmed','Succeeded',N'Bank credit confirmed',N'database';
            COMMIT TRANSACTION;
            SELECT @id;
            """;
        return await ExecuteAsync(sql, command =>
        {
            Add(command, "@store", SqlDbType.VarChar, store, 30);
            Add(command, "@date", SqlDbType.Date, Date(acceptance.DepositDate));
            Add(command, "@line", SqlDbType.BigInt, acceptance.BankLineId);
            Add(command, "@reason", SqlDbType.NVarChar, reason, 500);
        }, cancellationToken);
    }

    public async Task<long> ConfirmManuallyAsync(ManualDepositConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        var store = Store(confirmation.StoreCode);
        var reason = CollectionsSetupRules.Reason(confirmation.Reason);
        if (confirmation.Amount <= 0) throw new ArgumentException("Enter the amount the bank credited.");
        if (decimal.Round(confirmation.Amount, 4) != confirmation.Amount) throw new ArgumentException("Enter the amount in rupees and paise.");
        if (confirmation.CreditedDate < confirmation.DepositDate) throw new ArgumentException("The bank cannot credit a deposit before it was sent.");
        var reference = string.IsNullOrWhiteSpace(confirmation.BankReference) ? null : confirmation.BankReference.Trim();
        if (reference is { Length: > 100 }) throw new ArgumentException("Keep the bank reference to 100 characters.");
        await RequireAsync(session, CanAdminister, DepositAudit, OwnerRequired, cancellationToken);
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @id bigint;
            IF NOT EXISTS(SELECT 1 FROM dbo.bank_accounts WHERE bank_account_id=@account AND active=1 AND (store_code IS NULL OR store_code=@store))
              THROW 51958,'Choose an active bank account of this store.',1;
            IF EXISTS(SELECT 1 FROM dbo.cash_deposit_confirmations WITH(UPDLOCK,HOLDLOCK)
                      WHERE store_code=@store AND deposit_business_date=@date AND withdrawn_utc IS NULL)
              THROW 51958,'This deposit is already marked as credited.',1;
            INSERT dbo.cash_deposit_confirmations(store_code,deposit_business_date,credited_date,bank_account_id,bank_line_id,amount,bank_reference,proposal_rule,change_reason)
            VALUES(@store,@date,@credited,@account,NULL,@amount,@reference,NULL,@reason);
            SET @id=CONVERT(bigint,SCOPE_IDENTITY());
            EXEC dbo.record_operational_audit 'CashDepositConfirmed','Succeeded',N'Bank credit recorded by hand',N'database';
            COMMIT TRANSACTION;
            SELECT @id;
            """;
        return await ExecuteAsync(sql, command =>
        {
            Add(command, "@store", SqlDbType.VarChar, store, 30);
            Add(command, "@date", SqlDbType.Date, Date(confirmation.DepositDate));
            Add(command, "@credited", SqlDbType.Date, Date(confirmation.CreditedDate));
            Add(command, "@account", SqlDbType.Int, confirmation.BankAccountId);
            Add(command, "@amount", SqlDbType.Decimal, confirmation.Amount);
            command.Parameters["@amount"].Precision = 19; command.Parameters["@amount"].Scale = 4;
            Add(command, "@reference", SqlDbType.NVarChar, reference, 100);
            Add(command, "@reason", SqlDbType.NVarChar, reason, 500);
        }, cancellationToken);
    }

    public async Task WithdrawAsync(long confirmationId, string reason, CancellationToken cancellationToken = default)
    {
        var why = CollectionsSetupRules.Reason(reason);
        await RequireAsync(session, CanAdminister, DepositAudit, OwnerRequired, cancellationToken);
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @line bigint;
            SELECT @line=bank_line_id FROM dbo.cash_deposit_confirmations WITH(UPDLOCK) WHERE cash_deposit_confirmation_id=@id AND withdrawn_utc IS NULL;
            UPDATE dbo.cash_deposit_confirmations SET withdrawn_by=ORIGINAL_LOGIN(),withdrawn_utc=SYSUTCDATETIME(),withdrawn_reason=@reason
            WHERE cash_deposit_confirmation_id=@id AND withdrawn_utc IS NULL;
            IF @@ROWCOUNT<>1 THROW 51958,'This bank credit is already withdrawn or no longer exists. Refresh the list.',1;
            -- The line the confirmation classified is free again.
            UPDATE dbo.bank_statement_lines SET classification='UNCLASSIFIED',classified_by=NULL,classified_utc=NULL,classification_reason=NULL
            WHERE bank_line_id=@line AND classification='CASH_DEPOSIT'
              AND NOT EXISTS(SELECT 1 FROM dbo.cash_deposit_confirmations c WHERE c.bank_line_id=@line AND c.withdrawn_utc IS NULL);
            EXEC dbo.record_operational_audit 'CashDepositConfirmed','Cancelled',N'Bank credit withdrawn',N'database';
            COMMIT TRANSACTION;
            SELECT @id;
            """;
        await ExecuteAsync(sql, command =>
        {
            Add(command, "@id", SqlDbType.BigInt, confirmationId);
            Add(command, "@reason", SqlDbType.NVarChar, why, 500);
        }, cancellationToken);
    }

    private static string Store(string? storeCode)
    {
        var store = storeCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (store.Length is 0 or > 30) throw new ArgumentException("Choose the store.");
        return store;
    }

    private async Task<long> ExecuteAsync(string sql, Action<SqlCommand> parameters, CancellationToken cancellationToken)
    {
        await using var connection = await session.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        parameters(command);
        try
        {
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (SqlException exception) when (Plain(exception) is { } message)
        {
            throw new InvalidOperationException(message, exception);
        }
    }
}
