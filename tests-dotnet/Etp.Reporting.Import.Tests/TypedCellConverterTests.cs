using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Conversion;

namespace Etp.Reporting.Import.Tests;

public sealed class TypedCellConverterTests
{
    private readonly TypedCellConverter converter = new();

    [Fact]
    public void Identifier_preserves_leading_zeroes()
    {
        var result = converter.Convert(" 00123 ", CanonicalDataType.Identifier, true);
        Assert.True(result.IsSuccess);
        Assert.Equal("00123", result.Value);
    }

    [Theory]
    [InlineData("2026-08-25", 2026, 8, 25)]
    [InlineData("20260825", 2026, 8, 25)]
    [InlineData("25/08/2026", 2026, 8, 25)]
    public void Date_conversion_uses_explicit_formats(string source, int year, int month, int day)
    {
        var result = converter.Convert(source, CanonicalDataType.Date, true);
        Assert.Equal(new DateOnly(year, month, day), result.Value);
    }

    [Fact]
    public void Numeric_etp_date_conversion_uses_yyyyMMdd()
    {
        var result = converter.Convert(20260825m, CanonicalDataType.Date, true);
        Assert.Equal(new DateOnly(2026, 8, 25), result.Value);
    }

    [Fact]
    public void Optional_zero_date_placeholder_becomes_null()
    {
        var result = converter.Convert(0m, CanonicalDataType.Date, false);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Decimal_conversion_is_culture_invariant()
    {
        var result = converter.Convert("1,234.50", CanonicalDataType.Decimal, true);
        Assert.Equal(1234.50m, result.Value);
    }

    [Fact]
    public void Missing_required_value_returns_structured_failure()
    {
        var result = converter.Convert(" ", CanonicalDataType.Text, true);
        Assert.False(result.IsSuccess);
        Assert.Equal("VALUE_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public void Invalid_value_does_not_throw()
    {
        var result = converter.Convert("not-a-number", CanonicalDataType.Decimal, true);
        Assert.False(result.IsSuccess);
        Assert.Equal("VALUE_INVALID", result.ErrorCode);
    }
}
