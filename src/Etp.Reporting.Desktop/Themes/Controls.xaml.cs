using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
namespace Etp.Reporting.Desktop.Themes;

public partial class ControlsTheme : ResourceDictionary
{
    public ControlsTheme() => InitializeComponent();
    private void GridLoaded(object sender, RoutedEventArgs e) { if (sender is DataGrid grid) TablePresentation.Configure(grid); }
    private void InputLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox input) return;
        if (System.Text.RegularExpressions.Regex.IsMatch(input.Name, "Value|Quantity|Amount|DisplayInput|Backstock|Defective|YLocation|PhysicalInput|PortInput|MaximumAttachment"))
            input.InputScope = new System.Windows.Input.InputScope { Names = { new System.Windows.Input.InputScopeName(System.Windows.Input.InputScopeNameValue.Number) } };
    }
    private void DatePickerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker picker) return;
        picker.SelectedDateChanged -= DateChanged;
        picker.SelectedDateChanged += DateChanged;
        picker.RemoveHandler(Keyboard.KeyDownEvent, new KeyEventHandler(DateKeyDown));
        picker.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(DateKeyDown), true);
        picker.RemoveHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(DateFocusLost));
        picker.AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(DateFocusLost), true);
        FormatDate(picker);
        picker.CalendarOpened -= CalendarOpened;
        picker.CalendarOpened += CalendarOpened;
    }
    private void DateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker picker) picker.Dispatcher.BeginInvoke(() => FormatDate(picker));
    }
    private static void DateKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is DatePicker picker && e.Key == Key.Return)
            picker.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => FormatDate(picker));
    }
    private static void DateFocusLost(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is DatePicker picker)
            picker.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => { CommitTypedDate(picker); FormatDate(picker); });
    }
    // RA-UI-28 (1.10.1): a typed date commits when focus leaves the box, not only on Enter.
    private static void CommitTypedDate(DatePicker picker)
    {
        if (picker.Template?.FindName("PART_TextBox", picker) is not DatePickerTextBox editor) return;
        if (TryParseTypedDate(editor.Text, out var typed) && picker.SelectedDate?.Date != typed && IsInRange(picker, typed)) picker.SelectedDate = typed;
    }
    private static bool IsInRange(DatePicker picker, DateTime date) =>
        !picker.BlackoutDates.Contains(date) && (picker.DisplayDateStart is not { } start || date >= start) && (picker.DisplayDateEnd is not { } end || date <= end);
    internal static bool TryParseTypedDate(string? text, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        return DateTime.TryParseExact(text, ["dd MMM yyyy", "d MMM yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd"], PresentationCulture.Indian, System.Globalization.DateTimeStyles.None, out date)
            || DateTime.TryParse(text, PresentationCulture.Indian, System.Globalization.DateTimeStyles.None, out date);
    }
    private static void FormatDate(DatePicker picker)
    {
        // DatePicker.Text normalises back to the language's built-in short-date format.
        // Update its editor after that normalisation, including same-date typed commits.
        if (picker.SelectedDate is { } date && picker.Template?.FindName("PART_TextBox", picker) is DatePickerTextBox editor)
            editor.SetCurrentValue(TextBox.TextProperty, date.ToString("dd MMM yyyy", PresentationCulture.Indian));
    }
    private void CalendarOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker picker || picker.Template.FindName("PART_Popup", picker) is not Popup { Child: Calendar calendar }) return;
        calendar.MinWidth = 350; calendar.FontSize = 14;
        calendar.ApplyTemplate(); calendar.UpdateLayout();
        // S4 / DS-13: the day buttons follow the density setting (44 Touch, 36 Desktop) instead of a fixed 44.
        ResizeCalendarTargets(calendar, picker.TryFindResource("ActiveTargetHeight") is double height ? height : 44);
    }
    public static void ResizeCalendarTargets(DependencyObject root) => ResizeCalendarTargets(root, 44);
    public static void ResizeCalendarTargets(DependencyObject root, double targetHeight)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ButtonBase button) { button.MinWidth = targetHeight; button.MinHeight = targetHeight; button.FontSize = 14; }
            ResizeCalendarTargets(child, targetHeight);
        }
    }
}
