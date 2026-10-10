extern alias EtpApplication;

using System.Windows.Controls;
using ServicePendingLists = EtpApplication::Etp.Reporting.Application.Service.ServicePendingLists;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

public sealed record ServiceListChoice(string? Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The Service centre screens: their task ids and the lists the screens offer.</summary>
public static class ServiceScreens
{
    public const string Destination = "Service Centre";
    public const string JobsTask = "service-jobs";
    public const string PendingTask = "service-pending";
    public const string JobHistoryTask = "service-job-history";
    public const string MoneyTask = "service-money";
    /// <summary>Parts and purchases (1.10.0, design 3.6). Lane shell-today registers the task in TaskNavigation.</summary>
    public const string PartsTask = "service-parts";

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
    /// Opens another Service task from a screen (drill-down): the Parts screen opens Job history with the job's number.
    /// The shell supplies it (TaskNavigator); a screen built without one shows no drill-down.
    /// </summary>
    public delegate void ServiceTaskNavigate(string taskId, string? jobOrderNumber);

    /// <summary>
    /// After the shell has navigated to <see cref="JobHistoryTask"/>, shows the job the drill-down asked for.
    /// Kept here so the shell needs no knowledge of the history screen's members.
    /// </summary>
    public static void ShowJob(object? view, string jobOrderNumber)
    {
        if (view is not ServiceJobHistoryView history || string.IsNullOrWhiteSpace(jobOrderNumber)) return;
        history.JobNumber = jobOrderNumber;
        _ = history.ActivateAsync();
    }

    public static ServiceScreenView Create(string taskId, Func<ServiceReportQuery> query, ServiceExcelExport export, ServiceTaskNavigate? navigate = null)
    {
        ServiceScreenView view = taskId switch
        {
            JobsTask => new ServiceJobsView(query, export),
            PendingTask => new ServicePendingView(query, export),
            JobHistoryTask => new ServiceJobHistoryView(query, export),
            MoneyTask => new ServiceMoneyView(query, export),
            PartsTask => new ServicePartsView(query, export, navigate),
            _ => throw new ArgumentOutOfRangeException(nameof(taskId), taskId, "Not a Service centre task.")
        };
        _ = view.ActivateAsync();
        return view;
    }
}
