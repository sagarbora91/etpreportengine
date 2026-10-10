extern alias EtpApplication;

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ServiceJobHeader = EtpApplication::Etp.Reporting.Application.Service.ServiceJobHeader;
using ServiceJobStages = EtpApplication::Etp.Reporting.Application.Service.ServiceJobStages;
using ServiceJobTat = EtpApplication::Etp.Reporting.Application.Service.ServiceJobTat;
using ServiceJobTypes = EtpApplication::Etp.Reporting.Application.Service.ServiceJobTypes;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// Service jobs (1.10.0, design 3.4 "Jobs" list; replaces the interim "jobs by status"): one row per job from
/// <c>v_service_job</c> with its stage (design 4.2), Booking / Quick Billing, booking date, stage date and TAT days for
/// closed jobs. The default choice is "closed in the last 30 days" (Q8) with "All jobs" as the show-all switch; the other
/// choices are the open jobs and one stage at a time (S036/S037 are job lists, so there is no "delivery report" list, Q10).
/// TAT is booking to delivered; the status line gives the Booking median as the headline and Quick Billing beside it (Q2).
/// A row opens Service job history (double-click or the button) through <c>openJob</c>.
/// </summary>
public sealed class ServiceJobsView : ServiceScreenView
{
    public const int RecentClosedDays = 30;
    public const string RecentClosedCode = "RECENT_CLOSED";
    public const string AllJobsCode = "ALL";
    public const string OpenJobsCode = "OPEN";
    /// <summary>Date-scoped choices a Service Today card opens (R-UI-04): "BOOKED_ON:yyyy-MM-dd" / "DELIVERED_ON:yyyy-MM-dd".</summary>
    public const string BookedOnPrefix = "BOOKED_ON:";
    public const string DeliveredOnPrefix = "DELIVERED_ON:";

    /// <summary>The "Show" choices: the default closed-in-30-days view, the show-all switch, open jobs, then each stage.</summary>
    public static IReadOnlyList<ServiceListChoice> Choices { get; } =
        new ServiceListChoice[]
        {
            new(RecentClosedCode, $"Closed in the last {RecentClosedDays} days"),
            new(AllJobsCode, "All jobs"),
            new(OpenJobsCode, "Open jobs")
        }.Concat(ServiceJobStages.InOrder.Select(stage => new ServiceListChoice(stage, ServiceJobStages.Label(stage)))).ToArray();

    private readonly ComboBox showFilter = new() { MinWidth = 260, MinHeight = 44, ItemsSource = Choices, SelectedIndex = 0 };
    private readonly Button openButton = new() { Content = "Job history", MinHeight = 44, Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
    private readonly Action<string>? openJob;
    private IReadOnlyList<ServiceJobHeader> shown = [];

    public ServiceJobsView(Func<ServiceReportQuery> query, ServiceExcelExport export, Action<string>? openJob = null)
        : base("Service jobs",
            "Each job shows the stage it is in now, Booking or Quick Billing, and for closed jobs the TAT from booking to delivery. Double-click a job to open its history.",
            "Service.Jobs", "SERVICE_JOBS_LOAD_FAILED", query, export)
    {
        this.openJob = openJob;
        FilterBar.Children.Add(new TextBlock { Text = "Show", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        AutomationProperties.SetName(showFilter, "Show jobs");
        showFilter.SelectionChanged += async (_, _) => { if (IsLoaded) await ActivateAsync(); };
        FilterBar.Children.Add(showFilter);
        AutomationProperties.SetName(openButton, "Open job history");
        openButton.Click += (_, _) => OpenSelectedJob();
        openButton.IsEnabled = false;
        FilterBar.Children.Add(openButton);
        Table.SelectionChanged += (_, _) => openButton.IsEnabled = openJob is not null && Table.SelectedItem is ServiceGridRow;
        Table.MouseDoubleClick += (_, args) => { if (IsOnRow(args.OriginalSource)) OpenSelectedJob(); };
        Table.KeyDown += (_, args) => { if (args.Key == System.Windows.Input.Key.Enter && Table.SelectedItem is ServiceGridRow) { args.Handled = true; OpenSelectedJob(); } };
        SetColumns(
        [
            new("Job number", Width: 150), new("Stage", Width: 200), new("Job type", Width: 100),
            new("Booked on", DisplayFormat: "dd MMM yyyy", Width: 110), new("Stage date", DisplayFormat: "dd MMM yyyy", Width: 110),
            new("TAT (days)", "#,##0", Width: 90), new("Days open", "#,##0", Width: 90), new("EDD", DisplayFormat: "dd MMM yyyy", Width: 110),
            new("Brand"), new("Model", Width: 150), new("Product", Width: 140), new("Guarantee", Width: 130),
            new("Customer name", Width: 180), new("Spare value", "#,##0.00", "N2"), new("Labour", "#,##0.00", "N2"),
            new("As at", DisplayFormat: "dd MMM yyyy")
        ]);
    }

    public ServiceListChoice SelectedChoice
    {
        get => (ServiceListChoice)showFilter.SelectedItem;
        set => showFilter.SelectedItem = ((IReadOnlyList<ServiceListChoice>)showFilter.ItemsSource).FirstOrDefault(choice => choice.Code == value.Code)
            ?? Choices.Single(choice => choice.Code == value.Code);
    }

    /// <summary>The date-scoped choice for a "BOOKED_ON:" / "DELIVERED_ON:" code, or null when the code is not one.</summary>
    public static ServiceListChoice? DateChoice(string code)
    {
        if (DateScope(code) is not { } scope) return null;
        var on = scope.Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        return new(code, scope.Delivered ? $"Delivered on {on}" : $"Booked on {on}");
    }

    /// <summary>Shows only the jobs booked (or delivered) on one date: the choice is added above the fixed ones and selected.</summary>
    public void ShowDate(string code)
    {
        var choice = DateChoice(code) ?? throw new ArgumentException("Not a BOOKED_ON: or DELIVERED_ON: choice.", nameof(code));
        showFilter.ItemsSource = new[] { choice }.Concat(Choices).ToArray();
        showFilter.SelectedItem = choice;
    }

    private static (bool Delivered, DateOnly Date)? DateScope(string code)
    {
        var delivered = code.StartsWith(DeliveredOnPrefix, StringComparison.Ordinal);
        if (!delivered && !code.StartsWith(BookedOnPrefix, StringComparison.Ordinal)) return null;
        var text = code[(delivered ? DeliveredOnPrefix : BookedOnPrefix).Length..];
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? (delivered, date) : null;
    }

    /// <summary>The Q8 "show all" switch: true when every job is listed, false for the default closed-in-30-days view.</summary>
    public bool ShowAll
    {
        get => SelectedChoice.Code == AllJobsCode;
        set => SelectedChoice = Choices.Single(choice => choice.Code == (value ? AllJobsCode : RecentClosedCode));
    }

    /// <summary>Opens Service job history for the selected row; nothing happens without a selection or an opener.</summary>
    public void OpenSelectedJob()
    {
        if (openJob is null || Table.SelectedItem is not ServiceGridRow { Source: ServiceJobHeader job }) return;
        openJob(job.JobOrderNumber);
    }

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source)
    {
        var jobs = await source.LoadJobListAsync();
        shown = Filter(jobs, SelectedChoice.Code!);
        return shown.Select(job => new ServiceGridRow(job,
        [
            job.JobOrderNumber, ServiceJobStages.Label(job.Stage), ServiceJobTypes.Normalise(job.JoType), job.BookingDate, job.StageDate,
            job.TatDays, job.AgeDays, job.Edd, job.Brand, job.Model, job.ProductCategory, job.Guarantee?.Replace('_', ' '),
            job.CustomerName, job.SpareValue, job.LabourCharge, job.AsAt
        ])).ToArray();
    }

    /// <summary>
    /// The rows for a choice, newest stage date first. "Closed in the last 30 days" counts from each job's as-at date
    /// (the latest Service snapshot), so the view never empties on a weekend; closed = delivered, RWR or claimed DC/RA.
    /// </summary>
    public static IReadOnlyList<ServiceJobHeader> Filter(IReadOnlyList<ServiceJobHeader> jobs, string choice)
    {
        IEnumerable<ServiceJobHeader> selected = DateScope(choice) is { } scope
            ? scope.Delivered
                ? jobs.Where(job => job.Stage == ServiceJobStages.Delivered && job.StageDate == scope.Date)
                : jobs.Where(job => job.BookingDate == scope.Date)
            : choice switch
        {
            RecentClosedCode => jobs.Where(job => ServiceJobStages.IsClosed(job.Stage, job.ClaimRaised)
                && job.StageDate is { } closedOn && closedOn >= job.AsAt.AddDays(-RecentClosedDays)),
            AllJobsCode => jobs,
            OpenJobsCode => jobs.Where(job => !ServiceJobStages.IsClosed(job.Stage, job.ClaimRaised)),
            _ => jobs.Where(job => job.Stage == choice)
        };
        return selected
            .OrderByDescending(job => job.StageDate.HasValue).ThenByDescending(job => job.StageDate)
            .ThenBy(job => ServiceJobStages.Rank(job.Stage))
            .ThenBy(job => job.JobOrderNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Q2: the Booking median (booking to delivered) is the headline; Quick Billing is shown separately, never mixed in.</summary>
    public static string DescribeTat(IReadOnlyList<ServiceJobHeader> jobs)
    {
        var closed = jobs.Where(job => job.TatDays.HasValue && job.Stage is ServiceJobStages.Delivered).ToArray();
        if (closed.Length == 0) return "";
        var booking = closed.Where(job => ServiceJobTypes.Normalise(job.JoType) == ServiceJobTypes.Booking).Select(job => job.TatDays!.Value).ToArray();
        var quick = closed.Where(job => ServiceJobTypes.Normalise(job.JoType) == ServiceJobTypes.QuickBilling).Select(job => job.TatDays!.Value).ToArray();
        var parts = new List<string>();
        if (booking.Length > 0) parts.Add($"Booking median {ServiceJobTat.Median(booking):N0} days ({booking.Length:N0} delivered, {booking.Count(days => days > 15):N0} over 15 days)");
        if (quick.Length > 0) parts.Add($"Quick Billing median {ServiceJobTat.Median(quick):N0} days ({quick.Length:N0} delivered)");
        return "TAT booking to delivered: " + string.Join(" · ", parts) + ".";
    }

    protected override string EmptyRowsText => SelectedChoice.Code == RecentClosedCode
        ? $"No job closed in the last {RecentClosedDays} days. Choose \"All jobs\" to see every job."
        : "No jobs for this choice.";

    protected override string Summarise(int count)
    {
        var tat = DescribeTat(shown);
        return $"{count:N0} job{(count == 1 ? "" : "s")} · {SelectedChoice.Label}." + (tat.Length == 0 ? "" : " " + tat);
    }

    protected override string ExportName => "Service jobs";

    /// <summary>SD-09: the 30-day window for the default choice, otherwise the shown jobs' booking-to-as-at span.</summary>
    protected override (DateOnly From, DateOnly To) ExportPeriod
    {
        get
        {
            if (shown.Count == 0) return base.ExportPeriod;
            var asAt = shown.Max(job => job.AsAt);
            if (SelectedChoice.Code == RecentClosedCode) return (asAt.AddDays(-RecentClosedDays), asAt);
            if (DateScope(SelectedChoice.Code!) is { } scope) return (scope.Date, scope.Date);
            var from = shown.Min(job => job.BookingDate ?? job.StageDate ?? job.AsAt);
            return (from, asAt);
        }
    }

    /// <summary>The as-at line plus the TAT headline, so the export carries what the status line says.</summary>
    protected override string ExportMessage
    {
        get { var tat = DescribeTat(shown); return tat.Length == 0 ? base.ExportMessage : base.ExportMessage + " · " + tat; }
    }
}
