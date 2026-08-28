using Etp.Reporting.Desktop;
using System.Windows.Input;

namespace Etp.Reporting.Desktop.Tests;

public sealed class HelpCentreTests
{
    [Fact]
    public void Help_home_covers_every_approved_application_area()
    {
        var ids = HelpCentreRegistry.Topics.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in new[]
        {
            "getting-started", "dashboard", "business-day", "import-etp", "daily-sales-report",
            "sales-reports", "stock-reports", "tender-cash-service", "staff-cro", "exception-centre",
            "management", "investigation", "digital-registers", "accounting", "operations-support", "administration", "backup-recovery",
            "troubleshooting", "keyboard-shortcuts"
        }) Assert.Contains(expected, ids);
    }

    [Fact]
    public void No_help_tile_opens_an_empty_topic()
    {
        Assert.All(HelpCentreRegistry.Topics, topic =>
        {
            Assert.False(string.IsNullOrWhiteSpace(topic.Title));
            Assert.False(string.IsNullOrWhiteSpace(topic.Description));
            Assert.False(string.IsNullOrWhiteSpace(topic.Overview));
            Assert.NotEmpty(topic.Keywords);
        });
    }

    [Fact]
    public void Keyboard_guide_contains_required_windows_and_application_commands()
    {
        var keys = HelpCentreRegistry.Shortcuts.Select(x => x.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in new[] { "Alt + Left Arrow", "Alt + Right Arrow", "Alt + Home", "F1", "Ctrl + /", "F5", "Ctrl + P", "Ctrl + Shift + X", "Ctrl + S", "Ctrl + O", "F6", "Esc", "Alt + F4" })
            Assert.Contains(expected, keys);
    }

    [Theory]
    [InlineData("Sales Reports", "dsr", "daily-sales-report")]
    [InlineData("Manual Entry", null, "business-day")]
    [InlineData("Import ETP", null, "import-etp")]
    [InlineData("Admin / Settings", null, "administration")]
    [InlineData("Unknown", null, HelpCentreRegistry.HomeTopicId)]
    public void Context_help_routes_to_the_most_specific_available_topic(string destination, string? featureCode, string expected)
    {
        Assert.Equal(expected, ContextHelpRouter.ResolveTopicId(destination, featureCode));
        if (expected != HelpCentreRegistry.HomeTopicId) Assert.NotNull(HelpCentreRegistry.Find(expected));
    }

    [Fact]
    public void Search_finds_topics_by_business_language_and_shortcuts_by_action()
    {
        Assert.Contains(HelpCentreRegistry.Search("walk-ins"), x => x.Id == "business-day");
        Assert.Contains(HelpCentreRegistry.Search("ZIP"), x => x.Id == "import-etp");
        Assert.Contains(HelpCentreRegistry.SearchShortcuts("previous screen"), x => x.Keys == "Alt + Left Arrow");
        Assert.Contains(HelpCentreRegistry.SearchShortcuts("export", "Reports"), x => x.Keys == "Ctrl + Shift + X");
    }

    [Fact]
    public void Help_commands_expose_the_approved_input_gestures()
    {
        Assert.Contains(HelpCommands.OpenHelpCentre.InputGestures.OfType<System.Windows.Input.KeyGesture>(), x => x.Key == System.Windows.Input.Key.F1);
        Assert.Contains(HelpCommands.OpenKeyboardShortcuts.InputGestures.OfType<System.Windows.Input.KeyGesture>(), x => x.Key == System.Windows.Input.Key.Oem2 && x.Modifiers == System.Windows.Input.ModifierKeys.Control);
    }

    [Fact]
    public void Every_executable_shell_shortcut_has_exactly_one_help_entry()
    {
        foreach (var shortcut in ShellShortcutRegistry.All)
        {
            var help = Assert.Single(HelpCentreRegistry.Shortcuts, item => item.Command == shortcut.Command);
            Assert.Equal(shortcut.Display, help.Keys);
        }
    }

    [Fact]
    public void Every_help_entry_marked_executable_resolves_through_the_shell_registry()
    {
        foreach (var help in HelpCentreRegistry.Shortcuts.Where(item => item.Command is not null))
        {
            var shortcut = Assert.Single(ShellShortcutRegistry.All, item => item.Command == help.Command);
            Assert.Equal(help.Keys, shortcut.Display);
            Assert.Equal(help.Command, ShellShortcutRegistry.Resolve(shortcut.Key, Key.None, shortcut.Modifiers));
        }
    }

    [Fact]
    public void Native_help_entries_are_limited_to_standard_wpf_gestures()
    {
        var nativeGestures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Ctrl + Tab", "Ctrl + Shift + Tab", "Enter", "Arrow keys", "Home / End",
            "Ctrl + Home / Ctrl + End", "Page Up / Page Down", "Shift + F10", "Ctrl + C",
            "Tab / Shift + Tab", "Space", "Alt + F4"
        };

        Assert.All(
            HelpCentreRegistry.Shortcuts.Where(item => item.Command is null),
            item => Assert.Contains(item.Keys, nativeGestures));
    }

    [Theory]
    [InlineData("Ctrl + E")]
    [InlineData("Ctrl + N")]
    [InlineData("Ctrl + Shift + S")]
    public void Help_does_not_advertise_unimplemented_application_shortcuts(string keys)
    {
        Assert.DoesNotContain(
            HelpCentreRegistry.Shortcuts,
            item => string.Equals(item.Keys, keys, StringComparison.OrdinalIgnoreCase));
    }
}
