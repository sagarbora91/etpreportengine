extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using EtpApplication::Etp.Reporting.Application.Collections;

namespace Etp.Reporting.Desktop.Modules.Collections;

/// <summary>
/// Import > Bank and settlement files. Template: List (ETP-DESIGN-SYSTEM.md section 11 A) - the grid of chosen files is the
/// body; the card under it asks the one question the selected file needs (which account, or what to do with possible
/// duplicate lines). One primary button: Import. The role is checked by the model and again by the service.
/// </summary>
public sealed class BankFilesView : UserControl
{
    private readonly BankFilesModel model;
    private readonly ICollectionsDialogs dialogs;
    private readonly PageFrame frame = new(PageTemplate.List);
    private readonly DataGrid grid = CollectionsUi.Grid("Bank and settlement files");
    private readonly Button importButton = CollectionsUi.MakeButton("Import", "PrimaryButton");
    private readonly Button chooseButton = CollectionsUi.MakeButton("Choose...");
    private readonly Button clearButton = CollectionsUi.MakeButton("Clear list", "QuietButton");
    private readonly StackPanel actionCard = new();
    private readonly Border actionHost;
    private readonly ComboBox accountBox = new() { DisplayMemberPath = nameof(AccountChoice.Label) };
    private readonly Button useAccountButton = CollectionsUi.MakeButton("Use this account", "PrimaryButton");
    private readonly Button importNewButton = CollectionsUi.MakeButton("Import as new lines");
    private readonly Button restateButton = CollectionsUi.MakeButton("Replace the earlier file");
    private readonly TextBlock actionText = CollectionsUi.Text(string.Empty, "BodyText");
    private bool loaded;
    private bool rendering;

    public BankFilesView(BankFilesModel model, ICollectionsDialogs dialogs)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

        var toolbar = CollectionsUi.Toolbar();
        toolbar.Children.Add(importButton);
        toolbar.Children.Add(chooseButton);
        toolbar.Children.Add(clearButton);
        frame.Toolbar = toolbar;
        frame.Description = "Bank statements and payment-provider settlement files, as .csv or .xlsx. Each file is matched to a layout the Owner set up.";
        AutomationProperties.SetName(importButton, "Import the ready files");
        AutomationProperties.SetName(chooseButton, "Choose bank or settlement files");

        CollectionsUi.AddText(grid, "File", nameof(FileRow.File), minWidth: 220);
        CollectionsUi.AddText(grid, "Matches", nameof(FileRow.Matches), minWidth: 200);
        CollectionsUi.AddChip(grid, "Status", row => (((FileRow)row).Item.StatusWord, ((FileRow)row).Item.Tone));
        CollectionsUi.AddText(grid, "Result", nameof(FileRow.Result), minWidth: 260, star: true);

        accountBox.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        accountBox.Width = DesignTokens.Value("InputWide");
        AutomationProperties.SetName(accountBox, BankFilesModel.ChooseAccountPrompt);
        var actionRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = DesignTokens.Gap(null, "Space8", null, null) };
        actionRow.Children.Add(accountBox); actionRow.Children.Add(useAccountButton);
        actionRow.Children.Add(importNewButton); actionRow.Children.Add(restateButton);
        accountBox.Margin = DesignTokens.Gap(null, null, "Space8", "Space8");
        actionCard.Children.Add(actionText);
        actionCard.Children.Add(actionRow);
        actionHost = new Border { Child = actionCard, Margin = DesignTokens.Gap(null, "Space16", null, null), Visibility = Visibility.Collapsed };
        actionHost.SetResourceReference(FrameworkElement.StyleProperty, "InsetCard");

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(grid);
        Grid.SetRow(actionHost, 1); body.Children.Add(actionHost);
        frame.Body = body;
        Content = frame;
        AutomationProperties.SetName(this, "Bank and settlement files");

        importButton.Click += async (_, _) => await Run(async () => { await model.ImportReadyAsync(); });
        chooseButton.Click += async (_, _) => await ChooseAsync();
        clearButton.Click += (_, _) => model.Clear();
        grid.SelectionChanged += (_, _) => { if (!rendering) RenderActions((grid.SelectedItem as FileRow)?.Item); };
        useAccountButton.Click += (_, _) =>
        {
            if (grid.SelectedItem is FileRow row && accountBox.SelectedItem is AccountChoice choice) model.Choose(row.Item, choice.LayoutId);
        };
        importNewButton.Click += async (_, _) => await ResolveAsync(CollectionsOverlapChoice.ImportAsNew);
        restateButton.Click += async (_, _) => await ResolveAsync(CollectionsOverlapChoice.Restate);
        model.Changed += Render;
        AllowDrop = true;
        Drop += async (_, args) =>
        {
            if (args.Data.GetData(DataFormats.FileDrop) is string[] paths) await Run(() => model.AddFilesAsync(paths));
        };
        Loaded += (_, _) => { if (!loaded) { loaded = true; Render(); } };
        Render();
    }

    public BankFilesModel Model => model;
    public DataGrid Table => grid;
    internal PageFrame Frame => frame;
    internal Button ImportButton => importButton;
    internal Button UseAccountButton => useAccountButton;
    internal ComboBox AccountBox => accountBox;
    internal Border ActionHost => actionHost;

    /// <summary>Opens the chooser and checks the files (the same as pressing Choose).</summary>
    public async Task ChooseAsync()
    {
        var files = dialogs.ChooseStatementFiles();
        if (files.Count > 0) await Run(() => model.AddFilesAsync(files));
    }

    private async Task ResolveAsync(CollectionsOverlapChoice choice)
    {
        if (grid.SelectedItem is not FileRow row) return;
        var prompt = choice == CollectionsOverlapChoice.ImportAsNew
            ? "Why are these lines new, even though they look like ones imported before?"
            : "Why should this file replace the earlier one for its dates?";
        var reason = dialogs.AskReason(choice == CollectionsOverlapChoice.ImportAsNew ? "Import as new lines" : "Replace the earlier file", prompt);
        if (string.IsNullOrWhiteSpace(reason)) return;
        await Run(async () => { await model.ResolveAsync(row.Item, choice, reason); });
    }

    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { frame.Notice.Show(StatusTone.Critical, CollectionsScreens.Sentence(exception)); }
    }

    private void Render()
    {
        if (rendering) return;
        rendering = true;
        try { RenderCore(); }
        finally { rendering = false; }
    }

    private void RenderCore()
    {
        if (!model.IsAllowed)
        {
            frame.Notice.Show(model.Access.HasAssignedRole ? StatusTone.Information : StatusTone.Critical, model.RefusalText!);
            grid.ItemsSource = null; actionHost.Visibility = Visibility.Collapsed;
            CollectionsUi.SetEnabled(importButton, false, model.RefusalText!);
            CollectionsUi.SetEnabled(chooseButton, false, model.RefusalText!);
            clearButton.IsEnabled = false;
            frame.Footer = string.Empty;
            return;
        }
        var selected = (grid.SelectedItem as FileRow)?.Item;
        grid.ItemsSource = model.Items.Select(item => new FileRow(item)).ToList();
        if (selected is not null) grid.SelectedItem = ((IEnumerable<FileRow>)grid.ItemsSource).FirstOrDefault(row => row.Item == selected);
        CollectionsUi.SetEnabled(importButton, model.CanImportReady, model.ImportDisabledReason);
        clearButton.IsEnabled = model.Items.Count > 0;
        frame.Footer = model.Summary;
        var needsChoice = model.Items.FirstOrDefault(item => item.State == BankFileState.ChooseAccount);
        if (needsChoice is not null) frame.Notice.Show(StatusTone.Warning, BankFilesModel.ChooseAccountPrompt, "Pick the account for each file marked Choose the account. Import stays off until you do.");
        else frame.Notice.Clear();
        RenderActions((grid.SelectedItem as FileRow)?.Item);
    }

    private void RenderActions(BankFileItem? item)
    {
        var choosing = item is { NeedsAccountChoice: true, State: BankFileState.ChooseAccount or BankFileState.Ready };
        var duplicates = item is { State: BankFileState.PossibleDuplicates };
        actionHost.Visibility = choosing || duplicates ? Visibility.Visible : Visibility.Collapsed;
        if (item is null || !(choosing || duplicates)) return;
        accountBox.Visibility = useAccountButton.Visibility = choosing ? Visibility.Visible : Visibility.Collapsed;
        importNewButton.Visibility = restateButton.Visibility = duplicates ? Visibility.Visible : Visibility.Collapsed;
        if (choosing)
        {
            actionText.Text = $"{item.DisplayName}: {BankFilesModel.ChooseAccountPrompt}";
            accountBox.ItemsSource = item.Layouts.Select(layout => new AccountChoice(layout.LayoutId, BankFileItem.LayoutLabel(layout))).ToList();
            accountBox.SelectedItem = ((IEnumerable<AccountChoice>)accountBox.ItemsSource).FirstOrDefault(choice => choice.LayoutId == item.SelectedLayoutId);
        }
        else actionText.Text = $"{item.DisplayName}: {item.Message}";
    }

    private sealed record AccountChoice(int LayoutId, string Label);

    private sealed class FileRow(BankFileItem item)
    {
        public BankFileItem Item { get; } = item;
        public string File => Item.DisplayName;
        public string Matches => Item.SelectedLayout is { } layout ? BankFileItem.LayoutLabel(layout) : Item.NeedsAccountChoice ? $"{Item.Layouts.Count} layouts match" : CollectionsScreens.MissingValue;
        public string Result => Item.Message;
    }
}
