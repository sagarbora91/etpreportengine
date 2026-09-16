using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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
        FormatDate(picker);
        picker.CalendarOpened -= CalendarOpened;
        picker.CalendarOpened += CalendarOpened;
    }
    private void DateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker picker) picker.Dispatcher.BeginInvoke(() => FormatDate(picker));
    }
    private static void FormatDate(DatePicker picker)
    {
        if (picker.SelectedDate is { } date) picker.SetCurrentValue(DatePicker.TextProperty, date.ToString("dd MMM yyyy", PresentationCulture.Indian));
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
