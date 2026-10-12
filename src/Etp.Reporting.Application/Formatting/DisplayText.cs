using System.Globalization;

namespace Etp.Reporting.Application.Formatting;

/// <summary>
/// Dates in sentences the Application layer builds for a person ("07 Sept 2026"). The Application project cannot see the
/// Reporting project, so this is the Application-side twin of <c>Etp.Reporting.Reporting.IndianFormat</c>; a Desktop test
/// asserts both give identical text. Never <see cref="CultureInfo.InvariantCulture"/> (it prints "Sep").
/// </summary>
public static class DisplayText
{
    private static readonly CultureInfo Culture = Create();

    private static CultureInfo Create()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-IN").Clone();
        var format = culture.DateTimeFormat;
        format.ShortDatePattern = "dd MMM yyyy";
        format.AbbreviatedMonthNames = Sept(format.AbbreviatedMonthNames);
        format.AbbreviatedMonthGenitiveNames = Sept(format.AbbreviatedMonthGenitiveNames);
        return culture;
    }

    private static string[] Sept(string[] names)
    {
        var copy = (string[])names.Clone();
        if (copy.Length > 8 && copy[8].StartsWith("Sep", StringComparison.OrdinalIgnoreCase)) copy[8] = "Sept";
        return copy;
    }

    public static string Date(DateOnly value) => value.ToString("dd MMM yyyy", Culture);
    public static string Month(DateOnly value) => value.ToString("MMM yyyy", Culture);
}
