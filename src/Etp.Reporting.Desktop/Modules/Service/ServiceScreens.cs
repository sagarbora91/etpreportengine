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
    /// The route that opens Service job history for one job (1.10.0 "open from any grid", design 3.4). The job number
    /// travels as the route's <see cref="WorkspaceRoute.Argument"/>; <see cref="Create"/> receives it as <c>argument</c>.
    /// </summary>
    public static WorkspaceRoute JobHistoryRoute(string jobOrderNumber) =>
        TaskNavigation.Find(JobHistoryTask)!.RouteWith(jobOrderNumber);

    /// <summary>
    /// Creates the screen of a Service task and starts its first load. <paramref name="argument"/> is the route argument
    /// (a job number for job history, a stage for Pending and Jobs, a yyyy-MM-dd date for Today and Money), applied
    /// before the load. <paramref name="openJob"/> is what a grid calls to open a job's history (the shell passes
    /// <c>TaskNavigator.NavigateServiceJob</c>; lane history's grids take it). <paramref name="navigate"/> opens another
    /// Service screen with an argument (the shell passes <c>TaskNavigator.NavigateService</c>); null openers leave the
    /// cards, links and row actions inert.
    /// </summary>
    public static UserControl Create(string taskId, Func<ServiceReportQuery> query, ServiceExcelExport export,
        string? argument = null, Action<string>? openJob = null, Action<ServiceDrillDown>? navigate = null)
    {
        ServiceScreenView view = taskId switch
        {
            TodayTask => new ServiceTodayView(query, export, navigate),
            JobsTask => new ServiceJobsView(query, export, openJob),
            PendingTask => new ServicePendingView(query, export),
            JobHistoryTask => new ServiceJobHistoryView(query, export) { JobNumber = argument?.Trim() ?? "" },
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
            case ServiceJobsView jobs when JobsChoiceForStage(argument) is { } choice:
                jobs.SelectedChoice = ServiceJobsView.Choices.Single(item => item.Code == choice); break;
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

    /// <summary>
    /// The Jobs list choice a stage drill-down opens: BOOKED (every job is booked) opens all jobs, any other stage opens
    /// that stage's choice; an unknown argument leaves the default (closed in the last 30 days).
    /// </summary>
    public static string? JobsChoiceForStage(string stage) =>
        stage == ServiceStages.Booked ? ServiceJobsView.AllJobsCode
        : ServiceJobsView.Choices.Any(choice => choice.Code == stage) ? stage : null;
}
