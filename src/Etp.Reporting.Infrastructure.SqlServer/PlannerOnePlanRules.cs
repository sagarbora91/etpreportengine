using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// The pure part of planner 1's file plan (<c>SqlServerTransactionalImportStore.PlanImportAsync</c>): given the incoming
/// content keys and the current files whose period (or snapshot dates) it overlaps, is the file a content subset, a
/// superset that takes them over, or a refusal? ImportAudit reads the previous files without lock hints and calls this
/// (IMPORTAUDIT-CLI-DESIGN.md 5.3).
/// <para>
/// It is, statement for statement, the decision at the end of <c>PlanImportAsync</c> (PhaseOneImportPersistence.cs).
/// The design wants <c>PlanImportAsync</c> to call this method instead of holding its own copy, so the two cannot drift;
/// that one-line change to the import path is left to the integration (see <c>PlannerOnePlanRulesTests</c>, which pins
/// the same cases, and the integration test <c>Prediction_matches_real_import</c>). Until then, a change to either copy
/// must be made to both.
/// </para>
/// </summary>
internal static class PlannerOnePlanRules
{
    /// <summary>
    /// True when every incoming key is already held by the previous files (<c>DuplicateContent</c>, no restatement);
    /// false when the file is imported and promoted over every previous file. A refusal throws
    /// <see cref="ImportSourceException"/> at the <see cref="FailureStage.Plan"/> stage:
    /// <c>IMPORT_LEGACY_RESTATEMENT_REQUIRED</c>, <c>RESTATEMENT_TARGET_NOT_COVERED</c>,
    /// <c>RESTATEMENT_OTHER_IMPORT_CHANGED</c> or <c>IMPORT_PERIOD_ALREADY_PRESENT</c>.
    /// </summary>
    /// <param name="periodStart">The file's period start, or its business date.</param>
    /// <param name="periodEnd">The file's period end, or its business date.</param>
    /// <param name="sourceSha256">The file's SHA-256 as the import registers it.</param>
    /// <param name="incomingKeys">The file's content keys (<c>ContentKeys</c>).</param>
    /// <param name="previous">The current files the plan read, after <c>SharingSnapshotDates</c>.</param>
    /// <param name="restatementTarget">The import an explicit restatement replaces, if any.</param>
    public static bool Decide(DateOnly? periodStart, DateOnly? periodEnd, string sourceSha256, IEnumerable<string> incomingKeys,
        IReadOnlyList<SqlServerTransactionalImportStore.PreviousFile> previous, long? restatementTarget)
    {
        var incoming = incomingKeys.ToHashSet(StringComparer.Ordinal);
        var allExisting = previous.SelectMany(x => x.Keys).ToHashSet(StringComparer.Ordinal);
        if (previous.Count > 0 && incoming.IsSubsetOf(allExisting) && restatementTarget is null)
            return true;
        foreach (var old in previous)
        {
            var coversRange = periodStart <= old.Start && periodEnd >= old.End;
            var isExplicit = restatementTarget == old.Id;
            if (old.Version == 0 && old.Hash != sourceSha256 && !isExplicit)
                throw new ImportSourceException("IMPORT_LEGACY_RESTATEMENT_REQUIRED",
                    "This period was imported before the data-truth upgrade. Re-import its original workbook first, or use Restate with a reviewed replacement. No data was changed.")
                    { Stage = FailureStage.Plan };
            // During a restatement "Use Restate" sends the Owner in a circle: say which other import blocks it, and why.
            if (restatementTarget is { } restating && !isExplicit && !coversRange)
                throw new ImportSourceException(ImportCodes.RestatementTargetNotCovered,
                    $"This restatement replaces import {restating}, but its period only partly overlaps current import {old.Id}, so it cannot replace that one too. Import a file whose period covers it fully. No data was changed.")
                    { Stage = FailureStage.Plan };
            if (restatementTarget is { } replacing && !isExplicit && !old.Keys.IsSubsetOf(incoming))
                throw new ImportSourceException(ImportCodes.RestatementOtherImportChanged,
                    $"This restatement replaces import {replacing}, but its period also covers current import {old.Id}, and it changes or drops {old.Keys.Except(incoming).Count():N0} of that import's rows. A run restates only one import: restate each import with a corrected file for its own period. No data was changed.")
                    { Stage = FailureStage.Plan };
            if (!coversRange || (!isExplicit && !old.Keys.IsSubsetOf(incoming)))
                throw new ImportSourceException("IMPORT_PERIOD_ALREADY_PRESENT",
                    $"Already imported on {old.Imported:dd MMM yyyy} (hash {old.Hash[..12]}). Use Restate. {old.Keys.Except(incoming).Count():N0} conflicting or missing rows; no data was changed.")
                    { Stage = FailureStage.Plan };
        }
        return false;
    }
}
