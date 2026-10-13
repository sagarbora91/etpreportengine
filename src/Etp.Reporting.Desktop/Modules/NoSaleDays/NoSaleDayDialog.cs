extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

using NoSaleDayText = EtpApplication::Etp.Reporting.Application.NoSaleDays.NoSaleDayText;
using DisplayText = EtpApplication::Etp.Reporting.Application.Formatting.DisplayText;

namespace Etp.Reporting.Desktop.Modules.NoSaleDays;

/// <summary>What the dialog asks for: a new mark (reason choice and text) or a withdrawal (text only).</summary>
public enum NoSaleDayDialogMode { Mark, Withdraw }

/// <summary>
/// 1.12 lane N1 (decision 52): "Mark as no-sale day" / "Withdraw no-sale day". Design system L3 dialog: 480 wide,
/// <c>CardTitleText</c> heading, body, the reason choice (Mark only), the reason text (<c>InputWide</c>, always last), then a
/// right-aligned row with the no-op button first and the primary last; <c>Surface</c> background from the token.
/// The dialog stays open on a refusal and shows the sentence with the word "Not saved"; it closes when the change is saved.
/// Template: dialog (L3). No colours, sizes or font sizes are set here; styles own them.
/// </summary>
public sealed class NoSaleDayDialog : Window
{
    private readonly Func<string?, string, Task<string?>> submit;

    internal NoSaleDayDialogMode Mode { get; }
    internal IReadOnlyList<RadioButton> ReasonChoices { get; }
    internal TextBox ReasonInput { get; }
    internal Button PrimaryAction { get; }
    internal Button CancelAction { get; }
    internal TextBlock Message { get; }

    /// <param name="submit">Saves the change: (reason code or null when withdrawing, reason text) -> null when saved, or the
    /// sentence to show when it was refused.</param>
    public NoSaleDayDialog(NoSaleDayDialogMode mode, string shopName, DateOnly businessDate, Func<string?, string, Task<string?>> submit)
    {
        ArgumentNullException.ThrowIfNull(submit);
        this.submit = submit;
        Mode = mode;
        var mark = mode == NoSaleDayDialogMode.Mark;
        var title = mark ? "Mark as no-sale day" : "Withdraw no-sale day";
        Title = title; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Surface");
        AutomationProperties.SetName(this, title);

        var root = new StackPanel { Margin = DesignTokens.Gap("Space24") };
        root.Children.Add(Styled(title, "CardTitleText", DesignTokens.Gap(null, null, null, "Space12")));
        var day = $"{shopName}, {DisplayText.Date(businessDate)}.";
        root.Children.Add(Styled(mark
            ? $"{day} ETP stops asking for this shop's files and daily entries for this day. The month's targets still count. You can withdraw the mark until the day is finalised."
            : $"{day} ETP asks for this shop's files and daily entries for this day again.",
            "BodyText", DesignTokens.Gap(null, null, null, "Space16")));

        var choices = new List<RadioButton>();
        if (mark)
        {
            root.Children.Add(Styled("Why were there no sales?", "FieldLabelText", DesignTokens.Gap(null, null, null, "Space8")));
            foreach (var reason in NoSaleDayText.Reasons)
            {
                var choice = new RadioButton { Content = reason.Label, Tag = reason.Code, GroupName = "NoSaleReason", Margin = DesignTokens.Gap(null, null, null, "Space8") };
                AutomationProperties.SetName(choice, reason.Label);
                choices.Add(choice);
                root.Children.Add(choice);
            }
        }
        ReasonChoices = choices;

        root.Children.Add(Styled(mark ? "Reason" : "Reason for withdrawing", "FieldLabelText", DesignTokens.Gap(null, "Space8", null, "Space4")));
        ReasonInput = new TextBox
        {
            Width = DesignTokens.Value("InputWide"), MaxLength = NoSaleDayText.ReasonMaxLength,
            HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetName(ReasonInput, mark ? "Reason" : "Reason for withdrawing");
        root.Children.Add(ReasonInput);

        Message = Styled(string.Empty, "BodyText", DesignTokens.Gap(null, "Space12", null, null));
        Message.Visibility = Visibility.Collapsed;
        Message.SetResourceReference(TextBlock.ForegroundProperty, "Critical");
        AutomationProperties.SetLiveSetting(Message, AutomationLiveSetting.Assertive);
        root.Children.Add(Message);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = DesignTokens.Gap(null, "Space16", null, null) };
        CancelAction = new Button { Content = "Keep working", IsCancel = true, Margin = DesignTokens.Gap(null, null, "Space8", null) };
        PrimaryAction = new Button { Content = mark ? "Mark" : "Withdraw", IsDefault = true };
        PrimaryAction.SetResourceReference(StyleProperty, "PrimaryButton");
        AutomationProperties.SetName(PrimaryAction, mark ? "Mark as no-sale day" : "Withdraw no-sale day");
        PrimaryAction.Click += async (_, _) => await SubmitAsync();
        actions.Children.Add(CancelAction);
        actions.Children.Add(PrimaryAction);
        root.Children.Add(actions);
        Content = root;
    }

    /// <summary>The chosen reason code, or null.</summary>
    internal string? ChosenReasonCode => ReasonChoices.FirstOrDefault(x => x.IsChecked == true)?.Tag as string;

    /// <summary>Checks the input, saves it, and closes on success; otherwise shows why. Returns true when saved.</summary>
    internal async Task<bool> SubmitAsync()
    {
        var reason = ReasonInput.Text ?? string.Empty;
        var problem = Mode == NoSaleDayDialogMode.Mark
            ? NoSaleDayText.ValidateMark(ChosenReasonCode, reason)
            : NoSaleDayText.ValidateWithdraw(reason);
        if (problem is null)
        {
            PrimaryAction.IsEnabled = false;
            try { problem = await submit(Mode == NoSaleDayDialogMode.Mark ? ChosenReasonCode : null, reason.Trim()); }
            finally { PrimaryAction.IsEnabled = true; }
        }
        if (problem is not null)
        {
            Message.Text = "Not saved: " + problem;
            Message.Visibility = Visibility.Visible;
            return false;
        }
        Message.Visibility = Visibility.Collapsed;
        if (IsLoaded) DialogResult = true;
        return true;
    }

    private static TextBlock Styled(string text, string style, Thickness margin)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
        block.SetResourceReference(FrameworkElement.StyleProperty, style);
        return block;
    }
}
