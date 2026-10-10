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
/// The customer name is shown as before; never a phone, e-mail or address.
/// </summary>
public sealed class ServiceJobHistoryView : ServiceScreenView
{
    private readonly TextBox jobNumber = new() { MinWidth = 220, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Border headerCard;
    private readonly Grid headerGrid = new();
    private string searched = "";
    private ServiceJobHeader? header;
    private IReadOnlyList<(string Label, string Value)> headerItems = [];

    public ServiceJobHistoryView(Func<ServiceReportQuery> query, ServiceExcelExport export)
        : base("Service job history",
            "Enter a job number, or open a job from any Service grid, to see every reading the Service Centre exported about it, newest first.",
            "Service.JobHistory", "SERVICE_JOB_HISTORY_LOAD_FAILED", query, export)
    {
        FilterBar.Children.Add(new TextBlock { Text = "Job number", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        AutomationProperties.SetName(jobNumber, "Job number");
        jobNumber.KeyDown += async (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; await ActivateAsync(); } };
        FilterBar.Children.Add(jobNumber);
        var show = new Button { Content = "Show history", MinHeight = 44, Margin = new Thickness(8, 0, 0, 0) };
        show.Click += async (_, _) => await ActivateAsync();
        FilterBar.Children.Add(show);

        headerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        headerCard = DsrUi.Card(headerGrid, new Thickness(0, 4, 0, 4));
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
        headerCard.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        for (var index = 0; index < headerItems.Count; index++)
        {
            var row = index / 2; var column = index % 2 * 2;
            if (column == 0) headerGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var label = DsrUi.Text(headerItems[index].Label, 11, colour: "SecondaryText");
            label.Margin = new Thickness(0, 2, 12, 2);
            var text = DsrUi.Text(headerItems[index].Value, 13, index == 0 ? FontWeights.SemiBold : null, "PrimaryText");
            text.Margin = new Thickness(0, 2, 16, 2);
            Grid.SetRow(label, row); Grid.SetColumn(label, column);
            Grid.SetRow(text, row); Grid.SetColumn(text, column + 1);
            headerGrid.Children.Add(label); headerGrid.Children.Add(text);
        }
    }

    /// <summary>The header card's pairs (design 3.4). Money is "—" where the S003 columns are null.</summary>
    public static IReadOnlyList<(string Label, string Value)> DescribeHeader(ServiceJobHeader job)
    {
        var closed = ServiceJobStages.IsClosed(job.Stage, job.ClaimRaised);
        var stage = ServiceJobStages.Label(job.Stage) + (job.StageDate is { } stageDate ? $" · {stageDate:dd MMM yyyy}" : "")
            + (job.Stage is ServiceJobStages.DcIssued or ServiceJobStages.RaIssued ? job.ClaimRaised ? " · claim raised" : " · claim not raised" : "")
            + (!closed && job.PendingAt is { Length: > 0 } ? $" · at {job.PendingAt}" : "");
        var days = closed
            ? job.TatDays is { } tat ? $"TAT {tat:N0} day{(tat == 1 ? "" : "s")} (booked to {(job.Stage == ServiceJobStages.Rwr ? "returned" : job.Stage == ServiceJobStages.Delivered ? "delivered" : "closed")})" : "TAT not known"
            : job.AgeDays is { } age ? $"Open {age:N0} day{(age == 1 ? "" : "s")}" + (job.DaysInStage is { } inStage ? $" · {inStage:N0} in this stage" : "") + (job.IsOverdue ? job.Edd is { } edd && edd < job.AsAt ? " · EDD passed" : " · overdue" : "") : "Open";
        var booked = (job.BookingDate is { } date ? date.ToString("dd MMM yyyy") : "not known") + " · " + ServiceJobTypes.Normalise(job.JoType);
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
            ("EDD", job.Edd is { } edd ? edd.ToString("dd MMM yyyy") : "—"),
            ("Labour / spares (S003)", $"Labour {Money(job.LabourCharge)} · Spares {Money(job.SpareValue)}")
        };
        if (!closed && job.SpareRequired is { Length: > 0 }) items.Add(("Spare required", job.SpareRequired));
        return items;
    }

    private static string Money(decimal? value) => value is { } amount ? amount.ToString("N2") : "—";

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
        return $"Job {searched}: {count:N0} reading{(count == 1 ? "" : "s")} across {families:N0} famil{(families == 1 ? "y" : "ies")} · newest first.";
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
