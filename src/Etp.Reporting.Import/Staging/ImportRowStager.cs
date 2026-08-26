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
            // Only explicit profile mappings are emitted; customer/contact/loyalty identifiers never enter staging.
            rows.Add(new StagedImportRow(row.RowNumber, values));
        }
        return new(rows, diagnostics);
    }
}
