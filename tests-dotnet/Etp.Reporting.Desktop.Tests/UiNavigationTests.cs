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
    public void Six_sections_have_the_approved_daily_tabs()
    {
        Assert.Equal(new[]{"Today","Import","Reports","Stock","Service","Settings"},TaskNavigation.Sections);
        // 1.12 U1 (decision 2): the Owner lands on Today > Morning; every other role still lands on Today > Sales.
        Assert.Equal(new[]{"Morning","Sales","Cash","Walk-ins","Close day"},TaskNavigation.InSection("Today",ShellAccess.Owner).Select(t=>t.Tab).Distinct());
        Assert.Equal("morning",TaskNavigation.InSection("Today",ShellAccess.Owner)[0].Id);
        Assert.Equal("report-dsr",TaskNavigation.InSection("Today",ShellAccess.StoreManager)[0].Id);
        Assert.Equal("report-dsr",TaskNavigation.InSection("Today",ShellAccess.Viewer)[0].Id);
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

    // ------------------------------------------------------------------ 1.12 U1 pins and recents (wave plan C-e)

    [Fact]
    public void Pins_and_recents_round_trip_through_the_preferences_file()
    {
        var preference = new UiPreferences(UiDensity.Desktop, [], ["dsr"]) { PinnedTaskIds = ["morning", "import-history"], RecentTaskIds = ["walk-ins", "readiness"] };
        var restored = UiPreferenceStore.Normalize(JsonSerializer.Deserialize<UiPreferences>(JsonSerializer.Serialize(preference))!);

        Assert.Equal(["morning", "import-history"], restored.PinnedTaskIds);
        Assert.Equal(["walk-ins", "readiness"], restored.RecentTaskIds);
        Assert.Equal(UiDensity.Desktop, restored.Density);
    }

    [Fact]
    public void A_file_written_before_pins_and_recents_loads_with_empty_lists()
    {
        const string old = """{ "Density": "Touch", "PinnedModuleIds": [], "FavouriteReportCodes": ["dsr", "staff"] }""";
        var loaded = UiPreferenceStore.Normalize(JsonSerializer.Deserialize<UiPreferences>(old)!);

        Assert.Empty(loaded.PinnedTaskIds);
        Assert.Empty(loaded.RecentTaskIds);
        Assert.Equal(["dsr", "staff"], loaded.FavouriteReportCodes);
        // An explicit null (a hand-edited file) is the same as missing.
        var nulls = UiPreferenceStore.Normalize(JsonSerializer.Deserialize<UiPreferences>("""{ "Density": 0, "PinnedModuleIds": [], "FavouriteReportCodes": [], "PinnedTaskIds": null, "RecentTaskIds": null }""")!);
        Assert.Empty(nulls.PinnedTaskIds);
        Assert.Empty(nulls.RecentTaskIds);
    }

    [Fact]
    public void Recents_keep_the_newest_eight_task_ids_newest_first()
    {
        var preferences = UiPreferences.Default;
        foreach (var id in new[] { "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9", "a3" })
            preferences = preferences.WithRecent(id);

        Assert.Equal(UiPreferenceStore.MaximumTaskIds, preferences.RecentTaskIds.Count);
        Assert.Equal(["a3", "a9", "a8", "a7", "a6", "a5", "a4", "a2"], preferences.RecentTaskIds);
    }

    [Theory]
    [InlineData("help:keyboard-shortcuts")]
    [InlineData("error-register|ABC123DEF456")]
    [InlineData("service-job-history JOAW3301234567")]
    [InlineData("report-dsr/2026-10-09")]
    [InlineData(@"C:\ETP\file.xlsx")]
    [InlineData("Morning")]
    [InlineData("")]
    public void No_route_argument_path_or_help_topic_is_ever_stored(string id)
    {
        Assert.False(UiPreferenceStore.IsStorableTaskId(id));
        var preferences = UiPreferences.Default.WithRecent(id);
        Assert.Empty(preferences.RecentTaskIds);
        var loaded = UiPreferenceStore.Normalize(UiPreferences.Default with { PinnedTaskIds = [id, "morning"], RecentTaskIds = [id] });
        Assert.Equal(["morning"], loaded.PinnedTaskIds);
        Assert.Empty(loaded.RecentTaskIds);
    }

    [Fact]
    public void A_saved_file_holds_task_ids_and_nothing_else()
    {
        var preferences = UiPreferences.Default.WithRecent("readiness").TogglePin(TaskNavigation.Find("morning")!);
        var json = JsonSerializer.Serialize(preferences);
        using var document = JsonDocument.Parse(json);
        foreach (var list in new[] { "PinnedTaskIds", "RecentTaskIds" })
            Assert.All(document.RootElement.GetProperty(list).EnumerateArray(), id => Assert.True(UiPreferenceStore.IsStorableTaskId(id.GetString())));
        Assert.DoesNotContain("2026", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_star_pins_a_screen_and_a_report_star_is_its_favourite()
    {
        var morning = TaskNavigation.Find("morning")!;
        var cash = TaskNavigation.Find("report-cash")!;
        var preferences = UiPreferences.Default with { FavouriteReportCodes = [] };

        preferences = preferences.TogglePin(morning).TogglePin(cash);
        Assert.True(preferences.IsPinned(morning));
        Assert.True(preferences.IsPinned(cash));
        Assert.Equal(["morning"], preferences.PinnedTaskIds);
        Assert.Equal(["cash"], preferences.FavouriteReportCodes);
        Assert.Equal(["morning", "report-cash"], preferences.PinnedForSearch());

        preferences = preferences.TogglePin(morning).TogglePin(cash);
        Assert.False(preferences.IsPinned(morning));
        Assert.False(preferences.IsPinned(cash));
    }

    [Fact]
    public void A_ninth_pin_is_refused_rather_than_dropping_an_old_one()
    {
        var ids = TaskNavigation.All.Where(task => task.ReportCode is null).Select(task => task.Id).Take(9).ToArray();
        var preferences = UiPreferences.Default;
        foreach (var id in ids.Take(8)) preferences = preferences.TogglePin(TaskNavigation.Find(id)!);

        var refused = preferences.TogglePin(TaskNavigation.Find(ids[8])!);

        Assert.Same(preferences, refused);
        Assert.Equal(ids.Take(8), refused.PinnedTaskIds);
    }

    [Fact]
    public void Ctrl_K_lists_pinned_then_recent_then_the_rest()
    {
        var preferences = (UiPreferences.Default with { FavouriteReportCodes = ["cash"] })
            .TogglePin(TaskNavigation.Find("import-history")!).WithRecent("readiness").WithRecent("walk-ins");
        var found = TaskNavigation.Search("", ShellAccess.Owner, preferences.PinnedForSearch(), preferences.RecentTaskIds);

        Assert.Equal(["import-history", "report-cash", "walk-ins", "readiness"], found.Take(4).Select(task => task.Id));
    }

    [Fact]
    public void Shortcut_registry_has_no_duplicate_gestures()
    {
        var duplicates = ShellShortcutRegistry.All.GroupBy(x => (x.Key, x.Modifiers)).Where(x => x.Count() > 1);
        Assert.Empty(duplicates);
    }

}
