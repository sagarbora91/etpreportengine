using Etp.Reporting.Desktop;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void Starts_at_home_with_descriptor_derived_presentation()
    {
        var viewModel = new ShellViewModel(new ShellNavigationService());
        var descriptor = Assert.IsType<ShellRouteDescriptor>(ShellRouteRegistry.Find("Home"));

        Assert.Equal(WorkspaceRoute.Home, viewModel.CurrentRoute);
        Assert.False(viewModel.CanGoBack);
        Assert.False(viewModel.CanGoForward);
        Assert.Equal([WorkspaceRoute.Home], viewModel.History);
        Assert.Equal(PresentationFrom(descriptor), viewModel.CurrentPresentation);
        Assert.True(viewModel.LastNavigationDecision.IsAllowed);
    }

    [Fact]
    public void Allowed_navigation_updates_route_decision_and_presentation()
    {
        var viewModel = new ShellViewModel(new ShellNavigationService());

        var decision = viewModel.Navigate(new("Sales Reports", "dsr"), ShellAccess.Viewer);

        var descriptor = Assert.IsType<ShellRouteDescriptor>(ShellRouteRegistry.Find("Sales Reports"));
        Assert.Same(decision, viewModel.LastNavigationDecision);
        Assert.True(decision.IsAllowed);
        Assert.Equal(new WorkspaceRoute("Sales Reports", "dsr"), viewModel.CurrentRoute);
        Assert.Equal(PresentationFrom(descriptor), viewModel.CurrentPresentation);
        Assert.True(viewModel.CanGoBack);
        Assert.False(viewModel.CanGoForward);
    }

    [Fact]
    public void Denied_navigation_keeps_current_route_and_presentation_and_exposes_reason()
    {
        var viewModel = new ShellViewModel(new ShellNavigationService());
        var originalPresentation = viewModel.CurrentPresentation;

        var decision = viewModel.Navigate(new("Import ETP"), ShellAccess.Viewer);

        Assert.Same(decision, viewModel.LastNavigationDecision);
        Assert.False(decision.IsAllowed);
        Assert.Equal(
            "Owner or Store Manager permission is required to import ETP reports.",
            decision.DenialReason);
        Assert.Equal(WorkspaceRoute.Home, viewModel.CurrentRoute);
        Assert.Same(originalPresentation, viewModel.CurrentPresentation);
        Assert.Equal([WorkspaceRoute.Home], viewModel.History);
        Assert.False(viewModel.CanGoBack);
        Assert.False(viewModel.CanGoForward);
    }

    [Fact]
    public void Back_and_forward_surface_history_state_and_restore_metadata()
    {
        var viewModel = new ShellViewModel(new ShellNavigationService());
        viewModel.Navigate(new("Dashboard"), ShellAccess.Viewer);
        viewModel.Navigate(new("Report Archive"), ShellAccess.Viewer);

        var back = viewModel.GoBack(ShellAccess.Viewer);

        Assert.True(back.IsAllowed);
        Assert.Equal(new WorkspaceRoute("Dashboard"), viewModel.CurrentRoute);
        Assert.Equal("dashboard", viewModel.CurrentPresentation.ModuleId);
        Assert.True(viewModel.CanGoBack);
        Assert.True(viewModel.CanGoForward);

        var forward = viewModel.GoForward(ShellAccess.Viewer);

        Assert.True(forward.IsAllowed);
        Assert.Equal(new WorkspaceRoute("Report Archive"), viewModel.CurrentRoute);
        Assert.Equal("archive", viewModel.CurrentPresentation.ModuleId);
        Assert.True(viewModel.CanGoBack);
        Assert.False(viewModel.CanGoForward);
    }

    [Fact]
    public void Shell_view_model_state_is_framework_and_feature_neutral()
    {
        var stateTypes = typeof(ShellViewModel)
            .GetFields(System.Reflection.BindingFlags.Instance |
                       System.Reflection.BindingFlags.Public |
                       System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .Concat(typeof(ShellViewModel).GetProperties().Select(property => property.PropertyType))
            .Select(UnwrapSequence)
            .ToArray();

        string[] forbiddenNamespaces =
        [
            "System.Windows",
            "Etp.Reporting.Desktop.Modules",
            "Etp.Reporting.Import",
            "Etp.Reporting.Infrastructure",
            "Etp.Reporting.Reporting",
            "Microsoft.Data.SqlClient"
        ];

        foreach (var type in stateTypes)
        {
            Assert.DoesNotContain(
                forbiddenNamespaces,
                forbiddenNamespace => (type.Namespace ?? string.Empty).StartsWith(
                    forbiddenNamespace,
                    StringComparison.Ordinal));
        }

        Assert.DoesNotContain(
            typeof(ShellViewModel).GetProperties(),
            property => property.Name.Contains("Dashboard", StringComparison.Ordinal) ||
                        property.Name.Contains("Import", StringComparison.Ordinal) ||
                        property.Name.Contains("Report", StringComparison.Ordinal) ||
                        property.Name.Contains("Sql", StringComparison.Ordinal));
    }

    private static ShellPresentationMetadata PresentationFrom(ShellRouteDescriptor descriptor) =>
        new(
            descriptor.Destination,
            descriptor.ModuleId,
            descriptor.Description,
            descriptor.Heading,
            descriptor.Message,
            descriptor.ActionLabel,
            descriptor.ActionDestination);

    private static Type UnwrapSequence(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            ? type.GetGenericArguments()[0]
            : type;
}
