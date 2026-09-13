using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Desktop;

namespace Etp.Reporting.Desktop.Tests;

public sealed class FocusedTaskLayoutTests
{
    [Fact]
    public void Switching_tasks_and_returning_preserves_drafts_handlers_and_action_ownership()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var original = new StackPanel();
                var first = new WrapPanel(); var second = new WrapPanel();
                var input = new TextBox { Text = "Unfinished draft" };
                var save = new Button { Content = "Save" }; var refresh = new Button { Content = "Refresh" };
                var saves = 0; save.Click += (_, _) => saves++;
                first.Children.Add(input); first.Children.Add(save); second.Children.Add(refresh);
                original.Children.Add(first); original.Children.Add(second);
                var view = new UserControl { Content = original };
                for (var i = 0; i < 3; i++)
                {
                    FocusedTaskLayout.Show(view, "Edit", [], [0]);
                    Assert.Equal("Unfinished draft", input.Text);
                    Assert.NotSame(first, save.Parent);
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    FocusedTaskLayout.Show(view, "Review", [], [1]);
                    Assert.Same(first, save.Parent);
                    Assert.NotSame(second, refresh.Parent);
                }
                Assert.Equal(3, saves);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Retained task transition failed", failure);
    }
}
