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

    // 1.11.3 integration: one formatter (U5 IndianFormat, FW R1-R9): Indian grouping, "Sept", 2-dp money cells.
    public static string Whole(int value) => Etp.Reporting.Reporting.IndianFormat.Count(value);
    public static string Whole(decimal value) => value.ToString("N0", Culture);
    public static string Money(decimal value) => Etp.Reporting.Reporting.IndianFormat.MoneyCell(value);
    public static string Date(DateOnly value) => Etp.Reporting.Reporting.IndianFormat.Date(value);
    public static string Month(DateOnly value) => Etp.Reporting.Reporting.IndianFormat.Month(value);
}
