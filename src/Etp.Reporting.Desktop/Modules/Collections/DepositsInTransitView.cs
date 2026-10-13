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
/// </summary>
public sealed class DepositsInTransitView : UserControl
{
    private readonly DepositsInTransitModel model;
    private readonly ICollectionsDialogs dialogs;
    private readonly PageFrame frame = new(PageTemplate.List);
    private readonly DataGrid grid = CollectionsUi.Grid("Deposits in transit");
    private readonly Button refreshButton;
    private readonly Button acceptButton = CollectionsUi.MakeButton("Bank credited", "PrimaryButton");
    private bool loaded;
    private bool rendering;

    public DepositsInTransitView(DepositsInTransitModel model, ICollectionsDialogs dialogs)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        refreshButton = CollectionsUi.MakeButton("Refresh", model.CanAccept ? null : "PrimaryButton");

        var toolbar = CollectionsUi.Toolbar();
        if (model.CanAccept) toolbar.Children.Add(acceptButton);
        toolbar.Children.Add(refreshButton);
        frame.Toolbar = toolbar;
        frame.Description = "Cash deposits the daily sheets say were sent and the bank has not been confirmed to credit yet, oldest first.";
        AutomationProperties.SetName(acceptButton, "Mark the selected deposit as credited by the bank");

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
        frame.Body = grid;
        Content = frame;
        AutomationProperties.SetName(this, "Deposits in transit");

        refreshButton.Click += async (_, _) => await RefreshAsync();
        acceptButton.Click += async (_, _) => await AcceptAsync();
        grid.SelectionChanged += (_, _) => { if (!rendering) UpdateAcceptButton(); };
        model.Changed += Render;
        Loaded += async (_, _) => { if (!loaded) { loaded = true; await RefreshAsync(); } };
    }

    public DepositsInTransitModel Model => model;
    public DataGrid Table => grid;
    internal PageFrame Frame => frame;
    internal Button AcceptButton => acceptButton;

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
            UpdateAcceptButton();
        }
        finally { rendering = false; }
    }

    private void UpdateAcceptButton()
    {
        var row = grid.SelectedItem as DepositRow;
        CollectionsUi.SetEnabled(acceptButton, model.CanAcceptRow(row), model.AcceptDisabledReason(row));
    }
}
