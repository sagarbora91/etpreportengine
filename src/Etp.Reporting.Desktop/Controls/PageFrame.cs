using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

/// <summary>The four page templates of ETP-DESIGN-SYSTEM.md section 11 (decision 45 #3). A new screen names the one it
/// uses in its class comment and builds on <see cref="PageFrame"/>; <see cref="TaskBodyLayout"/> is frozen for new screens (L1).</summary>
public enum PageTemplate
{
    /// <summary>A. Registers, History, Problems, Error register, Archive, Jobs, Claims, Parts: the grid is the body and scrolls itself.</summary>
    List,
    /// <summary>B. Every Reports task, DSR, Pending board, Service today: tiles then Summary | Detail rows; the grid fills.</summary>
    Report,
    /// <summary>C. Manual entry, Stock count, Staff target, Register entry, Settings pages: SurfaceCards per section; the page scrolls.</summary>
    Form,
    /// <summary>D. Morning page, Health check summary, Database overview: tile rows and state cards, previews only; the body scrolls as a whole.</summary>
    Dashboard
}

/// <summary>
/// The shared frame of every page template (section 11): row 0 toolbar, row 1 one CaptionText description, row 2 one
/// <see cref="Notice"/> (collapsed when there is nothing to say), row 3 the body (Height="*"), row 4 the page footer
/// (page status once, CaptionText; the shell footer carries app status). List and Report bodies fill the row and
/// scroll themselves (no MaxHeight on a grid, S3/G6); Form and Dashboard bodies sit in one vertical ScrollViewer.
/// The frame never repeats the page title: the shell header owns it (T2, L2).
/// </summary>
public sealed class PageFrame : Grid
{
    private readonly ContentControl toolbarHost = new() { Focusable = false, IsTabStop = false };
    private readonly TextBlock description = new() { Visibility = Visibility.Collapsed };
    private readonly ContentControl bodyHost = new() { Focusable = false, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock footer = new() { Visibility = Visibility.Collapsed };
    private UIElement? body;

    public PageFrame(PageTemplate template)
    {
        Kind = template;
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            RowDefinitions.Add(new RowDefinition { Height = height });
        description.SetResourceReference(StyleProperty, "CaptionText");
        footer.SetResourceReference(StyleProperty, "CaptionText");
        description.Margin = DesignTokens.Gap(null, "Space4", null, null);
        Notice.Margin = DesignTokens.Gap(null, "Space8", null, null);
        bodyHost.Margin = DesignTokens.Gap(null, "Space16", null, null);
        footer.Margin = DesignTokens.Gap(null, "Space8", null, null);
        AutomationProperties.SetName(footer, "Page status");
        Place(toolbarHost, 0); Place(description, 1); Place(Notice, 2); Place(bodyHost, 3); Place(footer, 4);
    }

    public PageTemplate Kind { get; }

    /// <summary>Row 2: the page's one notice. Use <c>Notice.Show(tone, title, message)</c> and <c>Notice.Clear()</c>.</summary>
    public Notice Notice { get; } = new();

    /// <summary>Row 0: the toolbar (one PrimaryButton at most, B1). A WrapPanel lets it wrap below 1000 px.</summary>
    public UIElement? Toolbar { get => toolbarHost.Content as UIElement; set => toolbarHost.Content = value; }

    /// <summary>Row 1: one plain sentence under the toolbar, or nothing.</summary>
    public string Description { get => description.Text; set => SetLine(description, value); }

    /// <summary>Row 4: the page status line, shown once (ST6); "132 rows · as at 11 Oct 2026 07:42".</summary>
    public string Footer { get => footer.Text; set => SetLine(footer, value); }

    /// <summary>Row 3: the body. Form and Dashboard bodies are wrapped in the page's one vertical scroller.</summary>
    public UIElement? Body
    {
        get => body;
        set
        {
            body = value;
            bodyHost.Content = value is null || Kind is PageTemplate.List or PageTemplate.Report ? value
                : new ScrollViewer { Content = value, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        }
    }

    private static void SetLine(TextBlock line, string? text)
    {
        line.Text = text ?? string.Empty;
        line.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Place(UIElement element, int row) { SetRow(element, row); Children.Add(element); }
}
