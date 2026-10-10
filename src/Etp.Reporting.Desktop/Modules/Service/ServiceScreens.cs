extern alias EtpApplication;

using System.Windows.Controls;
using ServicePendingLists = EtpApplication::Etp.Reporting.Application.Service.ServicePendingLists;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;
using ServiceStages = EtpApplication::Etp.Reporting.Application.Service.ServiceStages;

namespace Etp.Reporting.Desktop.Modules.Service;

public sealed record ServiceListChoice(string? Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// A drill-down from one Service screen to another (design 3.2 and 3.3): the task to open and an argument the target
/// applies before it loads. The argument is a <see cref="ServiceStages"/> name for the Pending board and the Jobs
/// list, a job number for Job history and a business date (yyyy-MM-dd) for the money check.
/// </summary>
public sealed record ServiceDrillDown(string TaskId, string? Argument = null);

/// <summary>A Service screen that accepts a drill-down argument before its first load.</summary>
public interface IServiceDrillDownTarget
{
    void ApplyDrillDown(string argument);
}

/// <summary>The Service rail (design 3.1): its seven task ids, the lists the interim screens offer and the screen factory.</summary>
public static class ServiceScreens
{
    public const string Destination = "Service Centre";
    public const string TodayTask = "service-today";
    public const string PendingTask = "service-pending";
    public const string JobHistoryTask = "service-job-history";
    public const string JobsTask = "service-jobs";
    public const string ClaimsTask = "service-claims";
    public const string PartsTask = "service-parts";
    public const string MoneyTask = "service-money";

    /// <summary>The Service tasks in rail order: Today, Pending, Jobs (history then list), Claims, Parts, Money.</summary>
    public static IReadOnlyList<string> Tasks { get; } = [TodayTask, PendingTask, JobHistoryTask, JobsTask, ClaimsTask, PartsTask, MoneyTask];

    /// <summary>The six tabs of the Service rail, in order.</summary>
    public static IReadOnlyList<string> Tabs { get; } = ["Today", "Pending", "Jobs", "Claims", "Parts", "Money"];

    /// <summary>
    /// The ten status lists (Service interim design section 3). The code is the status view's report
    /// code, which is what IServiceReportQuery.LoadJobsByStatusAsync filters on; the label is the
    /// status label ServiceInterimFamilies.StatusViews gives each view.
    /// </summary>
    public static IReadOnlyList<ServiceListChoice> StatusLists { get; } =
    [
        new(null, "All lists"),
        new("S032", "PR (S032)"), new("S015", "IR (S015)"), new("S033", "SRN (S033)"), new("S035", "SRNINV (S035)"),
        new("S014", "DC (S014)"), new("S016", "RA (S016)"), new("S034", "REPAIRED (S034)"), new("S017", "RWR (S017)"),
        new("S031", "PD (S031)"), new("S018", "DELIVERED (S018)")
    ];

    /// <summary>
    /// The pending lists. The code is the contract list key (ServicePendingLists, as
    /// ServiceInterimFamilies.PendingLists maps S009/S010/S011), which is what
    /// IServiceReportQuery.LoadPendingAsync takes; the report code stays in the label only.
    /// </summary>
    public static IReadOnlyList<ServiceListChoice> PendingLists { get; } =
    [
        new(ServicePendingLists.PendingRepair, "Pending repair (S009)"),
        new(ServicePendingLists.PendingDelivery, "Pending delivery (S010)"),
        new(ServicePendingLists.SrnStatus, "SRN status (S011)")
    ];

    /// <summary>
    /// A query factory for a build whose composition root has no Service read model. The screens
    /// then show this message through DesktopFriendlyError instead of failing to open.
    /// </summary>
    public static ServiceReportQuery Unavailable() =>
        throw new InvalidOperationException("The Service read model is not available in this build.");

    /// <summary>
    /// Creates the screen of a Service task and starts its first load. <paramref name="navigate"/> opens another
    /// Service task (drill-down); null leaves the drill-down cards and links inert. <paramref name="argument"/> is the
    /// drill-down argument for this screen, applied before the load.
    /// </summary>
    public static UserControl Create(string taskId, Func<ServiceReportQuery> query, ServiceExcelExport export,
        Action<ServiceDrillDown>? navigate = null, string? argument = null)
    {
        ServiceScreenView view = taskId switch
        {
            TodayTask => new ServiceTodayView(query, export, navigate),
            JobsTask => new ServiceJobsView(query, export),
            PendingTask => new ServicePendingView(query, export),
            JobHistoryTask => new ServiceJobHistoryView(query, export),
            ClaimsTask => new ServicePlaceholderView(ServicePlaceholderView.Claims, query, export, navigate),
            PartsTask => new ServicePlaceholderView(ServicePlaceholderView.Parts, query, export, navigate),
            MoneyTask => new ServiceMoneyView(query, export),
            _ => throw new ArgumentOutOfRangeException(nameof(taskId), taskId, "Not a Service centre task.")
        };
        if (!string.IsNullOrWhiteSpace(argument)) ApplyDrillDown(view, argument.Trim());
        _ = view.ActivateAsync();
        return view;
    }

    /// <summary>
    /// Applies a drill-down argument to a screen. A screen that implements <see cref="IServiceDrillDownTarget"/>
    /// takes the argument itself; the interim screens are set through their public filters, so a stage name opens
    /// the interim list that holds that stage until the 1.10.0 screens replace them.
    /// </summary>
    public static void ApplyDrillDown(ServiceScreenView view, string argument)
    {
        switch (view)
        {
            case IServiceDrillDownTarget target: target.ApplyDrillDown(argument); break;
            case ServicePendingView pending when PendingListForStage(argument) is { } list:
                pending.SelectedList = PendingLists.Single(choice => choice.Code == list); break;
            case ServiceJobsView jobs:
                if (StatusViewForStage(argument) is { } status) jobs.SelectedStatus = StatusLists.Single(choice => choice.Code == status);
                else if (argument == ServiceStages.Booked) jobs.SelectedStatus = StatusLists[0];
                break;
            case ServiceJobHistoryView history: history.JobNumber = argument; break;
            case ServiceMoneyView money when DateOnly.TryParseExact(argument, "yyyy-MM-dd", out var date):
                money.From = date; money.To = date; break;
        }
    }

    /// <summary>The interim pending list that holds a stage (design 4.2 against the S009/S010/S011 lists), or null.</summary>
    public static string? PendingListForStage(string stage) => stage switch
    {
        ServiceStages.OnBench or ServiceStages.IndentRaised => ServicePendingLists.PendingRepair,
        ServiceStages.ReadyForDelivery or ServiceStages.InTransitBack => ServicePendingLists.PendingDelivery,
        ServiceStages.SrnOut => ServicePendingLists.SrnStatus,
        _ => null
    };

    /// <summary>The interim status view that holds a stage, or null for "all lists" (booked jobs have no status view).</summary>
    public static string? StatusViewForStage(string stage) => stage switch
    {
        ServiceStages.Delivered => "S018",
        ServiceStages.ReturnedWithoutRepair => "S017",
        ServiceStages.DcIssued => "S014",
        ServiceStages.RaIssued => "S016",
        ServiceStages.ReadyForDelivery => "S031",
        ServiceStages.SrnOut => "S033",
        ServiceStages.IndentRaised => "S015",
        ServiceStages.OnBench => "S032",
        _ => null
    };
}
