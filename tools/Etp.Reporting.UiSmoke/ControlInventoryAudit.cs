using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Etp.Reporting.Desktop;

internal static class ControlInventoryAudit
{
    private sealed record Original(string Owner, string Id, string Type, string Label, Control Control);
    public static void Run(MainWindow window, TaskNavigator navigator, string output)
    {
        var owners = typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.GetValue(window)).OfType<UserControl>().Distinct().ToArray();
        var original = owners.SelectMany(view => Walk(view, false).OfType<Control>().Where(IsTaskControl)
            .Select(control => new Original(view.GetType().Name, control.Name, control.GetType().Name, Label(control), control))).ToArray();
        var covered = new Dictionary<Control, List<string>>();
        var routeControls = new List<object>();
        foreach (var task in TaskNavigation.All.Where(task => task.Available && task.Section != "profile"))
        {
            if (!navigator.DisplayTaskRoute(task.Route)) throw new InvalidOperationException($"No task composition for {task.Id}");
            window.Measure(new Size(1000,600)); window.Arrange(new Rect(0,0,1000,600)); window.UpdateLayout();
            if (((ContentControl)window.FindName("FocusedWorkspaceHost")).Content is not DependencyObject content) continue;
            var controls = Walk(content, true).OfType<Control>().Where(IsTaskControl).Distinct().ToArray();
            foreach (var control in controls)
            {
                if (!covered.TryGetValue(control, out var routes)) covered[control] = routes = [];
                routes.Add(task.Id);
            }
            routeControls.Add(new { task.Id, task.Path, controls = controls.Select(control => new { id = control.Name, type = control.GetType().Name, label = Label(control), enabled = control.IsEnabled }).ToArray() });
        }
        var inventory = original.Select(item => new { item.Owner, item.Id, item.Type, item.Label, tag = item.Control.Tag?.ToString(), routes = covered.GetValueOrDefault(item.Control, []), result = covered.ContainsKey(item.Control) ? "EXPOSED IN TASK COMPOSITION" : "REQUIRES RECONCILIATION" }).ToArray();
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(output, "original-controls.json"), JsonSerializer.Serialize(inventory, options));
        File.WriteAllText(Path.Combine(output, "task-controls.json"), JsonSerializer.Serialize(routeControls, options));
        File.WriteAllText(Path.Combine(output, "method.txt"), "Direct WPF logical task composition under synthetic Owner, bypassing navigation guards. Captures retained controls present in a focused task; it does not invoke actions or prove navigation or installed UI interaction. Collapsed descendants are excluded. Tab content and menus are included as reachable subordinate surfaces.\n");
        Console.WriteLine($"Original controls: {inventory.Length}; exposed: {inventory.Count(item => item.result.StartsWith("EXPOSED"))}; reconcile: {inventory.Count(item => item.result.StartsWith("REQUIRES"))}.");
    }
    private static bool IsTaskControl(Control control) => control is ButtonBase or TextBox or ComboBox or DatePicker or DataGrid
        && !control.Name.StartsWith("PART_", StringComparison.Ordinal) && (control.Name.Length > 0 || Label(control).Length > 0);
    private static string Label(Control control) => AutomationProperties.GetName(control) is { Length: > 0 } name ? name
        : control is ContentControl { Content: string label } ? label : control.Name;
    private static IEnumerable<DependencyObject> Walk(DependencyObject root, bool visibleOnly)
    {
        if (visibleOnly && root is UIElement { Visibility: Visibility.Collapsed or Visibility.Hidden }) yield break;
        yield return root;
        if (root is FrameworkElement { ContextMenu: { } menu }) foreach (var descendant in Walk(menu, visibleOnly)) yield return descendant;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var descendant in Walk(child, visibleOnly)) yield return descendant;
    }
}
