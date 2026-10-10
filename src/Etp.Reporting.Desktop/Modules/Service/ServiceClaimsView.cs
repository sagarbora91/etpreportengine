extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ServiceClaimLine = EtpApplication::Etp.Reporting.Application.Service.ServiceClaimLine;
using ServiceClaims = EtpApplication::Etp.Reporting.Application.Service.ServiceClaims;
using ServiceClaimsSummaryRow = EtpApplication::Etp.Reporting.Application.Service.ServiceClaimsSummaryRow;
using ServiceClaimTypes = EtpApplication::Etp.Reporting.Application.Service.ServiceClaimTypes;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;
using ServiceStages = EtpApplication::Etp.Reporting.Application.Service.ServiceStages;
using ServiceUnclaimedJob = EtpApplication::Etp.Reporting.Application.Service.ServiceUnclaimedJob;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>One of the numbers at the top of the Claims screen (what its KpiCard shows).</summary>
public sealed record ServiceClaimsNumber(string Label, string Value, string Secondary);

/// <summary>
/// Service claims (1.10.0 design 3.5): what was claimed from Titan per month and claim type, the lines of each
/// document, and the DC/RA jobs that have no claim document yet. Everything is "raised" (decision 25, Q9 = A): no
/// Service export says whether Titan settled a claim, so the screen never shows a settlement state.
/// The grid shows one of three tables (summary, claim lines, not yet claimed); Export writes the table shown.
/// The month range goes to the query; the claim type is applied here.
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
        new("Month", DisplayFormat: "MMM yyyy", Width: 100), new("Claim type", Width: 150), new("Documents", "#,##0", Width: 100),
        new("Lines", "#,##0", Width: 80), new("Jobs", "#,##0", Width: 80), new("Net incl. tax", "#,##0.00", "N2", 140), new("UCP value", "#,##0.00", "N2", 130)
    ];

    private static readonly IReadOnlyList<ServiceColumn> DetailColumns =
    [
        new("Date", DisplayFormat: "dd MMM yyyy"), new("Document number", Width: 150), new("Job number", Width: 150), new("Item", Width: 140),
        new("Quantity", "#,##0.##", "N0", 90), new("Net incl. tax", "#,##0.00", "N2", 130), new("Source family", Width: 110), new("Claim type", Width: 150)
    ];

    private static readonly IReadOnlyList<ServiceColumn> NotYetClaimedColumns =
    [
        new("Job number", Width: 150), new("Stage", Width: 110), new("Issued on", DisplayFormat: "dd MMM yyyy"), new("Days since issued", "#,##0", Width: 130),
        new("Claim type due", Width: 150), new("DC/RA number", Width: 150), new("Brand"), new("Model", Width: 150)
    ];

    private readonly ComboBox typeFilter = new() { MinWidth = 180, MinHeight = 44, ItemsSource = ClaimTypes, SelectedIndex = 0 };
    private readonly ComboBox modeFilter = new() { MinWidth = 240, MinHeight = 44, ItemsSource = Modes, SelectedIndex = 0 };
    private readonly DatePicker fromMonth = new() { MinHeight = 44, MinWidth = 140 };
    private readonly DatePicker toMonth = new() { MinHeight = 44, MinWidth = 140 };
    private readonly WrapPanel numbers = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock gapWarning = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 6), Visibility = Visibility.Collapsed };
    private readonly TextBlock drillStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Action<string>? openJob;
    private ServiceClaims loaded = new([], [], [], false, null, null, null);
    private string shownMode = SummaryMode;

    public ServiceClaimsView(Func<ServiceReportQuery> query, ServiceExcelExport export, Action<string>? openJob = null)
        : base("Service claims",
            "Claims raised with Titan from the claim logs (GPRC cell, Module Bank, WDC, WRA), by month and claim type, with the lines of each document. " +
            "A DC or RA job with no claim document is a claim not yet raised.",
            "Service.Claims", "SERVICE_CLAIMS_LOAD_FAILED", query, export)
    {
        this.openJob = openJob;

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

        Table.MouseDoubleClick += (_, args) => { if (IsOnRow(args.OriginalSource) && Table.SelectedItem is ServiceGridRow row) DrillDown(row); };
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
        set => typeFilter.SelectedItem = ClaimTypes.FirstOrDefault(choice => choice.Code == value.Code) ?? ClaimTypes[0];
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

    /// <summary>The business-date range the query is asked for: the From month's first day to the To month's last day.</summary>
    public (DateOnly? From, DateOnly? To) QueryRange => (FromMonth, ToMonth is { } to ? to.AddMonths(1).AddDays(-1) : null);

    /// <summary>The numbers at the top of the screen, in the order their cards appear.</summary>
    public IReadOnlyList<ServiceClaimsNumber> Numbers { get; private set; } = [];

    /// <summary>The GPRC gap warning (Q11), or "" when the GPRC CLAIM export is not behind the DC/RA lists.</summary>
    public string GapWarningText => gapWarning.Visibility == Visibility.Visible ? gapWarning.Text : "";

    /// <summary>What the last drill-down did, or asked the user to do.</summary>
    public string DrillDownText => drillStatus.Text;

    /// <summary>The table the last load showed.</summary>
    public string ShownMode => shownMode;

    protected override string? PrepareLoad() =>
        FromMonth is { } from && ToMonth is { } to && from > to ? "The From month is after the To month. Choose a valid range." : null;

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source)
    {
        var (from, to) = QueryRange;
        loaded = await source.LoadClaimsAsync(from, to);
        shownMode = Mode;
        drillStatus.Text = "";
        SetColumns(shownMode switch { DetailMode => DetailColumns, NotYetClaimedMode => NotYetClaimedColumns, _ => SummaryColumns });
        RenderNumbers();
        var type = SelectedClaimType.Code;
        return shownMode switch
        {
            DetailMode => DetailRows(loaded.Lines.Where(line => type is null || line.ClaimType == type)),
            NotYetClaimedMode => NotYetClaimedRows(loaded.NotYetClaimed.Where(job => type is null || job.ClaimType == type)),
            _ => SummaryRows(loaded.Summary.Where(row => type is null || row.ClaimType == type))
        };
    }

    protected override void ClearExtras()
    {
        loaded = new([], [], [], false, null, null, null); Numbers = []; numbers.Children.Clear();
        gapWarning.Visibility = Visibility.Collapsed; gapWarning.Text = ""; drillStatus.Text = "";
    }

    /// <summary>The query's month x type rows, newest month first, claim types in <see cref="ServiceClaimTypes.All"/> order.</summary>
    public static IReadOnlyList<ServiceGridRow> SummaryRows(IEnumerable<ServiceClaimsSummaryRow> summary) =>
        summary.OrderByDescending(row => row.ClaimMonth).ThenBy(row => TypeOrder(row.ClaimType))
            .Select(row => new ServiceGridRow(row,
            [
                row.ClaimMonth, ServiceClaimTypes.Label(row.ClaimType), row.Documents, row.Lines, row.Jobs, row.NetAmountInclTax, row.UcpValue
            ])).ToArray();

    /// <summary>Every line, newest document first, the lines of one document together.</summary>
    public static IReadOnlyList<ServiceGridRow> DetailRows(IEnumerable<ServiceClaimLine> lines) =>
        lines.OrderByDescending(line => line.BusinessDate).ThenBy(line => line.DocumentNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.JobOrderNumber, StringComparer.OrdinalIgnoreCase).ThenBy(line => line.ItemId, StringComparer.OrdinalIgnoreCase)
            .Select(line => new ServiceGridRow(line,
            [
                line.BusinessDate, line.DocumentNumber, line.JobOrderNumber, line.ItemId, line.Quantity, line.NetAmountInclTax,
                line.ReportCode, ServiceClaimTypes.Label(line.ClaimType)
            ])).ToArray();

    /// <summary>DC/RA jobs without a claim document, longest waiting first; jobs with no DC/RA date last.</summary>
    public static IReadOnlyList<ServiceGridRow> NotYetClaimedRows(IEnumerable<ServiceUnclaimedJob> jobs) =>
        jobs.OrderByDescending(job => job.DaysSince.HasValue).ThenByDescending(job => job.DaysSince)
            .ThenBy(job => job.JobOrderNumber, StringComparer.OrdinalIgnoreCase)
            .Select(job => new ServiceGridRow(job,
            [
                job.JobOrderNumber, ServiceStages.Label(job.Stage), job.StageDate, job.DaysSince, ServiceClaimTypes.Label(job.ClaimType),
                job.DcNumber, job.Brand, job.Model
            ])).ToArray();

    /// <summary>The month "claims raised this month" counts: the latest Service snapshot's month (Q15), else the latest claim's.</summary>
    public static DateOnly? ReferenceMonth(ServiceClaims claims) =>
        claims.AsAt is { } asAt ? FirstOfMonth(asAt)
        : claims.Lines.Count == 0 ? null
        : claims.Lines.Max(line => line.ClaimMonth);

    private void RenderNumbers()
    {
        var month = ReferenceMonth(loaded);
        var thisMonth = month is { } first ? loaded.Summary.Where(row => row.ClaimMonth == first).ToArray() : [];
        var oldest = loaded.NotYetClaimed.Where(job => job.DaysSince.HasValue).OrderByDescending(job => job.DaysSince).FirstOrDefault();
        var ranged = FromMonth is not null || ToMonth is not null;
        var list = new List<ServiceClaimsNumber>
        {
            new("Claims raised this month", thisMonth.Sum(row => row.Documents).ToString("N0"),
                month is { } shown
                    ? $"{thisMonth.Sum(row => row.NetAmountInclTax):N2} net incl. tax · {shown:MMM yyyy}{(ranged ? " · within the months shown" : "")}"
                    : "No claim lines imported"),
            new("DC/RA jobs not yet claimed", loaded.NotYetClaimed.Count.ToString("N0"),
                $"WDC due {loaded.NotYetClaimed.Count(job => job.ClaimType == ServiceClaimTypes.Wdc):N0} · WRA due {loaded.NotYetClaimed.Count(job => job.ClaimType == ServiceClaimTypes.Wra):N0}"),
            new("Oldest not yet claimed", oldest is null ? "—" : $"{oldest.DaysSince:N0} days",
                oldest is null ? "No DC/RA job is waiting for a claim" : $"{oldest.JobOrderNumber} · {ServiceStages.Label(oldest.Stage)} on {oldest.StageDate:dd MMM yyyy}"),
            new("GPRC claim export",
                loaded.LatestGprcReading is { } gprc ? gprc.ToString("dd MMM yyyy") : "none",
                loaded.LatestDcRaReading is { } dcRa ? $"DC/RA lists to {dcRa:dd MMM yyyy}" : "No DC/RA list imported")
        };
        gapWarning.Text = loaded.GprcGapWarning
            ? $"GPRC cell claims may be incomplete: the latest GPRC CLAIM export (S041) is {(loaded.LatestGprcReading is { } g ? $"dated {g:dd MMM yyyy}" : "missing")} " +
              $"and the latest DC/RA list (S014/S016) is dated {loaded.LatestDcRaReading:dd MMM yyyy}. Export GPRC CLAIM up to that date and import it; " +
              "until then the GPRC claims of the gap are in no table."
            : "";
        gapWarning.Visibility = loaded.GprcGapWarning ? Visibility.Visible : Visibility.Collapsed;
        Numbers = list;
        numbers.Children.Clear();
        foreach (var number in list)
            numbers.Children.Add(new KpiCard(number.Label, number.Value, number.Secondary,
                number.Label == "DC/RA jobs not yet claimed" && loaded.NotYetClaimed.Count > 0 ? "Warning"
                : number.Label == "GPRC claim export" && loaded.GprcGapWarning ? "Critical"
                : "Accent"));
    }

    /// <summary>
    /// A month row opens its claim lines (month and type become the filters); a claim line or a not-yet-claimed job
    /// opens the job's history through the shell's <c>openJob</c>.
    /// </summary>
    public void DrillDown(ServiceGridRow row)
    {
        if (row.Source is ServiceClaimsSummaryRow summary)
        {
            FromMonth = summary.ClaimMonth; ToMonth = summary.ClaimMonth;
            SelectedClaimType = ClaimTypes.FirstOrDefault(choice => choice.Code == summary.ClaimType) ?? ClaimTypes[0];
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
        if (openJob is null)
        {
            drillStatus.Text = $"Open Service job history and enter job {jobOrderNumber}.";
            return;
        }
        drillStatus.Text = $"Opening Service job history for job {jobOrderNumber}.";
        openJob(jobOrderNumber);
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
                ? loaded.NotYetClaimed.Select(job => job.StageDate).Where(date => date.HasValue).Select(date => date!.Value).ToArray()
                : loaded.Lines.Select(line => line.BusinessDate).ToArray();
            if (dates.Length == 0) return base.ExportPeriod;
            var (from, to) = QueryRange;
            return (from ?? dates.Min(), to ?? dates.Max());
        }
    }
}
