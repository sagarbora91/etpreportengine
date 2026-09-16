namespace Etp.Reporting.Desktop;

public sealed record WorkspaceRoute(string Destination, string? FeatureCode = null, string? TaskId = null)
{
    public static WorkspaceRoute Home { get; } = new("Home");
}
