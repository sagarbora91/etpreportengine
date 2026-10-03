using System.Globalization;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Identity;

/// <summary>
/// Puts a row's values in the CLR types <c>ImportRowStager</c> gives them, so a row read back from a typed landing
/// table (re-decide, approval apply, upgrade) is projected exactly like the staged row it came from: NULL
/// (<see cref="DBNull"/>) becomes null, a <c>date</c> (<see cref="DateTime"/>) a <see cref="DateOnly"/>, an
/// <c>int</c> a <see cref="long"/>. Only the family's columns are kept; a staged row passes through unchanged.
/// </summary>
public static class StagedValues
{
    public static IReadOnlyDictionary<string, object?> Normalize(EtpReportFamily family, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(values);
        var staged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var column in family.Columns)
            if (values.TryGetValue(column.CanonicalField, out var value))
                staged[column.CanonicalField] = Normalize(column.DataType, value);
        return staged;
    }

    private static object? Normalize(CanonicalDataType type, object? value) => (type, value) switch
    {
        (_, null or DBNull) => null,
        (CanonicalDataType.Date, DateTime date) => DateOnly.FromDateTime(date),
        (CanonicalDataType.Integer, int number) => (long)number,
        (CanonicalDataType.Integer, short number) => (long)number,
        (CanonicalDataType.Integer, byte number) => (long)number,
        (CanonicalDataType.Decimal, double or float or int or long) => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        _ => value
    };
}
