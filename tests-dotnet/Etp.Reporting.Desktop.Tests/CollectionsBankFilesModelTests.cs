using Etp.Reporting.Application.Collections;
using Etp.Reporting.Desktop.Modules.Collections;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.12 wave 2, lane U4: Import > Bank and settlement files (presentation logic over the real service contract, fake service).
/// All files, accounts and layouts are synthetic.</summary>
public sealed class CollectionsBankFilesModelTests
{
    private const string Statement = @"C:\Synthetic\statement.csv";
    private const string Second = @"C:\Synthetic\second.xlsx";

    private static BankFilesModel NewModel(FakeImportService service, ShellAccess? access = null) => new(() => service, access ?? ShellAccess.StoreManager);

    [Fact]
    public async Task A_file_with_one_matching_layout_is_ready_and_imports_with_the_services_own_sentence()
    {
        var service = new FakeImportService();
        service.Previews[Statement] = FakeImportService.Preview("statement.csv", FakeImportService.Layout(11, "Synthetic main account"));
        var model = NewModel(service);

        await model.AddFilesAsync([Statement]);

        var item = Assert.Single(model.Items);
        Assert.Equal(BankFileState.Ready, item.State);
        Assert.Equal(11, item.SelectedLayoutId);
        Assert.True(model.CanImportReady);
        Assert.Equal("1 file ready.", model.Summary);

        Assert.Equal(1, await model.ImportReadyAsync());
        Assert.Equal(BankFileState.Imported, item.State);
        Assert.Equal("Imported 5 lines.", item.Message);
        Assert.Equal("Imported", item.StatusWord);
        Assert.Equal(StatusTone.Success, item.Tone);
        var request = Assert.Single(service.Imported);
        Assert.Equal((Statement, 11, CollectionsOverlapChoice.Stop, (string?)null), (request.FilePath, request.LayoutId, request.OverlapChoice, request.Reason));
        Assert.False(model.CanImportReady);
    }

    [Fact]
    public async Task Several_matching_layouts_make_the_account_choice_mandatory_and_Import_stays_off_until_it_is_made()
    {
        var service = new FakeImportService();
        service.Previews[Statement] = FakeImportService.Preview("statement.csv", FakeImportService.Layout(11, "Synthetic main account"), FakeImportService.Layout(12, "Synthetic second account"));
        var model = NewModel(service);

        await model.AddFilesAsync([Statement]);

        var item = Assert.Single(model.Items);
        Assert.Equal(BankFileState.ChooseAccount, item.State);
        Assert.True(item.NeedsAccountChoice);
        Assert.Null(item.SelectedLayoutId);
        Assert.Equal("Choose the account", item.StatusWord);
        Assert.False(model.CanImportReady);
        Assert.Equal("Which account is this statement for?", model.ImportDisabledReason);
        Assert.False(model.CanImport(item));
        Assert.Equal(0, await model.ImportReadyAsync());
        Assert.Empty(service.Imported);

        Assert.False(model.Choose(item, 99));
        Assert.True(model.Choose(item, 12));
        Assert.Equal(BankFileState.Ready, item.State);
        Assert.True(model.CanImportReady);
        await model.ImportReadyAsync();
        Assert.Equal(12, Assert.Single(service.Imported).LayoutId);
    }

    [Fact]
    public async Task A_file_no_layout_matches_says_so_in_the_services_words_and_cannot_be_imported()
    {
        var service = new FakeImportService();
        service.Previews[Statement] = FakeImportService.Preview("statement.csv");
        var model = NewModel(service);

        await model.AddFilesAsync([Statement]);

        var item = Assert.Single(model.Items);
        Assert.Equal(BankFileState.NoLayout, item.State);
        Assert.Equal("This file does not match any file layout. Ask the owner to add one in Settings > Collections setup.", item.Message);
        Assert.Equal(StatusTone.Critical, item.Tone);
        Assert.False(model.CanImport(item));
        Assert.Equal("1 file needs a look.", model.Summary);
    }

    [Fact]
    public async Task Possible_duplicate_lines_are_settled_only_with_a_choice_and_a_reason()
    {
        var service = new FakeImportService();
        service.Previews[Statement] = FakeImportService.Preview("statement.csv", FakeImportService.Layout(11, "Synthetic main account"));
        service.Results.Enqueue(new(CollectionsImportOutcome.PossibleDuplicates, null, 0, 0, 0, 2, 0,
            "Possible duplicate lines: 2 lines have the date, amount and direction of a line already imported. Nothing was added."));
        service.Results.Enqueue(new(CollectionsImportOutcome.Imported, 5, 2, 0, 0, 0, 1, "Imported 2 lines. It replaces an earlier export of the same statement."));
        var model = NewModel(service);
        await model.AddFilesAsync([Statement]);
        await model.ImportReadyAsync();

        var item = Assert.Single(model.Items);
        Assert.Equal(BankFileState.PossibleDuplicates, item.State);
        Assert.StartsWith("Possible duplicate lines", item.Message, StringComparison.Ordinal);
        Assert.False(model.CanImportReady);

        Assert.False(model.CanResolve(item, CollectionsOverlapChoice.Stop, "Because"));
        Assert.False(model.CanResolve(item, CollectionsOverlapChoice.Restate, "  "));
        Assert.False(await model.ResolveAsync(item, CollectionsOverlapChoice.Restate, null));
        Assert.Single(service.Imported);

        Assert.True(await model.ResolveAsync(item, CollectionsOverlapChoice.Restate, "  The bank re-issued the statement  "));
        var second = service.Imported[1];
        Assert.Equal(CollectionsOverlapChoice.Restate, second.OverlapChoice);
        Assert.Equal("The bank re-issued the statement", second.Reason);
        Assert.Equal(BankFileState.Imported, item.State);
    }

    [Fact]
    public async Task A_failed_import_keeps_the_file_in_the_list_with_one_plain_sentence_and_can_be_retried()
    {
        var service = new FakeImportService();
        service.Previews[Statement] = FakeImportService.Preview("statement.csv", FakeImportService.Layout(11, "Synthetic main account"));
        service.ImportFailure = new IOException("The file changed while it was being read. Close it in Excel and import it again.");
        var model = NewModel(service);
        await model.AddFilesAsync([Statement]);

        Assert.Equal(0, await model.ImportReadyAsync());

        var item = Assert.Single(model.Items);
        Assert.Equal(BankFileState.Ready, item.State);
        Assert.Equal("The file changed while it was being read. Close it in Excel and import it again.", item.Message);

        service.ImportFailure = null;
        Assert.Equal(1, await model.ImportReadyAsync());
        Assert.Equal(BankFileState.Imported, item.State);
    }

    [Fact]
    public async Task An_unreadable_or_missing_file_is_a_failed_row_not_an_exception()
    {
        var service = new FakeImportService();
        var model = NewModel(service);

        await model.AddFilesAsync([Statement]);

        var item = Assert.Single(model.Items);
        Assert.Equal(BankFileState.Failed, item.State);
        Assert.Equal("The selected file no longer exists. Choose it again.", item.Message);
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    public async Task A_Viewer_or_an_unsigned_user_never_reaches_the_service(bool assigned, bool canView, bool canImport, bool canAdminister)
    {
        var service = new FakeImportService();
        service.Previews[Statement] = FakeImportService.Preview("statement.csv", FakeImportService.Layout(11, "Synthetic main account"));
        var model = new BankFilesModel(() => service, new ShellAccess(assigned, canView, canImport, canAdminister));

        await model.AddFilesAsync([Statement]);

        Assert.False(model.IsAllowed);
        Assert.NotNull(model.RefusalText);
        Assert.Empty(model.Items);
        Assert.Empty(service.Previewed);
        Assert.False(model.CanImportReady);
        Assert.Equal(assigned ? CollectionsScreens.BankFilesRefusedText : CollectionsScreens.NotSignedInText, model.RefusalText);
    }

    [Fact]
    public async Task The_same_file_is_listed_once_and_Clear_empties_the_list()
    {
        var service = new FakeImportService();
        service.Previews[Statement] = FakeImportService.Preview("statement.csv", FakeImportService.Layout(11, "Synthetic main account"));
        service.Previews[Second] = FakeImportService.Preview("second.xlsx");
        var model = NewModel(service);

        await model.AddFilesAsync([Statement, Statement.ToUpperInvariant(), Second]);
        await model.AddFilesAsync([Statement]);

        Assert.Equal(2, model.Items.Count);
        Assert.Equal(2, service.Previewed.Count);
        Assert.Equal("1 file ready, 1 file needs a look.", model.Summary);
        model.Clear();
        Assert.Empty(model.Items);
        Assert.Equal("Choose the bank statement or settlement files to import.", model.Summary);
    }

    [Theory]
    [InlineData("statement 50100012345678.csv", "statement XXXXXXXXXX5678.csv")]
    [InlineData("export_1234.csv", "export_1234.csv")]
    [InlineData("ACC12345-20260801.xlsx", "ACCX2345-XXXX0801.xlsx")]
    public void A_whole_account_number_in_a_file_name_never_shows(string name, string shown)
    {
        Assert.Equal(shown, new BankFileItem(@"C:\Synthetic\" + name).DisplayName);
        Assert.Equal(shown, CollectionsScreens.MaskDigits(name));
    }
}
