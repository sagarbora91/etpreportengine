extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;
using ServiceStages = EtpApplication::Etp.Reporting.Application.Service.ServiceStages;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// The screen of a Service tab whose 1.10.0 lane has not landed yet (Claims, Parts). It keeps the Service frame
/// (title, freshness strip) and offers the interim screens that hold the nearest answer. Replaced by the lane's screen
/// in <see cref="ServiceScreens.Create"/>; nothing is queried beyond the refresh log.
/// </summary>
public sealed class ServicePlaceholderView : ServiceScreenView
{
    /// <summary>What a placeholder stands in for: its title, its intro and the interim screens it points at.</summary>
    public sealed record Definition(string TaskId, string Title, string Intro, IReadOnlyList<(string Label, ServiceDrillDown Target)> Links);

    public static readonly Definition Claims = new(ServiceScreens.ClaimsTask, "Service claims",
        "Claims raised with Titan per month and type (GPRC, Module Bank, WDC, WRA) and the DC/RA jobs not yet claimed arrive with the 1.10.0 Claims screen. Until then, the DC and RA jobs are on the jobs list.",
        [
            ("Open DC jobs (S014)", new(ServiceScreens.JobsTask, ServiceStages.DcIssued)),
            ("Open RA jobs (S016)", new(ServiceScreens.JobsTask, ServiceStages.RaIssued))
        ]);

    public static readonly Definition Parts = new(ServiceScreens.PartsTask, "Service parts and purchases",
        "Purchase invoices created and received, open invoices with their age, goods in transit and the jobs waiting for a part arrive with the 1.10.0 Parts screen. Until then, the jobs waiting for a part are on the Pending repair list.",
        [
            ("Open Pending repair (S009)", new(ServiceScreens.PendingTask, ServiceStages.IndentRaised)),
            ("Open IR jobs (S015)", new(ServiceScreens.JobsTask, ServiceStages.IndentRaised))
        ]);

    public const string NotYetText = "This screen arrives with 1.10.0.";

    public ServicePlaceholderView(Definition definition, Func<ServiceReportQuery> query, ServiceExcelExport export, Action<ServiceDrillDown>? navigate)
        : base(definition.Title, definition.Intro, "Service.Placeholder", "SERVICE_PLACEHOLDER_LOAD_FAILED", query, export)
    {
        TaskId = definition.TaskId;
        foreach (var (label, target) in definition.Links)
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), IsEnabled = navigate is not null };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => navigate?.Invoke(target);
            FilterBar.Children.Add(button);
        }
        SetColumns([]);
    }

    public string TaskId { get; }

    protected override Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source) =>
        Task.FromResult<IReadOnlyList<ServiceGridRow>>([]);

    protected override string EmptyRowsText => NotYetText + " Use the buttons above for the interim lists.";
}
