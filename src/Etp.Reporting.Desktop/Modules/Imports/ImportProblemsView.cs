using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Modules.Imports;

public sealed record ImportProblem(string File, string Status, string Store, string Period, string Detail);

public sealed class ImportProblemsView : UserControl
{
    private readonly ComboBox status = new() { ItemsSource = new[] { "All problems", "Quarantined", "Conflict", "Duplicate", "Failed", "Unknown layout" }, SelectedIndex = 0, MinWidth = 180 };
    private readonly DataGrid rows = new() { AutoGenerateColumns = true, IsReadOnly = true };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private IReadOnlyList<ImportProblem> problems = [];
    public ImportProblemsView(ImportWorkspaceView imports, Func<Task<IReadOnlyList<ImportProblem>>> load)
    {
        var root = new DockPanel { Margin = new Thickness(8) };
        var actions = new WrapPanel();
        var refresh = new Button { Content = "Refresh problems", Margin = new Thickness(8,0,0,0) };
        var retry = new Button { Content = "Retry failed", MinHeight = 44, MinWidth = 112, Margin = new Thickness(8,0,0,0), IsEnabled = imports.CanRetry };
        actions.Children.Add(status); actions.Children.Add(retry); actions.Children.Add(refresh);
        DockPanel.SetDock(actions,Dock.Top);root.Children.Add(actions);
        DockPanel.SetDock(message,Dock.Top);root.Children.Add(message);root.Children.Add(rows);Content=root;
        AutomationProperties.SetName(status,"Problem status filter");
        AutomationProperties.SetName(rows,"Import problems");
        AutomationProperties.SetName(retry,"Retry failed imports");
        AutomationProperties.SetHelpText(retry,"Retry only failed files from the latest import. Keyboard shortcut: Ctrl+R.");
        void UpdateRetry(object? sender, EventArgs args) => retry.IsEnabled = imports.CanRetry;
        status.SelectionChanged += (_,_) => ApplyFilter();
        async Task Refresh()
        {
            refresh.IsEnabled=false;
            try { problems=await load(); ApplyFilter(); message.Text=$"{problems.Count} problems. Select a status to filter."; }
            catch(Exception ex) { message.Text=DesktopFriendlyError.Describe(ex); }
            finally { refresh.IsEnabled=true; retry.IsEnabled=imports.CanRetry; }
        }
        refresh.Click += async (_,_) => await Refresh();
        retry.Click += async (_,_) =>
        {
            if (!imports.CanRetry) return;
            await imports.RetryFailedBatchAsync();
            await Refresh();
        };
        Loaded += async (_,_) => { imports.RetryAvailabilityChanged += UpdateRetry; await Refresh(); };
        Unloaded += (_,_) => imports.RetryAvailabilityChanged -= UpdateRetry;
    }
    private void ApplyFilter() => rows.ItemsSource = problems.Where(p => status.SelectedIndex == 0 || p.Status.Contains(status.SelectedItem?.ToString() ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
}
