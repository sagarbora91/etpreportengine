namespace Etp.Reporting.Application.Service;

// Service Centre Pending board rules (1.10.0, design review 3.3, 4.3, Q3, Q4, Q8; decision 25, 10 Oct 2026).
// Pure and without I/O: the SQL view gives the stage, the dates and the day counts; everything the board decides
// (who is on it, the group order, the age band, overdue, the five numbers) is here so a test can pin it.

/// <summary>One group of the Pending board: its stage code, label, position and the days-in-stage limit that makes a job without an EDD overdue (Q4).</summary>
public sealed record ServicePendingStage(string Code, string Label, int Order, int? LimitDays);

/// <summary>The eight board groups in stage order (design 3.3). DELIVERED and RWR are never on the board (Q8).</summary>
public static class ServicePendingStages
{
    public static readonly ServicePendingStage Booked = new(ServiceJobStages.Booked, "Booked, no status yet", 0, null);
    public static readonly ServicePendingStage OnBench = new(ServiceJobStages.OnBench, "On the bench", 1, 7);
    public static readonly ServicePendingStage IndentRaised = new(ServiceJobStages.IndentRaised, "Indent raised, parts awaited", 2, 15);
    public static readonly ServicePendingStage SrnOut = new(ServiceJobStages.SrnOut, "SRN out for repair", 3, 30);
    public static readonly ServicePendingStage InTransitBack = new(ServiceJobStages.InTransitBack, "Sent back after repair, in transit", 4, 15);
    public static readonly ServicePendingStage ReadyForDelivery = new(ServiceJobStages.ReadyForDelivery, "Ready for delivery at AW330", 5, 7);
    public static readonly ServicePendingStage DcIssued = new(ServiceJobStages.DcIssued, "DC issued, claim not raised", 6, null);
    public static readonly ServicePendingStage RaIssued = new(ServiceJobStages.RaIssued, "RA issued, claim not raised", 7, null);

    public static IReadOnlyList<ServicePendingStage> Board { get; } =
        [Booked, OnBench, IndentRaised, SrnOut, InTransitBack, ReadyForDelivery, DcIssued, RaIssued];

    /// <summary>The board group for a <see cref="ServiceJobStages"/> code, or null for a stage that is not on the board (DELIVERED, RWR, unknown).</summary>
    public static ServicePendingStage? Find(string? code) =>
        code is null ? null : Board.FirstOrDefault(stage => string.Equals(stage.Code, code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The age bands of the board (days since booking): 0-7, 8-15, 16-30, 31-60, 60+ (design 4.3).</summary>
public static class ServiceAgeBands
{
    public const string UpTo7 = "0-7";
    public const string UpTo15 = "8-15";
    public const string UpTo30 = "16-30";
    public const string UpTo60 = "31-60";
    public const string Over60 = "60+";

    public static IReadOnlyList<string> All { get; } = [UpTo7, UpTo15, UpTo30, UpTo60, Over60];

    /// <summary>The band of an age in days; null when the age is unknown (no booking date). A negative age counts as 0.</summary>
    public static string? Of(int? ageDays) => ageDays switch
    {
        null => null,
        <= 7 => UpTo7,
        <= 15 => UpTo15,
        <= 30 => UpTo30,
        <= 60 => UpTo60,
        _ => Over60
    };
}

/// <summary>
/// One job on the board: the view row with its group, age band and "overdue by" (days past the EDD, or days in stage past
/// the group's limit when the job has no EDD; null when not overdue).
/// </summary>
public sealed record ServicePendingBoardEntry(ServicePendingBoardRow Row, ServicePendingStage Stage, string? AgeBand, int? OverdueBy)
{
    public bool IsOverdue => OverdueBy is > 0;
    public string StageLabel => Stage.Label;
}

/// <summary>The board filters (design 3.3). A null or empty member means "all".</summary>
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

/// <summary>One board group with its jobs, sorted.</summary>
public sealed record ServicePendingGroup(ServicePendingStage Stage, IReadOnlyList<ServicePendingBoardEntry> Entries);

public static class ServicePendingBoard
{
    public const string Booking = "Booking";
    public const string QuickBilling = "Quick Billing";

    /// <summary>Q3 and Q8: on the board when the stage is a board group and a DC/RA job has no claim document yet.</summary>
    public static bool IsOnBoard(ServicePendingBoardRow row)
    {
        var stage = ServicePendingStages.Find(row.Stage);
        if (stage is null) return false;
        var claimStage = stage == ServicePendingStages.DcIssued || stage == ServicePendingStages.RaIssued;
        return !(claimStage && row.ClaimRaised);
    }

    /// <summary>
    /// Q4: a job with an EDD is overdue by the days its EDD is past the as-at date; a job without one is overdue by the
    /// days in stage past its group's limit (bench 7, indent 15, SRN out 30, in transit 15, ready 7). Groups without a
    /// limit (booked, DC/RA) are never overdue without an EDD. Null means not overdue.
    /// </summary>
    public static int? OverdueBy(ServicePendingBoardRow row, ServicePendingStage stage)
    {
        if (row.Edd is { } edd)
        {
            var past = row.AsAt.DayNumber - edd.DayNumber;
            return past > 0 ? past : null;
        }
        if (stage.LimitDays is { } limit && row.DaysInStage is { } days && days > limit) return days - limit;
        return null;
    }

    /// <summary>Every job on the board, sorted by stage order, then days in stage descending (unknown last), then job number.</summary>
    public static IReadOnlyList<ServicePendingBoardEntry> Entries(IEnumerable<ServicePendingBoardRow> rows) =>
        rows.Where(IsOnBoard)
            .Select(row =>
            {
                var stage = ServicePendingStages.Find(row.Stage)!;
                return new ServicePendingBoardEntry(row, stage, ServiceAgeBands.Of(row.AgeDays), OverdueBy(row, stage));
            })
            .OrderBy(entry => entry.Stage.Order)
            .ThenByDescending(entry => entry.Row.DaysInStage.HasValue)
            .ThenByDescending(entry => entry.Row.DaysInStage)
            .ThenBy(entry => entry.Row.JobOrderNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Applies the filters, keeping the order.</summary>
    public static IReadOnlyList<ServicePendingBoardEntry> Apply(IEnumerable<ServicePendingBoardEntry> entries, ServicePendingFilter filter) =>
        entries.Where(entry =>
                (filter.Stages is null || filter.Stages.Count == 0 || filter.Stages.Contains(entry.Stage.Code, StringComparer.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(filter.AgeBand) || string.Equals(entry.AgeBand, filter.AgeBand, StringComparison.Ordinal))
                && (!filter.OverdueOnly || entry.IsOverdue)
                && (string.IsNullOrEmpty(filter.Brand) || string.Equals(entry.Row.Brand, filter.Brand, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(filter.Guarantee) || string.Equals(entry.Row.Guarantee, filter.Guarantee, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(filter.JoType) || string.Equals(entry.Row.JoType, filter.JoType, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    /// <summary>The groups in stage order; a group with no job is left out.</summary>
    public static IReadOnlyList<ServicePendingGroup> Groups(IEnumerable<ServicePendingBoardEntry> entries) =>
        entries.GroupBy(entry => entry.Stage)
            .OrderBy(group => group.Key.Order)
            .Select(group => new ServicePendingGroup(group.Key, group.ToArray()))
            .ToArray();

    /// <summary>Open jobs; overdue (<see cref="OverdueBy"/>); over 30 days since booking; in transit back; indent raised (parts awaited).</summary>
    public static ServicePendingNumbers Numbers(IEnumerable<ServicePendingBoardEntry> entries)
    {
        var list = entries as IReadOnlyCollection<ServicePendingBoardEntry> ?? entries.ToArray();
        return new(
            list.Count,
            list.Count(entry => entry.IsOverdue),
            list.Count(entry => entry.Row.AgeDays is > 30),
            list.Count(entry => entry.Stage == ServicePendingStages.InTransitBack),
            list.Count(entry => entry.Stage == ServicePendingStages.IndentRaised));
    }
}
