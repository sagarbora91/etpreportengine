using Etp.Reporting.Desktop.Modules.Service;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>The Service centre tab: last on the Reports rail, four fixed read-only tasks for every role.</summary>
public sealed class ServiceNavigationTests
{
    private static readonly string[] ServiceTasks =
        [ServiceScreens.JobsTask, ServiceScreens.PendingTask, ServiceScreens.JobHistoryTask, ServiceScreens.MoneyTask];

    [Fact]
    public void Service_centre_is_the_last_tab_on_the_Reports_rail_with_its_four_tasks()
    {
        var reports = TaskNavigation.InSection("Reports", ShellAccess.Owner);
        Assert.Equal("Service centre", reports.Select(task => task.Tab).Distinct().Last());
        Assert.Equal(ServiceTasks, reports.Where(task => task.Tab == "Service centre").Select(task => task.Id));
        Assert.Equal(["Service jobs by status", "Service pending lists", "Service job history", "Service money check"],
            ServiceTasks.Select(id => TaskNavigation.Find(id)!.Title));
    }

    [Fact]
    public void Every_role_can_open_every_Service_screen()
    {
        foreach (var access in new[] { ShellAccess.Viewer, ShellAccess.StoreManager, ShellAccess.Owner })
            foreach (var id in ServiceTasks)
            {
                var task = TaskNavigation.Find(id)!;
                Assert.Equal(1, task.MinimumRole);
                Assert.Null(task.ReportCode);
                Assert.Equal(ServiceScreens.Destination, task.Destination);
                Assert.True(new ShellNavigationService().Navigate(task.Route, access).IsAllowed, $"{id} for {access}");
            }
        Assert.False(new ShellNavigationService().Navigate(TaskNavigation.Find(ServiceScreens.JobsTask)!.Route, ShellAccess.DatabaseSetup).IsAllowed);
    }

    [Fact]
    public void The_Service_destination_is_its_own_module_so_report_shortcuts_do_not_act_on_it()
    {
        Assert.Equal("service", ShellRouteRegistry.Find(ServiceScreens.Destination)?.ModuleId);
    }

    [Fact]
    public void Search_finds_the_Service_screens()
    {
        Assert.Equal(ServiceScreens.MoneyTask, TaskNavigation.Search("Service money check", ShellAccess.Viewer).First().Id);
        Assert.Contains(TaskNavigation.Search("service job history", ShellAccess.Viewer), task => task.Id == ServiceScreens.JobHistoryTask);
    }
}
