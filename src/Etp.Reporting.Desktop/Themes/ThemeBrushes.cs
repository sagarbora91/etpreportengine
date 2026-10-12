using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Etp.Reporting.Desktop.Themes;

public static class ThemeBrushes
{
    // Older report documents carry palette colours. Map them to the desktop theme.
    public static SolidColorBrush Resolve(string value)
    {
        if (!value.StartsWith('#')) return Get(value);
        var colour = (Color)ColorConverter.ConvertFromString(value);
        var key = colour.R > 225 && colour.G > 225 && colour.B > 225 ? "SurfaceSecondary"
            : colour.R > colour.G * 1.4 && colour.R > colour.B * 1.4 ? "Critical"
            : colour.G > colour.R * 1.25 && colour.G > colour.B * .8 ? "Success"
            : "PrimaryText";
        return Get(value.Equals("#FFFFFF",StringComparison.OrdinalIgnoreCase) ? "Surface" : key);
    }
    public static SolidColorBrush Get(string key) => (Application.Current?.TryFindResource(key) as SolidColorBrush)
        ?? (key is "Surface" or "SurfaceSecondary" or "AppBackground" ? SystemColors.WindowBrush : SystemColors.WindowTextBrush);

    // CS-09: every fill token (including the 1.12 zebra, pressed, disabled and progress-track fills that used to be hex
    // literals in Controls.xaml) maps to Window, every text and edge token to WindowText, selection to Highlight.
    private static readonly string[] WindowKeys = ["Surface", "SurfaceSecondary", "AppBackground", "AccentSoft", "SuccessSoft", "WarningSoft", "CriticalSoft", "InformationSoft",
        "NavigationBackground", "NavigationHover", "DarkSurface", "ZebraRow", "PressedFill", "DisabledFill", "ProgressTrack"];
    private static readonly string[] WindowTextKeys = ["PrimaryText", "SecondaryText", "Divider", "InputBorder", "NavigationMuted", "Success", "Critical", "Warning", "Information"];
    private static readonly HashSet<string> Applied = new(StringComparer.Ordinal);
    private static ResourceDictionary? watched;

    /// <summary>Maps the theme to the Windows high-contrast colours when high contrast is on, and follows the setting
    /// while ETP is open (CS-09): switching high contrast on or off re-applies or removes the overrides.</summary>
    public static void ApplyContrast(ResourceDictionary resources)
    {
        if (watched is null) { watched = resources; SystemParameters.StaticPropertyChanged += ContrastChanged; }
        Restore(resources);
        if (!SystemParameters.HighContrast) return;
        foreach (var key in WindowKeys) Set(resources, key, SystemColors.WindowBrush);
        foreach (var key in WindowTextKeys) Set(resources, key, SystemColors.WindowTextBrush);
        foreach (var key in new[] { "Accent", "AccentDark", "NavigationSelected", "SelectedRow" }) Set(resources, key, SystemColors.HighlightBrush);
        Set(resources, "OnAccent", SystemColors.HighlightTextBrush);
        Set(resources, "OnSelectedRow", SystemColors.HighlightTextBrush);
        Set(resources, "DisabledText", SystemColors.GrayTextBrush);
    }

    private static void ContrastChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast) && watched is { } resources) ApplyContrast(resources);
    }

    private static void Set(ResourceDictionary resources, string key, Brush brush) { resources[key] = brush; Applied.Add(key); }

    // The theme brushes live in the merged dictionaries; removing the top-level override brings them back.
    private static void Restore(ResourceDictionary resources)
    {
        foreach (var key in Applied) resources.Remove(key);
        Applied.Clear();
    }
}
