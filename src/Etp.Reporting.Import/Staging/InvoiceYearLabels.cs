using System.Globalization;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Documents;

namespace Etp.Reporting.Import.Staging;

/// <summary>
/// ETP's INVOICEYEAR is a label, never identity (OD-1, spec 7.3): every invoice is keyed by the financial year of its
/// own date. ETP labels a return dated 1 April with the year before, so the label is checked, never used. The rows
/// of an invoice family whose label names another year get one <c>INVOICE_YEAR_DIFFERS</c> information diagnostic
/// with their count and row numbers.
/// </summary>
public static class InvoiceYearLabels
{
    /// <summary>The families whose rows become invoice-keyed facts.</summary>
    private static readonly IReadOnlySet<string> InvoiceFamilies =
        new HashSet<string>(StringComparer.Ordinal) { "R003", "R013", "R022", "R025" };

    public static IReadOnlyList<ImportDiagnostic> Check(ImportProfile profile, string sheetName, IEnumerable<StagedImportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(rows);
        if (!InvoiceFamilies.Contains(profile.ReportCode)) return [];
        var differing = rows.Where(row => Differs(row.Values)).Select(row => row.SourceRowNumber).ToArray();
        if (differing.Length == 0) return [];
        var column = profile.Fields.FirstOrDefault(field => field.CanonicalField == "invoice_year")?.SourceHeader ?? "INVOICEYEAR";
        var rowText = differing.Length == 1 ? $"1 row (row {differing[0]}) carries" : $"{differing.Length} rows (rows {RowList(differing)}) carry";
        return
        [
            new ImportDiagnostic(ImportCodes.InvoiceYearDiffers, ImportDiagnosticSeverity.Information,
                $"{rowText} an {column} other than the financial year of the invoice date. " +
                "Invoices are keyed by the financial year of their date; ETP's year is kept as a label.",
                sheetName, differing[0], column) { Occurrences = differing.Length }
        ];
    }

    /// <summary>Whether a staged row carries a year label other than the financial year of its <c>transaction_date</c>.</summary>
    public static bool Differs(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Label(values) is { } year && values.GetValueOrDefault("transaction_date") is DateOnly date &&
            year != DocumentKey.FinancialYearEnd(date);
    }

    /// <summary>ETP's staged <c>invoice_year</c>, or null when the row has none.</summary>
    public static int? Label(IReadOnlyDictionary<string, object?> values) =>
        values.TryGetValue("invoice_year", out var value) && value is not null &&
        int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var year) && year > 1900 ? year : null;

    // Issue messages are stored in 500 characters; ten row numbers locate the rows.
    private static string RowList(IReadOnlyList<int> rows) => rows.Count <= 10
        ? string.Join(", ", rows)
        : $"{string.Join(", ", rows.Take(10))} and {rows.Count - 10} more";
}
