using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.Collections;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Collections;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.12 wave 2, lane U4: the four Collections screens as built views (STA, no database, fake services): what each role
/// sees, which controls exist, and the registration by task id.</summary>
[Collection(WpfViewCollection.Name)]
public sealed class CollectionsViewTests
{
    private static readonly DateOnly Business = new(2026, 8, 5);

    [Fact]
    public void The_four_screens_are_registered_by_task_id_and_open_for_a_role()
    {
        RunSta(() =>
        {
            var root = new DesktopCompositionRoot(Path.GetTempPath(), DesktopCompositionRoot.DefaultConnectionString);
            var registry = new ScreenRegistry();
            root.RegisterCollectionScreens(registry);

            Assert.Equal(CollectionsScreens.TaskIds.Order(StringComparer.Ordinal), registry.TaskIds.Order(StringComparer.Ordinal));
            Assert.Equal(["collections-setup", "bank-files", "collections-statement", "deposits-in-transit"], CollectionsScreens.TaskIds);
            Assert.Throws<InvalidOperationException>(() => root.RegisterCollectionScreens(registry));
            foreach (var id in CollectionsScreens.TaskIds)
            {
                Assert.True(registry.TryCreate(new ScreenRequest(id, null, Business, "COLA", ShellAccess.Viewer, _ => { }), out var view));
                Assert.NotNull(view);
            }
        });
    }

    [Fact]
    public void A_Viewers_statement_has_no_bank_reference_column_and_the_Owners_has_it()
    {
        RunSta(() =>
        {
            var line = new CollectionsStatementLine("COLA", new DateOnly(2026, 8, 1), "Cash", null, 12500m, 12500m, 0m, CollectionsLineState.DepositCredited,
                "Bank credited on 01 Aug 2026", 14000m, new DateOnly(2026, 8, 1), "DEP0001");
            var viewer = new FakeStatementQuery { Statement = new([line], true) };
            var viewerView = new CollectionsStatementView(new CollectionsStatementModel(() => viewer, ShellAccess.Viewer, "COLA", Business));
            viewerView.ShowAsync().GetAwaiter().GetResult();
            Assert.Equal(Visibility.Collapsed, viewerView.ReferenceColumn!.Visibility);
            Assert.Single(viewerView.Table.ItemsSource!.Cast<object>());

            var owner = new FakeStatementQuery { Statement = new([line], false) };
            var ownerView = new CollectionsStatementView(new CollectionsStatementModel(() => owner, ShellAccess.Owner, "COLA", Business));
            ownerView.ShowAsync().GetAwaiter().GetResult();
            Assert.Equal(Visibility.Visible, ownerView.ReferenceColumn!.Visibility);
        });
    }

    [Fact]
    public void The_statement_says_totals_only_in_its_notice_when_a_line_is_totals_only()
    {
        RunSta(() =>
        {
            var card = new CollectionsStatementLine("COLA", new DateOnly(2026, 8, 1), "Card", "CARD_ACQUIRER", 900m, 800m, -100m, CollectionsLineState.TotalsOnly, "Totals only, not matched yet.");
            var view = new CollectionsStatementView(new CollectionsStatementModel(() => new FakeStatementQuery { Statement = new([card], false) }, ShellAccess.StoreManager, "COLA", Business));

            view.ShowAsync().GetAwaiter().GetResult();

            Assert.Equal("Totals only, not matched yet", view.Frame.Notice.Title);
            Assert.Equal(StatusTone.Information, view.Frame.Notice.Tone);
            Assert.Equal(Visibility.Visible, view.Frame.Notice.Visibility);
            Assert.Equal("1 line · All amounts in INR", view.Frame.Footer);
        });
    }

    [Fact]
    public void A_Store_Managers_deposits_screen_has_no_bank_columns_and_no_Bank_credited_button()
    {
        RunSta(() =>
        {
            var deposit = FakeDepositService.Deposit("COLA", 1, 12500m, 2);
            var service = new FakeDepositService { Transit = [deposit], Proposals = [FakeDepositService.Proposal(deposit, 301)] };
            var view = new DepositsInTransitView(new DepositsInTransitModel(() => service, ShellAccess.StoreManager, "COLA"), new FakeDialogs());

            view.RefreshAsync().GetAwaiter().GetResult();

            Assert.Equal(["Store", "Deposit date", "Amount (₹)", "Age"], view.Table.Columns.Select(column => column.Header?.ToString()));
            Assert.Single(view.Table.ItemsSource!.Cast<object>());
            Assert.Equal(0, service.ProposalCalls);
            Assert.DoesNotContain(view.Table.Columns, column => column.Header?.ToString() == "Bank narration");
        });
    }

    [Fact]
    public void The_Owners_Bank_credited_button_waits_for_a_row_with_a_proposal_then_accepts_with_the_reason_asked()
    {
        RunSta(() =>
        {
            var deposit = FakeDepositService.Deposit("COLA", 1, 12500m, 2);
            var service = new FakeDepositService { Transit = [deposit], Proposals = [FakeDepositService.Proposal(deposit, 301)] };
            var dialogs = new FakeDialogs { ReasonAnswer = "Seen on the bank statement" };
            var view = new DepositsInTransitView(new DepositsInTransitModel(() => service, ShellAccess.Owner, "COLA"), dialogs);
            view.RefreshAsync().GetAwaiter().GetResult();

            Assert.False(view.AcceptButton.IsEnabled);
            Assert.Equal("Select a deposit first.", view.AcceptButton.ToolTip);
            Assert.Contains(view.Table.Columns, column => column.Header?.ToString() == "Bank narration");

            view.Table.SelectedIndex = 0;
            Assert.True(view.AcceptButton.IsEnabled);

            view.AcceptAsync().GetAwaiter().GetResult();
            Assert.Equal(["Bank credited"], dialogs.Asked);
            Assert.Equal(["Seen on the bank statement"], dialogs.Suggestions);
            Assert.Single(service.Accepted);
            Assert.Equal(StatusTone.Success, view.Frame.Notice.Tone);
        });
    }

    [Fact]
    public void Bank_files_keeps_Import_off_until_the_account_is_chosen_and_a_Viewer_gets_one_sentence()
    {
        RunSta(() =>
        {
            var service = new FakeImportService();
            service.Previews[@"C:\Synthetic\statement.csv"] = FakeImportService.Preview("statement.csv", FakeImportService.Layout(11, "Synthetic main account"), FakeImportService.Layout(12, "Synthetic second account"));
            var dialogs = new FakeDialogs { Files = [@"C:\Synthetic\statement.csv"] };
            var view = new BankFilesView(new BankFilesModel(() => service, ShellAccess.StoreManager), dialogs);

            view.ChooseAsync().GetAwaiter().GetResult();

            Assert.False(view.ImportButton.IsEnabled);
            Assert.Equal("Which account is this statement for?", view.ImportButton.ToolTip);
            Assert.Equal("Which account is this statement for?", view.Frame.Notice.Title);

            view.Table.SelectedIndex = 0;
            Assert.Equal(Visibility.Visible, view.ActionHost.Visibility);
            Assert.Equal(2, view.AccountBox.Items.Count);

            Assert.True(view.Model.Choose(view.Model.Items[0], 12));
            Assert.True(view.ImportButton.IsEnabled);

            var viewer = new BankFilesView(new BankFilesModel(() => service, ShellAccess.Viewer), dialogs);
            viewer.Model.AddFilesAsync([@"C:\Synthetic\statement.csv"]).GetAwaiter().GetResult();
            Assert.Equal(CollectionsScreens.BankFilesRefusedText, viewer.Frame.Notice.Title);
            Assert.False(viewer.ImportButton.IsEnabled);
            Assert.Single(service.Previewed);
        });
    }

    [Fact]
    public void Collections_setup_shows_its_sections_to_the_Owner_and_one_sentence_to_everyone_else()
    {
        RunSta(() =>
        {
            var service = new FakeSetupService();
            var owner = new CollectionsSetupView(new CollectionsSetupModel(() => service, ShellAccess.Owner, new FakeDialogs()));
            owner.ReloadAsync().GetAwaiter().GetResult();
            Assert.Equal(Visibility.Visible, owner.Sections.Visibility);
            // 1.12.1 P2 added "Settlement rules" (between File layouts and Statement folder).
            Assert.Equal(7, owner.Sections.Children.Count);
            Assert.Equal("Settlement rules", System.Windows.Automation.AutomationProperties.GetName(owner.Sections.Children[5]));

            var manager = new CollectionsSetupView(new CollectionsSetupModel(() => service, ShellAccess.StoreManager, new FakeDialogs()));
            manager.ReloadAsync().GetAwaiter().GetResult();
            Assert.Equal(Visibility.Collapsed, manager.Sections.Visibility);
            Assert.Equal(CollectionsScreens.SetupOwnerOnlyText, manager.Frame.Notice.Title);
            Assert.Equal(1, service.Loads);
        });
    }

    // ---- 1.12.1 P2 ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Owners_deposits_screen_records_by_hand_and_withdraws_and_the_Store_Managers_has_neither()
    {
        RunSta(() =>
        {
            var deposit = FakeDepositService.Deposit("COLA", 3, 8000m, 3);
            var service = new FakeDepositService { Transit = [deposit], CreditedList = [FakeDepositService.Credit(61, "COLA", 1, 12500m, 12500m, byHand: false)] };
            var dialogs = new FakeDialogs { ReasonAnswer = "Wrong line chosen", ConfirmAnswer = true };
            var view = new DepositsInTransitView(new DepositsInTransitModel(() => service, ShellAccess.Owner, "COLA", () => new DateOnly(2026, 8, 6)), dialogs);
            view.RefreshAsync().GetAwaiter().GetResult();

            Assert.False(view.RecordButton.IsEnabled);
            Assert.Equal("Select a deposit first.", view.RecordButton.ToolTip);
            Assert.Single(view.CreditedTable.ItemsSource!.Cast<object>());
            Assert.False(view.WithdrawButton.IsEnabled);

            view.Table.SelectedIndex = 0;
            Assert.True(view.RecordButton.IsEnabled);
            dialogs.ManualAnswer = new ManualCreditEntry(new DateOnly(2026, 8, 4), 7, "8000", null, "Seen on the bank passbook");
            view.RecordByHandAsync().GetAwaiter().GetResult();
            Assert.Equal("Record by hand", dialogs.Asked[^1]);
            Assert.Single(service.Manual);
            Assert.Equal(StatusTone.Success, view.Frame.Notice.Tone);

            view.CreditedTable.SelectedIndex = 0;
            Assert.True(view.WithdrawButton.IsEnabled);
            view.WithdrawAsync().GetAwaiter().GetResult();
            Assert.Equal("Wrong line chosen", Assert.Single(service.Withdrawn).Reason);

            var manager = new DepositsInTransitView(new DepositsInTransitModel(() => service, ShellAccess.StoreManager, "COLA"), new FakeDialogs());
            manager.RefreshAsync().GetAwaiter().GetResult();
            Assert.Null(manager.RecordButton.Parent);
            Assert.Null(manager.WithdrawButton.Parent);
            Assert.Same(manager.Table, manager.Frame.Body);
        });
    }

    [Fact]
    public void The_statement_exports_the_lines_on_screen_with_the_roles_columns()
    {
        RunSta(() =>
        {
            var line = new CollectionsStatementLine("COLA", new DateOnly(2026, 8, 1), "Cash", null, 12500m, 12500m, 0m, CollectionsLineState.DepositCredited,
                "Bank credited on 02 Aug 2026", 14000m, new DateOnly(2026, 8, 2), "DEP0001");
            var written = new List<(string Path, Etp.Reporting.Reporting.ExcelReportMetadata Metadata, Etp.Reporting.Reporting.ExcelReportData Data)>();
            CollectionsExcelExport export = (path, metadata, data) => { written.Add((path, metadata, data)); return Task.CompletedTask; };

            var plain = new CollectionsStatementView(new CollectionsStatementModel(() => new FakeStatementQuery { Statement = new([line], false) }, ShellAccess.Owner, "COLA", Business));
            Assert.Null(plain.ExportButton.Parent);

            var owner = new CollectionsStatementView(new CollectionsStatementModel(() => new FakeStatementQuery { Statement = new([line], false) }, ShellAccess.Owner, "COLA", Business), export);
            Assert.NotNull(owner.ExportButton.Parent);
            owner.ShowAsync().GetAwaiter().GetResult();
            Assert.True(owner.ExportButton.IsEnabled);
            owner.ExportToPathAsync(@"C:\Synthetic\owner.xlsx").GetAwaiter().GetResult();
            Assert.Contains(written[0].Data.Columns, column => column.Header == "Bank reference");
            Assert.Equal("Exported 1 line to owner.xlsx.", owner.Frame.Notice.Title);

            var manager = new CollectionsStatementView(new CollectionsStatementModel(() => new FakeStatementQuery { Statement = new([line], false) }, ShellAccess.StoreManager, "COLA", Business), export);
            manager.ShowAsync().GetAwaiter().GetResult();
            manager.ExportToPathAsync(@"C:\Synthetic\manager.xlsx").GetAwaiter().GetResult();
            Assert.DoesNotContain(written[1].Data.Columns, column => column.Header is "Bank reference" or "Credited on");
            Assert.EndsWith(CollectionsStatementModel.ExportWithoutBankColumnsText, manager.Frame.Notice.Title, StringComparison.Ordinal);

            CollectionsExcelExport failing = (_, _, _) => Task.FromException(new IOException("The file is open in another program."));
            var failed = new CollectionsStatementView(new CollectionsStatementModel(() => new FakeStatementQuery { Statement = new([line], false) }, ShellAccess.Owner, "COLA", Business), failing);
            failed.ShowAsync().GetAwaiter().GetResult();
            failed.ExportToPathAsync(@"C:\Synthetic\open.xlsx").GetAwaiter().GetResult();
            Assert.Equal(StatusTone.Critical, failed.Frame.Notice.Tone);
            Assert.Equal("The export could not be saved. The file is open in another program.", failed.Frame.Notice.Title);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
