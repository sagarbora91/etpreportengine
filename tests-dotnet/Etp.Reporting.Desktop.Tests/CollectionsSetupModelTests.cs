using Etp.Reporting.Application.Collections;
using Etp.Reporting.Desktop.Modules.Collections;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.12 wave 2, lane U4: Settings > Collections setup (Owner only; fake service; synthetic accounts and layouts).</summary>
public sealed class CollectionsSetupModelTests
{
    private static CollectionsSetupModel Owner(FakeSetupService service, FakeDialogs? dialogs = null) =>
        new(() => service, ShellAccess.Owner, dialogs ?? new FakeDialogs());

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, false)]
    public async Task Anyone_but_the_Owner_gets_one_sentence_no_data_and_no_save(bool assigned, bool canView, bool canImport, bool canAdminister)
    {
        var service = new FakeSetupService();
        var model = new CollectionsSetupModel(() => service, new ShellAccess(assigned, canView, canImport, canAdminister), new FakeDialogs());

        await model.LoadAsync();
        model.BankAccount.AccountKey = "SYN_MAIN"; model.BankAccount.Reason = "Synthetic";

        Assert.False(model.IsAllowed);
        Assert.Null(model.Setup);
        Assert.Equal(assigned ? CollectionsScreens.SetupOwnerOnlyText : CollectionsScreens.NotSignedInText, model.Message);
        Assert.False(await model.SaveBankAccountAsync());
        Assert.False(await model.SaveLayoutAsync());
        Assert.False(await model.ReadSampleAsync(@"C:\Synthetic\sample.csv"));
        Assert.Null(await model.TestOnFileAsync(@"C:\Synthetic\sample.csv"));
        Assert.Equal(0, service.Loads);
        Assert.Empty(service.BankAccounts);
        Assert.Empty(service.HeaderReads);
    }

    [Fact]
    public async Task The_Owner_loads_the_setup_and_a_bank_account_takes_only_the_last_four_digits()
    {
        var service = new FakeSetupService();
        var model = Owner(service);
        await model.LoadAsync();
        Assert.Equal(@"C:\SyntheticEvidence", model.EvidenceRoot);

        model.NewBankAccount();
        model.BankAccount.AccountKey = "syn_main"; model.BankAccount.DisplayName = "Synthetic main account"; model.BankAccount.BankName = "Synthetic Bank";
        model.BankAccount.Last4 = "50100012345678"; model.BankAccount.Reason = "New account";
        Assert.False(await model.SaveBankAccountAsync());
        Assert.Equal("Enter only the last four digits of the account number.", model.Message);
        Assert.Equal(StatusTone.Critical, model.MessageTone);
        Assert.Empty(service.BankAccounts);

        model.BankAccount.Last4 = "5678";
        model.BankAccount.Reason = "  ";
        Assert.False(await model.SaveBankAccountAsync());
        Assert.Equal("Give a reason for this change.", model.Message);

        model.BankAccount.Reason = "New account";
        model.BankAccount.TakesCashDeposits = true;
        Assert.True(await model.SaveBankAccountAsync());
        var saved = Assert.Single(service.BankAccounts);
        Assert.Equal(("SYN_MAIN", "5678", true, "New account"), (saved.Draft.AccountKey, saved.Draft.AccountLast4, saved.Draft.TakesCashDeposits, saved.Reason));
        Assert.Equal(StatusTone.Success, model.MessageTone);
        Assert.Equal("Saved the bank account Synthetic main account.", model.Message);
        Assert.Equal(string.Empty, model.BankAccount.AccountKey);
        Assert.True(service.Loads >= 2);
    }

    [Fact]
    public async Task A_name_that_holds_an_account_number_is_refused_in_plain_words()
    {
        var model = Owner(new FakeSetupService());
        await model.LoadAsync();
        model.BankAccount.AccountKey = "SYN_MAIN"; model.BankAccount.DisplayName = "Account 50100012345678"; model.BankAccount.BankName = "Synthetic Bank";
        model.BankAccount.Reason = "New account";

        Assert.False(await model.SaveBankAccountAsync());

        Assert.Equal("The account name cannot contain an account number. Use the last four digits field.", model.Message);
    }

    [Fact]
    public async Task A_layout_cannot_be_saved_until_its_header_was_read_from_a_sample_file_and_never_from_typing()
    {
        var service = new FakeSetupService();
        var model = Owner(service);
        await model.LoadAsync();
        model.NewLayout("BANK");
        model.Layout.LayoutKey = "SYN_BANK_CSV"; model.Layout.DisplayName = "Synthetic bank CSV"; model.Layout.BankAccountId = 7;
        model.Layout.HeaderRow = 3; model.Layout.Reason = "New layout";

        Assert.False(await model.SaveLayoutAsync());
        Assert.Equal("Read the header from a sample file before saving the layout.", model.Message);
        Assert.Empty(service.Layouts);

        Assert.True(await model.ReadSampleAsync(@"C:\Synthetic\sample.csv"));
        Assert.Equal(("C:\\Synthetic\\sample.csv", 3, (string?)null), service.HeaderReads.Single());
        Assert.Equal(new string('b', 64), model.Layout.HeaderSignature);
        Assert.Equal(["Date", "Narration", "Credit"], model.Layout.Headers);
        Assert.False(model.Layout.SignatureIsStale);

        model.Layout.HeaderRow = 4;
        Assert.True(model.Layout.SignatureIsStale);
        Assert.False(await model.SaveLayoutAsync());
        Assert.Equal("The sheet or header row changed. Read the header from the sample file again before saving.", model.Message);

        model.Layout.HeaderRow = 3;
        Assert.True(await model.SaveLayoutAsync());
        var saved = Assert.Single(service.Layouts).Draft;
        Assert.Equal((new string('b', 64), 3, 7, "BANK"), (saved.HeaderSignatureSha256, saved.HeaderRow, saved.BankAccountId, saved.LayoutKind));
        Assert.Null(saved.ProviderKey);
    }

    [Fact]
    public async Task Test_on_this_file_shows_the_layouts_reading_without_saving_anything()
    {
        var service = new FakeSetupService();
        var dialogs = new FakeDialogs { Sample = @"C:\Synthetic\sample.csv" };
        var model = Owner(service, dialogs);
        await model.LoadAsync();
        model.NewLayout("BANK");
        model.Layout.LayoutKey = "SYN_BANK_CSV"; model.Layout.DisplayName = "Synthetic bank CSV"; model.Layout.BankAccountId = 7; model.Layout.HeaderRow = 1;

        var result = await model.TestOnFileAsync();

        Assert.NotNull(result);
        Assert.Single(service.HeaderReads);
        Assert.Single(service.Tests);
        Assert.Empty(service.Layouts);
        Assert.Equal("The header matches. 2 lines could be read and 1 line could not.", model.Message);
        Assert.Equal(StatusTone.Warning, model.MessageTone);
        var row = Assert.Single(CollectionsSetupModel.TestRows(result!));
        Assert.Equal((3, "01 Aug 2026", "REF1", "1,250.00", "—", "Read"), (row.Row, row.Date, row.Reference, row.Credit, row.Debit, row.Result));
    }

    [Theory]
    [InlineData(true, 5, 0, StatusTone.Success, "The header matches. 5 lines could be read.")]
    [InlineData(true, 0, 3, StatusTone.Critical, "The header matches. 0 lines could be read and 3 lines could not.")]
    [InlineData(false, 0, 0, StatusTone.Critical, "The header does not match. 0 lines could be read.")]
    public void The_test_sentence_and_tone_follow_the_result(bool matches, int readable, int unreadable, StatusTone tone, string sentence)
    {
        var result = new LayoutTestResult(matches, [], readable, unreadable, []);
        Assert.Equal(tone, CollectionsSetupModel.TestTone(result));
        Assert.Equal(sentence, CollectionsSetupModel.TestSentence(result));
    }

    [Fact]
    public void A_header_problem_is_the_whole_sentence()
    {
        var result = new LayoutTestResult(false, ["This file's header row is not the one the layout was made from."], 0, 0, []);
        Assert.Equal("This file's header row is not the one the layout was made from.", CollectionsSetupModel.TestSentence(result));
    }

    [Theory]
    [InlineData("GROSS_WITH_DEDUCTIONS", "Gross, with fees taken out")]
    [InlineData("SETTLEMENT_SUMMARY", "Settlements only")]
    [InlineData("NONE", "Does not pay into a bank account")]
    [InlineData("SOMETHING_ELSE", "SOMETHING_ELSE")]
    public void Codes_read_as_words(string code, string words) => Assert.Equal(words, CollectionsSetupModel.WordsFor(code));
}
