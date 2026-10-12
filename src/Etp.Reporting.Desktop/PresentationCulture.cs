using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace Etp.Reporting.Desktop;

public static class PresentationCulture
{
    /// <summary>One culture for every figure and date a person reads: <see cref="Etp.Reporting.Reporting.IndianFormat.Culture"/>.</summary>
    public static CultureInfo Indian => Etp.Reporting.Reporting.IndianFormat.Culture;
    public static void Initialize()
    {
        CultureInfo.DefaultThreadCurrentCulture = Indian;
        CultureInfo.DefaultThreadCurrentUICulture = Indian;
        CultureInfo.CurrentCulture = Indian;
        CultureInfo.CurrentUICulture = Indian;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("en-IN")));
    }
}
