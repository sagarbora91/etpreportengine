using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.Collections;
using Etp.Reporting.Infrastructure.SqlServer.Collections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.12 lane D2: role refusals of the collections services, with an injected role (a SQL test runs under one Windows login).
/// A refused call writes its Blocked audit row first and never opens a connection for its own work.
/// </summary>
public sealed class CollectionsServiceBoundaryTests
{
    private sealed class FakeSession(AccessRole role) : ICollectionsSession
    {
        public List<string> Audits { get; } = [];

        public Task<AccessRole> LoadRoleAsync(CancellationToken cancellationToken) => Task.FromResult(role);

        public Task RecordAuditAsync(string eventType, string outcome, string? safeDetail, CancellationToken cancellationToken)
        {
            Audits.Add(eventType + "/" + outcome);
            return Task.CompletedTask;
        }

        public Task<SqlConnection> OpenAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A refused call must not open a connection.");
    }

    private static readonly BankAccountDraft Account = new(null, "SYN_MAIN", "Synthetic main", "Synthetic Bank", "0001", null, true, true);

    [Theory]
    [InlineData(AccessRole.StoreManager)]
    [InlineData(AccessRole.Viewer)]
    [InlineData(AccessRole.None)]
    public async Task Only_the_owner_changes_collections_setup(AccessRole role)
    {
        var session = new FakeSession(role);
        var service = new SqlServerCollectionsSetupService(session);

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveBankAccountAsync(Account, "Synthetic account"));

        Assert.Equal("Owner permission is required.", error.Message);
        Assert.Equal(new[] { "CollectionsSetup/Blocked" }, session.Audits);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.TestLayoutAsync("missing.csv",
            new StatementLayoutDraft(null, "L", "L", "BANK", 1, null, new string('a', 64), null, 1, 0, "{}", true)));
    }

    [Theory]
    [InlineData(AccessRole.StoreManager)]
    [InlineData(AccessRole.Viewer)]
    public async Task Only_the_owner_accepts_records_or_withdraws_a_bank_credit(AccessRole role)
    {
        var session = new FakeSession(role);
        var service = new SqlServerCashDepositService(session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AcceptProposalAsync(new("COLA", new DateOnly(2026, 8, 1), 1, "Bank statement")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConfirmManuallyAsync(
            new("COLA", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2), 1, 100m, null, "Passbook")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.WithdrawAsync(1, "Wrong line"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoadProposalsAsync(null));
        Assert.Equal(4, session.Audits.Count);
        Assert.All(session.Audits, audit => Assert.Equal("CashDepositConfirmed/Blocked", audit));
    }

    [Theory]
    [InlineData(AccessRole.StoreManager)]
    [InlineData(AccessRole.Viewer)]
    [InlineData(AccessRole.None)]
    public async Task Only_the_owner_lists_the_credits_to_withdraw_and_the_accounts_to_record_on(AccessRole role)
    {
        var session = new FakeSession(role);
        var service = new SqlServerCashDepositService(session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoadCreditedAsync(null, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoadDepositAccountsAsync("COLP"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SqlServerCollectionsSetupService(session).SavePolicyAsync(
            new(null, "COLP", "Cash", null, null, 3, null, new DateOnly(2026, 8, 1), null), "Synthetic rule"));
        Assert.Equal(new[] { "CashDepositConfirmed/Blocked", "CashDepositConfirmed/Blocked", "CollectionsSetup/Blocked" }, session.Audits);
    }

    [Fact]
    public async Task A_settlement_rule_and_a_credited_list_are_checked_before_the_role_so_they_never_reach_sql()
    {
        var session = new FakeSession(AccessRole.Owner);

        await Assert.ThrowsAsync<ArgumentException>(() => new SqlServerCollectionsSetupService(session).SavePolicyAsync(
            new(null, "COLP", "Cash", null, null, null, null, new DateOnly(2026, 8, 1), null), "Synthetic rule"));
        await Assert.ThrowsAsync<ArgumentException>(() => new SqlServerCashDepositService(session).LoadCreditedAsync(null, new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => new SqlServerCashDepositService(session).LoadDepositAccountsAsync(" "));
        Assert.Empty(session.Audits);
    }

    [Fact]
    public void The_lane_P2_throw_block_reads_as_its_own_sentence()
    {
        // 1.12.1 P2: THROW 52000-52009 pass through as plain sentences, like D2's 51950-51959.
        Assert.Equal("Another settlement rule covers these dates.", CollectionsAccess.Plain(SqlError(52000, "Another settlement rule covers these dates.")));
        Assert.Equal("This provider settles a different payment mode.", CollectionsAccess.Plain(SqlError(52001, "This provider settles a different payment mode.")));
        Assert.Null(CollectionsAccess.Plain(SqlError(52010, "Another lane's refusal.")));
    }

    private static SqlException SqlError(int number, string message) =>
        Etp.Reporting.TestSupport.SqlExceptionFactory.Create(new Etp.Reporting.TestSupport.SqlExceptionFactory.Error(number, message));

    [Fact]
    public async Task A_viewer_cannot_import_statements_or_list_deposits_in_transit()
    {
        var session = new FakeSession(AccessRole.Viewer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SqlServerCollectionsImportService(session).PreviewAsync("statement.csv"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SqlServerCollectionsImportService(session).ImportAsync(new("statement.csv", 1)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SqlServerCollectionsStatementQuery(session).LoadDepositsInTransitAsync(null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SqlServerCashDepositService(session).LoadInTransitAsync(null));
        Assert.Equal(new[] { "CollectionsImport/Blocked", "CollectionsImport/Blocked", "CashDepositConfirmed/Blocked", "CashDepositConfirmed/Blocked" }, session.Audits);
    }

    [Fact]
    public async Task Input_is_checked_before_the_role_so_a_bad_draft_never_reaches_sql()
    {
        var session = new FakeSession(AccessRole.Owner);
        var service = new SqlServerCollectionsSetupService(session);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveBankAccountAsync(Account with { AccountLast4 = "123456" }, "Synthetic"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveBankAccountAsync(Account, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveLayoutAsync(
            new StatementLayoutDraft(null, "SYN_LAYOUT", "Synthetic", "BANK", 1, null, new string('a', 64), null, 1, 0, """{"colour":"x"}""", true), "Synthetic"));
        await Assert.ThrowsAsync<ArgumentException>(() => new SqlServerCollectionsImportService(session)
            .ImportAsync(new("statement.csv", 1, CollectionsOverlapChoice.Restate, null)));
        Assert.Empty(session.Audits);
    }

    [Fact]
    public void A_stored_statement_file_name_keeps_only_the_last_four_of_any_long_number()
    {
        Assert.Equal("Statement_XXXXXXXXXX5678_Aug.csv", SqlServerCollectionsImportService.MaskedFileName("Statement_50100012345678_Aug.csv"));
        Assert.Equal("acct X2345 and 1234.csv", SqlServerCollectionsImportService.MaskedFileName("acct 12345 and 1234.csv"));
        Assert.Equal("settlement-2026-08-01.csv", SqlServerCollectionsImportService.MaskedFileName("settlement-2026-08-01.csv"));
    }

    [Fact]
    public void Import_results_read_as_plain_sentences()
    {
        Assert.Equal("Imported 3 lines. 5 lines were imported before. It replaces an earlier export of the same statement.",
            SqlServerCollectionsImportService.Describe(new(CollectionsImportOutcome.Imported, 7, 3, 5, 0, 0, 1, "")));
        Assert.StartsWith("Possible duplicate lines: 1 line ",
            SqlServerCollectionsImportService.Describe(new(CollectionsImportOutcome.PossibleDuplicates, null, 0, 0, 0, 1, 0, "")), StringComparison.Ordinal);
        Assert.StartsWith("Duplicate content",
            SqlServerCollectionsImportService.Describe(new(CollectionsImportOutcome.DuplicateContent, null, 0, 5, 0, 0, 0, "")), StringComparison.Ordinal);
    }
}
