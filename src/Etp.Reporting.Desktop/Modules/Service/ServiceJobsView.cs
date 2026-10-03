extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>Service jobs by status: one row per job with its current status list (read rule JobList).</summary>
public sealed class ServiceJobsView : ServiceScreenView
{
    private readonly ComboBox statusFilter = new() { MinWidth = 220, MinHeight = 44, ItemsSource = ServiceScreens.StatusLists, SelectedIndex = 0 };

    public ServiceJobsView(Func<ServiceReportQuery> query, ServiceExcelExport export)
        : base("Service jobs by status",
            "Each job shows the status list it is in now. \"In other lists\" counts the other status lists that have held the job.",
            "Service.Jobs", "SERVICE_JOBS_LOAD_FAILED", query, export)
    {
        FilterBar.Children.Add(new TextBlock { Text = "Status", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        AutomationProperties.SetName(statusFilter, "Status list");
        statusFilter.SelectionChanged += async (_, _) => { if (IsLoaded) await ActivateAsync(); };
        FilterBar.Children.Add(statusFilter);
        SetColumns(
        [
            new("Job number", Width: 150), new("Status", Width: 110), new("Job date", DisplayFormat: "dd MMM yyyy"),
            new("EDD", DisplayFormat: "dd MMM yyyy"), new("Brand"), new("Model", Width: 150), new("Product", Width: 140),
            new("Customer name", Width: 180), new("Spare value", "#,##0.00", "N2"), new("Labour", "#,##0.00", "N2"),
            new("Lines", "#,##0", Width: 70), new("In other lists", "#,##0", Width: 110), new("As at", DisplayFormat: "dd MMM yyyy")
        ]);
    }

    public ServiceListChoice SelectedStatus
    {
        get => (ServiceListChoice)statusFilter.SelectedItem;
        set => statusFilter.SelectedItem = ServiceScreens.StatusLists.Single(choice => choice.Code == value.Code);
    }

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source)
    {
        var rows = await source.LoadJobsByStatusAsync(SelectedStatus.Code);
        return rows.Select(row => new ServiceGridRow(row,
        [
            row.JobOrderNumber, row.StatusLabel, row.JobDate, row.Edd, row.Brand, row.Model, row.ProductCategory,
            row.CustomerName, row.SpareValue, row.LabourCharge, row.Lines, row.InOtherLists, row.SnapshotDate
        ])).ToArray();
    }

    protected override string EmptyRowsText => "No jobs are in this status list.";
    protected override string Summarise(int count) => $"{count:N0} jobs · {SelectedStatus.Label}.";
    protected override string ExportName => "Service jobs by status";
}
