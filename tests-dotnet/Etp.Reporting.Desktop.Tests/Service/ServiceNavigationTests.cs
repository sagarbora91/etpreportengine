using Etp.Reporting.Desktop.Modules.Service;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// The Service rail (1.10.0 Service UI wave, design 3.1): its own section between Stock and Settings, six tabs in
/// order, seven read-only tasks for every role (decision 25, Q13), the interim task ids unchanged.
/// </summary>
public sealed class ServiceNavigationTests
{
    [Fact]
    public void Service_is_its_own_rail_section_between_Stock_and_Settings()
    {
        Assert.Equal(["Today", "Import", "Reports", "Stock", "Service", "Settings"], TaskNavigation.Sections);
        Assert.DoesNotContain(TaskNavigation.InSection("Reports", ShellAccess.Owner), task => task.Destination == ServiceScreens.Destination);
    }

    [Fact]
    public void The_Service_rail_has_six_tabs_in_order_with_the_seven_tasks()
    {
        var service = TaskNavigation.InSection("Service", ShellAccess.Viewer);
        Assert.Equal(ServiceScreens.Tabs, service.Select(task => task.Tab).Distinct());
        Assert.Equal(ServiceScreens.Tasks, service.Select(task => task.Id));
        Assert.Equal(["Today", "Pending", "Jobs", "Jobs", "Claims", "Parts", "Money"], service.Select(task => task.Tab));
        Assert.Equal(["Service today", "Service pending lists", "Service job history", "Service jobs by status", "Service claims", "Service parts and purchases", "Service money check"],
            service.Select(task => task.Title));
        Assert.Equal("Service → Today → Service today", TaskNavigation.Find(ServiceScreens.TodayTask)!.Path);
    }

    [Fact]
    public void The_interim_task_ids_are_unchanged_so_favourites_keep_working()
    {
        Assert.Equal("service-jobs", ServiceScreens.JobsTask);
        Assert.Equal("service-pending", ServiceScreens.PendingTask);
        Assert.Equal("service-job-history", ServiceScreens.JobHistoryTask);
        Assert.Equal("service-money", ServiceScreens.MoneyTask);
        Assert.Equal("service-today", ServiceScreens.TodayTask);
        Assert.Equal("service-claims", ServiceScreens.ClaimsTask);
        Assert.Equal("service-parts", ServiceScreens.PartsTask);
    }

    [Fact]
    public void Every_role_can_open_every_Service_screen_and_nothing_needs_the_Store_Manager_role()
    {
        foreach (var access in new[] { ShellAccess.Viewer, ShellAccess.StoreManager, ShellAccess.Owner })
            foreach (var id in ServiceScreens.Tasks)
            {
                var task = TaskNavigation.Find(id)!;
                Assert.Equal(1, task.MinimumRole);
                Assert.Null(task.ReportCode);
                Assert.Equal(ServiceScreens.Destination, task.Destination);
                Assert.Equal("Service", task.Rail);
                Assert.True(new ShellNavigationService().Navigate(task.Route, access).IsAllowed, $"{id} for {access}");
            }
        Assert.Equal(7, TaskNavigation.InSection("Service", ShellAccess.Viewer).Count);
        Assert.False(new ShellNavigationService().Navigate(TaskNavigation.Find(ServiceScreens.TodayTask)!.Route, ShellAccess.DatabaseSetup).IsAllowed);
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
        Assert.Equal(ServiceScreens.TodayTask, TaskNavigation.Search("Service today", ShellAccess.Viewer).First().Id);
        Assert.Contains(TaskNavigation.Search("service job history", ShellAccess.Viewer), task => task.Id == ServiceScreens.JobHistoryTask);
        Assert.Contains(TaskNavigation.Search("claims", ShellAccess.Viewer), task => task.Id == ServiceScreens.ClaimsTask);
    }

    [Fact]
    public void The_Help_topic_opens_Service_today_and_describes_the_six_tabs()
    {
        var topic = HelpCentreRegistry.Find("service-centre");
        Assert.NotNull(topic);
        Assert.Equal(ServiceScreens.TodayTask, HelpTaskRoutes.Find("service-centre")!.Id);
        Assert.Equal("service-centre", ContextHelpRouter.ResolveTopicId(ServiceScreens.Destination));
        Assert.Equal("Service", HelpCentreRegistry.ScreenshotFor("service-centre"));
        foreach (var word in new[] { "Choose Service on the left", "Today", "Pending", "Jobs", "Claims", "Parts", "Money check", "freshness", "amber", "red" })
            Assert.Contains(word, topic.Overview, StringComparison.Ordinal);
        Assert.Contains("Service", HelpCentreRegistry.Topics.Single(x => x.Id == "getting-started").Overview);
    }

    [Theory]
    [InlineData("ON_BENCH", "PENDING_REPAIR", "S032")]
    [InlineData("INDENT_RAISED", "PENDING_REPAIR", "S015")]
    [InlineData("SRN_OUT", "SRN_STATUS", "S033")]
    [InlineData("READY_FOR_DELIVERY", "PENDING_DELIVERY", "S031")]
    [InlineData("IN_TRANSIT_BACK", "PENDING_DELIVERY", null)]
    [InlineData("DELIVERED", null, "S018")]
    [InlineData("RWR", null, "S017")]
    [InlineData("DC_ISSUED", null, "S014")]
    [InlineData("RA_ISSUED", null, "S016")]
    [InlineData("BOOKED", null, null)]
    public void A_stage_maps_to_the_interim_list_that_holds_it(string stage, string? pendingList, string? statusView)
    {
        Assert.Equal(pendingList, ServiceScreens.PendingListForStage(stage));
        Assert.Equal(statusView, ServiceScreens.StatusViewForStage(stage));
    }
}
