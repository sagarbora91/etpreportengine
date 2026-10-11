using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Etp.Reporting.Desktop;

public sealed record AutomaticImportTaskStatus(string Name, string State, DateTime? LastRun, int? LastResult, DateTime? NextRun, string Message)
{
    public static AutomaticImportTaskStatus Unavailable => new("Windows Task Scheduler", "Unknown", null, null, null,
        "Task status could not be read. Saved automatic-import settings do not prove a scheduled task is installed or running.");
    public string Summary => $"{Name}: {State}. Last run: {(LastRun is { } last ? last.ToString("dd MMM yyyy HH:mm") : "Never / unavailable")}. " +
        $"Last result: {(LastResult is { } code ? $"0x{code:X8}" : "Unavailable")}. Next run: {(NextRun is { } next ? next.ToString("dd MMM yyyy HH:mm") : "Unavailable")}. {Message}";
}

/// <summary>The three scheduled tasks ETP setup registers (scripts install-*-task.ps1).</summary>
public static class EtpScheduledTaskNames
{
    public const string AutomatedOperations = "ETP Reporting Automated Operations";
    public const string DailyBackup = "ETP Reporting Daily Backup";
    public const string MonthlyRecoveryDrill = "ETP Reporting Monthly Recovery Drill";

    public static IReadOnlyList<string> All { get; } = [AutomatedOperations, DailyBackup, MonthlyRecoveryDrill];
}

/// <summary>
/// One ETP task by name (1.11.0 health check OPS-03). <see cref="Found"/> false: Task Scheduler has no such task for this reader.
/// <see cref="Readable"/> false: the task exists but this account may not read it (a task registered under another account).
/// Times are local, as Task Scheduler reports them; <see cref="LastRun"/> and <see cref="LastResult"/> are null when the task never ran.
/// </summary>
public sealed record EtpScheduledTaskInfo(string Name, bool Found, bool Readable, bool Enabled, string State, DateTime? LastRun, int? LastResult, DateTime? NextRun)
{
    public static EtpScheduledTaskInfo NotFound(string name) => new(name, false, false, false, "Not installed", null, null, null);
}

/// <summary>
/// Result of reading the three tasks by name. <see cref="Readable"/> false: Task Scheduler could not be opened at all.
/// <see cref="Elevated"/>: an unelevated account may not see other principals' tasks, so "Found = false" is then not proof the task is missing.
/// </summary>
public sealed record EtpScheduledTaskReadResult(bool Readable, bool Elevated, IReadOnlyList<EtpScheduledTaskInfo> Tasks)
{
    public static EtpScheduledTaskReadResult Unavailable(bool elevated) => new(false, elevated, []);
}

public static class AutomaticImportTaskReader
{
    private const int FileNotFoundHResult = unchecked((int)0x80070002), PathNotFoundHResult = unchecked((int)0x80070003), AccessDeniedHResult = unchecked((int)0x80070005);

    /// <summary>
    /// Reads the named ETP tasks (default: all three) read-only through the Task Scheduler COM API. Never throws for an
    /// expected condition: an unreadable scheduler or task is reported in the result.
    /// </summary>
    public static Task<EtpScheduledTaskReadResult> ReadEtpTasksAsync(IEnumerable<string>? names = null, CancellationToken token = default)
    {
        var wanted = (names ?? EtpScheduledTaskNames.All).ToArray();
        return Task.Run(() => ReadNamed(wanted), token);
    }

    private static bool IsElevated()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static EtpScheduledTaskReadResult ReadNamed(IReadOnlyList<string> names)
    {
        var elevated = IsElevated();
        if (!OperatingSystem.IsWindows()) return EtpScheduledTaskReadResult.Unavailable(elevated);
        object? serviceObject = null;
        var comObjects = new List<object>();
        try
        {
            serviceObject = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            dynamic service = serviceObject!; service.Connect();
            dynamic folder = service.GetFolder("\\"); comObjects.Add(folder);
            var rows = new List<EtpScheduledTaskInfo>();
            foreach (var name in names)
            {
                try
                {
                    dynamic task = folder.GetTask(name); comObjects.Add(task);
                    DateTime last = task.LastRunTime; DateTime next = task.NextRunTime;
                    var enabled = (bool)task.Enabled;
                    rows.Add(new(name, true, true, enabled, !enabled ? "Disabled" : StateName((int)task.State),
                        last.Year > 2000 ? last : null, last.Year > 2000 ? (int)task.LastTaskResult : null, next.Year > 2000 ? next : null));
                }
                catch (COMException ex) when (ex.HResult is FileNotFoundHResult or PathNotFoundHResult)
                {
                    rows.Add(EtpScheduledTaskInfo.NotFound(name));
                }
                catch (UnauthorizedAccessException)
                {
                    rows.Add(new(name, true, false, false, "Unreadable", null, null, null));
                }
                catch (COMException ex) when (ex.HResult == AccessDeniedHResult)
                {
                    rows.Add(new(name, true, false, false, "Unreadable", null, null, null));
                }
            }
            return new(true, elevated, rows);
        }
        catch { return EtpScheduledTaskReadResult.Unavailable(elevated); }
        finally
        {
            for (var index = comObjects.Count - 1; index >= 0; index--)
                if (Marshal.IsComObject(comObjects[index])) Marshal.ReleaseComObject(comObjects[index]);
            if (serviceObject is not null && Marshal.IsComObject(serviceObject)) Marshal.ReleaseComObject(serviceObject);
        }
    }

    public static Task<IReadOnlyList<AutomaticImportTaskStatus>> ReadAsync(CancellationToken token = default) =>
        Task.Run<IReadOnlyList<AutomaticImportTaskStatus>>(() => Read(), token);

    private static IReadOnlyList<AutomaticImportTaskStatus> Read()
    {
        if (!OperatingSystem.IsWindows()) return [AutomaticImportTaskStatus.Unavailable];
        object? serviceObject = null;
        var comObjects = new List<object>();
        try
        {
            serviceObject = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            dynamic service = serviceObject!; service.Connect();
            dynamic folder = service.GetFolder("\\"); comObjects.Add(folder);
            dynamic tasks = folder.GetTasks(1); comObjects.Add(tasks);
            var rows = new List<AutomaticImportTaskStatus>();
            for (var index = 1; index <= (int)tasks.Count; index++)
            {
                dynamic task = tasks[index]; comObjects.Add(task);
                dynamic definition = task.Definition; comObjects.Add(definition);
                dynamic actions = definition.Actions; comObjects.Add(actions);
                var matches = false;
                for (var actionIndex = 1; actionIndex <= (int)actions.Count; actionIndex++)
                {
                    dynamic action = actions[actionIndex]; comObjects.Add(action);
                    if ((int)action.Type == 0 && Path.GetFileName((string)action.Path).Equals("Etp.Reporting.Desktop.exe", StringComparison.OrdinalIgnoreCase)
                        && ((string)action.Arguments).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("--automation-once")) matches = true;
                }
                if (!matches) continue;
                DateTime last = task.LastRunTime; DateTime next = task.NextRunTime;
                rows.Add(new((string)task.Name, !(bool)task.Enabled ? "Disabled" : StateName((int)task.State),
                    last.Year > 2000 ? last : null, last.Year > 2000 ? (int)task.LastTaskResult : null, next.Year > 2000 ? next : null,
                    "Observed on this PC. Task scheduling is configured in Windows, separately from the saved import enable switch."));
            }
            return rows.Count == 0 ? [new("Automatic import", "Not installed", null, null, null,
                "No Windows task launching ETP automatic import was found. Run now remains available; enabling saved settings does not install a task.")] : rows;
        }
        catch { return [AutomaticImportTaskStatus.Unavailable]; }
        finally
        {
            for (var index = comObjects.Count - 1; index >= 0; index--)
                if (Marshal.IsComObject(comObjects[index])) Marshal.ReleaseComObject(comObjects[index]);
            if (serviceObject is not null && Marshal.IsComObject(serviceObject)) Marshal.ReleaseComObject(serviceObject);
        }
    }

    public static string StateName(int state) => state switch { 1 => "Disabled", 2 => "Queued", 3 => "Ready", 4 => "Running", _ => "Unknown" };
}
