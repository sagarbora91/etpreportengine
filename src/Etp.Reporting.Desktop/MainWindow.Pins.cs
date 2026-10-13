using System.Windows;
using System.Windows.Automation;
using System.Windows.Shapes;
using Etp.Reporting.Desktop.Modules.Morning;

namespace Etp.Reporting.Desktop;

/// <summary>
/// 1.12 U1 (wave plan C-e): pins and recents that survive a restart. The star beside the page title pins the screen
/// (a report: its favourite, so report favourites show as pins); Ctrl+K lists pinned, then recent, then the rest. Only
/// task ids reach <c>ui-preferences.json</c> (UiPreferenceStore refuses anything else), at most eight of each.
/// </summary>
public partial class MainWindow
{
    /// <summary>The preferences as the shell holds them now (density, favourites, pins, recents).</summary>
    internal UiPreferences UiPreferencesSnapshot => uiPreferences;

    /// <summary>
    /// N1's "Mark as no-sale day" / "Withdraw" buttons for the Morning page and the Today checklist (plan section 4). Set
    /// where N1's <c>NoSaleDayActions</c> is built; null in a build without it (the pages then show the text only).
    /// </summary>
    internal NoSaleDayButtons? NoSaleDayButtons { get; set; }

    private TaskDestination? pinnableTask;

    /// <summary>The newest screen goes to the front of the recent list and is saved (a failed save is not an error).</summary>
    internal void RememberRecentTask(string taskId)
    {
        var next = uiPreferences.WithRecent(taskId);
        if (ReferenceEquals(next, uiPreferences) || next.RecentTaskIds.SequenceEqual(uiPreferences.RecentTaskIds)) return;
        uiPreferences = next;
        TrySavePreferences();
    }

    /// <summary>Shows the star for a task that can be pinned (not Help, not the profile) and draws it filled when pinned.</summary>
    internal void UpdatePinButton(TaskDestination? task)
    {
        pinnableTask = task is not null && UiPreferenceStore.IsStorableTaskId(task.Id) && task.Section != "profile" ? task : null;
        if (pinnableTask is null) { PinButton.Visibility = Visibility.Collapsed; return; }
        var pinned = uiPreferences.IsPinned(pinnableTask);
        var star = DesignIcon.Create("IconStar");
        if (pinned) star.SetResourceReference(Shape.FillProperty, "Accent");
        PinButton.Content = star;
        var name = pinned ? "Unpin this screen" : "Pin this screen";
        AutomationProperties.SetName(PinButton, name);
        AutomationProperties.SetItemStatus(PinButton, pinned ? "Pinned" : "");
        PinButton.ToolTip = pinned ? "Pinned: it is listed first in Ctrl+K. Press to unpin." : "Pin this screen to the top of Ctrl+K";
        PinButton.Visibility = Visibility.Visible;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (pinnableTask is not { } task) return;
        var next = uiPreferences.TogglePin(task);
        if (ReferenceEquals(next, uiPreferences))
        {
            ApplicationStatus.Text = $"You can pin up to {UiPreferenceStore.MaximumTaskIds} screens. Unpin one first.";
            return;
        }
        uiPreferences = next;
        TrySavePreferences();
        UpdatePinButton(task);
        ApplicationStatus.Text = uiPreferences.IsPinned(task) ? $"{task.Title} pinned. Ctrl+K lists it first." : $"{task.Title} unpinned.";
    }

    private void TrySavePreferences()
    {
        try { UiPreferenceStore.Save(uiPreferences); }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            DesktopDiagnostics.Record(exception, "Shell.Preferences", "UI_PREFERENCES_SAVE_FAILED", DesktopDiagnosticSeverity.Warning);
        }
    }
}
