using Etp.Reporting.Desktop;

namespace Etp.Reporting.Desktop.Tests;

public sealed class TaskNavigationTests
{
    [Fact]
    public void Store_manager_can_open_brand_editor_without_other_administration_access()
    {
        var navigation = new ShellNavigationService();
        var brands = TaskNavigation.Find("masters")!;
        Assert.Contains(brands, TaskNavigation.InSection("Settings", ShellAccess.StoreManager));
        Assert.True(navigation.Navigate(brands.Route, ShellAccess.StoreManager).IsAllowed);
        Assert.False(navigation.Navigate(brands.Route, ShellAccess.Viewer).IsAllowed);
        foreach (var id in new[] { "users", "connection", "tender-rules", "stores" })
            Assert.False(navigation.Navigate(TaskNavigation.Find(id)!.Route, ShellAccess.StoreManager).IsAllowed);
        Assert.False(navigation.Navigate(new("Admin / Settings"), ShellAccess.StoreManager).IsAllowed);
    }

    [Fact]
    public void Personal_display_preferences_remain_available_without_administration_access()
    {
        foreach (var access in new[] { ShellAccess.Viewer, ShellAccess.StoreManager, ShellAccess.Owner })
        {
            var navigation = new ShellNavigationService();
            var display = TaskNavigation.Search("Display", access).Single(x => x.Id == "settings");
            Assert.False(navigation.Navigate(new("Home", TaskId: "overview:Settings"), access).IsAllowed);
            Assert.False(navigation.Navigate(new("Home", TaskId: "category:Settings:Display"), access).IsAllowed);
            Assert.True(navigation.Navigate(display.Route, access).IsAllowed);
            Assert.Equal(display.Route, navigation.Current);
            // Every owner-only Settings task, not a sample of three: R4 put the database and
            // recovery block behind this gate, so "health" in particular has to be named here.
            foreach (var id in new[] { "connection", "health", "backups", "recovery", "support-package", "audit", "users", "profiles" })
                Assert.Equal(access.CanAdminister, navigation.Navigate(TaskNavigation.Find(id)!.Route, access).IsAllowed);
        }
        Assert.False(new ShellNavigationService().Navigate(TaskNavigation.Find("settings")!.Route, ShellAccess.DatabaseSetup).IsAllowed);
    }

    [Theory]
    [InlineData("DSR", "report-dsr", "Today → Sales → Sales")]
    [InlineData("support package", "support-package", "Settings → Database → Support package")]
    [InlineData("restore", "recovery", "Settings → Database → Recovery drill")]
    public void Search_opens_canonical_destination(string query, string id, string path)
    {
        var task = TaskNavigation.Search(query, ShellAccess.Owner).First();
        Assert.Equal(id, task.Id); Assert.Equal(path, task.Path);
        Assert.True(new ShellNavigationService().Navigate(task.Route, ShellAccess.Owner).IsAllowed);
    }

    [Fact]
    public void Roles_and_deferred_features_are_filtered_and_direct_routes_cannot_bypass_them()
    {
        var navigation = new ShellNavigationService();
        Assert.Empty(TaskNavigation.Search("support package", ShellAccess.Viewer));
        Assert.False(navigation.Navigate(TaskNavigation.Find("support-package")!.Route, ShellAccess.Viewer).IsAllowed);
        foreach (var task in TaskNavigation.All.Where(x => !x.Available))
        {
            Assert.DoesNotContain(task, TaskNavigation.Search("", ShellAccess.Owner));
            Assert.False(navigation.Navigate(task.Route, ShellAccess.Owner).IsAllowed);
        }
        Assert.False(navigation.Navigate(new("Dashboard", TaskId: "support-package"), ShellAccess.Owner).IsAllowed);
        Assert.False(navigation.Navigate(new("Dashboard", TaskId: "invented"), ShellAccess.Owner).IsAllowed);
    }

    [Fact]
    public void Every_menu_alias_is_mapped_and_reports_are_unique()
    {
        Assert.All(TaskNavigation.All, task => Assert.Contains(task.Rail, TaskNavigation.Sections));
        Assert.Equal(TaskNavigation.All.Count, TaskNavigation.All.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public void Back_and_forward_retain_exact_task_identity()
    {
        var navigation = new ShellNavigationService();
        var support = TaskNavigation.Find("support-package")!.Route;
        var restore = TaskNavigation.Find("recovery")!.Route;
        navigation.Navigate(support, ShellAccess.Owner); navigation.Navigate(restore, ShellAccess.Owner);
        Assert.Equal(support, navigation.GoBack(ShellAccess.Owner).RequestedRoute);
        Assert.Equal(restore, navigation.GoForward(ShellAccess.Owner).RequestedRoute);
    }

    [Fact]
    public void Every_search_result_can_be_opened_by_the_same_role()
    {
        foreach (var access in new[] { ShellAccess.DatabaseSetup, ShellAccess.Viewer, ShellAccess.StoreManager, ShellAccess.Owner })
        foreach (var task in TaskNavigation.Search("", access))
            Assert.True(new ShellNavigationService().Navigate(task.Route, access).IsAllowed, task.Path);
    }

    [Fact]
    public void Invalid_or_unauthorised_category_routes_are_denied()
    {
        var navigation = new ShellNavigationService();
        Assert.False(navigation.Navigate(new("Home", TaskId: "category:Settings:Database & Recovery"), ShellAccess.Viewer).IsAllowed);
        Assert.False(navigation.Navigate(new("Home", TaskId: "category:Invented"), ShellAccess.Owner).IsAllowed);
        Assert.False(navigation.Navigate(new("Home", TaskId: "overview:Invented"), ShellAccess.Owner).IsAllowed);
    }

    [Fact]
    public void Every_help_task_link_uses_an_executable_route_and_broad_guides_open_their_category()
    {
        foreach(var topic in HelpCentreRegistry.Topics.Where(topic=>topic.Destination is not null))
        {
            var task=HelpTaskRoutes.Find(topic.Id); Assert.NotNull(task);
            Assert.True(new ShellNavigationService().Navigate(task.Route,ShellAccess.Owner).IsAllowed,topic.Id);
        }
        Assert.Equal("report-stock-closing",HelpTaskRoutes.Find("stock-reports")!.Route.TaskId);
        Assert.False(HelpTaskRoutes.Find("administration")!.IsAllowed(ShellAccess.Viewer));
    }
}
