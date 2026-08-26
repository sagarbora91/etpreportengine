using System.Globalization;
using Etp.Reporting.Domain.Imports;

namespace Etp.Reporting.Import.Conversion;

public sealed record CellConversionResult(object? Value, string? ErrorCode = null, string? ErrorMessage = null)
{
    public bool IsSuccess => ErrorCode is null;
    public static CellConversionResult Success(object? value) => new(value);
    public static CellConversionResult Failure(string code, string message) => new(null, code, message);
}

public sealed class TypedCellConverter
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyyMMdd", "dd/MM/yyyy", "dd-MM-yyyy", "yyyy/MM/dd"];

    public CellConversionResult Convert(object? source, CanonicalDataType target, bool isRequired)
    {
        if (source is null || source is string text && string.IsNullOrWhiteSpace(text))
            return isRequired
                ? CellConversionResult.Failure("VALUE_REQUIRED", "A required value is missing.")
                : CellConversionResult.Success(null);
        if (target == CanonicalDataType.Date && IsNumericZero(source))
            return isRequired
                ? CellConversionResult.Failure("VALUE_REQUIRED", "A required date is missing.")
                : CellConversionResult.Success(null);

        try
        {
            return target switch
            {
                CanonicalDataType.Text => CellConversionResult.Success(source.ToString()!.Trim()),
                CanonicalDataType.Identifier => ConvertIdentifier(source),
                CanonicalDataType.Decimal => ParseDecimal(source),
                CanonicalDataType.Integer => ParseInteger(source),
                CanonicalDataType.Date => ParseDate(source),
                CanonicalDataType.Boolean => ParseBoolean(source),
                _ => CellConversionResult.Failure("TYPE_UNSUPPORTED", $"Unsupported target type '{target}'.")
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return CellConversionResult.Failure("VALUE_INVALID", $"Value cannot be converted to {target}.");
        }
    }

    private static CellConversionResult ConvertIdentifier(object source)
    {
        var value = source is string s ? s.Trim() : System.Convert.ToString(source, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(value)
            ? CellConversionResult.Failure("VALUE_REQUIRED", "An identifier cannot be empty.")
            : CellConversionResult.Success(value);
    }

    private static CellConversionResult ParseDecimal(object source) => source switch
    {
        decimal d => CellConversionResult.Success(d),
        _ => CellConversionResult.Success(decimal.Parse(source.ToString()!.Trim(), NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture))
    };

    private static CellConversionResult ParseInteger(object source) => source switch
    {
        int i => CellConversionResult.Success(i),
        long l => CellConversionResult.Success(l),
        _ => CellConversionResult.Success(long.Parse(source.ToString()!.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture))
    };

    private static CellConversionResult ParseDate(object source)
    {
        if (source is DateOnly dateOnly) return CellConversionResult.Success(dateOnly);
        if (source is DateTime dateTime) return CellConversionResult.Success(DateOnly.FromDateTime(dateTime));
        if (DateOnly.TryParseExact(source.ToString()!.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return CellConversionResult.Success(parsed);
        throw new FormatException();
    }

    private static CellConversionResult ParseBoolean(object source)
    {
        if (source is bool value) return CellConversionResult.Success(value);
        return source.ToString()!.Trim().ToUpperInvariant() switch
        {
            "TRUE" or "YES" or "Y" or "1" => CellConversionResult.Success(true),
            "FALSE" or "NO" or "N" or "0" => CellConversionResult.Success(false),
            _ => throw new FormatException()
        };
    }

    private static bool IsNumericZero(object source) => source switch
    {
        byte value => value == 0, short value => value == 0, int value => value == 0,
        long value => value == 0, float value => value == 0, double value => value == 0,
        decimal value => value == 0, _ => false
    };
}
