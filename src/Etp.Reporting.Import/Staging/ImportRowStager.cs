using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Conversion;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Staging;

public sealed record StagedImportRow(int SourceRowNumber, IReadOnlyDictionary<string, object?> Values);
public sealed record ImportStagingResult(IReadOnlyList<StagedImportRow> Rows, IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool CanPersist => Diagnostics.All(x => x.Severity != ImportDiagnosticSeverity.Blocker);
}

public sealed class ImportRowStager
{
    private readonly TypedCellConverter converter = new();

    public ImportStagingResult Stage(WorkbookSheet sheet, ImportProfile profile)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(profile);
        var headerIndexes = sheet.Headers.Select((h, i) => (Name: ImportProfile.NormalizeHeader(h), Index: i))
            .ToDictionary(x => x.Name, x => x.Index, StringComparer.Ordinal);
        var rows = new List<StagedImportRow>();
        var diagnostics = new List<ImportDiagnostic>();

        foreach (var row in sheet.Rows)
        {
            if (row.Cells.Skip(sheet.Headers.Count).Any(cell => cell.Value is not null &&
                !string.IsNullOrWhiteSpace(cell.Value.ToString())))
            {
                diagnostics.Add(new("ROW_EXTRA_COLUMNS", ImportDiagnosticSeverity.Blocker,
                    "This row contains data beyond the approved header columns.", sheet.Name, row.RowNumber));
                continue;
            }
            var typeIndex = headerIndexes.GetValueOrDefault("TRANS_TYPE", -1);
            var transactionType = typeIndex >= 0 && typeIndex < row.Cells.Count ? row.Cells[typeIndex].Value?.ToString()?.Trim() : null;
            var salesFamily = profile.ReportCode is "R003" or "R013" or "R022" or "R024" or "R025";
            if ((salesFamily && !SalesTransactionTypes.Contains(transactionType ?? "")) ||
                (profile.ReportCode == "STOCK_LEDGER" && !StockTransactionTypes.Contains(transactionType ?? "")))
            {
                diagnostics.Add(new(profile.ReportCode == "STOCK_LEDGER" ? "UNKNOWN_STOCK_TRANSACTION_TYPE" : "UNKNOWN_SALES_TRANSACTION_TYPE",
                    ImportDiagnosticSeverity.Warning, "Unrecognised transaction type; this row was skipped.", sheet.Name, row.RowNumber, "TRANS_TYPE"));
                continue;
            }
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var mapping in profile.Fields)
            {
                var header = ImportProfile.NormalizeHeader(mapping.SourceHeader);
                var source = headerIndexes.TryGetValue(header, out var index) && index < row.Cells.Count
                    ? row.Cells[index].Value : null;
                var converted = converter.Convert(source, mapping.DataType, mapping.IsRequired);
                if (!converted.IsSuccess)
                    diagnostics.Add(new ImportDiagnostic(converted.ErrorCode!, ImportDiagnosticSeverity.Blocker,
                        converted.ErrorMessage!, sheet.Name, row.RowNumber, mapping.SourceHeader));
                else
                    values[mapping.CanonicalField] = converted.Value;
            }
            if (salesFamily && transactionType is not null &&
                (transactionType.Equals("SR", StringComparison.OrdinalIgnoreCase) || transactionType.Equals("BC", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var field in new[] { "source_quantity", "source_invoice_quantity", "source_net_amount", "source_net_value", "source_tax_amount" })
                    if (values.TryGetValue(field, out var raw) && raw is decimal amount) values[field] = -Math.Abs(amount);
            }
            rows.Add(new StagedImportRow(row.RowNumber, values));
        }
        return new(rows, diagnostics);
    }

    public static IReadOnlySet<string> SalesTransactionTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "INV", "SR", "BC" };
    public static IReadOnlySet<string> StockTransactionTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "INV", "SR", "BC", "Purchase Receipt", "Purchase Return", "STM Issue", "STM Receipt", "STM Dispatch", "Stock Issue", "Stock Receipt" };
}
