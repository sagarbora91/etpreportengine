using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using Etp.Reporting.Application.Updates;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Desktop.Updates;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 wave 2 (B2). The banner and the Settings screen as controls: the banner is a Notice with one primary button and a
/// quiet one, it closes itself on "Not now" and installs only through the coordinator; the slot shows it once; the
/// Settings screen saves nothing without a reason, saves the values it shows, and gives a non-Owner one sentence and no
/// field. Window-free: a control tree can be built and clicked on an STA thread without a display.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class UpdateShellViewTests
{
    private static ReleaseManifest Offer()
    {
        var key = new ReleaseSigningKey("0123456789abcdef", Convert.ToBase64String(new byte[65]));
        return ReleaseManifestParser.Parse(Manifest("1.12.1", key.KeyId)).Manifest!;
    }

    private static UpdateBannerModel Model(Func<Task>? install = null) =>
        new(new ReleaseVersion(1, 12, 1), "ETP 1.12.1 is available", "Synthetic release notes.", install ?? (() => Task.CompletedTask));

    // ------------------------------------------------------------ banner

    [Fact]
    public void The_banner_has_a_stable_key_shows_the_words_and_closes_on_Not_now()
    {
        RunSta(() =>
        {
            var banner = new UpdateAvailableBanner(Model());
            Assert.Equal("update-ready", banner.Key);
            var texts = All<TextBlock>(banner.Content).Select(t => t.Text).ToArray();
            Assert.Contains("ETP 1.12.1 is available", texts);
            Assert.Contains("Synthetic release notes.", texts);
            var buttons = All<Button>(banner.Content).ToDictionary(b => b.Content?.ToString() ?? "", b => b);
            Assert.Equal(new[] { "Install now", "Not now" }, buttons.Keys.Order());
            var closed = 0;
            banner.Closed += (_, _) => closed++;
            Click(buttons["Not now"]);
            Assert.Equal(1, closed);
        });
    }

    [Fact]
    public void Install_now_runs_the_models_install_and_is_disabled_while_it_runs()
    {
        RunSta(() =>
        {
            var installs = 0;
            var release = new TaskCompletionSource();
            var banner = new UpdateAvailableBanner(Model(() => { installs++; return release.Task; }));
            var install = All<Button>(banner.Content).Single(b => (b.Content as string) == "Install now");
            Click(install);
            Assert.Equal(1, installs);
            Assert.False(install.IsEnabled);
            release.SetResult();
        });
    }

    [Fact]
    public void The_slot_shows_the_update_banner_once_and_removes_it_by_key()
    {
        RunSta(() =>
        {
            var host = new ContentControl();
            var slot = new ShellBannerSlot(host);
            slot.Show(new UpdateAvailableBanner(Model()));
            slot.Show(new UpdateAvailableBanner(Model()));
            Assert.Single(slot.Pending);
            Assert.Equal(Visibility.Visible, host.Visibility);
            slot.Remove(UpdateAvailableBanner.BannerKey);
            Assert.Equal(Visibility.Collapsed, host.Visibility);
            // "Not now" removes it through the slot's Closed handling.
            var banner = new UpdateAvailableBanner(Model());
            slot.Show(banner);
            Click(All<Button>(banner.Content).Single(b => (b.Content as string) == "Not now"));
            Assert.Empty(slot.Pending);
        });
    }

    [Fact]
    public void The_banner_uses_the_information_notice_style_and_no_colour_or_size_literals()
    {
        RunSta(() =>
        {
            var banner = new UpdateAvailableBanner(Model());
            var border = Assert.IsType<Border>(banner.Content);
            Assert.Contains("ETP 1.12.1 is available", AutomationProperties.GetName(border), StringComparison.Ordinal);
            Assert.Contains("information", AutomationProperties.GetName(border), StringComparison.Ordinal);
        });
        // Tokens only (ETP-DESIGN-SYSTEM.md section 12): the new files hold no hex, no Brushes, no font size, no MinHeight.
        var root = Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop");
        foreach (var file in new[] { @"Shell\UpdateBanner.cs", @"Shell\UpdateDialogs.cs", @"Shell\UpdateShell.cs", @"Modules\Settings\UpdateSettingsView.cs" })
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            Assert.DoesNotMatch(@"""#[0-9A-Fa-f]{3,8}""", text);
            Assert.DoesNotContain("Brushes.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("FontSize", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MinHeight", text, StringComparison.Ordinal);
            Assert.DoesNotContain("FontWeight", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_result_dialog_has_no_restore_button()
    {
        Assert.Equal("Update failed", UpdateResultDialog.ChipWord(new("t", UpdateTextTone.Critical, "b", true, null)));
        Assert.Equal("Not started", UpdateResultDialog.ChipWord(new("t", UpdateTextTone.Information, "b", true, null)));
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop", "Shell", "UpdateDialogs.cs"));
        Assert.DoesNotMatch(@"Content\s*=\s*""[^""]*[Rr]estore", text);
        Assert.DoesNotMatch(@"Content\s*=\s*""[^""]*[Rr]etry", text);
    }

    // ------------------------------------------------------------ settings screen

    private sealed class MemoryStore : IUpdateSettingsStore
    {
        public UpdateSettings Value = new(null, false);
        public readonly List<(UpdateSettings Settings, string Reason)> Saves = [];
        public Exception? SaveFails;

        public Task<UpdateSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);

        public Task SaveAsync(UpdateSettings settings, string reason, CancellationToken cancellationToken = default)
        {
            if (SaveFails is not null) return Task.FromException(SaveFails);
            Saves.Add((settings, reason));
            Value = settings;
            return Task.CompletedTask;
        }
    }

    private static UpdateShellCoordinator IdleCoordinator(bool owner = true, UpdateFeedChoice? choice = null) => new(new UpdateShellServices(
        Settings: new MemoryStore(),
        Running: () => new ReleaseVersion(1, 12, 0),
        IsOwner: () => owner,
        Check: (_, _) => choice ?? new UpdateFeedChoice(UpdateFeedStatus.UpToDate, null, null, []),
        Gate: new UpdateGateProbe(new UpdateGateSources(() => owner, () => false, _ => Task.FromResult(false), _ => Task.FromResult(false), () => false, () => false, () => false, () => false, () => false, () => true, _ => Task.FromResult(true))),
        Install: (_, _, _, _, _) => throw new InvalidOperationException("not used"),
        ResolvePending: _ => null,
        ShowBanner: _ => { },
        Confirm: _ => Task.FromResult(false),
        ShowResult: _ => { },
        SetStatus: _ => { },
        CloseApplication: () => { },
        NowUtc: () => new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void A_non_owner_gets_one_sentence_and_no_field_or_button()
    {
        RunSta(() =>
        {
            var view = new UpdateSettingsView(() => new MemoryStore(), IdleCoordinator(owner: false), () => false);
            Assert.Empty(All<TextBox>(view));
            Assert.Empty(All<Button>(view));
            Assert.Contains(All<Notice>(view), n => n.Title == "Only an Owner can change the update settings.");
        });
    }

    [Fact]
    public void An_owner_sees_the_saved_values_and_a_save_without_a_reason_writes_nothing()
    {
        RunSta(() =>
        {
            var store = new MemoryStore { Value = new UpdateSettings(@"D:\Feed", true) };
            var view = new UpdateSettingsView(() => store, IdleCoordinator(), () => true);
            view.LoadAsync().GetAwaiter().GetResult();
            var feed = All<TextBox>(view).Single(t => AutomationProperties.GetName(t) == "Update folder");
            Assert.Equal(@"D:\Feed", feed.Text);
            Assert.True(All<CheckBox>(view).Single().IsChecked);

            Click(FindButton(view, "Save"));
            Assert.Empty(store.Saves);
            Assert.Contains(All<Notice>(view), n => n.Title == "Enter a reason for the change.");
        });
    }

    [Fact]
    public void Save_writes_both_values_with_the_reason_and_says_when()
    {
        RunSta(() =>
        {
            var store = new MemoryStore();
            var view = new UpdateSettingsView(() => store, IdleCoordinator(), () => true);
            view.LoadAsync().GetAwaiter().GetResult();
            All<TextBox>(view).Single(t => AutomationProperties.GetName(t) == "Update folder").Text = @"  D:\Shared\ETP-Updates  ";
            All<CheckBox>(view).Single().IsChecked = true;
            All<TextBox>(view).Single(t => AutomationProperties.GetName(t) == "Reason for the change").Text = "Synthetic: turning checks on";

            Click(FindButton(view, "Save"));
            var (settings, reason) = Assert.Single(store.Saves);
            Assert.Equal(new UpdateSettings(@"D:\Shared\ETP-Updates", true), settings);
            Assert.Equal("Synthetic: turning checks on", reason);
            Assert.Contains(All<Notice>(view), n => n.Title.StartsWith("Saved at ", StringComparison.Ordinal));
            Assert.Equal("", All<TextBox>(view).Single(t => AutomationProperties.GetName(t) == "Reason for the change").Text);
        });
    }

    [Fact]
    public void A_blank_folder_is_saved_as_the_OneDrive_default()
    {
        RunSta(() =>
        {
            var store = new MemoryStore { Value = new UpdateSettings(@"D:\Old", false) };
            var view = new UpdateSettingsView(() => store, IdleCoordinator(), () => true);
            view.LoadAsync().GetAwaiter().GetResult();
            All<TextBox>(view).Single(t => AutomationProperties.GetName(t) == "Update folder").Text = "";
            All<TextBox>(view).Single(t => AutomationProperties.GetName(t) == "Reason for the change").Text = "Synthetic";
            Click(FindButton(view, "Save"));
            Assert.Null(Assert.Single(store.Saves).Settings.FeedPath);
        });
    }

    [Fact]
    public void A_refused_save_is_said_plainly_and_keeps_the_screens_values()
    {
        RunSta(() =>
        {
            var store = new MemoryStore { SaveFails = new UnauthorizedAccessException("Synthetic") };
            var view = new UpdateSettingsView(() => store, IdleCoordinator(), () => true);
            view.LoadAsync().GetAwaiter().GetResult();
            All<TextBox>(view).Single(t => AutomationProperties.GetName(t) == "Reason for the change").Text = "Synthetic";
            Click(FindButton(view, "Save"));
            Assert.Contains(All<Notice>(view), n => n.Title == "The update settings were not saved.");
        });
    }

    [Fact]
    public void Check_now_with_an_unsaved_change_asks_to_save_first_and_does_not_read_the_feed()
    {
        RunSta(() =>
        {
            var checks = 0;
            var coordinator = new UpdateShellCoordinator(new UpdateShellServices(
                Settings: new MemoryStore(), Running: () => new ReleaseVersion(1, 12, 0), IsOwner: () => true,
                Check: (_, _) => { checks++; return new UpdateFeedChoice(UpdateFeedStatus.UpToDate, null, null, []); },
                Gate: new UpdateGateProbe(new UpdateGateSources(() => true, () => false, _ => Task.FromResult(false), _ => Task.FromResult(false), () => false, () => false, () => false, () => false, () => false, () => true, _ => Task.FromResult(true))),
                Install: (_, _, _, _, _) => throw new InvalidOperationException(), ResolvePending: _ => null, ShowBanner: _ => { },
                Confirm: _ => Task.FromResult(false), ShowResult: _ => { }, SetStatus: _ => { }, CloseApplication: () => { }, NowUtc: () => DateTimeOffset.UtcNow));
            var view = new UpdateSettingsView(() => new MemoryStore(), coordinator, () => true);
            view.LoadAsync().GetAwaiter().GetResult();
            All<TextBox>(view).Single(t => AutomationProperties.GetName(t) == "Update folder").Text = @"D:\New";
            Click(FindButton(view, "Check now"));
            Assert.Equal(0, checks);
            Assert.Contains(All<Notice>(view), n => n.Title == "Save the change first.");
        });
    }

    [Fact]
    public void Before_any_check_the_last_check_is_a_neutral_chip_with_a_word()
    {
        RunSta(() =>
        {
            var view = new UpdateSettingsView(() => new MemoryStore(), IdleCoordinator(), () => true);
            view.LoadAsync().GetAwaiter().GetResult();
            Assert.Contains(All<StatusChip>(view), chip => chip.Text == "Not checked" && chip.Tone == StatusTone.Neutral);
        });
    }

    // ------------------------------------------------------------ helpers

    private static Button FindButton(DependencyObject root, string label) => All<Button>(root).Single(b => (b.Content as string) == label);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in All<T>(child))
                yield return found;
        if (root is ContentControl { Content: DependencyObject content } && !LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().Contains(content))
            foreach (var found in All<T>(content)) yield return found;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
