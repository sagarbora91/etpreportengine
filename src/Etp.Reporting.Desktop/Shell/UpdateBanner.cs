extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop;

/// <summary>The Application's tone words as the shell's status tones (ETP-DESIGN-SYSTEM.md section 8).</summary>
internal static class UpdateTones
{
    public static StatusTone ToStatusTone(UpdateTextTone tone) => tone switch
    {
        UpdateTextTone.Success => StatusTone.Success,
        UpdateTextTone.Warning => StatusTone.Warning,
        UpdateTextTone.Critical => StatusTone.Critical,
        UpdateTextTone.Information => StatusTone.Information,
        _ => StatusTone.Neutral,
    };
}

/// <summary>
/// 1.12 wave 2 (B2). The Owner-only "ETP 1.12.1 is available" notice in the shell's banner slot (<c>update-ready</c>).
/// Template: the shell frame's one Notice (NoticeInformation, section 5), a title, one line from the release notes, then
/// "Install now" (the one PrimaryButton) and "Not now" (QuietButton; the notice returns at the next start). The banner
/// never installs by itself: "Install now" opens the confirm sheet. Tokens only; no sizes or colours here.
/// </summary>
internal sealed class UpdateAvailableBanner : IShellBanner
{
    public const string BannerKey = "update-ready";

    private readonly Button install = new() { Content = UpdateScreenText.BannerInstallButton };
    private readonly Button later = new() { Content = UpdateScreenText.BannerLaterButton };

    public UpdateAvailableBanner(UpdateBannerModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var title = new TextBlock { Text = model.Title };
        title.SetResourceReference(FrameworkElement.StyleProperty, "CardTitleText");
        var body = new TextBlock { Text = model.Body, Margin = DesignTokens.Gap(null, "Space4", null, null) };
        body.SetResourceReference(FrameworkElement.StyleProperty, "BodyText");
        var text = new StackPanel(); text.Children.Add(title); text.Children.Add(body);

        install.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        later.SetResourceReference(FrameworkElement.StyleProperty, "QuietButton");
        install.Margin = DesignTokens.Gap("Space16", null, "Space8", null);
        AutomationProperties.SetName(install, $"Install ETP {model.Version} now");
        AutomationProperties.SetName(later, "Hide this notice until ETP starts again");
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false;
            try { await model.InstallNow(); }
            finally { install.IsEnabled = true; }
        };
        later.Click += (_, _) => Closed?.Invoke(this, EventArgs.Empty);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(install); buttons.Children.Add(later);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0); Grid.SetColumn(buttons, 1);
        grid.Children.Add(text); grid.Children.Add(buttons);

        var border = new Border { Child = grid };
        border.SetResourceReference(FrameworkElement.StyleProperty, StatusTones.NoticeStyleKey(StatusTone.Information));
        AutomationProperties.SetName(border, $"{model.Title}. {model.Body}, {StatusTones.Describe(StatusTone.Information)}");
        AutomationProperties.SetLiveSetting(border, AutomationLiveSetting.Polite);
        Content = border;
    }

    public string Key => BannerKey;

    public int Priority => 50;

    public UIElement Content { get; }

    public event EventHandler? Closed;
}
