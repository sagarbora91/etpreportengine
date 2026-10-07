using System.Globalization;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Conversion;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class TypedCellConverterTests
{
    [Fact]
    public void Numeric_identifier_scale_does_not_change_invoice_identity()
    {
        var converter = new TypedCellConverter();
        Assert.Equal("100000001", converter.Convert(100000001.0m, CanonicalDataType.Identifier, true).Value);
        Assert.Equal("00123", converter.Convert("00123", CanonicalDataType.Identifier, true).Value);
        Assert.Equal(2027L, converter.Convert(2027.0m, CanonicalDataType.Integer, true).Value);
    }
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
    [InlineData("28-09-2026", 2026, 9, 28)]
    [InlineData("25-4-2026", 2026, 4, 25)]
    [InlineData("7-2-2026", 2026, 2, 7)]
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

    // IF-026: the Service purchase registers (S007/S008) write zero tax as 0E-8; plain and signed values must keep working.
    [Theory]
    [InlineData("0E-8", "0")]
    [InlineData("0E-8 ", "0")]
    [InlineData("1.5E2", "150")]
    [InlineData("-1.25E-1", "-0.125")]
    [InlineData("18.00000000", "18")]
    [InlineData("-5", "-5")]
    public void Decimal_conversion_reads_scientific_notation(string text, string expected)
    {
        var result = converter.Convert(text, CanonicalDataType.Decimal, true);
        Assert.True(result.IsSuccess);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), result.Value);
    }

    [Theory]
    [InlineData("E5")]
    [InlineData("1E")]
    [InlineData("1E999")]
    public void Decimal_conversion_still_rejects_malformed_exponents(string text)
    {
        var result = converter.Convert(text, CanonicalDataType.Decimal, true);
        Assert.False(result.IsSuccess);
        Assert.Equal("VALUE_INVALID", result.ErrorCode);
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

    [Fact]
    public void Service_d_M_yyyy_text_dates_do_not_admit_a_text_date_with_a_time()
    {
        // S036/S037 Created Date text needs d-M-yyyy (review L1); a timestamp written as text still fails safely.
        Assert.Equal("VALUE_INVALID", converter.Convert("2026-09-28 14:55:37", CanonicalDataType.Date, false).ErrorCode);
        Assert.Equal("VALUE_INVALID", converter.Convert("31-2-2026", CanonicalDataType.Date, false).ErrorCode);
        Assert.Equal(new DateOnly(2026, 9, 28), converter.Convert(new DateTime(2026, 9, 28).ToOADate(), CanonicalDataType.Date, true).Value);
    }

    [Fact]
    public async Task Retail_fixture_dates_convert_exactly_as_with_the_v1_9_3_formats()
    {
        // Review L1: d-M-yyyy is global, so every Date cell of every Retail golden fixture must convert to the value the
        // v1.9.3 list gave. Only a text date can reach the new format; numbers and DateTime cells take the same path.
        string[] v193 = ["yyyy-MM-dd", "yyyyMMdd", "dd/MM/yyyy", "dd-MM-yyyy", "yyyy/MM/dd", "d-MMM-yyyy", "dd MMM yyyy", "d MMM yy"];
        var folder = Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample");
        var checkedCells = 0;
        foreach (var family in EtpReportFamilyRegistry.Families.Where(family => family.BusinessUnit == BusinessUnit.Retail))
        {
            var path = Directory.GetFiles(folder, family.FamilyCode + "_*.xlsx").Single();
            var sheet = (await new OpenXmlWorkbookReader().ReadAsync(path)).Sheets[0];
            var indexes = sheet.Headers.Select((header, index) => (Header: ImportProfile.NormalizeHeader(header), index))
                .ToDictionary(pair => pair.Header, pair => pair.index, StringComparer.OrdinalIgnoreCase);
            foreach (var column in family.Columns.Where(column => column.DataType == CanonicalDataType.Date))
            {
                if (!indexes.TryGetValue(ImportProfile.NormalizeHeader(column.SourceHeader), out var index)) continue;
                foreach (var row in sheet.Rows.Where(row => index < row.Cells.Count))
                {
                    var source = row.Cells[index].Value;
                    var now = converter.Convert(source, CanonicalDataType.Date, false);
                    if (source is string text && !string.IsNullOrWhiteSpace(text) && text.Trim() != "0")
                    {
                        var before = DateOnly.TryParseExact(text.Trim(), v193, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                            ? parsed : (DateOnly?)null;
                        Assert.Equal(before, now.Value as DateOnly?);
                    }
                    checkedCells++;
                }
            }
        }
        Assert.True(checkedCells > 0, "The Retail fixtures hold no Date cell to check.");
    }
}
