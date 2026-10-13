using Etp.Reporting.Application.Imports;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 U2 (wave plan C-c, review m-2): which Fix buttons a refused row shows, for every catalogue code and role, and the
/// shop-facing text the shop's result window and "Tell the shop" use - never an office instruction.
/// </summary>
public sealed class ImportFixAdvisorTests
{
    private static readonly ImportFixContext Owner = new(true, true, true, true, "1A2B3C4D5E6F", FromShop: true);
    private static readonly ImportFixContext Manager = Owner with { IsOwner = false };
    private static readonly ImportFixContext Viewer = new(false, false, true, true, "1A2B3C4D5E6F", FromShop: true);

    public static IEnumerable<object[]> EveryCode() =>
        ImportDiagnosticCatalogue.Codes.Concat(["IMPORT_NOT_COMPLETED", "SOME_IMPORTER_CODE", ""]).Select(code => new object[] { code });

    [Theory]
    [MemberData(nameof(EveryCode))]
    public void Every_code_gets_a_consistent_set_of_buttons_for_every_role(string code)
    {
        var owner = ImportFixAdvisor.For(code, Owner);
        var manager = ImportFixAdvisor.For(code, Manager);
        var viewer = ImportFixAdvisor.For(code, Viewer);

        // At most one of each, in the advisor's order, each with a label and a sentence.
        foreach (var actions in new[] { owner, manager, viewer })
        {
            Assert.Equal(actions.Count, actions.Select(action => action.Kind).Distinct().Count());
            Assert.Equal(actions.Select(action => action.Kind).Order(), actions.Select(action => action.Kind));
            Assert.All(actions, action => { Assert.NotEmpty(action.Label); Assert.NotEmpty(action.Explanation); });
        }
        // Reopen is the Owner's, and only for a finalised day; Retry never for a finalised day; the two never together.
        Assert.DoesNotContain(manager, action => action.Kind == ImportFixKind.ReopenDayAndReapply);
        Assert.Equal(ImportFixAdvisor.IsDayFinalised(code, null), owner.Any(action => action.Kind == ImportFixKind.ReopenDayAndReapply));
        Assert.False(owner.Any(a => a.Kind == ImportFixKind.Retry) && owner.Any(a => a.Kind == ImportFixKind.ReopenDayAndReapply));
        Assert.Equal(ImportFixAdvisor.IsRetryable(code, null), manager.Any(action => action.Kind == ImportFixKind.Retry));
        // A Viewer changes nothing and opens no path: Copy Ref only.
        Assert.Equal([ImportFixKind.CopyRef], viewer.Select(action => action.Kind));
        Assert.Contains(owner, action => action.Kind == ImportFixKind.OpenErrorRegister);
        Assert.DoesNotContain(manager, action => action.Kind == ImportFixKind.OpenErrorRegister);
    }

    [Theory]
    [InlineData("DAILY_INPUTS_DAY_FINALISED", null)]
    [InlineData("SQL_51030", 51030)]
    [InlineData("SQL_51021", 51021)]
    [InlineData("SQL_51042", 51042)]
    public void A_finalised_day_is_recognised_by_its_code_or_by_the_day_lock_trigger_number(string code, int? number)
    {
        Assert.True(ImportFixAdvisor.IsDayFinalised(code, number));
        Assert.False(ImportFixAdvisor.IsRetryable(code, number));
        Assert.Equal(ImportFixKind.ReopenDayAndReapply, ImportFixAdvisor.For(code, Owner, number)[0].Kind);
    }

    [Theory]
    [InlineData("IMPORT_TIMEOUT", null, true)]
    [InlineData("COMMIT_OUTCOME_UNKNOWN", null, true)]
    [InlineData("SQL_1205", 1205, true)]
    [InlineData("SQL_51310", 51310, false)]
    [InlineData("HEADER_INVALID", null, false)]
    [InlineData("IMPORT_CONFLICT", null, false)]
    [InlineData("DUPLICATE_FILE", null, false)]
    [InlineData("DAILY_INPUTS_NO_PERMISSION", null, true)]
    [InlineData(null, null, true)]
    public void Retry_is_offered_only_when_the_same_bytes_can_pass_next_time(string? code, int? number, bool retryable) =>
        Assert.Equal(retryable, ImportFixAdvisor.IsRetryable(code, number));

    [Fact]
    public void Without_a_known_location_no_button_imports_or_opens_anything_and_the_hint_says_what_to_do()
    {
        var unknown = Owner with { HasRoutedFile = false, RoutedFileUnderConfiguredRoot = false, Ref = null };
        var actions = ImportFixAdvisor.For("DAILY_INPUTS_DAY_FINALISED", unknown);
        Assert.Equal([ImportFixKind.TellShop], actions.Select(action => action.Kind));
        Assert.Contains("Reopen the day", ImportFixAdvisor.HintWithoutButton("DAILY_INPUTS_DAY_FINALISED", unknown));
        Assert.Contains("Ask the Owner", ImportFixAdvisor.HintWithoutButton("DAILY_INPUTS_DAY_FINALISED", Manager));
        Assert.Null(ImportFixAdvisor.HintWithoutButton("DAILY_INPUTS_DAY_FINALISED", Owner));
    }

    [Fact]
    public void Open_folder_needs_a_place_inside_the_configured_folders()
    {
        Assert.Contains(ImportFixAdvisor.For("HEADER_INVALID", Owner), action => action.Kind == ImportFixKind.OpenFolder);
        Assert.DoesNotContain(ImportFixAdvisor.For("HEADER_INVALID", Owner with { RoutedFileUnderConfiguredRoot = false }),
            action => action.Kind == ImportFixKind.OpenFolder);
    }

    // ----- shop-facing text (review m-2) -----

    // The plan's list (watch folder, reopen, Settings) plus the office's own buttons and folders.
    private static readonly string[] OfficeWords = ["watch folder", "reopen", "settings", "retry", "put the file", "close day", "error register"];

    [Theory]
    [MemberData(nameof(EveryCode))]
    public void The_shop_text_of_every_code_is_free_of_office_instructions(string code)
    {
        var text = ImportDiagnosticCatalogue.ShopSentence(code);
        foreach (var word in OfficeWords)
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(ImportDiagnosticCatalogue.MessageForShop(code));
        Assert.NotEmpty(ImportDiagnosticCatalogue.WhatToDoForShop(code));
        Assert.DoesNotMatch(@"\b[A-Z]{2,5}[0-9]{2,4}\b", text); // no store code
    }

    [Fact]
    public void The_shop_is_told_what_happened_in_the_templates_first_sentence_and_what_to_do_in_its_own_words()
    {
        Assert.Equal("This business day is finalised, so its manual entries cannot change.", ImportDiagnosticCatalogue.MessageForShop("DAILY_INPUTS_DAY_FINALISED"));
        Assert.StartsWith("Tell the office.", ImportDiagnosticCatalogue.WhatToDoForShop("DAILY_INPUTS_DAY_FINALISED"));
        Assert.Equal("Open the sheet, correct the date in the box, save, send again.", ImportDiagnosticCatalogue.WhatToDoForShop("DAILY_INPUTS_DATE_IN_FUTURE"));
        Assert.Equal(ImportDiagnosticCatalogue.TellTheOffice, ImportDiagnosticCatalogue.WhatToDoForShop("CONTRACT_BLOCK_GAP"));
        Assert.Equal(ImportDiagnosticCatalogue.ShopUnknownMessage, ImportDiagnosticCatalogue.MessageForShop("SOME_IMPORTER_CODE"));
        // The office keeps its own instruction in the template (History and the Error register still show it).
        Assert.Contains("Reopen the day", ImportDiagnosticCatalogue.Template("DAILY_INPUTS_DAY_FINALISED"));
    }

    [Fact]
    public void The_service_default_template_names_no_store_code()
    {
        Assert.Equal(ImportDiagnosticCatalogue.ServiceStoreDefaultedMessage, ImportDiagnosticCatalogue.Template("SERVICE_STORE_DEFAULTED"));
        Assert.DoesNotContain("AW330", ImportDiagnosticCatalogue.ServiceStoreDefaultedMessage);
    }
}
