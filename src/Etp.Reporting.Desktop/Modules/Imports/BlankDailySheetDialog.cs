using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Import.DailyInputs;

namespace Etp.Reporting.Desktop.Modules.Imports;

/// <summary>Asks which store and kind of blank daily sheet to get, and its business date (default today).</summary>
public sealed class BlankDailySheetDialog : Window
{
    public BlankDailySheetRequest? Request { get; private set; }
    internal ComboBox SheetInput { get; }
    internal DatePicker DateInput { get; }
    internal Button OkButton { get; }
    internal CheckBox AllSheetsInput { get; }
    internal TextBlock DateMessage { get; }

    public BlankDailySheetDialog(Window? owner, IReadOnlyList<BlankDailySheetOption> options, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (owner?.IsLoaded == true) Owner = owner;
        Title = "Get blank daily sheet"; Width = 520; SizeToContent = SizeToContent.Height; MinWidth = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Surface");
        AutomationProperties.SetName(this, "Get blank daily sheet");

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Which sheet do you need?", FontSize = 20, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "ETP saves an empty Excel sheet for the day. Fill it in, then save it in the shop PC's daily-inputs folder.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12)
        });
        root.Children.Add(new TextBlock { Text = "Store and sheet", Margin = new Thickness(0, 0, 0, 4) });
        SheetInput = new ComboBox { DisplayMemberPath = nameof(BlankDailySheetOption.Label), ItemsSource = options, SelectedIndex = RememberedIndex(options), MinHeight = 36, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(SheetInput, "Store and sheet");
        root.Children.Add(SheetInput);
        root.Children.Add(new TextBlock { Text = "Business date", Margin = new Thickness(0, 0, 0, 4) });
        var start = today.AddDays(-DailyInputsTemplate.LookbackDays);
        DateInput = new DatePicker
        {
            SelectedDate = today.ToDateTime(TimeOnly.MinValue), DisplayDateStart = start.ToDateTime(TimeOnly.MinValue),
            DisplayDateEnd = today.ToDateTime(TimeOnly.MinValue), MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Left, Width = 180
        };
        AutomationProperties.SetName(DateInput, "Business date");
        root.Children.Add(DateInput);
        DateMessage = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        DateMessage.SetResourceReference(TextBlock.ForegroundProperty, "Critical");
        AutomationProperties.SetLiveSetting(DateMessage, AutomationLiveSetting.Assertive);
        root.Children.Add(DateMessage);
        AllSheetsInput = new CheckBox { Content = "Save all sheets (one file for every shop PC)", Margin = new Thickness(0, 12, 0, 0), MinHeight = 32 };
        AutomationProperties.SetName(AllSheetsInput, "Save all sheets");
        root.Children.Add(AllSheetsInput);
        root.Children.Add(new TextBlock
        {
            Text = "You only need this once. The sheet works for any day: reuse the file and type the date each day.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        });

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 44, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0) };
        OkButton = new Button { Content = "Choose where to save", IsDefault = true, MinHeight = 44, Padding = new Thickness(12, 8, 12, 8) };
        OkButton.SetResourceReference(StyleProperty, "PrimaryButton");
        AutomationProperties.SetName(cancel, "Cancel");
        AutomationProperties.SetName(OkButton, "Choose where to save the blank sheet");
        actions.Children.Add(cancel); actions.Children.Add(OkButton);
        root.Children.Add(actions);
        OkButton.Click += (_, _) =>
        {
            if (SheetInput.SelectedItem is not BlankDailySheetOption option) return;
            if (DateInput.SelectedDate is not { } date)
            {
                DateMessage.Text = BlankDailySheet.BadDateMessage(today); DateMessage.Visibility = Visibility.Visible;
                return;
            }
            var businessDate = DateOnly.FromDateTime(date);
            if (!BlankDailySheet.IsDateAllowed(businessDate, today))
            {
                DateMessage.Text = BlankDailySheet.BadDateMessage(today); DateMessage.Visibility = Visibility.Visible;
                return;
            }
            DateMessage.Visibility = Visibility.Collapsed;
            BlankDailySheet.LastChoice = (option.StoreCode, option.Kind);
            Request = new BlankDailySheetRequest(option, businessDate, AllSheetsInput.IsChecked == true);
            DialogResult = true;
        };
        Content = root;
    }

    private static int RememberedIndex(IReadOnlyList<BlankDailySheetOption> options)
    {
        if (BlankDailySheet.LastChoice is not { } last) return 0;
        for (var i = 0; i < options.Count; i++)
            if (options[i].Kind == last.Kind && string.Equals(options[i].StoreCode, last.StoreCode, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    /// <summary>Shows the dialog; the chosen sheet and date, or null when cancelled.</summary>
    public static BlankDailySheetRequest? Choose(Window? owner, IReadOnlyList<BlankDailySheetOption> options, DateOnly today)
    {
        var dialog = new BlankDailySheetDialog(owner, options, today);
        return dialog.ShowDialog() == true ? dialog.Request : null;
    }
}
