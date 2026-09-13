using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Etp.Reporting.Desktop;

public static class KeyboardRegionNavigation
{
    public static void MoveNext(params FrameworkElement[] regions)
    {
        var current = Array.FindIndex(regions, region => region.IsKeyboardFocusWithin);
        for (var offset = 1; offset <= regions.Length; offset++)
        {
            var region = regions[(current + offset) % regions.Length];
            if (FirstTarget(region) is { } target && target.Focus()) return;
        }
    }

    private static UIElement? FirstTarget(DependencyObject element)
    {
        if (element is UIElement { IsVisible: false } or UIElement { IsEnabled: false }) return null;
        if (element is UIElement { Focusable: true } target && KeyboardNavigation.GetIsTabStop(target)) return target;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (FirstTarget(VisualTreeHelper.GetChild(element, index)) is { } child) return child;
        return null;
    }
}
