using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// Lane U1 (1.12 design system, decision 45 #1-#3): the resource keys other lanes code against exist, the contrast
/// palette passes WCAG, the shared status controls carry a word with every tone, tiles never get a decorative stripe,
/// DSR growth is coloured by sign and the page frame scrolls the way its template says.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class DesignSystemThemeTests
{
    public static readonly string[] RuleBookKeys =
    [
        // Colour tokens (section 2) and the 1.12 additions (C6, CS 2.4).
        "AppBackground", "Surface", "SurfaceSecondary", "PrimaryText", "SecondaryText", "Divider", "Accent", "AccentDark", "AccentSoft", "OnAccent",
        "Success", "SuccessSoft", "Warning", "WarningSoft", "Critical", "CriticalSoft", "Information", "InformationSoft",
        "NavigationBackground", "NavigationSelected", "NavigationMuted", "NavigationHover", "DarkSurface",
        "ZebraRow", "SelectedRow", "OnSelectedRow", "PressedFill", "ProgressTrack", "DisabledText", "DisabledFill", "InputBorder",
        // Text styles (section 3).
        "DisplayText", "PageTitleText", "SectionTitleText", "CardTitleText", "BodyText", "MetricText", "CaptionText", "EyebrowText", "FieldLabelText",
        // Spacing, radius and inputs (section 4).
        "Space4", "Space8", "Space12", "Space16", "Space24", "RadiusSmall", "RadiusMedium", "CardPadding", "InsetPadding", "SectionSpacing",
        "InputNarrow", "InputMedium", "InputWide", "ActiveTargetHeight", "ActiveGridRowHeight",
        // Containers, notices, buttons and navigation (sections 5, 6, 9).
        "SurfaceCard", "InsetCard", "Notice", "NoticeInformation", "NoticeSuccess", "NoticeWarning", "NoticeCritical",
        "PrimaryButton", "QuietButton", "DangerButton", "TabButton", "TabButtonSelected", "RailItem", "RailItemSelected", "FilterChip", "TileButton",
        // Icons (section 10, I2).
        "IconChevronDown", "IconChevronRight", "IconBack", "IconRefresh", "IconExport", "IconInfo", "IconCheck", "IconWarning", "IconClose", "IconStar"
    ];

    [Fact]
    public void Every_rule_book_key_resolves_from_the_theme()
    {
        Sta(() =>
        {
            var host = Themed(new Border());
            Assert.Empty(RuleBookKeys.Where(key => host.TryFindResource(key) is null));
            Assert.Equal(20d, Assert.IsType<Style>(host.FindResource("PageTitleText")).Setters.OfType<Setter>().Single(setter => setter.Property == TextBlock.FontSizeProperty).Value);
            Assert.Equal(new Thickness(16), host.FindResource("CardPadding"));
            Assert.Equal(new CornerRadius(8), host.FindResource("RadiusSmall"));
            Assert.Equal(new CornerRadius(12), host.FindResource("RadiusMedium"));
        });
    }

    [Fact]
    public void Retired_style_keys_are_gone_and_cards_have_no_shadow()
    {
        Sta(() =>
        {
            var host = Themed(new Border());
            foreach (var key in new[] { "DarkCard", "CardShadow", "RailButton", "SidebarItemButton" }) Assert.Null(host.TryFindResource(key));
            var card = Assert.IsType<Style>(host.FindResource("SurfaceCard"));
            Assert.DoesNotContain(card.Setters.OfType<Setter>(), setter => setter.Property == UIElement.EffectProperty);
        });
    }

    [Theory]
    [InlineData("SecondaryText", "Surface", 4.5)]
    [InlineData("SecondaryText", "AppBackground", 4.5)]
    [InlineData("SecondaryText", "SurfaceSecondary", 4.5)]
    [InlineData("SecondaryText", "AccentSoft", 4.5)]
    [InlineData("SecondaryText", "SelectedRow", 4.5)]
    [InlineData("Success", "SuccessSoft", 4.5)]
    [InlineData("Warning", "WarningSoft", 4.5)]
    [InlineData("Warning", "AppBackground", 4.5)]
    [InlineData("Critical", "CriticalSoft", 4.5)]
    [InlineData("Information", "InformationSoft", 4.5)]
    [InlineData("DisabledText", "DisabledFill", 4.5)]
    [InlineData("OnAccent", "AccentDark", 4.5)]
    [InlineData("AccentDark", "Surface", 4.5)]
    [InlineData("InputBorder", "Surface", 3.0)]
    public void Contrast_palette_passes(string foreground, string background, double minimum)
    {
        Sta(() =>
        {
            var host = Themed(new Border());
            var ratio = Contrast(((SolidColorBrush)host.FindResource(foreground)).Color, ((SolidColorBrush)host.FindResource(background)).Color);
            Assert.True(ratio >= minimum, $"{foreground} on {background} is {ratio:0.00}:1, needs {minimum}:1.");
        });
    }

    [Theory]
    [InlineData("Success", StatusTone.Success, "SuccessSoft", "Success")]
    [InlineData("Warning", StatusTone.Warning, "WarningSoft", "Warning")]
    [InlineData("Critical", StatusTone.Critical, "CriticalSoft", "Critical")]
    [InlineData("Information", StatusTone.Information, "InformationSoft", "Information")]
    [InlineData("PrimaryText", StatusTone.Neutral, "SurfaceSecondary", "SecondaryText")]
    [InlineData("Accent", StatusTone.Neutral, "SurfaceSecondary", "SecondaryText")]
    [InlineData("#2269E8", StatusTone.Neutral, "SurfaceSecondary", "SecondaryText")]
    public void Tones_map_to_one_set_of_brushes(string key, StatusTone tone, string fill, string text)
    {
        Assert.Equal(tone, StatusTones.FromKey(key));
        Assert.Equal(fill, StatusTones.BrushesFor(tone).Fill);
        Assert.Equal(text, StatusTones.BrushesFor(tone).Text);
        Assert.Equal(tone == StatusTone.Neutral ? "Divider" : tone.ToString(), StatusTones.StripeKey(tone));
        Assert.Equal(tone == StatusTone.Neutral ? "Notice" : "Notice" + tone, StatusTones.NoticeStyleKey(tone));
    }

    [Fact]
    public void A_chip_says_its_word_and_tone_and_is_not_a_tab_stop()
    {
        Sta(() =>
        {
            var chip = new StatusChip("Refused", StatusTone.Critical);
            Assert.Equal("Refused, problem", AutomationProperties.GetName(chip));
            Assert.False(chip.Focusable);
            chip.Tone = StatusTone.Neutral; chip.Text = "Not run";
            Assert.Equal("Not run", AutomationProperties.GetName(chip));
        });
    }

    [Fact]
    public void A_tile_never_gets_a_decorative_stripe_and_a_toned_tile_can_carry_its_word()
    {
        Sta(() =>
        {
            Assert.Equal(StatusTone.Neutral, new KpiCard("Ready for delivery", "4", "jobs", "PrimaryText").Tone);
            var overdue = new KpiCard("Overdue", "19", "over the EDD", "Critical", "Overdue");
            Assert.Equal(StatusTone.Critical, overdue.Tone);
            Assert.Single(Descendants(overdue).OfType<StatusChip>());
            Assert.Equal("Overdue: 19. over the EDD Overdue, problem.", AutomationProperties.GetName(overdue));
        });
    }

    [Theory]
    [InlineData("+12.5%", "Success")]
    [InlineData("+0.0%", "Success")]
    [InlineData("-3.1%", "Critical")]
    [InlineData("−3.1%", "Critical")]
    [InlineData("—", "SecondaryText")]
    [InlineData("", "SecondaryText")]
    public void Dsr_growth_is_coloured_by_sign(string growth, string expected) => Assert.Equal(expected, DsrUi.GrowthColour(growth));

    [Fact]
    public void Dsr_text_is_dark_by_default_not_green()
    {
        Sta(() =>
        {
            var host = Themed(new Border());
            var text = DsrUi.Text("1,23,456"); host.Child = text;
            Assert.Same(host.FindResource("PrimaryText"), text.Foreground);
        });
    }

    [Fact]
    public void A_notice_collapses_when_it_has_nothing_to_say()
    {
        Sta(() =>
        {
            var notice = new Notice();
            Assert.Equal(Visibility.Collapsed, notice.Visibility);
            notice.Show(StatusTone.Critical, "Blocked", "R025 not imported");
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Equal("Blocked. R025 not imported, problem", AutomationProperties.GetName(notice));
            notice.Clear();
            Assert.Equal(Visibility.Collapsed, notice.Visibility);
        });
    }

    [Theory]
    [InlineData(PageTemplate.List, false)]
    [InlineData(PageTemplate.Report, false)]
    [InlineData(PageTemplate.Form, true)]
    [InlineData(PageTemplate.Dashboard, true)]
    public void Form_and_dashboard_pages_scroll_as_one_list_and_report_bodies_fill(PageTemplate template, bool scrolls)
    {
        Sta(() =>
        {
            var body = new StackPanel();
            var frame = new PageFrame(template) { Body = body, Description = "One line", Footer = "132 rows" };
            Assert.Same(body, frame.Body);
            Assert.Equal(5, frame.RowDefinitions.Count);
            Assert.True(frame.RowDefinitions[3].Height.IsStar);
            Assert.Equal(scrolls, Descendants(frame).OfType<ScrollViewer>().Any(viewer => ReferenceEquals(viewer.Content, body)));
            Assert.Equal(Visibility.Collapsed, frame.Notice.Visibility);
        });
    }

    private static Border Themed(Border host)
    {
        host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Etp.Reporting.Desktop;component/Themes/Theme.xaml", UriKind.Relative) });
        return host;
    }

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte value) { var c = value / 255d; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "Design system test timed out.");
        if (error is not null) throw new InvalidOperationException("Design system test failed.", error);
    }
}
