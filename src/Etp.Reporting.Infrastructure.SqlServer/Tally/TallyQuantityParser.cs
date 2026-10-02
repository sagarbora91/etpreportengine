using System.Globalization;
using System.Text.RegularExpressions;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

public sealed record TallyQuantity(decimal Value, string? Unit);

/// <summary>Reads a Tally quantity such as <c>1 Nos</c> or <c>-2.500 Kg</c> (plan task 9).
/// Culture-invariant; a value Tally did not give in that shape is not parsed, so the caller
/// records the check as NOT_VERIFIABLE instead of treating it as zero.</summary>
public static class TallyQuantityParser
{
    private static readonly Regex Shape = new(@"^\s*(-?\d+(?:\.\d{1,3})?)(?:\s+(\S(?:.*\S)?))?\s*$", RegexOptions.CultureInvariant);

    public static bool TryParse(string? text, out TallyQuantity? quantity)
    {
        quantity = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var match = Shape.Match(text);
        if (!match.Success) return false;
        if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value)) return false;
        var unit = match.Groups[2].Success ? match.Groups[2].Value : null;
        quantity = new TallyQuantity(decimal.Round(value, 3, MidpointRounding.AwayFromZero), unit);
        return true;
    }
}
