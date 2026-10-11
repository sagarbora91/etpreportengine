using System.Globalization;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// The one display culture of the Service screens (R-UI-17): dates as "dd MMM yyyy" and numbers with the application's
/// presentation culture (<see cref="PresentationCulture.Indian"/>), whatever the thread's current culture is. Route
/// arguments stay on <see cref="CultureInfo.InvariantCulture"/>.
/// </summary>
public static class ServiceDisplay
{
    public static CultureInfo Culture => PresentationCulture.Indian;

    /// <summary>Formats an interpolated string with the display culture.</summary>
    public static string Text(FormattableString text) => text.ToString(Culture);

    public static string Whole(int value) => value.ToString("N0", Culture);
    public static string Whole(decimal value) => value.ToString("N0", Culture);
    public static string Money(decimal value) => value.ToString("N2", Culture);
    public static string Date(DateOnly value) => value.ToString("dd MMM yyyy", Culture);
    public static string Month(DateOnly value) => value.ToString("MMM yyyy", Culture);
}
