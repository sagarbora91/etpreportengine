using System.Text.Json;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Desktop;
using Etp.Reporting.Reporting;
using System.Windows.Input;

namespace Etp.Reporting.Desktop.Tests;

public sealed class UiNavigationTests
{
    [Fact]
    public void Store_manager_has_the_frozen_six_module_home()
    {
        var modules = UiNavigationRegistry.Modules.Where(x => x.DefaultVisibility && x.IsVisibleTo(AccessRole.StoreManager)).ToArray();

        Assert.Equal(6, modules.Length);
        Assert.Equal(["dashboard", "reports", "accounting", "imports", "archive", "exceptions"], modules.OrderBy(x => x.Order).Select(x => x.Id));
    }

    [Fact]
    public void Owner_can_expand_the_same_registry_to_nine_cards()
    {
        var modules = UiNavigationRegistry.Modules.Where(x => (x.DefaultVisibility || x.PinAllowed) && x.IsVisibleTo(AccessRole.Owner)).ToArray();

        Assert.Equal(9, modules.Length);
        Assert.Equal(3, modules.Count(x => x.PinAllowed));
    }

    [Fact]
    public void Viewer_visibility_never_expands_import_or_administration_permission()
    {
        var viewer = UiNavigationRegistry.Modules.Where(x => x.IsVisibleTo(AccessRole.Viewer)).Select(x => x.Id).ToArray();

        Assert.DoesNotContain("imports", viewer);
        Assert.DoesNotContain("registers", viewer);
        Assert.DoesNotContain("approvals", viewer);
        Assert.DoesNotContain("health", viewer);
    }

    [Fact]
    public void Every_production_report_is_reachable_from_reports_navigation()
    {
        var reachableCodes = UiNavigationRegistry.ForModule("reports").SelectMany(x => x.Items)
            .Where(x => x.IsAvailable && x.FeatureCode is not null).Select(x => x.FeatureCode!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(ProductReportCatalogue.All, report => Assert.Contains(report.Code, reachableCodes));
    }

    [Fact]
    public void Critical_operational_capabilities_have_authorised_routes()
    {
        var labels = UiNavigationRegistry.AllItems.Select(x => x.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var label in new[] { "Manual Entry", "Source Inbox", "OCR Review Queue", "Inward Register", "Prepare Batch", "Report Generations", "Open Items", "Approval Centre", "Backup & Recovery", "System Health" })
            Assert.Contains(label, labels);
    }

    [Fact]
    public void Manual_entry_is_a_store_manager_business_day_workspace()
    {
        var item = Assert.Single(UiNavigationRegistry.AllItems, x => x.Id == "manual-entry");

        Assert.Equal("Manual Entry", item.Destination);
        Assert.True(item.IsVisibleTo(AccessRole.StoreManager));
        Assert.False(item.IsVisibleTo(AccessRole.Viewer));
    }

    [Fact]
    public void No_live_navigation_item_has_an_orphan_destination()
    {
        var allowed = new HashSet<string>(["Dashboard", "Daily Workflow", "Manual Entry", "Sales Reports", "Stock Reports", "Import ETP", "Registers", "Accounting", "Report Archive", "Operations Center", "Settings", "Admin / Settings", "Masters"], StringComparer.Ordinal);

        Assert.All(UiNavigationRegistry.AllItems.Where(x => x.IsAvailable), item => Assert.Contains(item.Destination, allowed));
    }

    [Fact]
    public void Future_items_are_locked_with_a_plain_language_reason()
    {
        var future = UiNavigationRegistry.AllItems.Where(x => !x.IsAvailable).ToArray();

        Assert.NotEmpty(future);
        Assert.All(future, item => Assert.False(string.IsNullOrWhiteSpace(item.UnavailableReason)));
    }

    [Theory]
    [InlineData(UiDensity.Comfortable)]
    [InlineData(UiDensity.Compact)]
    public void Density_preference_round_trips_without_creating_a_second_ui(UiDensity density)
    {
        var preference = new UiPreferences(density, ["registers"], ["dsr"]);
        var restored = JsonSerializer.Deserialize<UiPreferences>(JsonSerializer.Serialize(preference));

        Assert.NotNull(restored);
        Assert.Equal(preference.Density, restored.Density);
        Assert.Equal(preference.PinnedModuleIds, restored.PinnedModuleIds);
        Assert.Equal(preference.FavouriteReportCodes, restored.FavouriteReportCodes);
    }

    [Fact]
    public void Navigation_history_supports_back_forward_and_truncates_abandoned_forward_path()
    {
        var history = new WorkspaceNavigationHistory();
        history.Visit(WorkspaceLocation.Home);
        history.Visit(new("Sales Reports"));
        history.Visit(new("Sales Reports", "dsr"));

        Assert.Equal(new WorkspaceLocation("Sales Reports"), history.GoBack());
        Assert.Equal(new WorkspaceLocation("Sales Reports", "dsr"), history.GoForward());
        Assert.Equal(new WorkspaceLocation("Sales Reports"), history.GoBack());
        history.Visit(new("Import ETP"));

        Assert.False(history.CanGoForward);
        Assert.Equal([WorkspaceLocation.Home, new("Sales Reports"), new("Import ETP")], history.Entries);
    }

    [Fact]
    public void Repeated_location_does_not_pollute_navigation_history()
    {
        var history = new WorkspaceNavigationHistory();
        Assert.True(history.Visit(WorkspaceLocation.Home));
        Assert.False(history.Visit(WorkspaceLocation.Home));
        Assert.Single(history.Entries);
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

    [Fact]
    public void Retry_shortcut_reuses_only_the_enabled_import_retry_action()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Etp.Reporting.Desktop",
            "MainWindow.Shell.cs"));

        Assert.Contains(
            "case ShellCommand.RetryImport when CurrentModuleId == \"imports\" && focusedWorkspaceKind != \"help\" && importWorkspaceView.CanRetry:",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "_ = importWorkspaceView.RetryFailedBatchAsync(); return true;",
            source,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
