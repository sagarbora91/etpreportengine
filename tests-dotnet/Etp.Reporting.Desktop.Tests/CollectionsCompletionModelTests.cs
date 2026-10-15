using Etp.Reporting.Application.Collections;
using Etp.Reporting.Desktop.Modules.Collections;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12.1 lane P2 "Collections completion" (U4 NOTE gaps): the statement's Excel export and its role columns, the Owner's
/// settlement rules, and recording / withdrawing a bank credit by hand. Fake services, synthetic store COLA, no real bank data.
/// </summary>
public sealed class CollectionsCompletionModelTests
{
    private static readonly DateOnly Business = new(2026, 8, 5);
    private static readonly DateOnly Today = new(2026, 8, 6);

    private static CollectionsStatementLine CreditedCash() =>
        new("COLA", new DateOnly(2026, 8, 1), "Cash", null, 12500m, 12500m, 0m, CollectionsLineState.DepositCredited, "Bank credited on 02 Aug 2026",
            14000m, new DateOnly(2026, 8, 2), "DEP0001");

    private static CollectionsStatementLine InTransit() =>
        new("COLA", new DateOnly(2026, 8, 3), "Cash", null, 8000m, null, null, CollectionsLineState.DepositInTransit, "Deposit in transit, 3 days", 9000m, AgeDays: 3);

    private static CollectionsStatementLine Card() =>
        new("COLA", new DateOnly(2026, 8, 1), "Card", "CARD_ACQUIRER", 900m, 800m, -100m, CollectionsLineState.TotalsOnly, "Totals only, not matched yet.");

    private static async Task<CollectionsStatementModel> LoadedStatement(ShellAccess access, bool withoutReferences)
    {
        var lines = withoutReferences
            ? new[] { CreditedCash() with { BankReference = null }, InTransit(), Card() }
            : new[] { CreditedCash(), InTransit(), Card() };
        var model = new CollectionsStatementModel(() => new FakeStatementQuery { Statement = new(lines, withoutReferences) }, access, "COLA", Business, () => Today);
        await model.LoadAsync();
        return model;
    }

    // ---- statement export -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Owners_export_has_the_bank_columns_with_dates_and_money_as_values()
    {
        var model = await LoadedStatement(ShellAccess.Owner, withoutReferences: false);

        Assert.True(model.CanExport);
        Assert.True(model.ExportIncludesBankColumns);
        var data = model.BuildExportData();
        Assert.Equal(["Store", "Business date", "Mode", "Provider", "Expected (₹)", "Evidence (₹)", "Difference (₹)", "Cash tenders (₹)", "Status", "Detail", "Age (days)", "Credited on", "Bank reference"],
            data.Columns.Select(column => column.Header));
        Assert.Equal("#,##0.00", data.Columns[4].NumberFormat);
        Assert.Equal(3, data.Rows.Count);
        var cash = data.Rows[0];
        Assert.Equal("COLA", cash[0]);
        Assert.Equal(new DateOnly(2026, 8, 1), cash[1]);
        Assert.Equal(12500m, cash[4]);
        Assert.Equal("Bank credited", cash[8]);
        Assert.Equal(new DateOnly(2026, 8, 2), cash[11]);
        Assert.Equal("DEP0001", cash[12]);
        var transit = data.Rows[1];
        Assert.Null(transit[5]);
        Assert.Equal(3, transit[10]);
        Assert.Null(transit[12]);
        Assert.Equal("Collections_expected_vs_evidence_20260801_20260805.xlsx", model.ExportFileName);

        var metadata = model.BuildExportMetadata(new DateTimeOffset(2026, 8, 6, 4, 0, 0, TimeSpan.Zero));
        Assert.Equal(("Collections: expected vs evidence", new DateOnly(2026, 8, 1), Business, "Store COLA"), (metadata.ReportName, metadata.DateFrom, metadata.DateTo, metadata.AppliedScope));
        Assert.StartsWith("Totals only, not matched yet.", metadata.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not in this file", metadata.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Store_Managers_export_has_no_bank_columns_even_though_the_screen_shows_the_reference()
    {
        var model = await LoadedStatement(ShellAccess.StoreManager, withoutReferences: false);

        Assert.False(model.WithoutBankReferences);
        Assert.False(model.ExportIncludesBankColumns);
        var data = model.BuildExportData();
        Assert.DoesNotContain(data.Columns, column => column.Header is "Bank reference" or "Credited on");
        Assert.Equal(11, data.Columns.Count);
        Assert.All(data.Rows, row => Assert.Equal(11, row.Count));
        Assert.DoesNotContain(data.Rows.SelectMany(row => row), cell => cell is "DEP0001");
        Assert.Contains(CollectionsStatementModel.ExportWithoutBankColumnsText, model.BuildExportMetadata(DateTimeOffset.UtcNow).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Viewers_export_has_no_bank_reference_and_no_bank_columns()
    {
        var model = await LoadedStatement(ShellAccess.Viewer, withoutReferences: true);

        Assert.False(model.ExportIncludesBankColumns);
        var data = model.BuildExportData();
        Assert.DoesNotContain(data.Columns, column => column.Header is "Bank reference" or "Credited on");
        Assert.DoesNotContain(data.Rows.SelectMany(row => row), cell => cell is "DEP0001");
        var message = model.BuildExportMetadata(DateTimeOffset.UtcNow).Message;
        Assert.Contains(CollectionsStatementModel.HiddenReferencesText, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_export_writes_the_lines_on_screen_and_waits_for_a_load()
    {
        var empty = new CollectionsStatementModel(() => new FakeStatementQuery(), ShellAccess.Owner, "COLA", Business, () => Today);
        Assert.False(empty.CanExport);
        Assert.Equal("Load the statement first.", empty.ExportDisabledReason);
        await empty.LoadAsync();
        Assert.Equal("There are no lines to export.", empty.ExportDisabledReason);

        var model = await LoadedStatement(ShellAccess.Owner, withoutReferences: false);
        model.ModeFilter = "Card";
        var row = Assert.Single(model.BuildExportData().Rows);
        Assert.Equal("Card", row[2]);
        Assert.Contains("Only the Card lines.", model.BuildExportMetadata(DateTimeOffset.UtcNow).Message, StringComparison.Ordinal);
    }

    // ---- deposits: overdue, record by hand, withdraw ----------------------------------------------------------------------

    [Fact]
    public void With_a_Cash_rule_an_old_deposit_says_Overdue_in_red_and_a_young_one_stays_quiet()
    {
        var overdue = new DepositRow(new DepositInTransit("COLA", new DateOnly(2026, 8, 1), 1000m, 5, OverdueAfterDays: 3), null);
        var young = new DepositRow(new DepositInTransit("COLA", new DateOnly(2026, 8, 4), 1000m, 2, OverdueAfterDays: 3), null);
        var noRule = new DepositRow(new DepositInTransit("COLA", new DateOnly(2026, 8, 1), 1000m, 5), null);

        Assert.Equal(("Overdue, 5 days", StatusTone.Critical), (overdue.AgeWord, overdue.AgeTone));
        Assert.Equal(("2 days", StatusTone.Neutral), (young.AgeWord, young.AgeTone));
        Assert.Equal(("5 days", StatusTone.Warning), (noRule.AgeWord, noRule.AgeTone));
    }

    [Fact]
    public async Task The_Owner_records_a_bank_credit_by_hand_after_every_field_is_checked()
    {
        var deposit = FakeDepositService.Deposit("COLA", 3, 8000m, 3);
        var service = new FakeDepositService { Transit = [deposit] };
        var model = new DepositsInTransitModel(() => service, ShellAccess.Owner, "COLA", () => Today);
        await model.LoadAsync();
        var row = Assert.Single(model.Rows);
        Assert.True(model.CanRecordRow(row));
        Assert.Equal((Today.AddDays(-62), Today), service.CreditedRanges[0]);

        var accounts = await model.LoadAccountsAsync(row);
        var suggestion = model.Suggestion(row, accounts);
        Assert.Equal((new DateOnly(2026, 8, 4), 7, "8000.00", DepositsInTransitModel.DefaultManualReason), (suggestion.CreditedDate, suggestion.BankAccountId, suggestion.AmountText, suggestion.Reason));

        Assert.False(await model.RecordByHandAsync(row, suggestion with { CreditedDate = new DateOnly(2026, 8, 2) }));
        Assert.Equal("The bank cannot credit a deposit before it was sent.", model.NoticeText);
        Assert.False(await model.RecordByHandAsync(row, suggestion with { CreditedDate = Today.AddDays(1) }));
        Assert.StartsWith("The bank cannot have credited it on a day that has not come yet.", model.NoticeText, StringComparison.Ordinal);
        Assert.False(await model.RecordByHandAsync(row, suggestion with { BankAccountId = null }));
        Assert.Equal("Choose the bank account that was credited.", model.NoticeText);
        Assert.False(await model.RecordByHandAsync(row, suggestion with { AmountText = "eight thousand" }));
        Assert.StartsWith("Enter the amount the bank credited", model.NoticeText, StringComparison.Ordinal);
        Assert.False(await model.RecordByHandAsync(row, suggestion with { AmountText = "8000.005" }));
        Assert.False(await model.RecordByHandAsync(row, suggestion with { Reason = " " }));
        Assert.Empty(service.Manual);

        Assert.True(await model.RecordByHandAsync(row, suggestion with { AmountText = "7,990.00", BankReference = " SYN-REF-1 " }));
        var sent = Assert.Single(service.Manual);
        Assert.Equal(("COLA", new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4), 7, 7990m, "SYN-REF-1", DepositsInTransitModel.DefaultManualReason),
            (sent.StoreCode, sent.DepositDate, sent.CreditedDate, sent.BankAccountId, sent.Amount, sent.BankReference, sent.Reason));
        Assert.Empty(model.Rows);
        Assert.Equal("Recorded by hand", Assert.Single(model.Credited).HowWord);
        // A different amount is allowed (the statement says "Credited, different amount") and the notice says so in amber.
        Assert.Equal(StatusTone.Warning, model.NoticeTone);
        Assert.Contains("not the ₹8,000.00 on the sheet", model.NoticeText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_record_shows_the_services_sentence_and_no_account_says_where_to_add_one()
    {
        var deposit = FakeDepositService.Deposit("COLA", 3, 8000m, 3);
        var service = new FakeDepositService { Transit = [deposit], ManualFailure = new InvalidOperationException("This deposit is already marked as credited.") };
        var model = new DepositsInTransitModel(() => service, ShellAccess.Owner, "COLA", () => Today);
        await model.LoadAsync();
        var row = model.Rows[0];

        Assert.False(await model.RecordByHandAsync(row, model.Suggestion(row, service.Accounts)));
        Assert.Equal(("This deposit is already marked as credited.", StatusTone.Critical), (model.NoticeText, model.NoticeTone));

        service.Accounts = [];
        Assert.Empty(await model.LoadAccountsAsync(row));
        Assert.Equal(DepositsInTransitModel.NoAccountText, model.NoticeText);
    }

    [Fact]
    public async Task The_Owner_withdraws_a_credit_with_a_reason_and_the_deposit_is_in_transit_again()
    {
        var service = new FakeDepositService { CreditedList = [FakeDepositService.Credit(61, "COLA", 1, 12500m, 12500m, byHand: false), FakeDepositService.Credit(62, "COLA", 2, 900m, 950m, byHand: true)] };
        var model = new DepositsInTransitModel(() => service, ShellAccess.Owner, "COLA", () => Today);
        await model.LoadAsync();

        Assert.Equal(["From the bank line", "Recorded by hand"], model.Credited.Select(row => row.HowWord));
        Assert.Equal(["Same amount", "Different amount"], model.Credited.Select(row => row.Agreement));
        Assert.Equal("DEP00061", model.Credited[0].BankReference);
        Assert.Equal("—", model.Credited[1].BankReference);
        Assert.False(model.CanWithdraw(null));
        Assert.Equal("Select a bank credit first.", model.WithdrawDisabledReason(null));

        Assert.False(await model.WithdrawAsync(model.Credited[0], ""));
        Assert.Empty(service.Withdrawn);
        Assert.True(await model.WithdrawAsync(model.Credited[0], "Wrong line chosen"));
        Assert.Equal((61L, "Wrong line chosen"), Assert.Single(service.Withdrawn));
        Assert.Equal("01 Aug 2026", Assert.Single(model.Rows).Date);
        Assert.Single(model.Credited);
        Assert.Equal(StatusTone.Success, model.NoticeTone);
    }

    [Fact]
    public async Task A_Store_Manager_never_asks_for_the_credited_list_or_the_accounts_and_cannot_record_or_withdraw()
    {
        var deposit = FakeDepositService.Deposit("COLA", 3, 8000m, 3);
        var service = new FakeDepositService { Transit = [deposit], CreditedList = [FakeDepositService.Credit(61, "COLA", 1, 12500m, 12500m, byHand: false)] };
        var model = new DepositsInTransitModel(() => service, ShellAccess.StoreManager, "COLA", () => Today);
        await model.LoadAsync();

        Assert.Equal(0, service.CreditedCalls);
        Assert.Empty(model.Credited);
        var row = Assert.Single(model.Rows);
        Assert.False(model.CanRecordRow(row));
        Assert.Equal(DepositsInTransitModel.OwnerAcceptsText, model.RecordDisabledReason(row));
        Assert.Empty(await model.LoadAccountsAsync(row));
        Assert.False(await model.RecordByHandAsync(row, new ManualCreditEntry(Today, 7, "8000", null, "Passbook")));
        Assert.False(await model.WithdrawAsync(new CreditedRow(service.CreditedList[0]), "Wrong"));
        Assert.Empty(service.Manual);
        Assert.Empty(service.Withdrawn);
    }

    [Fact]
    public void Amounts_read_with_Indian_grouping_and_never_as_a_guess()
    {
        Assert.Equal(125000m, DepositsInTransitModel.ParseAmount("1,25,000"));
        Assert.Equal(12500.5m, DepositsInTransitModel.ParseAmount(" ₹12,500.50 "));
        Assert.Null(DepositsInTransitModel.ParseAmount("-500"));
        Assert.Null(DepositsInTransitModel.ParseAmount("12 500"));
        Assert.Null(DepositsInTransitModel.ParseAmount(""));
    }

    // ---- settlement rules ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Owner_saves_a_settlement_rule_with_a_reason_and_an_overlapping_rule_is_refused_before_the_service()
    {
        var service = new FakeSetupService
        {
            Setup = new([], [], [], [new CollectionPolicyRow(5, "COLA", "Cash", null, null, 3, null, new DateOnly(2026, 8, 1), null)], [], [], @"C:\SyntheticEvidence")
        };
        var model = new CollectionsSetupModel(() => service, ShellAccess.Owner, new FakeDialogs(), () => Today);
        await model.LoadAsync();
        Assert.Equal(Today, model.Policy.EffectiveFrom);
        Assert.Equal("In force", model.PolicyState(service.Setup.Policies[0]));

        model.Policy.StoreCode = "cola"; model.Policy.OverdueAfterDays = "5"; model.Policy.Reason = "Bank takes longer in August";
        Assert.False(await model.SavePolicyAsync());
        Assert.Equal(CollectionPolicyResolver.OverlapText, model.Message);
        Assert.Empty(service.Policies);

        model.Policy.OverdueAfterDays = "five";
        Assert.False(await model.SavePolicyAsync());
        Assert.Equal("Overdue after must be a whole number of days.", model.Message);

        model.EditPolicy(service.Setup.Policies[0]);
        model.Policy.OverdueAfterDays = "4";
        model.Policy.Reason = "One more day";
        Assert.True(await model.SavePolicyAsync());
        var (draft, reason) = Assert.Single(service.Policies);
        Assert.Equal((5, "COLA", "Cash", 4, "One more day"), (draft.Id, draft.StoreCode, draft.Mode, draft.OverdueAfterDays, reason));
        Assert.Equal("Saved the Cash settlement rule for store COLA.", model.Message);
    }

    [Fact]
    public void A_rules_state_is_a_word()
    {
        var model = new CollectionsSetupModel(() => new FakeSetupService(), ShellAccess.Owner, new FakeDialogs(), () => Today);
        Assert.Equal("Ended", model.PolicyState(new CollectionPolicyRow(1, null, "Cash", null, null, 3, null, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31))));
        Assert.Equal("Starts later", model.PolicyState(new CollectionPolicyRow(2, null, "Cash", null, null, 3, null, new DateOnly(2026, 9, 1), null)));
    }
}
