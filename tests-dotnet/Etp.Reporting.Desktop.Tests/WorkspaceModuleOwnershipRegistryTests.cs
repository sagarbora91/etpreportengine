using Etp.Reporting.Reporting;
namespace Etp.Reporting.Desktop.Tests;
public sealed class TaskOwnershipTests
{
    [Fact]
    public void Every_task_has_one_route_and_a_known_destination()
    {
        Assert.Equal(TaskNavigation.All.Count,TaskNavigation.All.Select(t=>t.Route).Distinct().Count());
        Assert.All(TaskNavigation.All,t=>Assert.NotNull(ShellRouteRegistry.Find(t.Destination)));
    }
    [Fact]
    public void Removed_aliases_and_unregistered_tasks_cannot_open()
    {
        var navigation=new ShellNavigationService();
        foreach(var route in new[]{new WorkspaceRoute("Stock Reports"),new WorkspaceRoute("Masters"),new WorkspaceRoute("Sales Reports",TaskId:"missing")})
            Assert.False(navigation.Navigate(route,ShellAccess.Owner).IsAllowed);
    }
}
