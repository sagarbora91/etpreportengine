using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

/// <summary>
/// 1.9.8 (RA-SALES-04 remainder): the Brand column of the Invoice Source Drill-down is the owner's brand row by the shared
/// DSR / Brand-wise lookup (<see cref="BrandRowSql.MappedRowOfL"/>), or "Unmapped: export brand" when no row claims the line,
/// so a drilled Helios line reads SEIKO (or "Unmapped: HELIOS") like its Brand-wise row instead of the distributor.
/// </summary>
public sealed class InvoiceLineageBrandSqlTests
{
    [Fact]
    public void Brand_column_is_the_brand_row_or_Unmapped_with_the_Brand_wise_prefix()
    {
        Assert.Equal("COALESCE(mapped.row_label,N'Unmapped: '+COALESCE(l.source_brand_name,l.source_brand_code))", BrandRowSql.BrandRowOrUnmappedOfL);
        Assert.Contains($"N'{SalesReportingService.UnmappedPrefix}'", BrandRowSql.BrandRowOrUnmappedOfL, StringComparison.Ordinal);
    }

    [Fact]
    public void Drill_down_selects_the_brand_row_as_its_sixth_column_through_the_shared_lookup()
    {
        var sql = OperationalReportRepository.InvoiceLineageSql;

        Assert.Contains("l.product_code, " + BrandRowSql.BrandRowOrUnmappedOfL + ",l.brand_segment,l.source_transaction_type,", Flat(sql), StringComparison.Ordinal);
        Assert.Contains(BrandRowSql.MappedRowOfL.Trim(), sql, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(sql, "OUTER APPLY (SELECT TOP(1) r.row_label"));
        // The export's own brand is no longer the column on its own.
        Assert.DoesNotContain("l.product_code, COALESCE(l.source_brand_name,l.source_brand_code),", Flat(sql), StringComparison.Ordinal);
    }

    [Fact]
    public void Brand_row_is_a_lookup_not_a_filter_and_the_lineage_columns_are_unchanged()
    {
        var sql = OperationalReportRepository.InvoiceLineageSql;

        Assert.DoesNotContain("mapped.row_label IS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN dbo.brand_rows", sql.Replace(BrandRowSql.MappedRowOfL.Trim(), string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("WHERE i.transaction_date BETWEEN @from AND @to", sql, StringComparison.Ordinal);
        Assert.Contains("l.source_quantity,l.source_gross_amount,cro.source_cro_number,f.original_file_name,s.sheet_name,s.source_row_number", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY i.transaction_date,i.store_code,i.document_number,l.line_identifier;", sql, StringComparison.Ordinal);
    }

    private static string Flat(string sql) => System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ");

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
