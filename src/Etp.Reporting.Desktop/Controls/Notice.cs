using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

/// <summary>
/// The page notice (ETP-DESIGN-SYSTEM.md section 5, the rename of StatusBanner): soft fill of the tone, 1 px tone border,
/// radius 12, padding 12, a 15 px SemiBold first line and body text. One per page, directly under the toolbar
/// (template row 2); collapsed while it has nothing to say. "Refresh required", "Blocked: R025 not imported" and the
/// Today "What's missing" box live here, never as a coloured heading or a red-bordered panel (ST3).
/// </summary>
public sealed class Notice : Border
{
    private readonly TextBlock title = new();
    private readonly TextBlock message = new();
    private StatusTone tone;

    public Notice()
    {
        title.SetResourceReference(StyleProperty, "CardTitleText");
        message.SetResourceReference(StyleProperty, "BodyText");
        message.Margin = new Thickness(0, 4, 0, 0);
        var panel = new StackPanel(); panel.Children.Add(title); panel.Children.Add(message); Child = panel;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Tone = StatusTone.Neutral; Refresh();
    }

    public Notice(StatusTone tone, string title, string? message = null) : this() => Show(tone, title, message);

    public StatusTone Tone
    {
        get => tone;
        set { tone = value; SetResourceReference(StyleProperty, StatusTones.NoticeStyleKey(value)); Refresh(); }
    }

    /// <summary>The first line: the verdict or the thing to do ("Refresh required", "Missing: 30 items").</summary>
    public string Title { get => title.Text; set { title.Text = value; Refresh(); } }

    /// <summary>Body text under the first line; optional.</summary>
    public string Message { get => message.Text; set { message.Text = value; Refresh(); } }

    public void Show(StatusTone newTone, string newTitle, string? newMessage = null)
    {
        tone = newTone; SetResourceReference(StyleProperty, StatusTones.NoticeStyleKey(newTone));
        title.Text = newTitle; message.Text = newMessage ?? string.Empty; Refresh();
    }

    /// <summary>Nothing to say: the notice collapses and its row takes no space.</summary>
    public void Clear() { title.Text = string.Empty; message.Text = string.Empty; Refresh(); }

    private void Refresh()
    {
        var hasTitle = title.Text.Length > 0; var hasMessage = message.Text.Length > 0;
        title.Visibility = hasTitle ? Visibility.Visible : Visibility.Collapsed;
        message.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        message.Margin = hasTitle ? new Thickness(0, 4, 0, 0) : new Thickness(0);
        Visibility = hasTitle || hasMessage ? Visibility.Visible : Visibility.Collapsed;
        var words = string.Join(". ", new[] { title.Text, message.Text }.Where(text => text.Length > 0));
        AutomationProperties.SetName(this, tone == StatusTone.Neutral || words.Length == 0 ? words : $"{words}, {StatusTones.Describe(tone)}");
    }
}
