using Etp.Reporting.Desktop;

using System.Windows;

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

    // The shop's touch panel. These tests used to read the screen the tests ran on, and started
    // failing when the development machine moved to a 3440x1440 monitor - where the platform
    // default already shows every section, so there is nothing to fix. The rule is asserted
    // against the screen it exists for, and a large one, never against whatever is attached.
    private const double ShopPanelHeight = 768;
    private const double Row = 44;

    [Fact]
    public void Every_rail_shows_all_of_its_sections_without_scrolling_on_the_shop_panel()
    {
        // Seven rows is the platform default on a 768-pixel panel. Settings has nine sections,
        // so Registers and Help sat below the fold and the owner concluded the help module was gone.
        foreach (var rail in TaskNavigation.Sections)
        {
            var tabs = TaskNavigation.InSection(rail, ShellAccess.Owner).Select(t => t.Tab).Distinct().ToArray();
            Assert.True(MainWindow.DropDownHeightFor(tabs.Length, ShopPanelHeight) >= tabs.Length * Row,
                $"{rail} has {tabs.Length} sections and they do not all fit on the shop panel.");
        }
        var settings = TaskNavigation.InSection("Settings", ShellAccess.Owner).Select(t => t.Tab).Distinct().ToArray();
        Assert.Contains("Help", settings);
        Assert.Contains("Registers", settings);
    }

    [Theory]
    [InlineData(768)]
    [InlineData(1080)]
    [InlineData(1440)]
    public void A_list_is_never_cut_short_and_never_taller_than_the_bound(double screenHeight)
    {
        foreach (var items in new[] { 2, 9, 22 })
        {
            var height = MainWindow.DropDownHeightFor(items, screenHeight);
            // Never shorter than its contents need, up to the bound...
            Assert.True(height >= Math.Min(items * Row, screenHeight * 0.6), $"{items} items cut short at {screenHeight}px.");
            // ...and never taller than the bound, so a long list scrolls rather than covering the screen.
            Assert.True(height <= screenHeight * 0.6 + 0.001, $"{items} items too tall at {screenHeight}px.");
        }
    }

    [Fact]
    public void On_the_shop_panel_the_rule_shows_more_than_the_platform_default_would()
    {
        // The case the fix exists for: nine sections need more than a third of 768 pixels.
        var platformDefault = ShopPanelHeight / 3;
        Assert.True(9 * Row > platformDefault, "The premise no longer holds; revisit this test.");
        Assert.True(MainWindow.DropDownHeightFor(9, ShopPanelHeight) > platformDefault);
    }

    [Fact]
    public void A_task_reads_as_its_title_rather_than_as_a_record_dump()
    {
        // The shell binds the record itself into a ComboBox, so ToString is what assistive
        // technology announces.
        var task = TaskNavigation.Find("health")!;
        Assert.Equal("Database health", task.ToString());
        Assert.DoesNotContain("Destination", task.ToString());
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
