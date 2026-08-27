using Etp.Reporting.Desktop;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ShellNavigationServiceTests
{
    [Fact]
    public void Starts_at_module_home_with_no_back_or_forward_history()
    {
        var navigation = new ShellNavigationService();

        Assert.Equal(WorkspaceRoute.Home, navigation.Current);
        Assert.False(navigation.CanGoBack);
        Assert.False(navigation.CanGoForward);
        Assert.Equal([WorkspaceRoute.Home], navigation.History);
    }

    [Fact]
    public void Navigate_back_and_forward_preserve_report_feature_codes()
    {
        var navigation = new ShellNavigationService();
        navigation.Navigate(new("Sales Reports"), ShellAccess.Viewer);
        navigation.Navigate(new("Sales Reports", "dsr"), ShellAccess.Viewer);

        Assert.Equal(new WorkspaceRoute("Sales Reports"), navigation.GoBack(ShellAccess.Viewer).RequestedRoute);
        Assert.Equal(new WorkspaceRoute("Sales Reports", "dsr"), navigation.GoForward(ShellAccess.Viewer).RequestedRoute);
        Assert.Equal("dsr", navigation.Current.FeatureCode);
    }

    [Fact]
    public void New_navigation_after_back_discards_the_abandoned_forward_path()
    {
        var navigation = new ShellNavigationService();
        navigation.Navigate(new("Sales Reports"), ShellAccess.Viewer);
        navigation.Navigate(new("Report Archive"), ShellAccess.Viewer);
        navigation.GoBack(ShellAccess.Viewer);

        navigation.Navigate(new("Dashboard"), ShellAccess.Viewer);

        Assert.False(navigation.CanGoForward);
        Assert.Equal([WorkspaceRoute.Home, new("Sales Reports"), new("Dashboard")], navigation.History);
    }

    [Fact]
    public void Denied_navigation_does_not_change_current_route_or_history()
    {
        var navigation = new ShellNavigationService();

        var decision = navigation.Navigate(new("Import ETP"), ShellAccess.Viewer);

        Assert.False(decision.IsAllowed);
        Assert.Equal("Owner or Store Manager permission is required to import ETP reports.", decision.DenialReason);
        Assert.Equal(WorkspaceRoute.Home, navigation.Current);
        Assert.Equal([WorkspaceRoute.Home], navigation.History);
    }

    [Fact]
    public void Database_setup_access_can_open_settings_but_not_operational_routes()
    {
        var navigation = new ShellNavigationService();

        Assert.True(navigation.Navigate(new("Settings"), ShellAccess.DatabaseSetup).IsAllowed);
        Assert.False(navigation.Navigate(new("Dashboard"), ShellAccess.DatabaseSetup).IsAllowed);
    }

    [Theory]
    [InlineData("Dashboard", "dashboard")]
    [InlineData("Daily Workflow", "dashboard")]
    [InlineData("Sales Reports", "reports")]
    [InlineData("Stock Reports", "reports")]
    [InlineData("Import ETP", "imports")]
    [InlineData("Registers", "registers")]
    [InlineData("Accounting", "accounting")]
    [InlineData("Report Archive", "archive")]
    [InlineData("Operations Center", "exceptions")]
    [InlineData("Admin / Settings", "settings")]
    public void Every_destination_resolves_to_its_existing_module(string destination, string expectedModule)
    {
        Assert.Equal(expectedModule, ShellRouteRegistry.Find(destination)?.ModuleId);
    }

    [Fact]
    public void Unknown_destinations_are_rejected_without_mutating_history()
    {
        var navigation = new ShellNavigationService();

        var decision = navigation.Navigate(new("Not Registered"), ShellAccess.Owner);

        Assert.False(decision.IsAllowed);
        Assert.Null(decision.Descriptor);
        Assert.Equal([WorkspaceRoute.Home], navigation.History);
    }

    [Fact]
    public void Access_change_cannot_move_history_into_a_now_forbidden_route()
    {
        var navigation = new ShellNavigationService();
        navigation.Navigate(new("Import ETP"), ShellAccess.StoreManager);
        navigation.Navigate(new("Dashboard"), ShellAccess.StoreManager);

        var decision = navigation.GoBack(ShellAccess.Viewer);

        Assert.False(decision.IsAllowed);
        Assert.Equal(new WorkspaceRoute("Dashboard"), navigation.Current);
    }
}
