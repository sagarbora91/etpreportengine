extern alias EtpApplication;

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop;

internal static class UpdateDialogFrame
{
    /// <summary>The dialog shell of ETP-DESIGN-SYSTEM.md L3: 480 wide, Surface from the token, no taller than the work area.</summary>
    public static Window Create(Window? owner, string title)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 40),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };
        dialog.SetResourceReference(Control.BackgroundProperty, "Surface");
        if (owner?.IsVisible == true) dialog.Owner = owner;
        return dialog;
    }

    public static TextBlock Heading(string text)
    {
        var heading = new TextBlock { Text = text, Margin = DesignTokens.Gap(null, null, null, "Space12") };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "CardTitleText");
        return heading;
    }

    public static TextBlock Body(string text, string style = "BodyText")
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(FrameworkElement.StyleProperty, style);
        return block;
    }
}

/// <summary>
/// 1.12 wave 2 (B2). "Install ETP 1.12.1?" Template: dialog (L3). Lists the steps ETP will take (backup first, ETP closes,
/// Windows asks for permission, ETP opens again) and, when the gate says no, every reason at once in one Warning notice.
/// "Keep working" first, "Install now" last and disabled while anything blocks (it says why in its tooltip, B6).
/// </summary>
internal static class UpdateConfirmSheet
{
    public static bool Show(Window? owner, UpdateConfirmRequest request)
    {
        var text = request.Text;
        var dialog = UpdateDialogFrame.Create(owner, text.Title);
        var root = new DockPanel { Margin = DesignTokens.Gap("Space24") };
        var heading = UpdateDialogFrame.Heading(text.Title);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = DesignTokens.Gap(null, "Space16", null, null) };
        var cancel = new Button { Content = "Keep working", IsCancel = true, Margin = DesignTokens.Gap(null, null, "Space8", null) };
        var confirm = new Button { Content = UpdateScreenText.BannerInstallButton, IsEnabled = text.CanInstall };
        confirm.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        AutomationProperties.SetName(confirm, "Confirm install now");
        if (!text.CanInstall)
        {
            confirm.ToolTip = "Fix the items listed above first, then press Install now again.";
            ToolTipService.SetShowOnDisabled(confirm, true);
        }
        confirm.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(confirm);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);

        var body = new StackPanel();
        foreach (var (step, index) in text.Steps.Select((step, index) => (step, index)))
        {
            var line = UpdateDialogFrame.Body(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{index + 1}. {step}"));
            line.Margin = DesignTokens.Gap(null, index == 0 ? null : "Space4", null, null);
            body.Children.Add(line);
        }
        if (text.Blocks.Count > 0)
        {
            var notice = new Notice(StatusTone.Warning, text.BlockedIntro ?? "ETP cannot start the update yet:", string.Join(Environment.NewLine, text.Blocks))
            { Margin = DesignTokens.Gap(null, "Space16", null, null) };
            body.Children.Add(notice);
        }
        if (text.Notes.Length > 0)
        {
            var notesTitle = UpdateDialogFrame.Body("What is new", "FieldLabelText");
            notesTitle.Margin = DesignTokens.Gap(null, "Space16", null, null);
            body.Children.Add(notesTitle);
            body.Children.Add(UpdateDialogFrame.Body(text.Notes));
        }
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
        dialog.Content = root;
        return dialog.ShowDialog() == true;
    }
}

/// <summary>
/// 1.12 wave 2 (B2). The result screen: shown at the next start after an update attempt that failed or never started, and
/// for an install ETP refused. Template: dialog (L3). One StatusChip with the word, the sentence, "Open setup log folder"
/// (QuietButton) when setup left a log, and "Close". It never offers a restore (decision 39): setup may have applied some database changes before
/// it stopped (review M-3), so the text names the backup taken before the update and Sagar decides whether it is needed.
/// </summary>
internal static class UpdateResultDialog
{
    public static void Show(Window? owner, UpdateResultText result)
    {
        var dialog = UpdateDialogFrame.Create(owner, result.Title);
        var root = new DockPanel { Margin = DesignTokens.Gap("Space24") };
        var heading = UpdateDialogFrame.Heading(result.Title);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = DesignTokens.Gap(null, "Space16", null, null) };
        if (result.SetupLogFolder is { Length: > 0 } folder && Directory.Exists(folder))
        {
            var open = new Button { Content = UpdateScreenText.OpenSetupLogFolderButton, Margin = DesignTokens.Gap(null, null, "Space8", null) };
            open.SetResourceReference(FrameworkElement.StyleProperty, "QuietButton");
            AutomationProperties.SetName(open, "Open the folder with the setup log");
            open.Click += (_, _) => OpenFolder(folder);
            actions.Children.Add(open);
        }
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true };
        close.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);

        var tone = UpdateTones.ToStatusTone(result.Tone);
        var body = new StackPanel();
        var chip = new StatusChip(ChipWord(result), tone) { Margin = DesignTokens.Gap(null, null, null, "Space12") };
        body.Children.Add(chip);
        body.Children.Add(UpdateDialogFrame.Body(result.Body));
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
        dialog.Content = root;
        dialog.ShowDialog();
    }

    internal static string ChipWord(UpdateResultText result) => result.Tone switch
    {
        UpdateTextTone.Critical => "Update failed",
        UpdateTextTone.Success => "Updated",
        UpdateTextTone.Information => "Not started",
        _ => "Not installed",
    };

    private static void OpenFolder(string folder)
    {
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add(folder);
            Process.Start(start)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            DesktopDiagnostics.Record(exception, "Updates.OpenLogFolder", "UPDATE_LOG_FOLDER_NOT_OPENED", DesktopDiagnosticSeverity.Warning);
        }
    }
}
