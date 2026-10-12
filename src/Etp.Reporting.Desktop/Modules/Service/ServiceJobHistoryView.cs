extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ServiceJobDetail = EtpApplication::Etp.Reporting.Application.Service.ServiceJobDetail;
using ServiceJobHeader = EtpApplication::Etp.Reporting.Application.Service.ServiceJobHeader;
using ServiceJobStages = EtpApplication::Etp.Reporting.Application.Service.ServiceJobStages;
using ServiceJobTimelineRow = EtpApplication::Etp.Reporting.Application.Service.ServiceJobTimelineRow;
using ServiceJobTypes = EtpApplication::Etp.Reporting.Application.Service.ServiceJobTypes;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// Service job history (1.10.0, design 3.4): one job, everything the Service Centre exported about it, newest first.
/// A header card (job, booking, brand/model/product, guarantee, customer type, stage, days open or TAT, labour/spares)
/// above a timeline grid with one row per reading of every family that holds the job. The job number comes from the
/// search box or from another Service grid through the route argument (ServiceScreens.JobHistoryRoute).
/// The customer name is shown (decision 37; "—" for a Viewer, whom v_service_job gives no name); never a phone, e-mail or address.
/// </summary>
public sealed class ServiceJobHistoryView : ServiceScreenView
{
    private readonly TextBox jobNumber = new() { MinWidth = 220, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Border headerCard;
    private readonly Grid headerGrid = new();
    private readonly StackPanel headline = new() { Orientation = Orientation.Horizontal, Margin = ServiceUi.Gap(0, 0, 0, ServiceUi.S8) };
    private string searched = "";
    private ServiceJobHeader? header;
    private IReadOnlyList<(string Label, string Value)> headerItems = [];

    public ServiceJobHistoryView(Func<ServiceReportQuery> query, ServiceExcelExport export)
        : base("Service job history",
            "Enter a job number, or open a job from any Service grid, to see every reading the Service Centre exported about it, newest first.",
            "Service.JobHistory", "SERVICE_JOB_HISTORY_LOAD_FAILED", query, export)
    {
        FilterBar.Children.Add(new TextBlock { Text = "Job number", VerticalAlignment = VerticalAlignment.Center, Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0) });
        AutomationProperties.SetName(jobNumber, "Job number");
        jobNumber.KeyDown += async (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; await ActivateAsync(); } };
        FilterBar.Children.Add(jobNumber);
        var show = ServiceUi.MakeButton("Show history");
        show.Margin = ServiceUi.Gap(ServiceUi.S8, 0, 0, 0);
        show.Click += async (_, _) => await ActivateAsync();
        FilterBar.Children.Add(show);

        headerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        // SVC-19: the job's state is the headline (stage dot and "Open 41 days ..." at section-title size), the other pairs sit below it.
        var headerStack = new StackPanel();
        headerStack.Children.Add(headline);
        headerStack.Children.Add(headerGrid);
        headerCard = DsrUi.Card(headerStack, ServiceUi.Gap(0, ServiceUi.S4, 0, ServiceUi.S4));
        headerCard.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(headerCard, "Job header");
        HeaderPanel.Children.Add(headerCard);

        SetColumns(
        [
            new("Snapshot", DisplayFormat: "dd MMM yyyy", Width: 110), new("Family", Width: 200), new("Report", Width: 70),
            new("Event date", DisplayFormat: "dd MMM yyyy", Width: 110), new("Status", Width: 220), new("Pending at", Width: 100),
            new("Document", Width: 140), new("Amount", "#,##0.00", "N2", 100), new("Source", Width: 110)
        ]);
    }

    public string JobNumber { get => jobNumber.Text; set => jobNumber.Text = value; }

    /// <summary>The trimmed job number the last load asked for.</summary>
    public string SearchedJobNumber => searched;

    /// <summary>The header of the job the last load found, or null.</summary>
    public ServiceJobHeader? Header => header;

    /// <summary>The header card's label/value pairs, in display order (what the export's message line carries).</summary>
    public IReadOnlyList<(string Label, string Value)> HeaderItems => headerItems;

    public bool IsHeaderVisible => headerCard.Visibility == Visibility.Visible;

    protected override string? PrepareLoad()
    {
        searched = (jobNumber.Text ?? "").Trim();
        return searched.Length == 0 ? "Enter a job number and select Show history." : null;
    }

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source)
    {
        var detail = await source.LoadJobAsync(searched);
        ShowHeader(detail?.Header);
        return detail is null ? [] : TimelineRows(detail.Timeline);
    }

    protected override void ClearExtras() => ShowHeader(null);

    private void ShowHeader(ServiceJobHeader? value)
    {
        header = value;
        headerItems = value is null ? [] : DescribeHeader(value);
        headerGrid.Children.Clear();
        headerGrid.RowDefinitions.Clear();
        headline.Children.Clear();
        if (value is not null)
        {
            var dot = ServiceUi.StageDot(value.Stage);
            dot.Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0);
            headline.Children.Add(dot);
            var days = ServiceUi.SectionTitle(headerItems.FirstOrDefault(item => item.Label is "Days open" or "TAT").Value ?? value.JobOrderNumber);
            days.VerticalAlignment = VerticalAlignment.Center;
            days.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            headline.Children.Add(days);
            var who = ServiceUi.Caption($"{value.JobOrderNumber} · {ServiceJobStages.Label(value.Stage)}");
            who.VerticalAlignment = VerticalAlignment.Center; who.Margin = ServiceUi.Gap(ServiceUi.S12, 0, 0, 0);
            headline.Children.Add(who);
        }
        headerCard.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        for (var index = 0; index < headerItems.Count; index++)
        {
            var row = index / 2; var column = index % 2 * 2;
            if (column == 0) headerGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var label = ServiceUi.Caption(headerItems[index].Label);
            label.Margin = ServiceUi.Gap(0, ServiceUi.S4, ServiceUi.S12, ServiceUi.S4);
            var text = ServiceUi.Body(headerItems[index].Value);
            if (index == 0) text.FontWeight = FontWeights.SemiBold;
            text.Margin = ServiceUi.Gap(0, ServiceUi.S4, ServiceUi.S16, ServiceUi.S4);
            Grid.SetRow(label, row); Grid.SetColumn(label, column);
            Grid.SetRow(text, row); Grid.SetColumn(text, column + 1);
            headerGrid.Children.Add(label); headerGrid.Children.Add(text);
        }
    }

    /// <summary>The header card's pairs (design 3.4). Money is "—" where the S003 columns are null.</summary>
    public static IReadOnlyList<(string Label, string Value)> DescribeHeader(ServiceJobHeader job)
    {
        var closed = ServiceJobStages.IsClosed(job.Stage, job.ClaimRaised);
        var stage = ServiceJobStages.Label(job.Stage) + (job.StageDate is { } stageDate ? ServiceDisplay.Text($" · {stageDate:dd MMM yyyy}") : "")
            + (job.Stage is ServiceJobStages.DcIssued or ServiceJobStages.RaIssued ? job.ClaimRaised ? " · claim raised" : " · claim not raised" : "")
            + (!closed && job.PendingAt is { Length: > 0 } ? $" · at {job.PendingAt}" : "");
        var days = closed
            ? job.TatDays is { } tat ? ServiceDisplay.Text($"TAT {tat:N0} day{(tat == 1 ? "" : "s")} (booked to {(job.Stage == ServiceJobStages.Rwr ? "returned" : job.Stage == ServiceJobStages.Delivered ? "delivered" : "closed")})") : "TAT not known"
            : job.AgeDays is { } age ? ServiceDisplay.Text($"Open {age:N0} day{(age == 1 ? "" : "s")}") + (job.DaysInStage is { } inStage ? ServiceDisplay.Text($" · {inStage:N0} in this stage") : "") + (job.IsOverdue ? job.Edd is { } promised && promised < job.AsAt ? " · EDD passed" : " · overdue" : "") : "Open";
        var booked = (job.BookingDate is { } date ? ServiceDisplay.Date(date) : "not known") + " · " + ServiceJobTypes.Normalise(job.JoType);
        var items = new List<(string, string)>
        {
            ("Job number", job.JobOrderNumber),
            ("Booked on", booked),
            ("Brand / model / product", Joined([job.Brand ?? "", job.Model ?? "", job.ProductCategory ?? ""]) is { Length: > 0 } product ? product : "—"),
            ("Guarantee", job.Guarantee is { Length: > 0 } ? job.Guarantee.Replace('_', ' ') : "—"),
            ("Customer type", job.CustomerType is { Length: > 0 } ? job.CustomerType : "—"),
            ("Customer name", job.CustomerName is { Length: > 0 } ? job.CustomerName : "—"),
            ("Current stage", stage),
            (closed ? "TAT" : "Days open", days),
            ("EDD", job.Edd is { } edd ? ServiceDisplay.Date(edd) : "—"),
            ("Labour / spares (S003)", $"Labour {Money(job.LabourCharge)} · Spares {Money(job.SpareValue)}")
        };
        if (!closed && job.SpareRequired is { Length: > 0 }) items.Add(("Spare required", job.SpareRequired));
        return items;
    }

    private static string Money(decimal? value) => value is { } amount ? ServiceDisplay.Money(amount) : "—";

    /// <summary>Snapshot date desc, then event date desc (unknown event dates last), then report code (design 3.4).</summary>
    public static IReadOnlyList<ServiceGridRow> TimelineRows(IReadOnlyList<ServiceJobTimelineRow> timeline) =>
        timeline
            .OrderByDescending(row => row.SnapshotDate)
            .ThenByDescending(row => row.EventDate.HasValue).ThenByDescending(row => row.EventDate)
            .ThenBy(row => row.ReportCode, StringComparer.Ordinal).ThenByDescending(row => row.ImportFileId)
            .Select(row => new ServiceGridRow(row,
            [
                row.SnapshotDate, row.FamilyLabel, row.ReportCode, row.EventDate, row.StatusText, row.PendingStore,
                row.DocumentNumber, row.Amount, SourceLabel(row.SourceKind)
            ])).ToArray();

    private static string SourceLabel(string? kind) => kind?.Trim().ToUpperInvariant() switch
    {
        "CONSOLIDATED" => "consolidated",
        "RAW" => "raw",
        null or "" => "",
        var other => other.ToLowerInvariant()
    };

    protected override string EmptyRowsText => header is null
        ? $"No Service family holds job {searched}. Check the number."
        : $"Job {searched} is known but no reading row was exported for it.";

    protected override string Summarise(int count)
    {
        var families = Rows.Select(row => (string?)row.Cells[2]).Distinct(StringComparer.Ordinal).Count();
        return ServiceDisplay.Text($"Job {searched}: {count:N0} reading{(count == 1 ? "" : "s")} across {families:N0} famil{(families == 1 ? "y" : "ies")} · newest first.");
    }

    protected override string ExportName => "Service job history";

    /// <summary>SD-09: the timeline's snapshot range (the as-at date when the job has no rows).</summary>
    protected override (DateOnly From, DateOnly To) ExportPeriod
    {
        get
        {
            var dates = Rows.Select(row => (DateOnly)row.Cells[0]!).ToArray();
            if (dates.Length > 0) return (dates.Min(), dates.Max());
            var asAt = header?.AsAt ?? DateOnly.FromDateTime(DateTime.Today);
            return (asAt, asAt);
        }
    }

    /// <summary>The as-at line, then the header card as "label: value" pairs, so the export carries header + timeline.</summary>
    protected override string ExportMessage =>
        headerItems.Count == 0 ? base.ExportMessage : base.ExportMessage + " · " + string.Join(" · ", headerItems.Select(item => $"{item.Label}: {item.Value}"));
}

