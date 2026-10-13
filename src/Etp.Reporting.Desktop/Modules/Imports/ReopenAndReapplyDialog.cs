using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Modules.Imports;

/// <summary>The row a "Reopen day and re-apply" starts from: the file and the store and period its attempt recorded.</summary>
public sealed record ReopenAndReapplyRequest(string FileName, string? StoreCode, DateOnly? PeriodStart, DateOnly? PeriodEnd);

/// <summary>The Owner's answer: which store-day to reopen and why.</summary>
public sealed record ReopenAndReapplyAnswer(string StoreCode, DateOnly BusinessDate, string Reason);

/// <summary>
/// "Reopen day and re-apply" (1.12 U2, wave plan C-c). Dialog L3 of the design system: 480 wide, Surface background,
/// CardTitleText heading, the body, the store and day (the attempt's own; the day can be chosen only inside the file's
/// period), the reason in an InputWide box, then a right-aligned row with "Keep working" first and the Danger action last.
/// The answer is validated here (store, a day in the period, a reason); SQL checks the Owner and the lock again.
/// </summary>
public sealed class ReopenAndReapplyDialog : Window
{
    internal TextBox StoreInput { get; }
    internal DatePicker DayInput { get; }
    internal TextBox ReasonInput { get; }
    internal Notice Problem { get; }
    internal Button ReopenButton { get; }
    public ReopenAndReapplyAnswer? Answer { get; private set; }
    private readonly ReopenAndReapplyRequest request;

    public const string Heading = "Reopen day and re-apply";

    public ReopenAndReapplyDialog(Window? owner, ReopenAndReapplyRequest request)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        if (owner?.IsVisible == true) Owner = owner;
        Title = Heading; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Surface");
        AutomationProperties.SetName(this, Heading);

        var root = new DockPanel { Margin = DesignTokens.Gap("Space24") };
        var heading = new TextBlock { Text = Heading, Margin = DesignTokens.Gap(null, null, null, "Space12") };
        heading.SetResourceReference(StyleProperty, "CardTitleText");
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = DesignTokens.Gap(null, "Space16", null, null) };
        var keep = new Button { Content = "Keep working", IsCancel = true, Margin = DesignTokens.Gap(null, null, "Space8", null) };
        ReopenButton = new Button { Content = Heading };
        ReopenButton.SetResourceReference(StyleProperty, "DangerButton");
        AutomationProperties.SetName(ReopenButton, "Reopen the day and import the file again");
        ReopenButton.Click += (_, _) => { if (TryAnswer()) DialogResult = true; };
        actions.Children.Add(keep); actions.Children.Add(ReopenButton);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);

        var body = new StackPanel();
        var text = new TextBlock
        {
            Text = $"{request.FileName} was refused because its day is finalised. ETP reopens the day with your reason, then imports the file " +
                   "again from where it keeps it. Finalise the day again when it is complete.",
            TextWrapping = TextWrapping.Wrap
        };
        text.SetResourceReference(StyleProperty, "BodyText");
        body.Children.Add(text);
        StoreInput = new TextBox { Text = request.StoreCode ?? "", IsReadOnly = request.StoreCode is not null };
        StoreInput.SetResourceReference(WidthProperty, "InputMedium");
        AutomationProperties.SetName(StoreInput, "Store whose day is reopened");
        body.Children.Add(Field("Store", StoreInput));
        DayInput = new DatePicker
        {
            SelectedDate = request.PeriodEnd?.ToDateTime(TimeOnly.MinValue),
            DisplayDateStart = request.PeriodStart?.ToDateTime(TimeOnly.MinValue),
            DisplayDateEnd = request.PeriodEnd?.ToDateTime(TimeOnly.MinValue)
        };
        DayInput.SetResourceReference(WidthProperty, "InputMedium");
        AutomationProperties.SetName(DayInput, "Business day to reopen");
        body.Children.Add(Field("Business day", DayInput));
        ReasonInput = new TextBox { TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };
        ReasonInput.SetResourceReference(WidthProperty, "InputWide");
        AutomationProperties.SetName(ReasonInput, "Reason for reopening");
        body.Children.Add(Field("Reason", ReasonInput));
        // C3: a problem is a Notice with words, never red body text alone. It collapses while there is nothing to say.
        Problem = new Notice { Margin = DesignTokens.Gap(null, "Space12", null, null) };
        body.Children.Add(Problem);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        Content = root;
        Loaded += (_, _) => (string.IsNullOrEmpty(StoreInput.Text) ? StoreInput : ReasonInput).Focus();
    }

    private static FrameworkElement Field(string label, FrameworkElement input)
    {
        var panel = new StackPanel { Margin = DesignTokens.Gap(null, "Space12", null, null) };
        var caption = new TextBlock { Text = label, Margin = DesignTokens.Gap(null, null, null, "Space4") };
        caption.SetResourceReference(StyleProperty, "FieldLabelText");
        input.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(caption); panel.Children.Add(input);
        return panel;
    }

    /// <summary>The problem with the typed answer, or null when it can be sent (store, a day inside the file's period, a reason).</summary>
    public static string? Validate(ReopenAndReapplyRequest request, string? store, DateOnly? day, string? reason)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(store)) return "Type the store code whose day is reopened.";
        if (day is null) return "Choose the business day to reopen.";
        if (request.PeriodStart is { } from && day < from || request.PeriodEnd is { } to && day > to)
            return "Choose a day inside the file's dates.";
        if (string.IsNullOrWhiteSpace(reason)) return "Type the reason for reopening the day.";
        return null;
    }

    internal bool TryAnswer()
    {
        var day = DayInput.SelectedDate is { } chosen ? DateOnly.FromDateTime(chosen) : (DateOnly?)null;
        var problem = Validate(request, StoreInput.Text, day, ReasonInput.Text);
        if (problem is not null) { Problem.Show(StatusTone.Critical, "Not sent yet", problem); return false; }
        Problem.Clear();
        Answer = new(StoreInput.Text.Trim().ToUpperInvariant(), day!.Value, ReasonInput.Text.Trim());
        return true;
    }

    public static ReopenAndReapplyAnswer? Ask(DependencyObject source, ReopenAndReapplyRequest request)
    {
        var dialog = new ReopenAndReapplyDialog(Window.GetWindow(source), request);
        return dialog.ShowDialog() == true ? dialog.Answer : null;
    }
}
