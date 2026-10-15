using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Modules.Collections;

/// <summary>
/// Reports > Deposits in transit. Template: List (ETP-DESIGN-SYSTEM.md section 11 A): the grid of deposits, oldest first, is the
/// body. The age is a chip (quiet, then amber after three days, then red after seven). The Owner also sees the bank line
/// R-DEP-1 proposes and presses "Bank credited" (the one primary) to accept it with a reason; a Store Manager sees the same
/// deposits without any bank line or narration; a Viewer gets one sentence.
/// 1.12.1 P2: the Owner also has "Record by hand" (a credit with no bank line in ETP: date, account, amount, reference, reason)
/// and, under the list, "Marked as credited" (the last two months' active credits) with "Withdraw" for a mistake. Both write
/// the CashDepositConfirmed audit row in the service; neither is on the Store Manager's screen.
/// </summary>
public sealed class DepositsInTransitView : UserControl
{
    private readonly DepositsInTransitModel model;
    private readonly ICollectionsDialogs dialogs;
    private readonly PageFrame frame = new(PageTemplate.List);
    private readonly DataGrid grid = CollectionsUi.Grid("Deposits in transit");
    private readonly Button refreshButton;
    private readonly Button acceptButton = CollectionsUi.MakeButton("Bank credited", "PrimaryButton");
    private readonly Button recordButton = CollectionsUi.MakeButton("Record by hand");
    private readonly Button withdrawButton = CollectionsUi.MakeButton("Withdraw");
    private readonly DataGrid creditedGrid = CollectionsUi.Grid("Marked as credited");
    private readonly TextBlock creditedEmpty = CollectionsUi.Text("No bank credit marked in the last two months.", "CaptionText");
    private bool loaded;
    private bool rendering;

    public DepositsInTransitView(DepositsInTransitModel model, ICollectionsDialogs dialogs)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        refreshButton = CollectionsUi.MakeButton("Refresh", model.CanAccept ? null : "PrimaryButton");

        var toolbar = CollectionsUi.Toolbar();
        if (model.CanAccept) { toolbar.Children.Add(acceptButton); toolbar.Children.Add(recordButton); }
        toolbar.Children.Add(refreshButton);
        frame.Toolbar = toolbar;
        frame.Description = "Cash deposits the daily sheets say were sent and the bank has not been confirmed to credit yet, oldest first.";
        AutomationProperties.SetName(acceptButton, "Mark the selected deposit as credited by the bank");
        AutomationProperties.SetName(recordButton, "Record a bank credit by hand for the selected deposit");
        AutomationProperties.SetName(withdrawButton, "Withdraw the selected bank credit");

        CollectionsUi.AddText(grid, "Store", nameof(DepositRow.Store), minWidth: 80);
        CollectionsUi.AddText(grid, "Deposit date", nameof(DepositRow.Date), minWidth: 120);
        CollectionsUi.AddText(grid, IndianFormat.MoneyHeader("Amount"), nameof(DepositRow.Amount), number: true, minWidth: 130);
        CollectionsUi.AddChip(grid, "Age", row => (((DepositRow)row).AgeWord, ((DepositRow)row).AgeTone), minWidth: 100);
        if (model.CanAccept)
        {
            // The bank's side is the Owner's: a Store Manager's screen has no such columns, not blank ones.
            CollectionsUi.AddText(grid, "Bank line that fits", nameof(DepositRow.BankLine), minWidth: 300, star: true);
            CollectionsUi.AddChip(grid, "Match", row => (((DepositRow)row).ProposalWord, ((DepositRow)row).ProposalTone), minWidth: 150);
            CollectionsUi.AddText(grid, "Bank narration", nameof(DepositRow.Narration), minWidth: 220);
        }
        if (model.CanAccept)
        {
            // The grid stays the body (List template); the Owner's credited list sits under it in its own card.
            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(grid, 0);
            body.Children.Add(grid);
            var card = CreditedCard();
            card.Margin = DesignTokens.Gap(null, "Space24", null, null);
            Grid.SetRow(card, 1);
            body.Children.Add(card);
            frame.Body = body;
        }
        else frame.Body = grid;
        Content = frame;
        AutomationProperties.SetName(this, "Deposits in transit");

        refreshButton.Click += async (_, _) => await RefreshAsync();
        acceptButton.Click += async (_, _) => await AcceptAsync();
        recordButton.Click += async (_, _) => await RecordByHandAsync();
        withdrawButton.Click += async (_, _) => await WithdrawAsync();
        grid.SelectionChanged += (_, _) => { if (!rendering) UpdateAcceptButton(); };
        creditedGrid.SelectionChanged += (_, _) => { if (!rendering) UpdateWithdrawButton(); };
        model.Changed += Render;
        Loaded += async (_, _) => { if (!loaded) { loaded = true; await RefreshAsync(); } };
    }

    public DepositsInTransitModel Model => model;
    public DataGrid Table => grid;
    internal PageFrame Frame => frame;
    internal Button AcceptButton => acceptButton;
    internal Button RecordButton => recordButton;
    internal Button WithdrawButton => withdrawButton;
    internal DataGrid CreditedTable => creditedGrid;

    public async Task RefreshAsync()
    {
        try { await model.LoadAsync(); }
        catch (OperationCanceledException) { }
        Render();
    }

    /// <summary>Asks for the reason and accepts the selected row's bank line. A line that fits several deposits (or a deposit
    /// with several lines) says so in the confirmation first, because the choice is the Owner's.</summary>
    public async Task AcceptAsync()
    {
        if (grid.SelectedItem is not DepositRow row || !model.CanAcceptRow(row)) return;
        if (row.IsAmbiguous && !dialogs.Confirm("Accept this bank line",
                $"More than one bank line fits the deposit of {row.Amount} on {row.Date}, or this line fits more than one deposit. Accept this line for store {row.Store}?")) return;
        var reason = dialogs.AskReason("Bank credited", $"Why do you accept this bank credit for store {row.Store}, {row.Date}?", DepositsInTransitModel.DefaultReason);
        if (string.IsNullOrWhiteSpace(reason)) return;
        try { await model.AcceptAsync(row, reason); }
        catch (OperationCanceledException) { }
        Render();
    }

    /// <summary>"Record by hand": the bank credited the selected deposit and ETP has no bank line for it. The form starts with the
    /// sheet's amount; a different amount is allowed (the statement then says so) and said in the result.</summary>
    public async Task RecordByHandAsync()
    {
        if (grid.SelectedItem is not DepositRow row || !model.CanRecordRow(row)) return;
        var accounts = await model.LoadAccountsAsync(row);
        if (accounts.Count == 0) { Render(); return; } // the model's notice says why
        var entry = dialogs.AskManualCredit("Record by hand",
            $"The bank credited the deposit of {row.Amount} sent on {row.Date} by store {row.Store}. Enter what the bank statement or passbook shows.",
            model.Suggestion(row, accounts), accounts);
        if (entry is null) return;
        try { await model.RecordByHandAsync(row, entry); }
        catch (OperationCanceledException) { }
        Render();
    }

    /// <summary>"Withdraw": the selected credit was marked by mistake. A confirmation names it, then a reason is asked.</summary>
    public async Task WithdrawAsync()
    {
        if (creditedGrid.SelectedItem is not CreditedRow row || !model.CanWithdraw(row)) return;
        if (!dialogs.Confirm("Withdraw this bank credit",
                $"Store {row.Store}, deposit of {row.DepositDate}, {row.Credited} credited on {row.CreditedOn}. The deposit goes back to Deposits in transit; nothing is deleted.")) return;
        var reason = dialogs.AskReason("Withdraw", $"Why is the bank credit for store {row.Store}, {row.DepositDate} withdrawn?", DepositsInTransitModel.DefaultWithdrawReason);
        if (string.IsNullOrWhiteSpace(reason)) return;
        try { await model.WithdrawAsync(row, reason); }
        catch (OperationCanceledException) { }
        Render();
    }

    private Border CreditedCard()
    {
        CollectionsUi.AddText(creditedGrid, "Store", nameof(CreditedRow.Store), minWidth: 80);
        CollectionsUi.AddText(creditedGrid, "Deposit date", nameof(CreditedRow.DepositDate), minWidth: 120);
        CollectionsUi.AddText(creditedGrid, IndianFormat.MoneyHeader("Sent"), nameof(CreditedRow.Sent), number: true, minWidth: 120);
        CollectionsUi.AddText(creditedGrid, IndianFormat.MoneyHeader("Credited"), nameof(CreditedRow.Credited), number: true, minWidth: 120);
        CollectionsUi.AddText(creditedGrid, "Credited on", nameof(CreditedRow.CreditedOn), minWidth: 120);
        CollectionsUi.AddText(creditedGrid, "Account", nameof(CreditedRow.Account), minWidth: 180);
        CollectionsUi.AddText(creditedGrid, "Bank reference", nameof(CreditedRow.BankReference), minWidth: 130);
        CollectionsUi.AddChip(creditedGrid, "How", row => (((CreditedRow)row).HowWord, ((CreditedRow)row).HowTone), minWidth: 160);
        CollectionsUi.AddText(creditedGrid, "Amounts", nameof(CreditedRow.Agreement), minWidth: 140);
        CollectionsUi.AddText(creditedGrid, "Reason", nameof(CreditedRow.Reason), minWidth: 200, star: true);
        var stack = new StackPanel();
        stack.Children.Add(creditedGrid);
        stack.Children.Add(creditedEmpty);
        var buttons = new WrapPanel { Margin = DesignTokens.Gap(null, "Space8", null, null) };
        buttons.Children.Add(withdrawButton);
        stack.Children.Add(buttons);
        return CollectionsUi.Card("Marked as credited", stack, "Bank credits for the last two months' deposits, and those marked in that time. Withdraw one that was marked by mistake; the deposit is then in transit again.");
    }

    private void Render()
    {
        if (rendering) return;
        rendering = true;
        try
        {
            if (!model.IsAllowed)
            {
                frame.Notice.Show(model.Access.HasAssignedRole ? StatusTone.Information : StatusTone.Critical, model.RefusalText!);
                grid.ItemsSource = null;
                frame.Footer = string.Empty;
                CollectionsUi.SetEnabled(refreshButton, false, model.RefusalText!);
                return;
            }
            if (model.NoticeText.Length > 0) frame.Notice.Show(model.NoticeTone, model.NoticeText);
            else if (model.StatusText.Length > 0) frame.Notice.Show(model.StatusTone == StatusTone.Neutral ? StatusTone.Neutral : model.StatusTone, model.StatusText);
            else frame.Notice.Clear();
            var selected = grid.SelectedItem as DepositRow;
            grid.ItemsSource = model.Rows;
            if (selected is not null) grid.SelectedItem = model.Rows.FirstOrDefault(row => row.Store == selected.Store && row.Deposit.DepositDate == selected.Deposit.DepositDate
                && row.Proposal?.Line.BankLineId == selected.Proposal?.Line.BankLineId);
            frame.Footer = model.FooterText;
            if (model.CanAccept)
            {
                var chosen = creditedGrid.SelectedItem as CreditedRow;
                creditedGrid.ItemsSource = model.Credited;
                CollectionsUi.SizePreview(creditedGrid, model.Credited.Count);
                creditedGrid.Visibility = model.Credited.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                creditedEmpty.Visibility = model.Credited.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (chosen is not null) creditedGrid.SelectedItem = model.Credited.FirstOrDefault(row => row.Credit.ConfirmationId == chosen.Credit.ConfirmationId);
            }
            UpdateAcceptButton();
            UpdateWithdrawButton();
        }
        finally { rendering = false; }
    }

    private void UpdateWithdrawButton()
    {
        var row = creditedGrid.SelectedItem as CreditedRow;
        CollectionsUi.SetEnabled(withdrawButton, model.CanWithdraw(row), model.WithdrawDisabledReason(row));
    }

    private void UpdateAcceptButton()
    {
        var row = grid.SelectedItem as DepositRow;
        CollectionsUi.SetEnabled(acceptButton, model.CanAcceptRow(row), model.AcceptDisabledReason(row));
        CollectionsUi.SetEnabled(recordButton, model.CanRecordRow(row), model.RecordDisabledReason(row));
    }
}
