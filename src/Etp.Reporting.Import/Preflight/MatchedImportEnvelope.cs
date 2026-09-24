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
        IReadOnlyList<ImportDiagnostic> diagnostics, IReadOnlyList<string>? knownStores = null)
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
        Scope = ImportScope.Detect(workbook, profile, staging, knownStores);
    }

    public WorkbookSnapshot Workbook { get; }
    public WorkbookSheet MatchedSheet { get; }
    public ImportProfile Profile { get; }
    public ImportStagingResult Staging { get; }
    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; }
    public ImportScope Scope { get; }
    public ImportProfileIdentity ProfileIdentity => Profile.Identity;

    private static WorkbookSnapshot Snapshot(WorkbookSnapshot workbook) => new(
        workbook.FileName,
        workbook.FileSizeBytes,
        workbook.Sha256,
        ReadOnly(workbook.Sheets.Select(Snapshot)), workbook.SourcePath);

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

public sealed class MatchedImportEnvelopeFactory(IReadOnlyList<string>? knownStores = null)
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
            if (staging.Rows.Select(row => row.Values.GetValueOrDefault("store_code") as string)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() > 1)
                diagnostics.Add(new("WORKBOOK_MULTIPLE_STORES", ImportDiagnosticSeverity.Blocker,
                    "This workbook contains more than one store. Export one workbook per store.", inspected.Sheet!.Name));
        }

        if (inspected.CanImport && staging is not null && staging.CanPersist &&
            inspected.Profile!.ReportCode is "STOCK_LEDGER" or "CLOSING_STOCK")
        {
            var provisional = new MatchedImportEnvelope(
                workbook,
                inspected.Sheet!,
                inspected.Profile,
                staging,
                diagnostics.AsReadOnly(), knownStores);
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
                diagnostics.AsReadOnly(), knownStores)
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
