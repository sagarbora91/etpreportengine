using System.Runtime.CompilerServices;
using System.Windows;

namespace Etp.Reporting.Desktop;

/// <summary>
/// 1.12 wave 2 (B2). The window-side facts the update screens need, kept in a partial of their own so no other lane's
/// MainWindow file changes (the same pattern as IShellBanner.cs).
/// </summary>
public partial class MainWindow
{
    /// <summary>The welcome overlay is gone and the signed-in role is known: the earliest moment an update notice may appear.</summary>
    internal bool IsReadyForUpdates => WelcomeOverlay.Visibility != Visibility.Visible && CurrentShellAccess.HasAssignedRole;

    /// <summary>
    /// Any screen holds unsaved work (the same list <c>TaskNavigator.HasUnsavedDrafts</c> and its retained-draft list guard
    /// the window close with). The update gate refuses while this is true, so the window close that follows an update
    /// never raises the "unsaved work" question and the setup program is not left waiting.
    /// </summary>
    internal bool HasUnsavedWorkForUpdate() =>
        registersWorkspaceView.HasUnsavedChanges
        || dailyWorkflowWorkspace.UnsavedDrafts.Count > 0
        || settingsWorkspace.HasProductDraft
        || operationsWorkspaceView.HasWatchDraft
        || operationsWorkspaceView.UnsavedSchedules.Count > 0
        || administrationWorkspaceView.UnsavedDrafts.Count > 0
        || archiveWorkspaceView.UnsavedContacts.Count > 0
        || accountingWorkspaceView.HasRetainedDraft
        || investigationWorkspaceView.HasRetainedDraft
        || operationsWorkspaceView.HasRetainedDraft;
}

/// <summary>
/// Runs a callback once for one window when it has loaded, without a change to the window's own start-up code. The
/// composition root's <c>RegisterUpdates</c> hook only receives the screen registry, so the callback is keyed by that
/// registry (one per window) and found again through the window's <see cref="MainWindow.Screens"/>.
/// </summary>
internal static class UpdateStartupHook
{
    private static readonly ConditionalWeakTable<ScreenRegistry, Action<MainWindow>> Pending = new();
    private static int classHandlerRegistered;

    public static void WhenLoaded(ScreenRegistry registry, Action<MainWindow> attach)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(attach);
        Pending.AddOrUpdate(registry, attach);
        if (Interlocked.Exchange(ref classHandlerRegistered, 1) == 0)
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (!Pending.TryGetValue(window.Screens, out var attach)) return;
        Pending.Remove(window.Screens);
        try { attach(window); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Updates are a convenience: a failure here must never stop ETP from starting.
            DesktopDiagnostics.Record(exception, "Updates.Startup", "UPDATE_SHELL_NOT_ATTACHED", DesktopDiagnosticSeverity.Warning);
        }
    }
}
