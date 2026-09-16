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

    public static void ApplyContrast(ResourceDictionary resources)
    {
        if (!SystemParameters.HighContrast) return;
        foreach (var key in new[] { "Surface", "SurfaceSecondary", "AppBackground", "AccentSoft", "SuccessSoft", "WarningSoft", "CriticalSoft", "InformationSoft", "NavigationBackground", "DarkSurface" }) resources[key] = SystemColors.WindowBrush;
        foreach (var key in new[] { "PrimaryText", "SecondaryText", "Divider", "NavigationMuted", "Success", "Critical", "Warning", "Information" }) resources[key] = SystemColors.WindowTextBrush;
        resources["Accent"] = resources["AccentDark"] = resources["NavigationSelected"] = SystemColors.HighlightBrush;
        resources["OnAccent"] = SystemColors.HighlightTextBrush;
    }
}
