using System.Windows.Input;

namespace Etp.Reporting.Desktop;

public sealed record WorkspaceLocation(string Destination, string? FeatureCode = null)
{
    public static WorkspaceLocation Home { get; } = new("Home");
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

public enum ShellCommand
{
    None,
    Back,
    Forward,
    Home,
    Help,
    ShortcutGuide,
    Refresh,
    Search,
    Run,
    ExportPdf,
    ExportExcel,
    GenerateReportPack,
    OpenExportFolder,
    FocusPeriod,
    GoToReport,
    Save,
    ImportFiles,
    ImportFolder,
    CycleRegion,
    CloseOrCancel
}

public sealed record ShellShortcut(Key Key, ModifierKeys Modifiers, ShellCommand Command, string Display, string Description);

public static class ShellShortcutRegistry
{
    public static IReadOnlyList<ShellShortcut> All { get; } =
    [
        new(Key.Left, ModifierKeys.Alt, ShellCommand.Back, "Alt + Left", "Go to the previous screen"),
        new(Key.Right, ModifierKeys.Alt, ShellCommand.Forward, "Alt + Right", "Go to the next screen"),
        new(Key.Home, ModifierKeys.Alt, ShellCommand.Home, "Alt + Home", "Open Dashboard"),
        new(Key.F1, ModifierKeys.None, ShellCommand.Help, "F1", "Open help for this screen"),
        new(Key.Oem2, ModifierKeys.Control, ShellCommand.ShortcutGuide, "Ctrl + /", "Open keyboard shortcuts"),
        new(Key.F5, ModifierKeys.None, ShellCommand.Refresh, "F5", "Refresh the current screen"),
        new(Key.F, ModifierKeys.Control, ShellCommand.Search, "Ctrl + F", "Search the current screen"),
        new(Key.Enter, ModifierKeys.Control, ShellCommand.Run, "Ctrl + Enter", "Run the current report"),
        new(Key.P, ModifierKeys.Control, ShellCommand.ExportPdf, "Ctrl + P", "Open PDF or print options"),
        new(Key.X, ModifierKeys.Control | ModifierKeys.Shift, ShellCommand.ExportExcel, "Ctrl + Shift + X", "Export the current report to Excel"),
        new(Key.P, ModifierKeys.Control | ModifierKeys.Shift, ShellCommand.GenerateReportPack, "Ctrl + Shift + P", "Generate a report pack"),
        new(Key.F, ModifierKeys.Control | ModifierKeys.Shift, ShellCommand.OpenExportFolder, "Ctrl + Shift + F", "Open the export folder"),
        new(Key.L, ModifierKeys.Control, ShellCommand.FocusPeriod, "Ctrl + L", "Focus the business date or report period"),
        new(Key.G, ModifierKeys.Control, ShellCommand.GoToReport, "Ctrl + G", "Find and open a report"),
        new(Key.S, ModifierKeys.Control, ShellCommand.Save, "Ctrl + S", "Save the current entry"),
        new(Key.O, ModifierKeys.Control, ShellCommand.ImportFiles, "Ctrl + O", "Select ETP files or a ZIP"),
        new(Key.O, ModifierKeys.Control | ModifierKeys.Shift, ShellCommand.ImportFolder, "Ctrl + Shift + O", "Select an ETP import folder"),
        new(Key.F6, ModifierKeys.None, ShellCommand.CycleRegion, "F6", "Move to the next screen region"),
        new(Key.Escape, ModifierKeys.None, ShellCommand.CloseOrCancel, "Esc", "Close the active panel or cancel")
    ];

    public static ShellCommand Resolve(Key key, Key systemKey, ModifierKeys modifiers)
    {
        var normalized = key == Key.System ? systemKey : key;
        return All.FirstOrDefault(x => x.Key == normalized && x.Modifiers == modifiers)?.Command ?? ShellCommand.None;
    }
}
