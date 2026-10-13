extern alias EtpApplication;

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MorningSummary = EtpApplication::Etp.Reporting.Application.Morning.MorningSummary;
using MorningStoreCard = EtpApplication::Etp.Reporting.Application.Morning.MorningStoreCard;
using MorningStoreStatus = EtpApplication::Etp.Reporting.Application.Morning.MorningStoreStatus;
using MorningChip = EtpApplication::Etp.Reporting.Application.Morning.MorningChip;
using MorningCardLine = EtpApplication::Etp.Reporting.Application.Morning.MorningCardLine;
using MorningMonthStartCard = EtpApplication::Etp.Reporting.Application.Morning.MorningMonthStartCard;
using MorningFinaliseResult = EtpApplication::Etp.Reporting.Application.Morning.MorningFinaliseResult;

namespace Etp.Reporting.Desktop.Modules.Morning;

/// <summary>
/// The Owner's Morning page (task <c>morning</c>, Today &gt; Morning, the Owner's landing screen; 1.12 U1, wave plan C-a,
/// decision 2). Template D (Dashboard, ETP-DESIGN-SYSTEM.md section 11): toolbar with one primary ("Finalise N ready
/// stores", shown only when a store is ready), the verdict line, one Notice, then the three state chips (System check,
/// Shop files, Service), one card per active shop, the month-start card on days 1-7, and a quiet footer (off-PC backup
/// age, backup disk copy age, as-at). No grid, no amounts, no customer data; Today &gt; Sales is one click away.
/// The update line is not here: B2's shell banner shows on every page.
/// </summary>
public sealed class MorningView : UserControl, IShellScreen
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    private readonly MorningScreenServices services;
    private readonly ScreenRequest request;
    private readonly PageFrame frame = new(PageTemplate.Dashboard);
    private readonly Button finaliseButton = new();
    private readonly Button refreshButton = new() { Content = "Refresh" };
    private readonly Button publishButton = new() { Content = "Publish shop results now" };
    private readonly TextBlock verdict = new();
    private readonly TextBlock verdictDetail = new();
    private readonly WrapPanel chips = new();
    private readonly WrapPanel stores = new();
    private readonly ContentControl monthStart = new() { Focusable = false, IsTabStop = false };
    private CancellationTokenSource? work;
    private int revision;

    /// <summary>Asks before "Finalise all ready stores" (ConfirmationSheet in the app; replaced in tests).</summary>
    public Func<DependencyObject, string, string, bool> Confirm { get; set; } = ConfirmationSheet.Show;

    /// <summary>N1's buttons; when null the shell's <c>MainWindow.NoSaleDayButtons</c> is used, and without both no button shows.</summary>
    public NoSaleDayButtons? NoSaleButtons { get; set; }

    /// <summary>The user name the finalise rows carry (as Close day: the Windows user).</summary>
    public Func<string> User { get; set; } = () => Environment.UserName;

    public MorningView(MorningScreenServices services, ScreenRequest request)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        AutomationProperties.SetName(this, "Morning page");

        finaliseButton.SetResourceReference(StyleProperty, "PrimaryButton");
        finaliseButton.Visibility = Visibility.Collapsed;
        finaliseButton.Click += async (_, _) => await FinaliseAllAsync();
        refreshButton.Click += (_, _) => Refresh();
        publishButton.Click += async (_, _) => await PublishAsync();
        ToolTipService.SetShowOnDisabled(publishButton, true);
        publishButton.ToolTip = "Runs the 'ETP Publish Shop Results' task now instead of waiting for 07:40.";
        var toolbar = new WrapPanel();
        foreach (var button in new[] { finaliseButton, refreshButton, publishButton })
        {
            button.Margin = DesignTokens.Gap(null, null, "Space8", "Space4");
            toolbar.Children.Add(button);
        }
        frame.Toolbar = toolbar;

        verdict.SetResourceReference(StyleProperty, "SectionTitleText");
        AutomationProperties.SetHeadingLevel(verdict, AutomationHeadingLevel.Level2);
        AutomationProperties.SetLiveSetting(verdict, AutomationLiveSetting.Polite);
        verdictDetail.SetResourceReference(StyleProperty, "BodyText");
        verdictDetail.Margin = DesignTokens.Gap(null, "Space4", null, null);

        var body = new StackPanel();
        body.Children.Add(verdict);
        body.Children.Add(verdictDetail);
        chips.Margin = DesignTokens.Gap(null, "Space16", null, null);
        AutomationProperties.SetName(chips, "System state");
        body.Children.Add(chips);
        stores.Margin = DesignTokens.Gap(null, "Space16", null, null);
        AutomationProperties.SetName(stores, "Stores");
        body.Children.Add(stores);
        monthStart.Margin = DesignTokens.Gap(null, "Space16", null, null);
        body.Children.Add(monthStart);
        frame.Body = body;
        Content = frame;

        verdict.Text = "Loading the morning summary…";
        Loaded += (_, _) => { if (Summary is null && !IsBusy) Refresh(); };
        Unloaded += (_, _) => work?.Cancel();
    }

    /// <summary>The summary on screen; null before the first load or after a failed one.</summary>
    public MorningSummary? Summary { get; private set; }

    public bool IsBusy { get; private set; }

    /// <summary>The page's Notice (row 2): load failures, Finalise all and publish results.</summary>
    public Notice Notice => frame.Notice;

    public Button FinaliseButton => finaliseButton;
    public Button PublishButton => publishButton;

    public void Refresh() => _ = LoadAsync();

    public async Task LoadAsync()
    {
        if (!request.Access.CanAdminister)
        {
            Show(null);
            verdict.Text = "The Morning page is for the Owner.";
            verdictDetail.Text = "Today > Sales shows the day for every role.";
            return;
        }
        var mine = ++revision;
        work?.Cancel();
        work = new CancellationTokenSource();
        var token = work.Token;
        refreshButton.IsEnabled = false;
        try
        {
            var summary = await services.Summary().LoadAsync(request.BusinessDate, token);
            if (mine != revision) return;
            Show(summary);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (mine != revision) return;
            DesktopDiagnostics.Record(exception, "Morning.Load", "MORNING_LOAD_FAILED");
            Show(null);
            verdict.Text = "The morning summary could not be read.";
            verdictDetail.Text = string.Empty;
            frame.Notice.Show(StatusTone.Critical, "Could not read the morning summary", DesktopFriendlyError.Describe(exception));
        }
        finally { if (mine == revision) refreshButton.IsEnabled = !IsBusy; }
    }

    /// <summary>Draws a summary (public for the view tests).</summary>
    public void Show(MorningSummary? summary)
    {
        Summary = summary;
        chips.Children.Clear();
        stores.Children.Clear();
        monthStart.Content = null;
        frame.Footer = string.Empty;
        finaliseButton.Visibility = Visibility.Collapsed;
        if (summary is null) return;

        verdict.Text = summary.Verdict;
        verdictDetail.Text = summary.VerdictDetail;
        if (summary.FinaliseLabel is { } label)
        {
            finaliseButton.Content = label;
            AutomationProperties.SetName(finaliseButton, label);
            finaliseButton.Visibility = Visibility.Visible;
            finaliseButton.IsEnabled = !IsBusy;
        }
        foreach (var chip in summary.Chips) chips.Children.Add(ChipCard(chip));
        foreach (var store in summary.Stores) stores.Children.Add(StoreCard(store, summary.BusinessDate));
        if (summary.MonthStart is { } month) monthStart.Content = MonthCard(month);
        frame.Footer = string.Join(" · ", summary.Footer.Select(line => line.Label + ": " + line.Detail));
    }

    // ------------------------------------------------------------------ actions

    public async Task FinaliseAllAsync()
    {
        if (Summary is not { } summary || summary.ReadyStoreCodes.Count == 0 || IsBusy) return;
        var ready = summary.Stores.Where(card => summary.ReadyStoreCodes.Contains(card.StoreCode)).ToArray();
        var label = summary.FinaliseLabel ?? "Finalise ready stores";
        var detail = $"Lock {string.Join(", ", ready.Select(card => $"{card.StoreName} ({card.StoreCode})"))} for "
            + $"{summary.BusinessDate.ToString("dd MMM yyyy", English)}? Each store is locked on its own, so one refusal does not stop the others. "
            + "A change after this needs Reopen day on Close day.";
        if (!Confirm(this, label, detail)) return;
        await RunAsync(async token =>
        {
            var progress = new Progress<string>(store =>
            {
                void ShowProgress() => frame.Notice.Show(StatusTone.Information, "Finalising", $"Locking {NameOf(store)}…");
                if (Dispatcher.CheckAccess()) ShowProgress(); else Dispatcher.BeginInvoke(ShowProgress);
            });
            var results = await services.FinaliseAll(summary.BusinessDate, ready.Select(card => card.StoreCode).ToArray(), progress, token);
            frame.Notice.Show(FinaliseTone(results), FinaliseTitle(results), FinaliseMessage(results, NameOf));
        });
        await LoadAsync();
    }

    public async Task PublishAsync()
    {
        if (IsBusy) return;
        await RunAsync(async token =>
        {
            frame.Notice.Show(StatusTone.Information, "Publishing shop results", "Signing and sending the waiting results to the shops…");
            var result = await services.PublishShopResults(token);
            frame.Notice.Show(result.Succeeded ? StatusTone.Success : StatusTone.Warning,
                result.Succeeded ? "Shop results published" : "Shop results were not all published", result.Message);
        });
        await LoadAsync();
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        IsBusy = true;
        finaliseButton.IsEnabled = refreshButton.IsEnabled = publishButton.IsEnabled = false;
        using var cancel = new CancellationTokenSource();
        try { await action(cancel.Token); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DesktopDiagnostics.Record(exception, "Morning.Action", "MORNING_ACTION_FAILED");
            frame.Notice.Show(StatusTone.Critical, "That did not work", DesktopFriendlyError.Describe(exception));
        }
        finally
        {
            IsBusy = false;
            finaliseButton.IsEnabled = refreshButton.IsEnabled = publishButton.IsEnabled = true;
        }
    }

    private string NameOf(string storeCode) =>
        Summary?.Stores.FirstOrDefault(card => card.StoreCode.Equals(storeCode, StringComparison.OrdinalIgnoreCase))?.StoreName ?? storeCode;

    public static StatusTone FinaliseTone(IReadOnlyList<MorningFinaliseResult> results) =>
        results.Count == 0 ? StatusTone.Neutral : results.All(result => result.Locked) ? StatusTone.Success
        : results.Any(result => result.Locked) ? StatusTone.Warning : StatusTone.Critical;

    public static string FinaliseTitle(IReadOnlyList<MorningFinaliseResult> results)
    {
        var locked = results.Count(result => result.Locked);
        return locked == results.Count ? (locked == 1 ? "1 store locked" : $"{locked} stores locked") : $"Locked {locked} of {results.Count} stores";
    }

    public static string FinaliseMessage(IReadOnlyList<MorningFinaliseResult> results, Func<string, string> name) =>
        string.Join(Environment.NewLine, results.Select(result => $"{name(result.StoreCode)}: {result.Message}"));

    // ------------------------------------------------------------------ parts

    private UIElement ChipCard(MorningChip chip)
    {
        var card = new Border { Margin = DesignTokens.Gap(null, null, "Space8", "Space8"), MinWidth = 240, MaxWidth = 340 };
        card.SetResourceReference(StyleProperty, "SurfaceCard");
        var panel = new StackPanel();
        panel.Children.Add(Styled(chip.Label, "EyebrowText"));
        var state = new StatusChip(chip.Word, MorningTones.ToStatusTone(chip.Tone)) { Margin = DesignTokens.Gap(null, "Space4", null, "Space4") };
        panel.Children.Add(state);
        panel.Children.Add(Styled(chip.Detail, "CaptionText"));
        if (chip.OpenTaskId is { } task && TaskNavigation.Find(task) is { } destination && destination.IsAllowed(request.Access))
            panel.Children.Add(Quiet("Open " + destination.Title.ToLowerInvariant(), () => request.Navigate(task), $"Open {destination.Title} ({chip.Label})"));
        card.Child = panel;
        AutomationProperties.SetName(card, $"{chip.Label}: {chip.Word}, {StatusTones.Describe(MorningTones.ToStatusTone(chip.Tone))}. {chip.Detail}");
        return card;
    }

    private UIElement StoreCard(MorningStoreCard store, DateOnly businessDate)
    {
        var card = new Border { Margin = DesignTokens.Gap(null, null, "Space8", "Space8"), MinWidth = 300, MaxWidth = 420 };
        card.SetResourceReference(StyleProperty, "SurfaceCard");
        var panel = new StackPanel();
        var header = new DockPanel { LastChildFill = true };
        var chip = new StatusChip(store.StatusWord, MorningTones.ToStatusTone(store.Tone)) { Margin = DesignTokens.Gap("Space8", null, null, null) };
        DockPanel.SetDock(chip, Dock.Right);
        header.Children.Add(chip);
        var title = Styled(store.StoreName, "CardTitleText");
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level3);
        header.Children.Add(title);
        panel.Children.Add(header);

        var lines = new Grid { Margin = DesignTokens.Gap(null, "Space12", null, null) };
        lines.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lines.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lines.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < store.Lines.Count; row++)
        {
            lines.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddLine(lines, row, store.Lines[row]);
        }
        panel.Children.Add(lines);

        if (store.NoSale is { } mark)
        {
            var marked = Styled($"No-sale day (marked by {mark.MarkedBy} on {mark.MarkedUtc.ToLocalTime().ToString("dd MMM", English)})", "BodyText");
            marked.Margin = DesignTokens.Gap(null, "Space8", null, null);
            panel.Children.Add(marked);
        }
        if (store.Blockers.Count > 0)
        {
            var why = new StackPanel { Margin = DesignTokens.Gap(null, "Space8", null, null) };
            foreach (var blocker in store.Blockers.Take(4)) why.Children.Add(Styled(blocker, "BodyText"));
            if (store.Blockers.Count > 4) why.Children.Add(Styled($"and {store.Blockers.Count - 4} more on Close day", "CaptionText"));
            panel.Children.Add(why);
        }

        var actions = new WrapPanel { Margin = DesignTokens.Gap(null, "Space8", null, null) };
        foreach (var action in store.Actions)
        {
            if (TaskNavigation.Find(action.TaskId) is { } destination && !destination.IsAllowed(request.Access)) continue;
            var target = action;
            actions.Children.Add(Quiet(action.Label, () =>
            {
                if (target.Argument is null) request.Navigate(target.TaskId);
                else request.NavigateWithArgument(target.TaskId, target.Argument);
            }, $"{action.Label} ({store.StoreName})"));
        }
        var buttons = NoSaleButtons ?? (Window.GetWindow(this) as MainWindow)?.NoSaleDayButtons;
        if (buttons is not null && request.Access.CanImport)
        {
            if (store.NoSale is not null && store.Status != MorningStoreStatus.Locked) actions.Children.Add(buttons.Withdraw(store.StoreCode, businessDate, Refresh));
            else if (store.CanMarkNoSale) actions.Children.Add(buttons.Mark(store.StoreCode, businessDate, Refresh));
        }
        if (actions.Children.Count > 0) panel.Children.Add(actions);

        card.Child = panel;
        AutomationProperties.SetName(card, $"{store.StoreName}: {store.StatusWord}, {StatusTones.Describe(MorningTones.ToStatusTone(store.Tone))}");
        return card;
    }

    private static void AddLine(Grid grid, int row, MorningCardLine line)
    {
        var label = Styled(line.Label, "FieldLabelText");
        label.Margin = DesignTokens.Gap(null, "Space4", "Space12", "Space4");
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        var chip = new StatusChip(line.Word, MorningTones.ToStatusTone(line.Tone)) { Margin = DesignTokens.Gap(null, "Space4", "Space8", "Space4") };
        Grid.SetRow(chip, row); Grid.SetColumn(chip, 1);
        grid.Children.Add(chip);
        var detail = Styled(line.Detail, "CaptionText");
        detail.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(detail, row); Grid.SetColumn(detail, 2);
        grid.Children.Add(detail);
    }

    private UIElement MonthCard(MorningMonthStartCard month)
    {
        var card = new Border();
        card.SetResourceReference(StyleProperty, "SurfaceCard");
        var panel = new StackPanel();
        var title = Styled(month.Title, "CardTitleText");
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level3);
        panel.Children.Add(new StatusChip("Not set", StatusTone.Warning) { Margin = DesignTokens.Gap(null, null, null, "Space8") });
        panel.Children.Add(title);
        var body = Styled(month.Message, "BodyText");
        body.Margin = DesignTokens.Gap(null, "Space4", null, "Space8");
        panel.Children.Add(body);
        if (TaskNavigation.Find(month.OpenTaskId) is { } destination && destination.IsAllowed(request.Access))
        {
            var open = new Button { Content = "Open " + destination.Title.ToLowerInvariant(), HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(open, "Open " + destination.Title + " to copy last month's targets");
            open.Click += (_, _) => request.Navigate(month.OpenTaskId);
            panel.Children.Add(open);
        }
        card.Child = panel;
        return card;
    }

    private static Button Quiet(string label, Action click, string automationName)
    {
        var button = new Button { Content = label, Margin = DesignTokens.Gap(null, null, "Space4", null) };
        button.SetResourceReference(StyleProperty, "QuietButton");
        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Styled(string text, string style)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(StyleProperty, style);
        return block;
    }
}
