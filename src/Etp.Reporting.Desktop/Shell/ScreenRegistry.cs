using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

/// <summary>What a registered screen is opened with: the task, its route argument and the shell's header scope.</summary>
/// <param name="TaskId">The task row's id (<see cref="TaskNavigation"/>).</param>
/// <param name="Argument">The route argument (a job number, a drill-down key), or null.</param>
/// <param name="BusinessDate">The header's business date.</param>
/// <param name="StoreCode">The header's store, or null for all stores.</param>
/// <param name="Access">The signed-in role's shell rights.</param>
/// <param name="Navigate">Opens another task by id (a Fix button, a drill-down); unknown ids are ignored.</param>
public sealed record ScreenRequest(
    string TaskId,
    string? Argument,
    DateOnly BusinessDate,
    string? StoreCode,
    ShellAccess Access,
    Action<string> Navigate)
{
    /// <summary>
    /// 1.12 U1: opens another task with a route argument (a Fix button passes the import history key). The argument is a
    /// route value only; it is never stored on disk (pins and recents keep task ids). Unknown ids are ignored.
    /// </summary>
    public Action<string, string?> NavigateWithArgument { get; init; } = (taskId, _) => { };
}

/// <summary>
/// 1.12 U1: a registered screen the shell can ask for its state. <see cref="IsBusy"/> keeps the shell from leaving the
/// screen (or closing ETP) while it works, as for the built-in workspaces; <see cref="Refresh"/> is what F5 does on it.
/// </summary>
public interface IShellScreen
{
    bool IsBusy { get; }
    void Refresh();
}

/// <summary>
/// Task id -> view factory (1.12 A1 seam). The composition root registers each wave-2 screen here
/// (<c>DesktopCompositionRoot.Register*</c> partial hooks); <see cref="TaskNavigator"/> asks the registry before its
/// built-in screens, so a new screen needs a task row (U1) and one registration, and no edit to the navigator.
/// A task id can be registered once; the factory runs on every visit and never receives a connection string.
/// </summary>
public sealed class ScreenRegistry
{
    private readonly Dictionary<string, Func<ScreenRequest, UserControl>> factories = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> TaskIds => factories.Keys;

    public void Register(string taskId, Func<ScreenRequest, UserControl> create)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("A screen needs its task id.", nameof(taskId));
        ArgumentNullException.ThrowIfNull(create);
        if (!factories.TryAdd(taskId, create))
            throw new InvalidOperationException($"The screen for task '{taskId}' is already registered.");
    }

    public bool Contains(string taskId) => factories.ContainsKey(taskId);

    /// <summary>The registered screen for the request's task, or false when the task is not registered.</summary>
    public bool TryCreate(ScreenRequest request, out UserControl? view)
    {
        ArgumentNullException.ThrowIfNull(request);
        view = null;
        if (!factories.TryGetValue(request.TaskId, out var create)) return false;
        view = create(request) ?? throw new InvalidOperationException($"The screen for task '{request.TaskId}' returned no view.");
        return true;
    }
}

public partial class MainWindow
{
    /// <summary>Screens registered by the composition root (1.12 A1); <see cref="TaskNavigator"/> consults it first.</summary>
    internal ScreenRegistry Screens { get; } = new();
}
