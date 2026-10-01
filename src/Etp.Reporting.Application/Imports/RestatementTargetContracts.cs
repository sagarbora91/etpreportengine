namespace Etp.Reporting.Application.Imports;

/// <summary>
/// A current import whose declared period overlaps a restatement's replacement (IF-016 interim, planner 1).
/// Only file metadata: id, file name, period and the source rows it held.
/// </summary>
public sealed record RestatementCandidate(
    long ImportFileId,
    string FileName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int Rows)
{
    public string Period => PeriodStart == PeriodEnd
        ? PeriodStart.ToString("dd MMM yyyy")
        : $"{PeriodStart:dd MMM yyyy} – {PeriodEnd:dd MMM yyyy}";
}

/// <summary>
/// What the importer asks when a restatement's period overlaps more than one current import: which one it
/// replaces. The others go through superset promotion as before.
/// </summary>
public sealed record RestatementTargetChoice(
    string FileName,
    string ReportCode,
    string StoreCode,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<RestatementCandidate> Candidates)
{
    public string Period => PeriodStart == PeriodEnd
        ? PeriodStart.ToString("dd MMM yyyy")
        : $"{PeriodStart:dd MMM yyyy} – {PeriodEnd:dd MMM yyyy}";
}
