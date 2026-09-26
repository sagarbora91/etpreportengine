using System.IO;
using System.Runtime.InteropServices;

namespace Etp.Reporting.Desktop;

public sealed record AutomaticImportTaskStatus(string Name, string State, DateTime? LastRun, int? LastResult, DateTime? NextRun, string Message)
{
    public static AutomaticImportTaskStatus Unavailable => new("Windows Task Scheduler", "Unknown", null, null, null,
        "Task status could not be read. Saved automatic-import settings do not prove a scheduled task is installed or running.");
    public string Summary => $"{Name}: {State}. Last run: {(LastRun is { } last ? last.ToString("dd MMM yyyy HH:mm") : "Never / unavailable")}. " +
        $"Last result: {(LastResult is { } code ? $"0x{code:X8}" : "Unavailable")}. Next run: {(NextRun is { } next ? next.ToString("dd MMM yyyy HH:mm") : "Unavailable")}. {Message}";
}

public static class AutomaticImportTaskReader
{
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
