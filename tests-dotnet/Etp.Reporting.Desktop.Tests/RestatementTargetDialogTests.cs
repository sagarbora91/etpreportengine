using Etp.Reporting.Application.Imports;
using Etp.Reporting.Desktop.Modules.Imports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>IF-016 interim: the Owner picks which overlapping current import a restatement replaces.</summary>
[Collection(WpfViewCollection.Name)]
public sealed class RestatementTargetDialogTests
{
    private static readonly RestatementTargetChoice Choice = new("R025_WLMHW_full.xlsx", "R025", "WLMHW",
        new(2026, 8, 1), new(2026, 8, 31),
        [
            new(11, "R025_WLMHW_first_half.xlsx", new(2026, 8, 1), new(2026, 8, 15), 1250),
            new(12, "R025_WLMHW_25Aug.xlsx", new(2026, 8, 25), new(2026, 8, 25), 40)
        ]);

    [Fact]
    public void Dialog_lists_file_id_name_period_and_rows_and_needs_a_selection()
    {
        RunSta(() =>
        {
            var dialog = new RestatementTargetDialog(null, Choice);
            Assert.Equal(["File id", "File name", "Period", "Rows"], dialog.CandidateGrid.Columns.Select(column => column.Header?.ToString()));
            Assert.Equal(Choice.Candidates, dialog.CandidateGrid.ItemsSource);
            Assert.False(dialog.RestateButton.IsEnabled);
            dialog.CandidateGrid.SelectedIndex = 1;
            Assert.True(dialog.RestateButton.IsEnabled);
            Assert.Null(dialog.SelectedCandidate);
            dialog.Close();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dialog_returns_the_picked_import_or_none_when_closed(bool pick)
    {
        RunSta(() =>
        {
            var dialog = new RestatementTargetDialog(null, Choice)
            {
                ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000
            };
            // Never leave a modal window behind if the test goes wrong.
            var guard = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Normal, (_, _) => dialog.Close(), dialog.Dispatcher);
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
            {
                if (!pick) { dialog.Close(); return; }
                dialog.CandidateGrid.SelectedIndex = 1;
                dialog.RestateButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }, DispatcherPriority.Background);

            var result = dialog.ShowDialog();
            guard.Stop();

            Assert.Equal(pick, result == true);
            Assert.Equal(pick ? Choice.Candidates[1] : null, dialog.SelectedCandidate);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { action(); } catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "Restatement target dialog test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
