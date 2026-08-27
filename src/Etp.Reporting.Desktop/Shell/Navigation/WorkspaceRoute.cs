namespace Etp.Reporting.Desktop;

public sealed record WorkspaceRoute(string Destination, string? FeatureCode = null)
{
    public static WorkspaceRoute Home { get; } = new("Home");
}

// Compatibility types retained until MainWindow adopts IShellNavigationService.
public sealed record WorkspaceLocation(string Destination, string? FeatureCode = null)
{
    public static WorkspaceLocation Home { get; } = new("Home");

    public WorkspaceRoute ToRoute() => new(Destination, FeatureCode);
}

public sealed class WorkspaceNavigationHistory
{
    private readonly List<WorkspaceLocation> entries = [];
    private int index = -1;

    public WorkspaceLocation? Current => index >= 0 ? entries[index] : null;
    public bool CanGoBack => index > 0;
    public bool CanGoForward => index >= 0 && index < entries.Count - 1;
    public IReadOnlyList<WorkspaceLocation> Entries => entries;

    public bool Visit(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (Current == location) return false;
        if (CanGoForward) entries.RemoveRange(index + 1, entries.Count - index - 1);
        entries.Add(location);
        index = entries.Count - 1;
        return true;
    }

    public WorkspaceLocation? GoBack()
    {
        if (!CanGoBack) return null;
        return entries[--index];
    }

    public WorkspaceLocation? GoForward()
    {
        if (!CanGoForward) return null;
        return entries[++index];
    }
}
