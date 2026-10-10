namespace Etp.Reporting.Application.Service;

// Service Centre Pending board (1.10.0, design review 3.3, Q3, Q4, Q8; decision 25, 10 Oct 2026): the screen-side rules,
// pure and without I/O, on top of lane sql's contract (ServiceUiContracts.cs, ServiceUiRules.cs). The query gives the
// rows with stage, day counts and OverdueBy (ServiceAgeing.OverdueBy); this file decides who is on the board, the group
// order, the filters, the groups and the five numbers, so a Desktop test can pin each one against a fake query.

/// <summary>The board filters (design 3.3). A null or empty member means "all". <c>AgeBand</c> is a <see cref="ServiceAgeing.Bands"/> value; <c>JoType</c> a <see cref="ServiceJobTypes"/> value.</summary>
public sealed record ServicePendingFilter(
    IReadOnlyCollection<string>? Stages = null,
    string? AgeBand = null,
    bool OverdueOnly = false,
    string? Brand = null,
    string? Guarantee = null,
    string? JoType = null)
{
    public static readonly ServicePendingFilter None = new();
}

/// <summary>The five numbers on the board (design 3.3), over every job on the board before the filters.</summary>
public sealed record ServicePendingNumbers(int OpenJobs, int Overdue, int Over30Days, int InTransit, int PartsAwaited);

/// <summary>One board group: a stage, its label and its jobs in board order.</summary>
public sealed record ServicePendingGroup(string Stage, string Label, IReadOnlyList<ServicePendingBoardRow> Rows);

public static class ServicePendingBoardRules
{
    /// <summary>The eight board groups in stage order: <see cref="ServiceStages.Order"/> without the closed stages (Q8).</summary>
    public static IReadOnlyList<string> BoardStages { get; } = ServiceStages.Order.Where(stage => !ServiceStages.Closed.Contains(stage)).ToArray();

    /// <summary>Q3 and Q8: on the board when the stage is a board group and a DC/RA job has no claim document yet ("closed by claim" otherwise).</summary>
    public static bool IsOnBoard(ServicePendingBoardRow row) =>
        BoardStages.Contains(row.Stage, StringComparer.OrdinalIgnoreCase)
        && !(row.ClaimRaised && row.Stage is ServiceStages.DcIssued or ServiceStages.RaIssued);

    /// <summary>The rows the board shows, whatever order the query returned them: stage order, then days in stage descending (unknown last), then job number.</summary>
    public static IReadOnlyList<ServicePendingBoardRow> Rows(IEnumerable<ServicePendingBoardRow> rows) =>
        rows.Where(IsOnBoard)
            .OrderBy(row => ServiceStages.Rank(row.Stage))
            .ThenByDescending(row => row.DaysInStage ?? -1)
            .ThenBy(row => row.JobOrderNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Applies the filters, keeping the order. The age band is the row's (days in stage, <see cref="ServiceAgeing.Band"/>).</summary>
    public static IReadOnlyList<ServicePendingBoardRow> Apply(IEnumerable<ServicePendingBoardRow> rows, ServicePendingFilter filter) =>
        rows.Where(row =>
                (filter.Stages is null || filter.Stages.Count == 0 || filter.Stages.Contains(row.Stage, StringComparer.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(filter.AgeBand) || string.Equals(row.AgeBand, filter.AgeBand, StringComparison.Ordinal))
                && (!filter.OverdueOnly || row.IsOverdue)
                && (string.IsNullOrEmpty(filter.Brand) || string.Equals(row.Brand?.Trim(), filter.Brand, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(filter.Guarantee) || string.Equals(row.Guarantee?.Trim(), filter.Guarantee, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(filter.JoType) || row.IsQuickBilling == ServiceJobTypes.IsQuickBilling(filter.JoType)))
            .ToArray();

    /// <summary>The groups in stage order; a group with no job is left out.</summary>
    public static IReadOnlyList<ServicePendingGroup> Groups(IEnumerable<ServicePendingBoardRow> rows) =>
        rows.GroupBy(row => row.Stage, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => ServiceStages.Rank(group.Key))
            .Select(group => new ServicePendingGroup(group.Key, ServiceStages.Label(group.Key), group.ToArray()))
            .ToArray();

    /// <summary>Open jobs; overdue (<see cref="ServicePendingBoardRow.IsOverdue"/>); over 30 days since booking; in transit back; indent raised (parts awaited). As <see cref="ServiceBoard.Build"/> counts them.</summary>
    public static ServicePendingNumbers Numbers(IEnumerable<ServicePendingBoardRow> rows)
    {
        var list = rows as IReadOnlyCollection<ServicePendingBoardRow> ?? rows.ToArray();
        return new(
            list.Count,
            list.Count(row => row.IsOverdue),
            list.Count(row => row.DaysSinceBooking is > 30),
            list.Count(row => row.Stage == ServiceStages.InTransitBack),
            list.Count(row => row.Stage == ServiceStages.IndentRaised));
    }
}
