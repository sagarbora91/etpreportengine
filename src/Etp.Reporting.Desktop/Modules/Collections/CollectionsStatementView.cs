using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Modules.Collections;

/// <summary>
/// Reports > Collections: expected vs evidence. Template: Report (ETP-DESIGN-SYSTEM.md section 11 B): toolbar with the dates and
/// the mode, the scope line, one Notice ("Totals only, not matched yet"), the grid as the body and a footer with the unit.
/// Every state is a chip with its word; money is under a (₹) header and a missing figure is an em dash. A Viewer's statement
/// has no bank references and the column is not shown.
/// 1.12.1 P2: "Export to Excel" (secondary; Refresh stays the primary) writes the lines on screen. The bank columns (credited on,
/// bank reference) are in the Owner's file only (<see cref="CollectionsStatementModel.ExportIncludesBankColumns"/>).
/// </summary>
public sealed class CollectionsStatementView : UserControl
{
    private readonly CollectionsStatementModel model;
    private readonly PageFrame frame = new(PageTemplate.Report);
    private readonly DataGrid grid = CollectionsUi.Grid("Collections: expected vs evidence");
    private readonly Button refreshButton = CollectionsUi.MakeButton("Refresh", "PrimaryButton");
    private readonly Button exportButton = CollectionsUi.MakeButton("Export to Excel", automationName: "Export the statement to Excel");
    private readonly CollectionsExcelExport? export;
    private readonly DatePicker fromPicker = new();
    private readonly DatePicker toPicker = new();
    private readonly ComboBox modeBox = new();
    private DataGridColumn? referenceColumn;
    private bool loaded;
    private bool rendering;

    public CollectionsStatementView(CollectionsStatementModel model, CollectionsExcelExport? export = null)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.export = export;
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
        if (export is not null) toolbar.Children.Add(exportButton);
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
        exportButton.Click += async (_, _) =>
        {
            var path = ExportSaveDialog.Show(this, ExportSaveDialog.ExcelFilter, model.ExportFileName);
            if (path is not null) await ExportToPathAsync(path);
        };
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
    internal Button ExportButton => exportButton;

    /// <summary>The last export's sentence (shown in the notice until the next load).</summary>
    public string ExportText { get; private set; } = string.Empty;
    public StatusTone ExportTone { get; private set; } = StatusTone.Neutral;

    /// <summary>Writes the lines on screen to <paramref name="path"/> with the role's columns.</summary>
    public async Task ExportToPathAsync(string path)
    {
        if (export is null || !model.CanExport) return;
        try
        {
            await export(path, model.BuildExportMetadata(DateTimeOffset.UtcNow), model.BuildExportData());
            ExportTone = StatusTone.Success;
            ExportText = $"Exported {IndianFormat.Plural(model.Rows.Count, "line")} to {System.IO.Path.GetFileName(path)}."
                + (model.ExportIncludesBankColumns ? string.Empty : " " + CollectionsStatementModel.ExportWithoutBankColumnsText);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ExportTone = StatusTone.Critical;
            ExportText = "The export could not be saved. " + CollectionsScreens.Sentence(exception);
        }
        Render();
    }

    /// <summary>Reads the statement for the dates on screen (the first load, Refresh and a changed date all end here).</summary>
    public async Task ShowAsync()
    {
        ExportText = string.Empty;
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
            else if (ExportText.Length > 0) frame.Notice.Show(ExportTone, ExportText);
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
            CollectionsUi.SetEnabled(exportButton, model.CanExport, model.ExportDisabledReason);
        }
        finally { rendering = false; }
    }
}
