namespace Etp.Reporting.Desktop;

/// <summary>
/// A workspace destination. <c>Argument</c> (1.10.0, Service UI wave) is an optional task argument carried by the
/// route - today the job order number a Service grid passes to Service job history (<c>TaskDestination.RouteWith</c>).
/// It is part of the route's identity, so opening another job is a new history entry and Back returns to the previous job.
/// </summary>
public sealed record WorkspaceRoute(string Destination, string? FeatureCode = null, string? TaskId = null, string? Argument = null)
{
    public static WorkspaceRoute Home { get; } = new("Home");
}
