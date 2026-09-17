using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class PhaseThreeDateInputTests
{
    [Theory]
    [InlineData("Shell", "ShellBusinessDateSelector")]
    [InlineData("Daily", "BusinessDateInput")]
    [InlineData("Daily", "StaffTargetFromInput")]
    [InlineData("Daily", "StaffTargetToInput")]
    [InlineData("Reports", "ReportFrom")]
    [InlineData("Reports", "ReportTo")]
    [InlineData("Cash", "From")]
    [InlineData("Cash", "To")]
    public void Date_inputs_keep_day_month_year_after_typed_commit(string workspace, string name)
    {
        Sta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                var owner = workspace switch
                {
                    "Shell" => (FrameworkElement)window,
                    "Daily" => window.dailyWorkflowWorkspace,
                    _ => window.reportsWorkspaceView
                };
                var cash = workspace == "Cash" ? new ReportWorkspaceControl(ReportWorkspaceDefinition.ForReport("cash")) : null;
                var picker = cash is null ? Assert.IsType<DatePicker>(owner.FindName(name))
                    : name == "From" ? cash.DateFromPicker : cash.DateToPicker;
                Theme(picker);
                picker.Language = XmlLanguage.GetLanguage("en-IN");
                picker.SelectedDate = new DateTime(2026, 8, 1);
                Layout(picker, 220, 48);
                picker.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, picker));
                Pump();
                var text = Assert.IsType<DatePickerTextBox>(picker.Template.FindName("PART_TextBox", picker));
                using var source = new HwndSource(new HwndSourceParameters("Date input test")
                    { Width = 1, Height = 1, WindowStyle = 0 });
                text.Text = "25 Aug 2026";
                text.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Return)
                    { RoutedEvent = Keyboard.KeyDownEvent });
                Pump();
                Assert.Equal(new DateTime(2026, 8, 25), picker.SelectedDate);
                Assert.Equal("25 Aug 2026", text.Text);
                // Committing the same date must also format correctly without a date-change event.
                text.Text = "25/08/2026";
                text.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Return)
                    { RoutedEvent = Keyboard.KeyDownEvent });
                Pump();
                Assert.Equal("25 Aug 2026", text.Text);
            }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
    }

    [Theory]
    [InlineData("Daily", "StaffTargetCroInput")]
    [InlineData("Operations", "ScheduleTimeInput")]
    [InlineData("Archive", "SharePhoneInput")]
    [InlineData("Archive", "ContactPhoneInput")]
    public void Numeric_fields_request_the_touch_number_keyboard(string workspace, string name)
    {
        Sta(() =>
        {
            var workspaces = ExtractedWorkspaceUiSmokeTests.CreateWorkspaces(Path.Combine(Path.GetTempPath(), "EtpNumericScope", Guid.NewGuid().ToString("N")));
            try
            {
                var owner = workspaces.Single(item => item.Name == workspace).View;
                var input = Assert.IsType<TextBox>(owner.FindName(name));
                Assert.Contains(input.InputScope?.Names.Cast<InputScopeName>() ?? [],
                    value => value.NameValue == InputScopeNameValue.Number);
            }
            finally
            {
                foreach (var disposable in workspaces.Select(item => item.View).OfType<IAsyncDisposable>())
                    disposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static void Theme(FrameworkElement element) => element.Resources.MergedDictionaries.Add(
        new ResourceDictionary { Source = new Uri("/Etp.Reporting.Desktop;component/Themes/Theme.xaml", UriKind.Relative) });

    private static void Layout(FrameworkElement root, double width, double height)
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            Pump();
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = PresentationCulture.Indian;
                action();
            }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "UI regression test timed out.");
        if (error is not null) throw new InvalidOperationException("Phase 3 audit regression failed.", error);
    }
}
