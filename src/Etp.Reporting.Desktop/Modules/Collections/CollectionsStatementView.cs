using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Modules.Collections;

/// <summary>
/// Reports > Collections: expected vs evidence. Template: Report (ETP-DESIGN-SYSTEM.md section 11 B): toolbar with the dates and
/// the mode, the scope line, one Notice ("Totals only, not matched yet"), the grid as the body and a footer with the unit.
/// Every state is a chip with its word; money is under a (₹) header and a missing figure is an em dash. A Viewer's statement
/// has no bank references and the column is not shown.
/// </summary>
public sealed class CollectionsStatementView : UserControl
{
    private readonly CollectionsStatementModel model;
    private readonly PageFrame frame = new(PageTemplate.Report);
    private readonly DataGrid grid = CollectionsUi.Grid("Collections: expected vs evidence");
    private readonly Button refreshButton = CollectionsUi.MakeButton("Refresh", "PrimaryButton");
    private readonly DatePicker fromPicker = new();
    private readonly DatePicker toPicker = new();
    private readonly ComboBox modeBox = new();
    private DataGridColumn? referenceColumn;
    private bool loaded;
    private bool rendering;

    public CollectionsStatementView(CollectionsStatementModel model)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        foreach (var picker in new[] { fromPicker, toPicker })
        {
            picker.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
            picker.Width = DesignTokens.Value("InputMedium");
            picker.Margin = DesignTokens.Gap(null, null, "Space8", "Space8");
        }
        modeBox.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        modeBox.Width = DesignTokens.Value("InputMedium");
        modeBox.Margin = DesignTokens.Gap(null, null, "Space8", "Space8");
        AutomationProperties.SetName(fromPicker, "From date");
        AutomationProperties.SetName(toPicker, "To date");
        AutomationProperties.SetName(modeBox, "Payment mode");
        fromPicker.SelectedDate = model.From.ToDateTime(TimeOnly.MinValue);
        toPicker.SelectedDate = model.To.ToDateTime(TimeOnly.MinValue);

        var toolbar = CollectionsUi.Toolbar();
        toolbar.Children.Add(refreshButton);
        toolbar.Children.Add(fromPicker);
        toolbar.Children.Add(toPicker);
        toolbar.Children.Add(modeBox);
        frame.Toolbar = toolbar;

        CollectionsUi.AddText(grid, "Store", nameof(StatementRow.Store), minWidth: 80);
        CollectionsUi.AddText(grid, "Date", nameof(StatementRow.Date), minWidth: 110);
        CollectionsUi.AddText(grid, "Mode", nameof(StatementRow.Mode), minWidth: 100);
        CollectionsUi.AddText(grid, "Provider", nameof(StatementRow.Provider), minWidth: 110);
        CollectionsUi.AddText(grid, IndianFormat.MoneyHeader("Expected"), nameof(StatementRow.Expected), number: true, minWidth: 120);
        CollectionsUi.AddText(grid, IndianFormat.MoneyHeader("Evidence"), nameof(StatementRow.Evidence), number: true, minWidth: 120);
        CollectionsUi.AddText(grid, IndianFormat.MoneyHeader("Difference"), nameof(StatementRow.Difference), number: true, minWidth: 120);
        CollectionsUi.AddText(grid, IndianFormat.MoneyHeader("Cash tenders"), nameof(StatementRow.CashTenders), number: true, minWidth: 120);
        CollectionsUi.AddChip(grid, "Status", row => (((StatementRow)row).StatusWord, ((StatementRow)row).Tone), minWidth: 190);
        CollectionsUi.AddText(grid, "Bank reference", nameof(StatementRow.BankReference), minWidth: 130);
        referenceColumn = grid.Columns[^1];
        CollectionsUi.AddText(grid, "Detail", nameof(StatementRow.Detail), minWidth: 240, star: true);
        frame.Body = grid;
        Content = frame;
        AutomationProperties.SetName(this, "Collections: expected vs evidence");

        refreshButton.Click += async (_, _) => await ShowAsync();
        fromPicker.SelectedDateChanged += (_, _) => { if (!rendering && fromPicker.SelectedDate is { } date) model.From = DateOnly.FromDateTime(date); };
        toPicker.SelectedDateChanged += (_, _) => { if (!rendering && toPicker.SelectedDate is { } date) model.To = DateOnly.FromDateTime(date); };
        modeBox.SelectionChanged += (_, _) => { if (!rendering && modeBox.SelectedItem is string mode) model.ModeFilter = mode; };
        model.Changed += Render;
        Loaded += async (_, _) => { if (!loaded) { loaded = true; await ShowAsync(); } };
    }

    public CollectionsStatementModel Model => model;
    public DataGrid Table => grid;
    internal PageFrame Frame => frame;
    internal DataGridColumn? ReferenceColumn => referenceColumn;

    /// <summary>Reads the statement for the dates on screen (the first load, Refresh and a changed date all end here).</summary>
    public async Task ShowAsync()
    {
        try { await model.LoadAsync(); }
        catch (OperationCanceledException) { }
        Render();
    }

    private void Render()
    {
        if (rendering) return;
        rendering = true;
        try
        {
            frame.Description = model.IsAllowed ? model.ScopeText : string.Empty;
            if (!model.IsAllowed) frame.Notice.Show(StatusTone.Critical, model.RefusalText!);
            else if (model.StatusText.Length > 0 && model.StatusTone != StatusTone.Neutral) frame.Notice.Show(model.StatusTone, model.StatusText);
            else if (model.ShowsTotalsOnlyNotice) frame.Notice.Show(StatusTone.Information, CollectionsStatementModel.TotalsOnlyNoticeTitle, CollectionsStatementModel.TotalsOnlyNoticeBody);
            else if (model.StatusText.Length > 0) frame.Notice.Show(StatusTone.Neutral, model.StatusText);
            else frame.Notice.Clear();

            grid.ItemsSource = model.Rows;
            if (referenceColumn is not null) referenceColumn.Visibility = model.WithoutBankReferences ? Visibility.Collapsed : Visibility.Visible;
            var modes = model.Modes;
            if (!modes.SequenceEqual(modeBox.Items.Cast<string>())) modeBox.ItemsSource = modes;
            modeBox.SelectedItem = model.ModeFilter;
            frame.Footer = model.FooterText;
            CollectionsUi.SetEnabled(refreshButton, model.IsAllowed, model.RefusalText ?? string.Empty);
        }
        finally { rendering = false; }
    }
}
