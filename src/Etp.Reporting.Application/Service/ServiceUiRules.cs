namespace Etp.Reporting.Application.Service;

// Service Centre UI wave (1.10.0, decision 25): the pure rules of design section 4.3, Q2, Q4 and Q14, without I/O, so
// the unit tests pin them and SqlServerServiceReportQuery only feeds them rows. Lane sql owns this file; the UI lanes
// copy it beside ServiceUiContracts.cs.

/// <summary>Age bands, per-stage limits and the overdue rule (design 4.3, Q4).</summary>
public static class ServiceAgeing
{
    /// <summary>The bands of the Pending board filter: 0-7, 8-15, 16-30, 31-60, 60+.</summary>
    public static IReadOnlyList<string> Bands { get; } = ["0-7", "8-15", "16-30", "31-60", "60+"];

    public static string? Band(int? days) => days switch
    {
        null => null,
        <= 7 => "0-7",
        <= 15 => "8-15",
        <= 30 => "16-30",
        <= 60 => "31-60",
        _ => "60+",
    };

    /// <summary>Days in stage after which a job without an EDD is overdue (Q4): bench 7, indent 15, SRN out 30, in transit 15, ready 7; null when the stage has no limit.</summary>
    public static int? StageLimit(string stage) => stage switch
    {
        ServiceStages.OnBench => 7,
        ServiceStages.IndentRaised => 15,
        ServiceStages.SrnOut => 30,
        ServiceStages.InTransitBack => 15,
        ServiceStages.ReadyForDelivery => 7,
        _ => null,
    };

    /// <summary>
    /// Days overdue, or null when not overdue: EDD passed (as-at minus EDD, when positive) where an EDD exists; otherwise days
    /// in stage over the stage limit. A closed stage (DELIVERED, RWR) is never overdue.
    /// </summary>
    public static int? OverdueBy(string stage, DateOnly? edd, int? daysInStage, DateOnly asAt)
    {
        if (ServiceStages.Closed.Contains(stage)) return null;
        if (edd is { } promised)
        {
            var late = asAt.DayNumber - promised.DayNumber;
            return late > 0 ? late : null;
        }
        if (StageLimit(stage) is { } limit && daysInStage is { } days && days > limit) return days - limit;
        return null;
    }
}

/// <summary>TAT of closed jobs (Q2): medians per job type and the count over 15 days.</summary>
public static class ServiceTat
{
    public static double? Median(IEnumerable<int> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return null;
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>Booking jobs and Quick Billing jobs apart (a job without an S002 row is Booking); only closed jobs with a TAT count.</summary>
    public static ServiceTatSummary Summarise(IEnumerable<ServiceJobSummary> jobs)
    {
        var closed = jobs.Where(job => ServiceStages.Closed.Contains(job.Stage) && job.TatDays is not null).ToArray();
        var booking = closed.Where(job => !job.IsQuickBilling).ToArray();
        var quick = closed.Where(job => job.IsQuickBilling).ToArray();
        var bookingTat = booking.Select(job => job.TatDays!.Value).ToArray();
        return new ServiceTatSummary(
            closed.Length, booking.Length, quick.Length,
            Median(bookingTat),
            Median(booking.Where(job => job.TatRepairDays is not null).Select(job => job.TatRepairDays!.Value)),
            bookingTat.Length == 0 ? null : bookingTat.Average(),
            Median(quick.Select(job => job.TatDays!.Value)),
            closed.Count(job => job.TatDays > 15));
    }
}

/// <summary>The Pending board and Service Today composed from v_service_job rows (design 3.2, 3.3).</summary>
public static class ServiceBoard
{
    public static ServicePendingBoardRow ToBoardRow(ServiceJobSummary job) => new(job.JobOrderNumber, job.Stage, job.BookingDate, job.AgeDays,
        job.DaysInStage, job.Edd, job.OverdueBy, job.Brand, job.Model, job.ProductCategory, job.Guarantee, job.CustomerType, job.PendingAt,
        job.SpareRequired, job.JoType, job.IsQuickBilling, job.ClaimRaised, job.LastReadingDate);

    /// <summary>Open jobs only, stage order then days in stage descending, with the numbers on the screen.</summary>
    public static ServicePendingBoard Build(IEnumerable<ServiceJobSummary> jobs, DateOnly? asAt)
    {
        var rows = jobs.Where(job => job.IsOpen)
            .OrderBy(job => ServiceStages.Rank(job.Stage)).ThenByDescending(job => job.DaysInStage ?? -1).ThenBy(job => job.JobOrderNumber, StringComparer.Ordinal)
            .Select(ToBoardRow).ToArray();
        return new ServicePendingBoard(rows, rows.Length, rows.Count(row => row.IsOverdue), rows.Count(row => row.DaysSinceBooking > 30),
            rows.Count(row => row.Stage == ServiceStages.InTransitBack), rows.Count(row => row.Stage == ServiceStages.IndentRaised), asAt);
    }

    /// <summary>
    /// Service Today for <paramref name="businessDate"/>: bookings and deliveries by their own dates, the open-job counts as at
    /// the snapshot, S004 tenders of the date and the Service-money shop's manual entries of the date (decision 16).
    /// </summary>
    public static ServiceToday Today(DateOnly businessDate, DateOnly? asAt, IEnumerable<ServiceJobSummary> jobs,
        IEnumerable<ServiceS004TenderAmount> s004, IEnumerable<ServiceManualMoneyEntry> manual, IEnumerable<ServiceClaimLine> claims)
    {
        var all = jobs.ToArray();
        var month = new DateOnly(businessDate.Year, businessDate.Month, 1);
        bool InMonth(DateOnly? date) => date is { } d && d >= month && d <= businessDate;
        var bookedToday = all.Where(job => job.BookingDate == businessDate).ToArray();
        var bookedMonth = all.Where(job => InMonth(job.BookingDate)).ToArray();
        var open = all.Where(job => job.IsOpen).ToArray();
        var tenders = s004.Where(t => t.BusinessDate == businessDate).ToArray();
        decimal? Tender(string name) => tenders.Where(t => t.Tender.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(t => t.Amount).Sum();
        var cash = Tender("CASH"); var card = Tender("CARD"); var upi = Tender("UPI");
        decimal? collection = tenders.Length == 0 ? null : (cash ?? 0) + (card ?? 0) + (upi ?? 0);
        var entered = manual.Where(entry => entry.IsServiceMoneyShop && entry.BusinessDate == businessDate
            && entry.FieldCode is "SERVICE_CASH" or "SERVICE_CARD" or "SERVICE_UPI").ToArray();
        var monthClaims = claims.Where(line => InMonth(line.BusinessDate)).ToArray();
        return new ServiceToday(businessDate, asAt,
            bookedToday.Length, bookedToday.Count(job => !job.IsQuickBilling), bookedToday.Count(job => job.IsQuickBilling),
            bookedMonth.Length, bookedMonth.Count(job => !job.IsQuickBilling), bookedMonth.Count(job => job.IsQuickBilling),
            all.Count(job => job.Stage == ServiceStages.Delivered && job.DeliveryDate == businessDate),
            all.Count(job => job.Stage == ServiceStages.Delivered && InMonth(job.DeliveryDate)),
            all.Count(job => job.Stage == ServiceStages.Rwr && job.RwrDate == businessDate),
            open.Count(job => job.Stage is ServiceStages.OnBench or ServiceStages.IndentRaised),
            open.Count(job => job.Stage == ServiceStages.IndentRaised),
            open.Count(job => job.Stage is ServiceStages.OnBench or ServiceStages.IndentRaised && job.Edd is { } edd && asAt is { } at && edd < at),
            open.Count(job => job.Stage == ServiceStages.InTransitBack),
            open.Count(job => job.Stage == ServiceStages.ReadyForDelivery),
            collection, cash, card, upi,
            entered.Length > 0, entered.Length == 0 ? null : entered.Sum(entry => entry.Amount),
            open.Count(job => job.AgeDays > 15),
            monthClaims.Select(line => line.DocumentNumber ?? "").Distinct(StringComparer.Ordinal).Count(),
            monthClaims.Length == 0 ? null : monthClaims.Sum(line => line.NetAmountInclTax ?? 0));
    }
}

/// <summary>The freshness strip (design 3.7, Q14) built from the refresh log; no SQL of its own.</summary>
public static class ServiceFreshness
{
    public const int AmberAfterDays = 7, RedAfterDays = 14;

    /// <summary>The family groups and their report codes, in strip order.</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<string> ReportCodes)> Groups { get; } =
    [
        ("Jobs", ["S002", "S036", "S037"]),
        ("Status views", ["S014", "S015", "S016", "S017", "S018", "S031", "S032", "S033", "S034", "S035"]),
        ("Pending lists", ["S009", "S010"]),
        ("SRN", ["S011", "S012", "S013"]),
        ("Money", ["S003", "S004"]),
        ("Claims", ["S023", "S024", "S025", "S026", "S039", "S040", "S041"]),
        ("Parts", ["S006", "S007", "S008"]),
        ("Tests", ["S030"]),
        ("Deftran", ["S029"]),
    ];

    public static ServiceFreshnessColour Colour(DateOnly? latest, DateOnly asOf)
    {
        if (latest is not { } date) return ServiceFreshnessColour.NoData;
        var age = asOf.DayNumber - date.DayNumber;
        return age > RedAfterDays ? ServiceFreshnessColour.Red : age > AmberAfterDays ? ServiceFreshnessColour.Amber : ServiceFreshnessColour.Fresh;
    }

    /// <summary>
    /// One chip per group. A group's date is the oldest of its families' latest readings (so one stale family colours the
    /// chip); a family with no reading makes the group "no data". <paramref name="sourceKinds"/> gives the source kind of
    /// each family's latest reading when the caller has it (v_service_readings); the refresh log alone has none.
    /// </summary>
    public static IReadOnlyList<ServiceFreshnessChip> Build(IEnumerable<ServiceRefresh> refreshes, DateOnly asOf,
        IReadOnlyDictionary<string, string>? sourceKinds = null)
    {
        var latestByFamily = refreshes.GroupBy(r => r.ReportCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(r => r.SnapshotDate), StringComparer.OrdinalIgnoreCase);
        var chips = new List<ServiceFreshnessChip>();
        foreach (var (group, codes) in Groups)
        {
            var dates = codes.Select(code => latestByFamily.TryGetValue(code, out var date) ? date : (DateOnly?)null).ToArray();
            var oldest = dates.Any(date => date is null) ? null : dates.Min();
            var oldestCode = oldest is null ? null : codes[Array.IndexOf(dates, oldest)];
            string? kind = oldestCode is not null && sourceKinds is not null && sourceKinds.TryGetValue(oldestCode, out var k) ? k : null;
            var colour = Colour(oldest, asOf);
            var text = oldest is { } date
                ? $"last export {date:dd MMM yyyy}" + (kind is null ? "" : $" ({kind.ToLowerInvariant()})")
                : dates.All(d => d is null) ? "no export yet" : "some families never exported";
            chips.Add(new ServiceFreshnessChip(group, codes, oldest, kind, colour, text));
        }
        return chips;
    }
}
