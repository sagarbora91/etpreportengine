extern alias EtpApplication;

using System.Windows.Controls;
using ServicePendingLists = EtpApplication::Etp.Reporting.Application.Service.ServicePendingLists;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

public sealed record ServiceListChoice(string? Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The Service centre screens: their task ids, the lists the screens offer and the job history route (1.10.0).</summary>
public static class ServiceScreens
{
    public const string Destination = "Service Centre";
    public const string JobsTask = "service-jobs";
    public const string PendingTask = "service-pending";
    public const string JobHistoryTask = "service-job-history";
    public const string MoneyTask = "service-money";

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
    /// <paramref name="argument"/> is the route argument (a job number for <see cref="JobHistoryTask"/>, ignored by the
    /// other screens); <paramref name="openJob"/> is what a grid calls to open a job's history (the shell passes
    /// <c>TaskNavigator.NavigateServiceJob</c>; null disables the row action).
    /// </summary>
    public static UserControl Create(string taskId, Func<ServiceReportQuery> query, ServiceExcelExport export,
        string? argument = null, Action<string>? openJob = null)
    {
        ServiceScreenView view = taskId switch
        {
            JobsTask => new ServiceJobsView(query, export, openJob),
            PendingTask => new ServicePendingView(query, export),
            JobHistoryTask => new ServiceJobHistoryView(query, export) { JobNumber = argument?.Trim() ?? "" },
            MoneyTask => new ServiceMoneyView(query, export),
            _ => throw new ArgumentOutOfRangeException(nameof(taskId), taskId, "Not a Service centre task.")
        };
        _ = view.ActivateAsync();
        return view;
    }
}
