using Etp.Reporting.Desktop;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class WorkspaceModuleOwnershipRegistryTests
{
    [Fact]
    public void Shell_destinations_and_ownership_entries_match_bidirectionally()
    {
        var shellDestinations = ShellRouteRegistry.All.Select(route => route.Destination)
            .ToHashSet(StringComparer.Ordinal);
        var ownedDestinations = WorkspaceModuleOwnershipRegistry.Destinations.Select(owner => owner.Destination)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(shellDestinations.Order(StringComparer.Ordinal), ownedDestinations.Order(StringComparer.Ordinal));
        Assert.All(ShellRouteRegistry.All, route =>
        {
            var owner = Assert.IsType<WorkspaceModuleOwnership>(
                WorkspaceModuleOwnershipRegistry.Find(new WorkspaceRoute(route.Destination)));
            Assert.Equal(route.ModuleId, owner.ModuleId);
        });
        Assert.All(WorkspaceModuleOwnershipRegistry.Destinations, owner =>
        {
            var route = Assert.IsType<ShellRouteDescriptor>(ShellRouteRegistry.Find(owner.Destination));
            Assert.Equal(route.ModuleId, owner.ModuleId);
        });
    }

    [Fact]
    public void Executable_report_routes_and_ownership_entries_match_bidirectionally()
    {
        var catalogueCodes = ProductReportCatalogue.All.Select(report => report.Code)
            .ToHashSet(StringComparer.Ordinal);
        var ownedCodes = WorkspaceModuleOwnershipRegistry.ReportRoutes.Select(owner => owner.FeatureCode!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(catalogueCodes.Order(StringComparer.Ordinal), ownedCodes.Order(StringComparer.Ordinal));
        Assert.All(ProductReportCatalogue.All, report =>
        {
            var owner = Assert.IsType<WorkspaceModuleOwnership>(
                WorkspaceModuleOwnershipRegistry.Find(new WorkspaceRoute("Sales Reports", report.Code)));
            Assert.Equal("reports", owner.ModuleId);
        });
        Assert.All(WorkspaceModuleOwnershipRegistry.ReportRoutes, owner =>
        {
            Assert.Contains(ProductReportCatalogue.All, report => report.Code == owner.FeatureCode);
            var destination = Assert.IsType<ShellRouteDescriptor>(ShellRouteRegistry.Find(owner.Destination));
            Assert.Equal(destination.ModuleId, owner.ModuleId);
        });
    }

    [Fact]
    public void Every_route_has_one_owner_and_report_routes_share_their_destination_owner()
    {
        Assert.Equal(WorkspaceModuleOwnershipRegistry.All.Count,
            WorkspaceModuleOwnershipRegistry.All.Select(owner => owner.Route).Distinct().Count());

        Assert.All(WorkspaceModuleOwnershipRegistry.ReportRoutes, reportOwner =>
        {
            var destinationOwner = Assert.IsType<WorkspaceModuleOwnership>(
                WorkspaceModuleOwnershipRegistry.Find(new WorkspaceRoute(reportOwner.Destination)));
            Assert.Equal(destinationOwner.ModuleId, reportOwner.ModuleId);
        });
    }

    [Fact]
    public void Unknown_destination_or_report_feature_has_no_implicit_owner()
    {
        Assert.Null(WorkspaceModuleOwnershipRegistry.Find(new WorkspaceRoute("Not Registered")));
        Assert.Null(WorkspaceModuleOwnershipRegistry.Find(new WorkspaceRoute("Sales Reports", "not-a-report")));
    }
}
