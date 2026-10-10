namespace Etp.Reporting.Application.Service;

// Service Centre UI wave (1.10.0, decision 25): the pure rules of design section 4.3, Q2, Q4 and Q14, without I/O, so
// the unit tests pin them and SqlServerServiceReportQuery only feeds them rows. Lane sql owns this file; the UI lanes
// copy it beside ServiceUiContracts.cs.

/// <summary>Age bands, per-stage limits and the overdue rule (design 4.3, Q4).</summary>
public static class ServiceAgeing
{
    /// <summary>The bands of the Pending board filter: 0-7, 8-15, 16-30, 31-60, 60+, counted on days since booking (design 4.3, decision 25).</summary>
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
    /// in stage over the stage limit. A closed stage (DELIVERED, RWR) is never overdue, nor is a DC/RA job closed by its
    /// claim (Q3, R-SQL-05).
    /// </summary>
    public static int? OverdueBy(string stage, DateOnly? edd, int? daysInStage, DateOnly asAt, bool claimRaised = false)
    {
        if (ServiceStages.Closed.Contains(stage)) return null;
        if (claimRaised && stage is ServiceStages.DcIssued or ServiceStages.RaIssued) return null;
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

    /// <summary>
    /// Booking jobs and Quick Billing jobs apart (a job without an S002 row is Booking). <c>ClosedJobs</c> counts every closed
    /// job with a TAT; the Q2 headline (medians, average, over 15 days) is booking to DELIVERED only, so RWR jobs are left
    /// out of it (R-SQL-06).
    /// </summary>
    public static ServiceTatSummary Summarise(IEnumerable<ServiceJobSummary> jobs)
    {
        var closed = jobs.Where(job => ServiceStages.Closed.Contains(job.Stage) && job.TatDays is not null).ToArray();
        var delivered = closed.Where(job => job.Stage == ServiceStages.Delivered).ToArray();
        var booking = delivered.Where(job => !job.IsQuickBilling).ToArray();
        var quick = delivered.Where(job => job.IsQuickBilling).ToArray();
        var bookingTat = booking.Select(job => job.TatDays!.Value).ToArray();
        return new ServiceTatSummary(
            closed.Length, booking.Length, quick.Length,
            Median(bookingTat),
            Median(booking.Where(job => job.TatRepairDays is not null).Select(job => job.TatRepairDays!.Value)),
            bookingTat.Length == 0 ? null : bookingTat.Average(),
            Median(quick.Select(job => job.TatDays!.Value)),
            booking.Count(job => job.TatDays > 15));
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
    /// The default Service Today date (Q15, R-SQL-01): the latest business date, up to <paramref name="asAt"/>, on which the
    /// exports hold something Today counts (a booking, a delivery of a delivered job, an RWR of a returned job, or an S004
    /// collection). A raw pack is named by its export day but holds data to the day before, so the snapshot date itself
    /// would show zeros. Falls back to <paramref name="asAt"/> when nothing is dated.
    /// </summary>
    public static DateOnly? LatestDataDate(IEnumerable<ServiceJobSummary> jobs, DateOnly? latestS004, DateOnly? asAt)
    {
        var dates = jobs.SelectMany(job => new[]
            {
                job.BookingDate,
                job.Stage == ServiceStages.Delivered ? job.DeliveryDate : null,
                job.Stage == ServiceStages.Rwr ? job.RwrDate : null,
            })
            .Append(latestS004)
            .Where(date => date is { } d && (asAt is null || d <= asAt.Value))
            .Select(date => date!.Value)
            .ToArray();
        return dates.Length == 0 ? asAt : dates.Max();
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

    /// <summary>
    /// Q14 / R-SQL-14: families with no raw export come only in the monthly consolidated workbook (design 1.x, Q14), so they
    /// are judged against a monthly cadence: amber after a month plus the 7-day grace, red after a month plus 14 days.
    /// </summary>
    public const int MonthlyAmberAfterDays = 31 + AmberAfterDays, MonthlyRedAfterDays = 31 + RedAfterDays;

    /// <summary>The consolidated-only families (design Q14: S011-S014, S016, S019-S021, S023-S026, S030, S033, S035).</summary>
    public static IReadOnlySet<string> MonthlyFamilies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "S011", "S012", "S013", "S014", "S016", "S019", "S020", "S021", "S023", "S024", "S025", "S026", "S030", "S033", "S035" };

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

    public static ServiceFreshnessColour Colour(DateOnly? latest, DateOnly asOf, bool monthly = false)
    {
        if (latest is not { } date) return ServiceFreshnessColour.NoData;
        var age = asOf.DayNumber - date.DayNumber;
        var (amber, red) = monthly ? (MonthlyAmberAfterDays, MonthlyRedAfterDays) : (AmberAfterDays, RedAfterDays);
        return age > red ? ServiceFreshnessColour.Red : age > amber ? ServiceFreshnessColour.Amber : ServiceFreshnessColour.Fresh;
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
            // Each family is judged by its own cadence (daily raw or monthly consolidated); the chip takes the worst.
            var colour = oldest is null ? ServiceFreshnessColour.NoData
                : codes.Select(code => Colour(latestByFamily[code], asOf, MonthlyFamilies.Contains(code))).Max();
            var text = oldest is { } date
                ? "last export " + date.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + (kind is null ? "" : $" ({kind.ToLowerInvariant()})")
                : dates.All(d => d is null) ? "no export yet" : "some families never exported";
            chips.Add(new ServiceFreshnessChip(group, codes, oldest, kind, colour, text));
        }
        return chips;
    }
}

/// <summary>The Claims screen composed from v_service_claims lines and v_service_job rows (design 3.5, Q9 = A: raised only).</summary>
public static class ServiceClaimRules
{
    /// <summary>Per month and claim type: distinct documents, lines, distinct jobs, net incl. tax and UCP value; newest month first.</summary>
    public static IReadOnlyList<ServiceClaimsSummaryRow> Summarise(IEnumerable<ServiceClaimLine> lines) =>
        lines.GroupBy(line => (line.ClaimMonth, line.ClaimType))
            .Select(g => new ServiceClaimsSummaryRow(g.Key.ClaimMonth, g.Key.ClaimType,
                g.Where(l => l.DocumentNumber is not null).Select(l => l.DocumentNumber!).Distinct(StringComparer.Ordinal).Count(),
                g.Count(),
                g.Where(l => l.JobOrderNumber is not null).Select(l => l.JobOrderNumber!).Distinct(StringComparer.Ordinal).Count(),
                g.Sum(l => l.NetAmountInclTax ?? 0m), g.Sum(l => l.UcpValue ?? 0m)))
            .OrderByDescending(row => row.ClaimMonth).ThenBy(row => Array.IndexOf(ServiceClaimTypes.All.ToArray(), row.ClaimType))
            .ToArray();

    /// <summary>DC/RA jobs without a WDC/WRA claim document ("not yet claimed"), oldest first; days since the DC/RA date to as-at.</summary>
    public static IReadOnlyList<ServiceUnclaimedJob> NotYetClaimed(IEnumerable<ServiceJobSummary> jobs) =>
        jobs.Where(job => job.Stage is ServiceStages.DcIssued or ServiceStages.RaIssued && !job.ClaimRaised)
            .Select(job => new ServiceUnclaimedJob(job.JobOrderNumber, job.Stage,
                job.Stage == ServiceStages.DcIssued ? ServiceClaimTypes.Wdc : ServiceClaimTypes.Wra, job.StageDate,
                job.StageDate is { } date ? job.AsAt.DayNumber - date.DayNumber : null, job.Brand, job.Model,
                job.Stage == ServiceStages.DcIssued ? job.WdcNumber : job.RadcNumber))
            .OrderByDescending(job => job.DaysSince ?? -1).ThenBy(job => job.JobOrderNumber, StringComparer.Ordinal).ToArray();

    /// <summary>The GPRC gap warning (design 3.5, Q11): the latest GPRC reading is older than the latest DC/RA status reading.</summary>
    public static bool GprcGap(DateOnly? latestGprc, DateOnly? latestDcRa) =>
        latestDcRa is { } dcRa && (latestGprc is null || latestGprc < dcRa);
}

/// <summary>One row of dbo.v_service_parts (an invoice line).</summary>
public sealed record ServicePartsRow(
    string InvoiceNumber, DateOnly? InvoiceDate, string? ItemId, decimal? ShippedQuantity, decimal? ReceivedQuantity, decimal? NetAmount,
    string? GrnNumber, DateOnly? GrnDate, DateOnly? ReceivedDate, string Status, string? FromLocation, int? DaysOpen, DateOnly SnapshotDate);

/// <summary>The Parts screen composed from v_service_parts lines (design 3.6).</summary>
public static class ServicePartsRules
{
    /// <summary>
    /// Lines grouped per invoice. An invoice is Open while any line is open; days open is the greatest over its lines.
    /// Order: open first, then oldest invoice date first.
    /// </summary>
    public static IReadOnlyList<ServicePartsInvoice> Invoices(IEnumerable<ServicePartsRow> rows) =>
        rows.GroupBy(row => row.InvoiceNumber, StringComparer.Ordinal)
            .Select(g =>
            {
                var open = g.Any(row => row.Status == "Open");
                return new ServicePartsInvoice(g.Key, g.Min(row => row.InvoiceDate), g.Select(row => row.GrnNumber).FirstOrDefault(n => n is not null),
                    g.Max(row => row.GrnDate), g.Max(row => row.ReceivedDate), g.Count(), Sum(g.Select(row => row.ShippedQuantity)),
                    Sum(g.Select(row => row.ReceivedQuantity)), Sum(g.Select(row => row.NetAmount)), open ? "Open" : "Received",
                    g.Max(row => row.DaysOpen), g.Select(row => row.FromLocation).FirstOrDefault(n => n is not null), g.Max(row => row.SnapshotDate),
                    g.Select(row => new ServicePartsLine(row.ItemId, row.ShippedQuantity, row.ReceivedQuantity, row.NetAmount, row.GrnNumber, row.GrnDate)).ToArray());
            })
            .OrderByDescending(invoice => invoice.IsOpen).ThenBy(invoice => invoice.InvoiceDate ?? DateOnly.MaxValue)
            .ThenBy(invoice => invoice.InvoiceNumber, StringComparer.Ordinal).ToArray();

    public static ServiceParts Build(IEnumerable<ServicePartsRow> rows, IEnumerable<ServiceGitLine> git, ServiceStockSummary? stock,
        IEnumerable<ServiceJobSummary> jobs, DateOnly? asAt)
    {
        var invoices = Invoices(rows);
        var open = invoices.Where(invoice => invoice.IsOpen).ToArray();
        var gitLines = git.OrderByDescending(line => line.BusinessDate).ToArray();
        var waiting = jobs.Where(job => job.IsOpen && job.Stage == ServiceStages.IndentRaised)
            .Select(job => new ServiceWaitingJob(job.JobOrderNumber, job.SpareRequired, job.IndentDate,
                job.IndentDate is { } d ? job.AsAt.DayNumber - d.DayNumber : null, job.Brand, job.Model, job.PendingAt))
            .OrderByDescending(job => job.DaysWaiting ?? -1).ThenBy(job => job.JobOrderNumber, StringComparer.Ordinal).ToArray();
        var month = asAt is { } at ? new DateOnly(at.Year, at.Month, 1) : (DateOnly?)null;
        return new ServiceParts(invoices, gitLines, stock, waiting, open.Length, open.Sum(invoice => invoice.NetAmount ?? 0m),
            open.Length == 0 ? null : open.Max(invoice => invoice.DaysOpen),
            month is { } m ? invoices.Count(invoice => !invoice.IsOpen && invoice.GrnDate >= m && invoice.GrnDate <= asAt) : 0,
            waiting.Length, asAt is { } a ? gitLines.Count(line => line.BusinessDate > a.AddDays(-30) && line.BusinessDate <= a) : 0, asAt);
    }

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        var known = values.Where(value => value is not null).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }
}

/// <summary>Projection of a v_service_job row onto lane history's header record (ServiceJobContracts.cs).</summary>
public static class ServiceJobProjection
{
    public static ServiceJobHeader Header(ServiceJobSummary job) => new(job.JobOrderNumber, job.BookingDate, job.JoType, job.ExportedStatus,
        job.Brand, job.Model, job.ProductCategory, job.Guarantee, job.CustomerType, job.CustomerName, job.Edd, job.Stage, job.StageDate,
        job.PendingAt, job.SpareRequired, job.ClaimRaised, job.SpareValue, job.LabourCharge, job.TatDays, job.AgeDays, job.DaysInStage,
        job.OverdueBy is > 0, job.AsAt);
}

/// <summary>
/// The Service job key (design 1.3, Q1): the exported job order number, trimmed, never padded or re-formatted. On live every
/// key is <c>JOAW330</c> plus 8 digits (FY 2024-25 numbering) or 9 digits (FY code plus a 5-digit sequence). A key of another
/// shape is still a key (it is just listed); this check is for counts and warnings, never for dropping a row.
/// </summary>
public static class ServiceJobKey
{
    public const string Pattern = "^JOAW330[0-9]{8,9}$";

    public static string? Normalise(string? exported) => string.IsNullOrWhiteSpace(exported) ? null : exported.Trim();

    public static bool IsWellFormed(string? key) =>
        Normalise(key) is { } k && System.Text.RegularExpressions.Regex.IsMatch(k, Pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
