using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace Etp.Reporting.Desktop.Themes;

public partial class ControlsTheme : ResourceDictionary
{
    public ControlsTheme() => InitializeComponent();
    private void DatePickerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker picker) return;
        picker.CalendarOpened -= CalendarOpened;
        picker.CalendarOpened += CalendarOpened;
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
