namespace Etp.Reporting.Desktop;

public sealed record HelpWorkspaceSnapshot(
    object? FocusedContent,
    string? FocusedWorkspaceKind,
    string? PageTitle,
    string? PageDescription,
    string? Breadcrumb,
    bool WasSidebarVisible)
{
    public bool CanRestoreFocusedWorkspace =>
        string.Equals(FocusedWorkspaceKind, "report", StringComparison.Ordinal) && FocusedContent is not null;
}

public sealed class HelpWorkspaceSession
{
    public bool IsOpen { get; private set; }
    public HelpWorkspaceSnapshot? ReturnState { get; private set; }

    public bool Open(HelpWorkspaceSnapshot returnState)
    {
        ArgumentNullException.ThrowIfNull(returnState);
        if (IsOpen) return false;

        ReturnState = returnState;
        IsOpen = true;
        return true;
    }

    public HelpWorkspaceSnapshot? Close()
    {
        if (!IsOpen) return null;

        var returnState = ReturnState;
        Clear();
        return returnState;
    }

    public void Abandon() => Clear();

    private void Clear()
    {
        IsOpen = false;
        ReturnState = null;
    }
}
