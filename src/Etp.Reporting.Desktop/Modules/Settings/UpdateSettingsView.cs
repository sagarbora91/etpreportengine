extern alias EtpApplication;

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Modules.Settings;

/// <summary>
/// 1.12 wave 2 (B2). Settings &gt; Software update (task <c>update-settings</c>), Owner only.
/// Template: C, Form (ETP-DESIGN-SYSTEM.md section 11). Row 0 toolbar: Save (the one primary) and Check now; row 2 the page's
/// one Notice (validation, "Saved at ...", a failure); body: three SurfaceCards (the update folder, the switch with the
/// reason last, and the last check as a StatusChip with its sentence). The switch stays off until a real unattended
/// upgrade has worked (design rule 9). Check now reads the SAVED folder, so it says so when the box has an unsaved change.
/// A non-Owner is shown one sentence and no field (SQL refuses a non-Owner write whatever this screen does).
/// </summary>
public sealed class UpdateSettingsView : UserControl
{
    private readonly PageFrame frame = new(PageTemplate.Form);
    private readonly Func<IUpdateSettingsStore> store;
    private readonly UpdateShellCoordinator coordinator;
    private readonly Func<bool> isOwner;
    private readonly TextBox feedPath = new() { MaxLength = UpdateSettingsRules.MaximumPathLength, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox checkEnabled = new() { Content = "Check for updates when ETP starts" };
    private readonly TextBlock switchHelp = new();
    private readonly TextBox reason = new() { MaxLength = UpdateSettingsRules.MaximumReasonLength, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button save = new() { Content = "Save" };
    private readonly Button checkNow = new() { Content = "Check now" };
    private readonly Button choose = new() { Content = "Choose..." };
    private readonly StackPanel lastCheck = new();
    private UpdateSettings? saved;
    private bool busy;

    internal UpdateSettingsView(Func<IUpdateSettingsStore> store, UpdateShellCoordinator coordinator, Func<bool> isOwner)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.isOwner = isOwner ?? throw new ArgumentNullException(nameof(isOwner));
        AutomationProperties.SetName(this, "Software update settings");
        Content = frame;

        if (!isOwner())
        {
            frame.Notice.Show(StatusTone.Information, "Only an Owner can change the update settings.");
            return;
        }

        save.SetResourceReference(StyleProperty, "PrimaryButton");
        choose.SetResourceReference(StyleProperty, "QuietButton");
        AutomationProperties.SetName(save, "Save the update settings");
        AutomationProperties.SetName(checkNow, "Check the update folder now");
        AutomationProperties.SetName(choose, "Choose the update folder");
        AutomationProperties.SetName(feedPath, "Update folder");
        AutomationProperties.SetName(checkEnabled, "Check for updates when ETP starts");
        AutomationProperties.SetName(reason, "Reason for the change");
        reason.Width = DesignTokens.Value("InputWide");
        switchHelp.SetResourceReference(StyleProperty, "CaptionText");
        save.Click += async (_, _) => await SaveAsync();
        checkNow.Click += async (_, _) => await CheckNowAsync();
        choose.Click += (_, _) => ChooseFolder();
        checkEnabled.Checked += (_, _) => switchHelp.Text = UpdateScreenText.CheckSwitchHelp(true);
        checkEnabled.Unchecked += (_, _) => switchHelp.Text = UpdateScreenText.CheckSwitchHelp(false);

        var toolbar = new WrapPanel();
        save.Margin = DesignTokens.Gap(null, null, "Space8", null);
        toolbar.Children.Add(save); toolbar.Children.Add(checkNow);
        frame.Toolbar = toolbar;
        frame.Description = "Where ETP looks for new releases, and whether it checks by itself. Only an Owner can change this.";

        var body = new StackPanel();
        body.Children.Add(Card("Update folder", FolderCard()));
        body.Children.Add(Card("Check for updates", SwitchCard(), "Space24"));
        body.Children.Add(Card("Last check", lastCheck, "Space24"));
        frame.Body = body;
        RenderLastCheck();
        coordinator.StateChanged += (_, _) => RenderLastCheck();
        Loaded += async (_, _) => await LoadAsync();
    }

    private UIElement FolderCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(FieldLabel("Update folder"));
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(feedPath, 0); Grid.SetColumn(choose, 1);
        choose.Margin = DesignTokens.Gap("Space8", null, null, null);
        row.Children.Add(feedPath); row.Children.Add(choose);
        panel.Children.Add(row);
        var hint = new TextBlock { Text = @"Leave blank to use the ETP-Updates folder in this Windows account's OneDrive. Only Sagar puts releases there; ETP installs a release only when its signature is valid.", Margin = DesignTokens.Gap(null, "Space4", null, null) };
        hint.SetResourceReference(StyleProperty, "CaptionText");
        panel.Children.Add(hint);
        return panel;
    }

    private UIElement SwitchCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(checkEnabled);
        switchHelp.Margin = DesignTokens.Gap(null, "Space4", null, null);
        panel.Children.Add(switchHelp);
        var label = FieldLabel("Reason for the change");
        label.Margin = DesignTokens.Gap(null, "Space16", null, "Space4");
        panel.Children.Add(label);
        panel.Children.Add(reason);
        return panel;
    }

    private static TextBlock FieldLabel(string text)
    {
        var label = new TextBlock { Text = text };
        label.SetResourceReference(StyleProperty, "FieldLabelText");
        return label;
    }

    private static Border Card(string title, UIElement content, string? topGap = null)
    {
        var heading = new TextBlock { Text = title, Margin = DesignTokens.Gap(null, null, null, "Space12") };
        heading.SetResourceReference(StyleProperty, "SectionTitleText");
        var inner = new StackPanel(); inner.Children.Add(heading); inner.Children.Add(content);
        var card = new Border { Child = inner, Margin = topGap is null ? new Thickness(0) : DesignTokens.Gap(null, topGap, null, null) };
        card.SetResourceReference(StyleProperty, "SurfaceCard");
        return card;
    }

    private void SetBusy(bool value)
    {
        busy = value;
        save.IsEnabled = checkNow.IsEnabled = choose.IsEnabled = !value;
    }

    private string? SavedFeedText => saved?.FeedPath ?? string.Empty;

    private bool HasUnsavedChange =>
        saved is not null && (!string.Equals(UpdateSettingsRules.NormalizePath(feedPath.Text) ?? string.Empty, SavedFeedText, StringComparison.Ordinal) || (checkEnabled.IsChecked == true) != saved.CheckEnabled);

    public async Task LoadAsync()
    {
        if (!isOwner() || busy) return;
        SetBusy(true);
        try
        {
            saved = await store().LoadAsync();
            feedPath.Text = saved.FeedPath ?? string.Empty;
            checkEnabled.IsChecked = saved.CheckEnabled;
            switchHelp.Text = UpdateScreenText.CheckSwitchHelp(saved.CheckEnabled);
            frame.Notice.Clear();
        }
        catch (Exception exception)
        {
            frame.Notice.Show(StatusTone.Critical, "The update settings could not be read.", Describe(exception, "UPDATE_SETTINGS_LOAD_FAILED"));
        }
        finally { SetBusy(false); }
        RenderLastCheck();
    }

    private async Task SaveAsync()
    {
        if (!isOwner() || busy) return;
        var problem = UpdateSettingsRules.Validate(feedPath.Text, reason.Text);
        if (problem is not null) { frame.Notice.Show(StatusTone.Warning, problem); return; }
        SetBusy(true);
        try
        {
            var next = new UpdateSettings(UpdateSettingsRules.NormalizePath(feedPath.Text), checkEnabled.IsChecked == true);
            await store().SaveAsync(next, reason.Text.Trim());
            saved = next;
            reason.Clear();
            frame.Notice.Show(StatusTone.Success, string.Create(CultureInfo.InvariantCulture, $"Saved at {DateTime.Now:HH:mm}."),
                next.CheckEnabled ? "ETP will check the update folder when it starts." : "ETP will not check the update folder by itself.");
        }
        catch (Exception exception)
        {
            frame.Notice.Show(StatusTone.Critical, "The update settings were not saved.", Describe(exception, "UPDATE_SETTINGS_SAVE_FAILED"));
        }
        finally { SetBusy(false); }
    }

    private async Task CheckNowAsync()
    {
        if (!isOwner() || busy) return;
        if (HasUnsavedChange)
        {
            frame.Notice.Show(StatusTone.Information, "Save the change first.", "Check now reads the folder as it was last saved.");
            return;
        }
        SetBusy(true);
        try
        {
            frame.Notice.Clear();
            await coordinator.CheckAsync();
        }
        catch (Exception exception)
        {
            frame.Notice.Show(StatusTone.Critical, "The update folder could not be checked.", Describe(exception, "UPDATE_CHECK_FAILED"));
        }
        finally { SetBusy(false); }
        RenderLastCheck();
    }

    private void ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the update folder" };
        if (dialog.ShowDialog() == true) feedPath.Text = dialog.FolderName;
    }

    private void RenderLastCheck()
    {
        lastCheck.Children.Clear();
        var summary = UpdateScreenText.LastCheck(coordinator.LastChoice, coordinator.Running, coordinator.LastCheckedUtc, TimeZoneInfo.Local);
        var chip = new StatusChip(summary.Word, UpdateTones.ToStatusTone(summary.Tone)) { Margin = DesignTokens.Gap(null, null, null, "Space8") };
        lastCheck.Children.Add(chip);
        var sentence = new TextBlock { Text = summary.Sentence };
        sentence.SetResourceReference(StyleProperty, "BodyText");
        lastCheck.Children.Add(sentence);
        if (summary.Extra is { } extra)
        {
            // The one line that is not a good state: the Owner reads it, Sagar looks at the folder.
            var notice = new Notice(StatusTone.Warning, extra) { Margin = DesignTokens.Gap(null, "Space8", null, null) };
            lastCheck.Children.Add(notice);
        }
        var version = new TextBlock { Text = coordinator.Running is { } running ? string.Create(CultureInfo.InvariantCulture, $"This PC runs ETP {running}.") : "This PC runs a development build.", Margin = DesignTokens.Gap(null, "Space8", null, null) };
        version.SetResourceReference(StyleProperty, "CaptionText");
        lastCheck.Children.Add(version);
    }

    private static string Describe(Exception exception, string eventId) =>
        DesktopDiagnostics.WithReference(DesktopFriendlyError.Describe(exception, "Only an Owner can change the update settings."),
            DesktopDiagnostics.Record(exception, "Settings.Updates", eventId));
}
