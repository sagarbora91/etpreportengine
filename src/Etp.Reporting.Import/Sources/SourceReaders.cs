using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Sources;

/// <summary>A workbook to describe: the family matched by header signature and the sheet that matched.</summary>
public sealed record SourceDescriptionRequest(WorkbookSnapshot Workbook, EtpReportFamily Family, WorkbookSheet DataSheet)
{
    /// <summary>The Owner ticked "Import as reviewed file": one complete block, export time unknown.</summary>
    public bool ImportAsReviewed { get; init; }
}

/// <summary>
/// Describes a source as blocks (spec 6.1-6.2, 6.5-6.6): detects CONSOLIDATED, CONSOLIDATED_LEGACY, REVIEWED or RAW,
/// and rebuilds each block into its export's rows, virtual rows included.
/// </summary>
public interface ISourceDescriptionReader
{
    SourceDescription Describe(SourceDescriptionRequest request);

    /// <summary>
    /// The rows of one block as its export held them: the block's physical rows (staged, with locators) plus one
    /// virtual row per map row, staged with its twin's values (spec 6.5 rebuild).
    /// </summary>
    IReadOnlyList<SourceRow> RebuildBlockRows(SourceDescription source, SourceBlock block, IReadOnlyList<SourceRow> stagedRows);
}

/// <summary>A contract workbook to validate against the family matched by header signature.</summary>
public sealed record ContractValidationRequest(WorkbookSnapshot Workbook, ConsolidationContract Contract, EtpReportFamily MatchedFamily)
{
    /// <summary><c>--raw</c>: raw exports by lowercase SHA-256, for the block rebuild check.</summary>
    public IReadOnlyDictionary<string, WorkbookSnapshot> RawExports { get; init; } = new Dictionary<string, WorkbookSnapshot>();
}

/// <summary>The validated blocks and exclusion map, and every contract diagnostic found.</summary>
public sealed record ContractValidationResult(
    IReadOnlyList<SourceBlock> Blocks,
    IReadOnlyList<VirtualRow> VirtualRows,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool HasBlockers => Diagnostics.Any(diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Blocker);
}

/// <summary>
/// Every rule of contract section 8, one implementation for the importer and <c>ImportAudit --validate-contract</c>.
/// A blocker refuses the whole workbook.
/// </summary>
public interface IConsolidationContractValidator
{
    ContractValidationResult Validate(ContractValidationRequest request);
}
