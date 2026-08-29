using System.Collections.ObjectModel;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Stock;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Preflight;

public sealed class MatchedImportEnvelope
{
    internal MatchedImportEnvelope(
        WorkbookSnapshot workbook,
        WorkbookSheet matchedSheet,
        ImportProfile profile,
        ImportStagingResult staging,
        IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(matchedSheet);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Workbook = Snapshot(workbook);
        MatchedSheet = Snapshot(matchedSheet);
        Profile = profile;
        Staging = Snapshot(staging);
        Diagnostics = ReadOnly(diagnostics);
    }

    public WorkbookSnapshot Workbook { get; }
    public WorkbookSheet MatchedSheet { get; }
    public ImportProfile Profile { get; }
    public ImportStagingResult Staging { get; }
    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; }
    public ImportProfileIdentity ProfileIdentity => Profile.Identity;

    private static WorkbookSnapshot Snapshot(WorkbookSnapshot workbook) => new(
        workbook.FileName,
        workbook.FileSizeBytes,
        workbook.Sha256,
        ReadOnly(workbook.Sheets.Select(Snapshot)));

    private static WorkbookSheet Snapshot(WorkbookSheet sheet) => new(
        sheet.Name,
        sheet.HeaderRowNumber,
        ReadOnly(sheet.Headers),
        ReadOnly(sheet.Rows.Select(row => new WorkbookRow(
            row.RowNumber,
            ReadOnly(row.Cells.Select(cell => new WorkbookCell(cell.Value, cell.DisplayText)))))));

    private static ImportStagingResult Snapshot(ImportStagingResult staging) => new(
        ReadOnly(staging.Rows.Select(row => new StagedImportRow(
            row.SourceRowNumber,
            new ReadOnlyDictionary<string, object?>(row.Values.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal))))),
        ReadOnly(staging.Diagnostics));

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}

public sealed record MatchedImportInspection(
    MatchedImportEnvelope? AcceptedImport,
    ImportProfile? MatchedProfile,
    int StagedRows,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool Accepted => AcceptedImport is not null;
}

public sealed class MatchedImportEnvelopeFactory
{
    private readonly ImportPreflight preflight = new();
    private readonly ImportRowStager stager = new();

    public MatchedImportInspection Inspect(WorkbookSnapshot workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var inspected = preflight.Inspect(workbook, ApprovedImportProfileRegistry.All);
        var diagnostics = inspected.Diagnostics.ToList();
        ImportStagingResult? staging = null;
        if (inspected.CanImport)
        {
            staging = stager.Stage(inspected.Sheet!, inspected.Profile!);
            diagnostics.AddRange(staging.Diagnostics);
        }

        if (inspected.CanImport && staging is not null && staging.CanPersist &&
            inspected.Profile!.ReportCode is "STOCK_LEDGER" or "CLOSING_STOCK")
        {
            var provisional = new MatchedImportEnvelope(
                workbook,
                inspected.Sheet!,
                inspected.Profile,
                staging,
                diagnostics.AsReadOnly());
            diagnostics = new StockWorkbookParser().Parse(provisional).Diagnostics.ToList();
        }

        var accepted = inspected.CanImport
            && staging is not null
            && diagnostics.All(row => row.Severity != ImportDiagnosticSeverity.Blocker);
        var envelope = accepted
            ? new MatchedImportEnvelope(
                workbook,
                inspected.Sheet!,
                inspected.Profile!,
                staging!,
                diagnostics.AsReadOnly())
            : null;
        return new(envelope, inspected.Profile, staging?.Rows.Count ?? 0, diagnostics.AsReadOnly());
    }

    public MatchedImportEnvelope RequireAccepted(WorkbookSnapshot workbook)
    {
        var inspection = Inspect(workbook);
        if (inspection.AcceptedImport is not null) return inspection.AcceptedImport;
        var codes = string.Join(", ", inspection.Diagnostics
            .Where(row => row.Severity == ImportDiagnosticSeverity.Blocker)
            .Select(row => row.Code)
            .Distinct(StringComparer.Ordinal));
        throw new ImportSourceException(
            "IMPORT_LAYOUT_BLOCKED",
            $"Workbook validation was blocked: {codes}.");
    }
}
