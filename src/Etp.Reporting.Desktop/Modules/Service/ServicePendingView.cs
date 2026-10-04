extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>Service pending lists: Pending repair and Pending delivery (latest state) and SRN status, oldest first.</summary>
public sealed class ServicePendingView : ServiceScreenView
{
    private readonly ComboBox listFilter = new() { MinWidth = 220, MinHeight = 44, ItemsSource = ServiceScreens.PendingLists, SelectedIndex = 0 };

    public ServicePendingView(Func<ServiceReportQuery> query, ServiceExcelExport export)
        : base("Service pending lists",
            "Pending repair and Pending delivery are the latest lists the Service centre exported. Age is days from the job date to that list's date.",
            "Service.Pending", "SERVICE_PENDING_LOAD_FAILED", query, export)
    {
        FilterBar.Children.Add(new TextBlock { Text = "List", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        AutomationProperties.SetName(listFilter, "Pending list");
        listFilter.SelectionChanged += async (_, _) => { if (IsLoaded) await ActivateAsync(); };
        FilterBar.Children.Add(listFilter);
        SetColumns(
        [
            new("Job number", Width: 150), new("Job date", DisplayFormat: "dd MMM yyyy"), new("Age (days)", "#,##0", Width: 90),
            new("Brand"), new("Model", Width: 150), new("Customer name", Width: 180), new("Pending at", Width: 120),
            new("As at", DisplayFormat: "dd MMM yyyy")
        ]);
    }

    public ServiceListChoice SelectedList
    {
        get => (ServiceListChoice)listFilter.SelectedItem;
        set => listFilter.SelectedItem = ServiceScreens.PendingLists.Single(choice => choice.Code == value.Code);
    }

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source)
    {
        var rows = await source.LoadPendingAsync(SelectedList.Code!);
        return rows
            .OrderByDescending(row => row.AgeDays.HasValue).ThenByDescending(row => row.AgeDays)
            .ThenBy(row => row.JobOrderNumber, StringComparer.OrdinalIgnoreCase)
            .Select(row => new ServiceGridRow(row,
            [
                row.JobOrderNumber, row.JobDate, row.AgeDays, row.Brand, row.Model, row.CustomerName, row.PendingStore, row.SnapshotDate
            ])).ToArray();
    }

    protected override string EmptyRowsText => "No jobs are in this list.";
    protected override string Summarise(int count) => $"{count:N0} jobs · {SelectedList.Label} · oldest first.";
    protected override string ExportName => "Service pending lists";
}
