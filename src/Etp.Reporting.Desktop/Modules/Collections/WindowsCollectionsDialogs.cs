extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using EtpApplication::Etp.Reporting.Application.Collections;
using Microsoft.Win32;

namespace Etp.Reporting.Desktop.Modules.Collections;

/// <summary>The Windows dialogs of the Collections screens: file choosers, and a reason box built to the dialog rule (L3: 480
/// wide, card-title heading, the no-op button first and the action last, Surface from the token).</summary>
public sealed class WindowsCollectionsDialogs(Func<DependencyObject?> owner) : ICollectionsDialogs
{
    private const string StatementFilter = "Statement files (*.csv;*.xlsx)|*.csv;*.xlsx";

    public IReadOnlyList<string> ChooseStatementFiles()
    {
        var dialog = new OpenFileDialog { Filter = StatementFilter, CheckFileExists = true, Multiselect = true, Title = "Choose bank or settlement files" };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public string? ChooseSampleFile()
    {
        var dialog = new OpenFileDialog { Filter = StatementFilter, CheckFileExists = true, Multiselect = false, Title = "Choose a sample file" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? AskReason(string title, string prompt, string? suggestion = null)
    {
        var host = owner();
        var parent = host is null ? null : Window.GetWindow(host);
        var dialog = new Window
        {
            Title = title, Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize
        };
        dialog.SetResourceReference(Control.BackgroundProperty, "Surface");
        if (parent?.IsVisible == true) dialog.Owner = parent;
        var root = new DockPanel { Margin = DesignTokens.Gap("Space24") };
        var heading = CollectionsUi.Text(title, "CardTitleText");
        heading.Margin = DesignTokens.Gap(null, null, null, "Space12");
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = DesignTokens.Gap(null, "Space16", null, null) };
        var cancel = CollectionsUi.MakeButton("Cancel");
        cancel.IsCancel = true;
        var confirm = CollectionsUi.MakeButton("Confirm", "PrimaryButton");
        confirm.IsDefault = true;
        buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var box = CollectionsUi.Input("InputWide", 500);
        box.Width = double.NaN; box.Text = suggestion ?? string.Empty; box.SelectAll();
        AutomationProperties.SetName(box, "Reason");
        var body = new StackPanel();
        body.Children.Add(CollectionsUi.Text(prompt, "BodyText"));
        body.Children.Add(box);
        root.Children.Add(body);
        dialog.Content = root;
        string? answer = null;
        confirm.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text)) return;
            answer = box.Text.Trim();
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => box.Focus();
        return dialog.ShowDialog() == true ? answer : null;
    }

    /// <summary>1.12.1 P2: the "Record by hand" form (same dialog rule as the reason box): credited date, account (by name),
    /// amount, bank reference and reason, the reason last. The entry is returned as typed; the model says what is wrong with it.</summary>
    public ManualCreditEntry? AskManualCredit(string title, string prompt, ManualCreditEntry suggestion, IReadOnlyList<DepositAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        ArgumentNullException.ThrowIfNull(accounts);
        var host = owner();
        var parent = host is null ? null : Window.GetWindow(host);
        var dialog = new Window
        {
            Title = title, Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize
        };
        dialog.SetResourceReference(Control.BackgroundProperty, "Surface");
        if (parent?.IsVisible == true) dialog.Owner = parent;
        var root = new DockPanel { Margin = DesignTokens.Gap("Space24") };
        var heading = CollectionsUi.Text(title, "CardTitleText");
        heading.Margin = DesignTokens.Gap(null, null, null, "Space12");
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = DesignTokens.Gap(null, "Space16", null, null) };
        var cancel = CollectionsUi.MakeButton("Cancel");
        cancel.IsCancel = true;
        var confirm = CollectionsUi.MakeButton("Record", "PrimaryButton");
        confirm.IsDefault = true;
        buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);

        var date = new DatePicker { SelectedDate = suggestion.CreditedDate?.ToDateTime(TimeOnly.MinValue), Width = DesignTokens.Value("InputMedium") };
        date.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        var account = new ComboBox { Width = double.NaN, DisplayMemberPath = nameof(AccountChoice.Name) };
        account.SetResourceReference(FrameworkElement.MinHeightProperty, "ActiveTargetHeight");
        var choices = accounts.Select(item => new AccountChoice(item.BankAccountId, CollectionsScreens.MaskDigits(item.DisplayName)
            + (item.StoreCode is null ? " (all stores)" : string.Empty))).ToList();
        account.ItemsSource = choices;
        account.SelectedItem = choices.FirstOrDefault(choice => choice.Id == suggestion.BankAccountId);
        var amount = CollectionsUi.Input("InputMedium", 20);
        amount.Text = suggestion.AmountText;
        var reference = CollectionsUi.Input("InputMedium", 100);
        reference.Text = suggestion.BankReference ?? string.Empty;
        var reason = CollectionsUi.Input("InputWide", 500);
        reason.Width = double.NaN; reason.Text = suggestion.Reason;

        var body = new StackPanel();
        body.Children.Add(CollectionsUi.Text(prompt, "BodyText"));
        body.Children.Add(CollectionsUi.Field("Credited on", date));
        body.Children.Add(CollectionsUi.Field("Bank account", account));
        body.Children.Add(CollectionsUi.Field("Amount credited (₹)", amount));
        body.Children.Add(CollectionsUi.Field("Bank reference (optional)", reference));
        body.Children.Add(CollectionsUi.Field("Reason", reason));
        root.Children.Add(body);
        dialog.Content = root;
        ManualCreditEntry? answer = null;
        confirm.Click += (_, _) =>
        {
            answer = new ManualCreditEntry(date.SelectedDate is { } picked ? DateOnly.FromDateTime(picked) : null, (account.SelectedItem as AccountChoice)?.Id,
                amount.Text, reference.Text, reason.Text);
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => amount.Focus();
        return dialog.ShowDialog() == true ? answer : null;
    }

    private sealed record AccountChoice(int Id, string Name);

    public bool Confirm(string action, string detail)
    {
        var host = owner();
        return host is not null && ConfirmationSheet.Show(host, action, detail);
    }
}
