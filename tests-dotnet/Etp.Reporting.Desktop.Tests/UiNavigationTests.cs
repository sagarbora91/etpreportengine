using System.Text.Json;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Desktop;
using Etp.Reporting.Reporting;
using System.Windows.Input;

namespace Etp.Reporting.Desktop.Tests;

public sealed class UiNavigationTests
{
    [Fact]
    public void Viewer_can_read_import_history_and_reports_but_cannot_import_or_administer()
    {
        Assert.Contains(TaskNavigation.Find("import-history"), TaskNavigation.InSection("Import", ShellAccess.Viewer));
        foreach (var id in new[] { "import-files" })
            Assert.False(new ShellNavigationService().Navigate(TaskNavigation.Find(id)!.Route, ShellAccess.Viewer).IsAllowed);
        foreach (var id in new[] { "conflicts", "source-inbox" })
            Assert.True(new ShellNavigationService().Navigate(TaskNavigation.Find(id)!.Route, ShellAccess.Viewer).IsAllowed);
        Assert.All(TaskNavigation.InSection("Settings",ShellAccess.Viewer), t => Assert.Contains(t.Tab,new[] {"Display","Help"}));
        Assert.NotEmpty(TaskNavigation.InSection("Reports",ShellAccess.Viewer));
    }
    [Fact]
    public void Every_production_report_has_one_executable_task()
    {
        foreach (var report in ProductReportCatalogue.All)
        {
            var task = Assert.Single(TaskNavigation.All,t => t.ReportCode == report.Code);
            Assert.True(new ShellNavigationService().Navigate(task.Route,ShellAccess.Viewer).IsAllowed);
        }
    }
    [Fact]
    public void Operational_tasks_are_reused_and_access_checked()
    {
        foreach (var id in new[] {"walk-ins","source-inbox","register-inward","prepare-batch","generations","open-items","approval-centre","backups","health"})
        {
            var task = TaskNavigation.Find(id)!;
            Assert.NotNull(task);
            Assert.True(new ShellNavigationService().Navigate(task.Route,ShellAccess.Owner).IsAllowed);
        }
        Assert.False(TaskNavigation.Find("walk-ins")!.IsAllowed(ShellAccess.Viewer));
        Assert.True(TaskNavigation.Find("walk-ins")!.IsAllowed(ShellAccess.StoreManager));
    }
    [Fact]
    public void Five_sections_have_the_approved_daily_tabs()
    {
        Assert.Equal(new[]{"Today","Import","Reports","Stock","Settings"},TaskNavigation.Sections);
        Assert.Equal(new[]{"Sales","Cash","Walk-ins","Close day"},TaskNavigation.InSection("Today",ShellAccess.Owner).Select(t=>t.Tab).Distinct());
        Assert.Equal("report-dsr",TaskNavigation.InSection("Today",ShellAccess.Owner)[0].Id);
        Assert.Equal("import-files",TaskNavigation.InSection("Import",ShellAccess.Owner)[0].Id);
    }

    [Theory]
    [InlineData(UiDensity.Touch)]
    [InlineData(UiDensity.Desktop)]
    public void Density_preference_round_trips_without_creating_a_second_ui(UiDensity density)
    {
        var preference = new UiPreferences(density, ["registers"], ["dsr"]);
        var restored = JsonSerializer.Deserialize<UiPreferences>(JsonSerializer.Serialize(preference));

        Assert.NotNull(restored);
        Assert.Equal(preference.Density, restored.Density);
        Assert.Equal(preference.PinnedModuleIds, restored.PinnedModuleIds);
        Assert.Equal(preference.FavouriteReportCodes, restored.FavouriteReportCodes);
    }

    [Theory]
    [InlineData("0", UiDensity.Touch)]
    [InlineData("1", UiDensity.Desktop)]
    [InlineData("\"Comfortable\"", UiDensity.Touch)]
    [InlineData("\"Compact\"", UiDensity.Desktop)]
    [InlineData("\"Touch\"", UiDensity.Touch)]
    [InlineData("\"Desktop\"", UiDensity.Desktop)]
    public void Legacy_density_preferences_retain_choice_and_save_current_names(string json, UiDensity expected)
    {
        var density = JsonSerializer.Deserialize<UiDensity>(json);
        Assert.Equal(expected, density);
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(density));
    }

    [Fact]
    public void One_navigation_history_truncates_abandoned_forward_routes()
    {
        var history = new ShellNavigationService();
        var sales=TaskNavigation.Find("report-dsr")!.Route;
        var stock=TaskNavigation.Find("report-stock-closing")!.Route;
        history.Navigate(sales,ShellAccess.Owner); history.Navigate(stock,ShellAccess.Owner);
        Assert.Equal(sales,history.GoBack(ShellAccess.Owner).RequestedRoute);
        Assert.Equal(stock,history.GoForward(ShellAccess.Owner).RequestedRoute);
        history.GoBack(ShellAccess.Owner);
        history.Navigate(TaskNavigation.Find("import-files")!.Route,ShellAccess.Owner);
        Assert.False(history.CanGoForward);
        var count=history.History.Count;
        history.Navigate(history.Current,ShellAccess.Owner);
        Assert.Equal(count,history.History.Count);
    }

    [Theory]
    [InlineData(Key.Left, Key.None, ModifierKeys.Alt, ShellCommand.Back)]
    [InlineData(Key.System, Key.Left, ModifierKeys.Alt, ShellCommand.Back)]
    [InlineData(Key.Right, Key.None, ModifierKeys.Alt, ShellCommand.Forward)]
    [InlineData(Key.F1, Key.None, ModifierKeys.None, ShellCommand.Help)]
    [InlineData(Key.Oem2, Key.None, ModifierKeys.Control, ShellCommand.ShortcutGuide)]
    [InlineData(Key.F5, Key.None, ModifierKeys.None, ShellCommand.Refresh)]
    [InlineData(Key.F, Key.None, ModifierKeys.Control, ShellCommand.Search)]
    [InlineData(Key.P, Key.None, ModifierKeys.Control, ShellCommand.ExportPdf)]
    [InlineData(Key.R, Key.None, ModifierKeys.Control, ShellCommand.RetryImport)]
    [InlineData(Key.Escape, Key.None, ModifierKeys.None, ShellCommand.CloseOrCancel)]
    public void Windows_shortcuts_resolve_to_shell_commands(Key key, Key systemKey, ModifierKeys modifiers, ShellCommand expected)
    {
        Assert.Equal(expected, ShellShortcutRegistry.Resolve(key, systemKey, modifiers));
    }

    [Fact]
    public void Shortcut_registry_has_no_duplicate_gestures()
    {
        var duplicates = ShellShortcutRegistry.All.GroupBy(x => (x.Key, x.Modifiers)).Where(x => x.Count() > 1);
        Assert.Empty(duplicates);
    }

}
