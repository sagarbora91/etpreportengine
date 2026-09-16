using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Themes;

internal static class ThemeControlAudit
{
    public static void Run(string output)
    {
        var picker = new DatePicker { Width = 190, HorizontalAlignment = HorizontalAlignment.Left };
        var root = new StackPanel { Background=Brushes.White, Margin = new Thickness(16) }; root.Children.Add(new TextBlock { Text="Business date", Margin=new Thickness(0,0,0,4) }); root.Children.Add(picker);
        var window = new Window { Content = root, Background=Brushes.White };
        Program.Render(window,Path.Combine(output,"date-field.png"),440,140);
        var text = picker.Template.FindName("PART_TextBox",picker) as DatePickerTextBox ?? throw new InvalidOperationException("Native date text part missing");
        var button = picker.Template.FindName("PART_Button",picker) as Button ?? throw new InvalidOperationException("Native calendar button missing");
        var expected = new DateTime(2026,9,15); picker.Text=expected.ToShortDateString();
        if(picker.SelectedDate!=expected) throw new InvalidOperationException("Typed date not parsed");
        var popup=picker.Template.FindName("PART_Popup",picker) as Popup ?? throw new InvalidOperationException("Native calendar popup missing");
        var calendar=popup.Child as Calendar ?? throw new InvalidOperationException("Native calendar missing");
        popup.Child=null; calendar.MinWidth=350; root.Children.Add(calendar);
        Program.Render(window,Path.Combine(output,"calendar-before-target-sizing.png"),440,470);
        ControlsTheme.ResizeCalendarTargets(calendar);
        Program.Render(window,Path.Combine(output,"calendar.png"),440,470);
        var days=Visuals(calendar).OfType<CalendarDayButton>().ToArray();
        if(days.Length!=42 || days.Any(day=>day.ActualWidth<44 || day.ActualHeight<44) || button.ActualWidth<44 || button.ActualHeight<44)
            throw new InvalidOperationException("Calendar has a target smaller than 44 DIP");
        calendar.SelectedDate=expected.AddDays(1);
        if(picker.SelectedDate!=expected.AddDays(1)) throw new InvalidOperationException("Calendar selection not reflected in field");
        var status=new StatusDetailsDialog(null,"Synthetic operational failure.\n\n"+string.Join("\n",Enumerable.Range(1,20).Select(index=>$"Detail line {index}: the original operation result is retained.")));
        Program.Render(status,Path.Combine(output,"status-details-360x280.png"),360,280);
        var report=new ReportWorkspaceControl(ReportWorkspaceDefinition.ForReport("invoice")); report.SelectReport("invoice");report.ConfigureTaskScope("Titan");
        var scopeDialog=new Etp.Reporting.Desktop.Modules.Reports.ReportScopeDialog(report);
        Program.Render(scopeDialog,Path.Combine(output,"report-scope-400x370.png"),400,370);
        File.WriteAllText(Path.Combine(output,"controls.json"),JsonSerializer.Serialize(new { result="PASS", method="WPF component events and offscreen bounds, full App resources. Not physical input or installed UI acceptance.", dateParsed=true, selectionUpdatesDate=true, dayCount=days.Length, minimumDayWidth=days.Min(day=>day.ActualWidth), minimumDayHeight=days.Min(day=>day.ActualHeight), calendarButtonWidth=button.ActualWidth,calendarButtonHeight=button.ActualHeight,timestamp=DateTimeOffset.UtcNow }));
        Console.WriteLine("Date parsing, selection, 42 day targets and status dialog composition passed.");
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
        { var child=VisualTreeHelper.GetChild(root,index);yield return child;foreach(var item in Visuals(child))yield return item; }
    }
}
