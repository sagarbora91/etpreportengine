extern alias EtpApplication;

using System.Windows.Controls;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

public sealed record ServiceListChoice(string? Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The Service centre tab on the Reports rail: its four task ids and the lists the screens offer.</summary>
public static class ServiceScreens
{
    public const string Destination = "Service Centre";
    public const string JobsTask = "service-jobs";
    public const string PendingTask = "service-pending";
    public const string JobHistoryTask = "service-job-history";
    public const string MoneyTask = "service-money";

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

    /// <summary>The pending lists, by report code, as IServiceReportQuery.LoadPendingAsync takes them.</summary>
    public static IReadOnlyList<ServiceListChoice> PendingLists { get; } =
    [
        new("S009", "Pending repair (S009)"), new("S010", "Pending delivery (S010)"), new("S011", "SRN status (S011)")
    ];

    /// <summary>
    /// A query factory for a build whose composition root has no Service read model. The screens
    /// then show this message through DesktopFriendlyError instead of failing to open.
    /// </summary>
    public static ServiceReportQuery Unavailable() =>
        throw new InvalidOperationException("The Service read model is not available in this build.");

    public static UserControl Create(string taskId, Func<ServiceReportQuery> query, ServiceExcelExport export)
    {
        ServiceScreenView view = taskId switch
        {
            JobsTask => new ServiceJobsView(query, export),
            PendingTask => new ServicePendingView(query, export),
            JobHistoryTask => new ServiceJobHistoryView(query, export),
            MoneyTask => new ServiceMoneyView(query, export),
            _ => throw new ArgumentOutOfRangeException(nameof(taskId), taskId, "Not a Service centre task.")
        };
        _ = view.ActivateAsync();
        return view;
    }
}
