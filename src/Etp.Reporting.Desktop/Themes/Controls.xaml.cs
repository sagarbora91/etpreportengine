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
            picker.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => FormatDate(picker));
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
        ResizeCalendarTargets(calendar);
    }
    public static void ResizeCalendarTargets(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ButtonBase button) { button.MinWidth = 44; button.MinHeight = 44; button.FontSize = 14; }
            ResizeCalendarTargets(child);
        }
    }
}
