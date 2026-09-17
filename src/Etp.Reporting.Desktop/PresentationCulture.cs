using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace Etp.Reporting.Desktop;

public static class PresentationCulture
{
    public static CultureInfo Indian { get; } = Create();
    private static CultureInfo Create()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-IN").Clone();
        culture.DateTimeFormat.ShortDatePattern = "dd MMM yyyy";
        return culture;
    }
    public static void Initialize()
    {
        CultureInfo.DefaultThreadCurrentCulture = Indian;
        CultureInfo.DefaultThreadCurrentUICulture = Indian;
        CultureInfo.CurrentCulture = Indian;
        CultureInfo.CurrentUICulture = Indian;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("en-IN")));
    }
}
