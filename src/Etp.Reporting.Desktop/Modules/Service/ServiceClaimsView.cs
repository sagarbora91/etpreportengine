extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ServiceClaimLine = EtpApplication::Etp.Reporting.Application.Service.ServiceClaimLine;
using ServiceClaimTypes = EtpApplication::Etp.Reporting.Application.Service.ServiceClaimTypes;
using ServiceRefresh = EtpApplication::Etp.Reporting.Application.Service.ServiceRefresh;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;
using ServiceUnclaimedJob = EtpApplication::Etp.Reporting.Application.Service.ServiceUnclaimedJob;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>One of the numbers at the top of the Claims screen (what its KpiCard shows).</summary>
public sealed record ServiceClaimsNumber(string Label, string Value, string Secondary);

/// <summary>The GPRC gap check (design Q11): is the latest GPRC CLAIM export older than the latest DC/RA list?</summary>
public sealed record ServiceGprcGap(DateOnly? LatestGprcReading, DateOnly? LatestDcRaReading, bool IsBehind);

/// <summary>
/// Service claims (1.10.0 design 3.5): what was claimed from Titan per month and claim type, the lines of each
/// document, and the DC/RA jobs that have no claim document yet. Everything is "raised" (decision 25, Q9 = A): no
/// Service export says whether Titan settled a claim, so the screen never shows a settlement state.
/// The grid shows one of three tables (summary, claim lines, not yet claimed); Export writes the table shown.
/// </summary>
public sealed class ServiceClaimsView : ServiceScreenView
{
    public const string SummaryMode = "By month and claim type";
    public const string DetailMode = "Claim lines by document";
    public const string NotYetClaimedMode = "Not yet claimed (DC/RA jobs)";
    public static IReadOnlyList<string> Modes { get; } = [SummaryMode, DetailMode, NotYetClaimedMode];

    /// <summary>The note under the title (Q9 = A): the screen shows claims raised and nothing about settlement.</summary>
    public const string SettlementNote =
        "Claims raised with Titan only: no Service export carries whether a claim was settled or paid, so this screen cannot show that. " +
        "The number to act on is the DC/RA jobs with no claim document yet.";

    public static IReadOnlyList<ServiceListChoice> ClaimTypes { get; } =
        [new(null, "All claim types"), .. ServiceClaimTypes.All.Select(code => new ServiceListChoice(code, ServiceClaimTypes.Label(code)))];

    private static readonly IReadOnlyList<ServiceColumn> SummaryColumns =
    [
        new("Month", DisplayFormat: "MMM yyyy", Width: 100), new("Claim type", Width: 120), new("Documents", "#,##0", Width: 100),
        new("Lines", "#,##0", Width: 80), new("Jobs", "#,##0", Width: 80), new("Net incl. tax", "#,##0.00", "N2", 140), new("UCP value", "#,##0.00", "N2", 130)
    ];

    private static readonly IReadOnlyList<ServiceColumn> DetailColumns =
    [
        new("Date", DisplayFormat: "dd MMM yyyy"), new("Document number", Width: 150), new("Job number", Width: 150), new("Item", Width: 140),
        new("Quantity", "#,##0.##", "N0", 90), new("Net incl. tax", "#,##0.00", "N2", 130), new("Source family", Width: 110), new("Claim type", Width: 110)
    ];

    private static readonly IReadOnlyList<ServiceColumn> NotYetClaimedColumns =
    [
        new("Job number", Width: 150), new("Issued as", Width: 90), new("Issued on", DisplayFormat: "dd MMM yyyy"), new("Days since issued", "#,##0", Width: 130),
        new("Claim type due", Width: 120), new("DC/RA number", Width: 150), new("Brand"), new("Model", Width: 150), new("As at", DisplayFormat: "dd MMM yyyy")
    ];

    private readonly ComboBox typeFilter = new() { MinWidth = 180, MinHeight = 44, ItemsSource = ClaimTypes, SelectedIndex = 0 };
    private readonly ComboBox modeFilter = new() { MinWidth = 240, MinHeight = 44, ItemsSource = Modes, SelectedIndex = 0 };
    private readonly DatePicker fromMonth = new() { MinHeight = 44, MinWidth = 140 };
    private readonly DatePicker toMonth = new() { MinHeight = 44, MinWidth = 140 };
    private readonly WrapPanel numbers = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock gapWarning = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 6), Visibility = Visibility.Collapsed };
    private readonly TextBlock drillStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly ServiceTaskNavigation? navigate;
    private IReadOnlyList<ServiceClaimLine> lines = [];
    private IReadOnlyList<ServiceUnclaimedJob> notYetClaimed = [];
    private string shownMode = SummaryMode;

    public ServiceClaimsView(Func<ServiceReportQuery> query, ServiceExcelExport export, ServiceTaskNavigation? navigate = null)
        : base("Service claims",
            "Claims raised with Titan from the claim logs (GPRC cell, Module Bank, WDC, WRA), by month and claim type, with the lines of each document. " +
            "A DC or RA job with no claim document is a claim not yet raised.",
            "Service.Claims", "SERVICE_CLAIMS_LOAD_FAILED", query, export)
    {
        this.navigate = navigate;

        // The numbers and the GPRC note sit between the intro and the filters, inside the frame's heading.
        var heading = (Panel)FilterBar.Parent;
        var at = heading.Children.IndexOf(FilterBar);
        heading.Children.Insert(at, new TextBlock { Text = SettlementNote, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        AutomationProperties.SetName(numbers, "Claims numbers");
        heading.Children.Insert(at + 1, numbers);
        AutomationProperties.SetName(gapWarning, "GPRC gap warning");
        heading.Children.Insert(at + 2, gapWarning);

        FilterBar.Children.Add(new TextBlock { Text = "Show", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        AutomationProperties.SetName(modeFilter, "Claims table");
        modeFilter.SelectionChanged += async (_, _) => { if (IsLoaded) await ActivateAsync(); };
        FilterBar.Children.Add(modeFilter);
        FilterBar.Children.Add(new TextBlock { Text = "Claim type", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) });
        AutomationProperties.SetName(typeFilter, "Claim type");
        typeFilter.SelectionChanged += async (_, _) => { if (IsLoaded) await ActivateAsync(); };
        FilterBar.Children.Add(typeFilter);
        AutomationProperties.SetName(fromMonth, "From month");
        AutomationProperties.SetName(toMonth, "To month");
        FilterBar.Children.Add(new TextBlock { Text = "From month", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) });
        FilterBar.Children.Add(fromMonth);
        FilterBar.Children.Add(new TextBlock { Text = "To month", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) });
        FilterBar.Children.Add(toMonth);
        var show = new Button { Content = "Show", MinHeight = 44, Margin = new Thickness(8, 0, 0, 0) };
        show.Click += async (_, _) => await ActivateAsync();
        FilterBar.Children.Add(show);

        Table.MouseDoubleClick += (_, _) => { if (Table.SelectedItem is ServiceGridRow row) DrillDown(row); };
        Table.KeyDown += (_, args) => { if (args.Key == Key.Enter && Table.SelectedItem is ServiceGridRow row) { args.Handled = true; DrillDown(row); } };
        Footer.Children.Add(new TextBlock
        {
            Text = "Open a month row to see its claim lines; open a line or a job row to see the job's history.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        });
        Footer.Children.Add(drillStatus);
        SetColumns(SummaryColumns);
    }

    public string Mode
    {
        get => (string)modeFilter.SelectedItem;
        set => modeFilter.SelectedItem = Modes.Single(mode => mode == value);
    }

    public ServiceListChoice SelectedClaimType
    {
        get => (ServiceListChoice)typeFilter.SelectedItem;
        set => typeFilter.SelectedItem = ClaimTypes.Single(choice => choice.Code == value.Code);
    }

    /// <summary>The first month shown, or null for no lower bound. Any day of the month selects the whole month.</summary>
    public DateOnly? FromMonth
    {
        get => fromMonth.SelectedDate is { } date ? FirstOfMonth(DateOnly.FromDateTime(date)) : null;
        set => fromMonth.SelectedDate = value?.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>The last month shown, or null for no upper bound.</summary>
    public DateOnly? ToMonth
    {
        get => toMonth.SelectedDate is { } date ? FirstOfMonth(DateOnly.FromDateTime(date)) : null;
        set => toMonth.SelectedDate = value?.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>The numbers at the top of the screen, in the order their cards appear.</summary>
    public IReadOnlyList<ServiceClaimsNumber> Numbers { get; private set; } = [];

    /// <summary>The GPRC gap warning, or "" when the GPRC CLAIM export is not behind the DC/RA lists.</summary>
    public string GapWarningText => gapWarning.Visibility == Visibility.Visible ? gapWarning.Text : "";

    /// <summary>What the last drill-down did, or asked the user to do.</summary>
    public string DrillDownText => drillStatus.Text;

    /// <summary>The table the last load showed.</summary>
    public string ShownMode => shownMode;

    protected override string? PrepareLoad() =>
        FromMonth is { } from && ToMonth is { } to && from > to ? "The From month is after the To month. Choose a valid range." : null;

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source)
    {
        var claims = await source.LoadClaimsAsync();
        lines = claims.Lines;
        notYetClaimed = claims.NotYetClaimed;
        shownMode = Mode;
        drillStatus.Text = "";
        SetColumns(shownMode switch { DetailMode => DetailColumns, NotYetClaimedMode => NotYetClaimedColumns, _ => SummaryColumns });
        RenderNumbers(null);
        var type = SelectedClaimType.Code;
        return shownMode switch
        {
            DetailMode => DetailRows(FilteredLines()),
            NotYetClaimedMode => NotYetClaimedRows(notYetClaimed.Where(job => type is null || job.ClaimType == type)),
            _ => SummaryRows(FilteredLines())
        };
    }

    protected override async Task LoadExtrasAsync(ServiceReportQuery source, Func<bool> isCurrent)
    {
        var refreshes = await source.LoadRefreshesAsync();
        if (!isCurrent()) return;
        RenderNumbers(refreshes);
    }

    protected override void ClearExtras()
    {
        lines = []; notYetClaimed = []; Numbers = []; numbers.Children.Clear();
        gapWarning.Visibility = Visibility.Collapsed; gapWarning.Text = ""; drillStatus.Text = "";
    }

    private IEnumerable<ServiceClaimLine> FilteredLines()
    {
        var type = SelectedClaimType.Code;
        var from = FromMonth;
        var to = ToMonth is { } last ? last.AddMonths(1) : (DateOnly?)null;
        return lines.Where(line => (type is null || line.ClaimType == type)
            && (from is null || line.ClaimDate >= from) && (to is null || line.ClaimDate < to));
    }

    /// <summary>One row per month and claim type, month descending: documents, lines, jobs, net incl. tax, UCP value.</summary>
    public static IReadOnlyList<ServiceGridRow> SummaryRows(IEnumerable<ServiceClaimLine> lines) =>
        lines.GroupBy(line => (Month: FirstOfMonth(line.ClaimDate), line.ClaimType))
            .OrderByDescending(group => group.Key.Month).ThenBy(group => TypeOrder(group.Key.ClaimType))
            .Select(group => new ServiceGridRow(group.ToArray(),
            [
                group.Key.Month, ServiceClaimTypes.Label(group.Key.ClaimType),
                group.Select(line => line.DocumentNumber).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                group.Count(),
                group.Select(line => line.JobOrderNumber).Where(job => !string.IsNullOrWhiteSpace(job)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                group.Sum(line => line.NetAmountIncTax ?? 0m), group.Sum(line => line.UcpValue ?? 0m)
            ])).ToArray();

    /// <summary>Every line, newest document first, the lines of one document together.</summary>
    public static IReadOnlyList<ServiceGridRow> DetailRows(IEnumerable<ServiceClaimLine> lines) =>
        lines.OrderByDescending(line => line.ClaimDate).ThenBy(line => line.DocumentNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.JobOrderNumber, StringComparer.OrdinalIgnoreCase).ThenBy(line => line.ItemId, StringComparer.OrdinalIgnoreCase)
            .Select(line => new ServiceGridRow(line,
            [
                line.ClaimDate, line.DocumentNumber, line.JobOrderNumber, line.ItemId, line.Quantity, line.NetAmountIncTax,
                line.ReportCode, ServiceClaimTypes.Label(line.ClaimType)
            ])).ToArray();

    /// <summary>DC/RA jobs without a claim document, longest waiting first; jobs with no DC/RA date last.</summary>
    public static IReadOnlyList<ServiceGridRow> NotYetClaimedRows(IEnumerable<ServiceUnclaimedJob> jobs) =>
        jobs.Select(job => (Job: job, Days: DaysSinceIssued(job)))
            .OrderByDescending(item => item.Days.HasValue).ThenByDescending(item => item.Days)
            .ThenBy(item => item.Job.JobOrderNumber, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ServiceGridRow(item.Job,
            [
                item.Job.JobOrderNumber, item.Job.StatusLabel, item.Job.IssuedDate, item.Days, ServiceClaimTypes.Label(item.Job.ClaimType),
                item.Job.IssuedReference, item.Job.Brand, item.Job.Model, item.Job.SnapshotDate
            ])).ToArray();

    /// <summary>Days from the DC/RA date to the list's reading date (as the pending lists count age), or null without a date.</summary>
    public static int? DaysSinceIssued(ServiceUnclaimedJob job) =>
        job.IssuedDate is { } issued ? Math.Max(0, job.SnapshotDate.DayNumber - issued.DayNumber) : null;

    /// <summary>
    /// Q11: the GPRC CLAIM export (S041) is behind when its latest reading is older than the latest DC (S014) or RA (S016)
    /// list, or missing while a DC/RA list exists. Then the GPRC cell claims of the gap are in no table.
    /// </summary>
    public static ServiceGprcGap GprcGap(IReadOnlyList<ServiceRefresh> refreshes)
    {
        DateOnly? latest(Func<string, bool> family) =>
            refreshes.Where(refresh => family(refresh.ReportCode)).Select(refresh => (DateOnly?)refresh.SnapshotDate).Max();
        var gprc = latest(code => code == "S041");
        var dcRa = latest(code => code is "S014" or "S016");
        return new(gprc, dcRa, dcRa is { } lists && (gprc is null || gprc < lists));
    }

    private void RenderNumbers(IReadOnlyList<ServiceRefresh>? refreshes)
    {
        var latestReading = lines.Count == 0 ? (DateOnly?)null : lines.Max(line => line.SnapshotDate);
        var month = latestReading is { } reading ? FirstOfMonth(reading) : (DateOnly?)null;
        var thisMonth = month is { } first ? lines.Where(line => FirstOfMonth(line.ClaimDate) == first).ToArray() : [];
        var waiting = notYetClaimed.Select(job => (Job: job, Days: DaysSinceIssued(job))).ToArray();
        var oldest = waiting.Where(item => item.Days.HasValue).OrderByDescending(item => item.Days).FirstOrDefault();
        var list = new List<ServiceClaimsNumber>
        {
            new("Claims raised this month",
                thisMonth.Select(line => line.DocumentNumber).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0"),
                month is { } shown
                    ? $"{thisMonth.Sum(line => line.NetAmountIncTax ?? 0m):N2} net incl. tax · {shown:MMM yyyy} (latest claims reading)"
                    : "No claim lines imported"),
            new("DC/RA jobs not yet claimed", notYetClaimed.Count.ToString("N0"),
                $"WDC due {notYetClaimed.Count(job => job.ClaimType == ServiceClaimTypes.Wdc):N0} · WRA due {notYetClaimed.Count(job => job.ClaimType == ServiceClaimTypes.Wra):N0}"),
            new("Oldest not yet claimed", oldest.Job is null ? "—" : $"{oldest.Days:N0} days",
                oldest.Job is null ? "No DC/RA job is waiting for a claim" : $"{oldest.Job.JobOrderNumber} · {oldest.Job.StatusLabel} on {oldest.Job.IssuedDate:dd MMM yyyy}")
        };
        if (refreshes is not null)
        {
            var gap = GprcGap(refreshes);
            list.Add(new("GPRC claim export",
                gap.LatestGprcReading is { } gprc ? gprc.ToString("dd MMM yyyy") : "none",
                gap.LatestDcRaReading is { } dcRa ? $"DC/RA lists to {dcRa:dd MMM yyyy}" : "No DC/RA list imported"));
            gapWarning.Text = gap.IsBehind
                ? $"GPRC cell claims may be incomplete: the latest GPRC CLAIM export (S041) is {(gap.LatestGprcReading is { } g ? $"dated {g:dd MMM yyyy}" : "missing")} " +
                  $"and the latest DC/RA list (S014/S016) is dated {gap.LatestDcRaReading:dd MMM yyyy}. Export GPRC CLAIM up to that date and import it; " +
                  "until then the GPRC claims of the gap are in no table."
                : "";
            gapWarning.Visibility = gap.IsBehind ? Visibility.Visible : Visibility.Collapsed;
        }
        Numbers = list;
        numbers.Children.Clear();
        foreach (var number in list)
            numbers.Children.Add(new KpiCard(number.Label, number.Value, number.Secondary,
                number.Label == "DC/RA jobs not yet claimed" && notYetClaimed.Count > 0 ? "Warning"
                : number.Label == "GPRC claim export" && gapWarning.Visibility == Visibility.Visible ? "Critical"
                : "Accent"));
    }

    /// <summary>
    /// A month row opens its claim lines (month and type become the filters); a claim line or a not-yet-claimed job
    /// opens the job's history through <see cref="ServiceScreens.Navigate"/>.
    /// </summary>
    public void DrillDown(ServiceGridRow row)
    {
        if (row.Source is ServiceClaimLine[] group && group.Length > 0)
        {
            var month = FirstOfMonth(group[0].ClaimDate);
            FromMonth = month; ToMonth = month;
            SelectedClaimType = ClaimTypes.Single(choice => choice.Code == group[0].ClaimType);
            Mode = DetailMode;
            _ = ActivateAsync();
            return;
        }
        var job = row.Source switch
        {
            ServiceClaimLine line => line.JobOrderNumber,
            ServiceUnclaimedJob waiting => waiting.JobOrderNumber,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(job)) { drillStatus.Text = "This claim line carries no job number."; return; }
        OpenJobHistory(job.Trim());
    }

    public void OpenJobHistory(string jobOrderNumber)
    {
        if (navigate is null)
        {
            drillStatus.Text = $"Open Service job history and enter job {jobOrderNumber}.";
            return;
        }
        drillStatus.Text = $"Opening Service job history for job {jobOrderNumber}.";
        navigate(ServiceScreens.JobHistoryTask, jobOrderNumber);
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static int TypeOrder(string claimType) =>
        ServiceClaimTypes.All.Select((code, index) => (code, index)).Where(item => item.code == claimType).Select(item => item.index).DefaultIfEmpty(int.MaxValue).First();

    protected override string EmptyRowsText => shownMode == NotYetClaimedMode
        ? "Every DC/RA job in this choice has a claim document."
        : "No claims raised for this choice.";

    protected override string Summarise(int count) => shownMode switch
    {
        DetailMode => $"{count:N0} claim lines · {SelectedClaimType.Label} · claims raised only.",
        NotYetClaimedMode => $"{count:N0} DC/RA jobs with no claim document yet · longest waiting first.",
        _ => $"{count:N0} month and claim type rows · {SelectedClaimType.Label} · claims raised only."
    };

    protected override string ExportName => shownMode switch
    {
        DetailMode => "Service claim lines",
        NotYetClaimedMode => "Service claims not yet raised",
        _ => "Service claims by month"
    };

    protected override (DateOnly From, DateOnly To) ExportPeriod
    {
        get
        {
            var dates = shownMode == NotYetClaimedMode
                ? Rows.Select(row => row.Source).OfType<ServiceUnclaimedJob>().Select(job => job.IssuedDate ?? job.SnapshotDate).ToArray()
                : FilteredLines().Select(line => line.ClaimDate).ToArray();
            if (dates.Length == 0) return base.ExportPeriod;
            return (FromMonth ?? dates.Min(), ToMonth is { } to ? to.AddMonths(1).AddDays(-1) : dates.Max());
        }
    }
}
