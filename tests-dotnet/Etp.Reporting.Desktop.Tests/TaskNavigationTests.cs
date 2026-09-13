using Etp.Reporting.Desktop;

namespace Etp.Reporting.Desktop.Tests;

public sealed class TaskNavigationTests
{
    [Theory]
    [InlineData("DSR", "report-dsr", "Reports → Sales → Daily Sales Report")]
    [InlineData("support package", "support-package", "Settings → Database & Recovery → Support Package")]
    [InlineData("restore", "recovery", "Settings → Database & Recovery → Restore & Recovery Drill")]
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
        Assert.All(UiNavigationRegistry.AllItems, item => Assert.NotNull(TaskNavigation.ForItem(item)));
        Assert.Equal(29, TaskNavigation.All.Count(x => x.ReportCode is not null));
        Assert.Equal(TaskNavigation.All.Count, TaskNavigation.All.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public void Every_legacy_catalogue_button_label_finds_its_original_report()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(root!.FullName, "src", "Etp.Reporting.Desktop", "Modules", "Reports", "ReportsWorkspaceView.xaml"));
        foreach (var button in document.Descendants().Where(element => element.Name.LocalName == "Button" && element.Attribute("Tag") is not null))
        {
            var label = button.Attribute("Content")!.Value; var code = button.Attribute("Tag")!.Value;
            Assert.Contains(label, ReportTaskAliases.For(code));
            Assert.Equal("report-" + code, TaskNavigation.Search(label, ShellAccess.Owner).First().Id);
        }
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
        Assert.Equal("category:Reports:Stock",HelpTaskRoutes.Find("stock-reports")!.Route.TaskId);
        Assert.False(HelpTaskRoutes.Find("administration")!.IsAllowed(ShellAccess.Viewer));
    }
}
