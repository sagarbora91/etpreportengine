using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 U2 (Close day): Finalise sits in the verdict band (the 1.11.3 leftover), the root children TaskNavigator counts are
/// unchanged, and a day with no sales file offers N1's "Mark as no-sale day" there to the Owner and the Store Manager only, never
/// on a finalised day (decision 52).
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class DailyWorkflowVerdictBandTests
{
    private static readonly DateOnly Day = new(2026, 10, 11);

    private static DailyWorkflowState State(DailyWorkflowStatus status, params string[] missing) =>
        new("TST01", Day, status, ["R022"], missing, [], [], status == DailyWorkflowStatus.Reconciled, "Synthetic");

    [Fact]
    public void The_presentation_says_when_the_sales_file_is_missing_and_when_the_day_is_locked()
    {
        var session = new DailyWorkflowPresentationSession();
        var open = session.Show(State(DailyWorkflowStatus.NotReady, "R025", "R011"), []);
        Assert.True(open.NoSalesFile);
        Assert.False(open.IsLocked);
        var locked = session.Show(State(DailyWorkflowStatus.Locked), []);
        Assert.False(locked.NoSalesFile);
        Assert.True(locked.IsLocked);
        Assert.False(session.Show(State(DailyWorkflowStatus.NotReady, "R011"), []).NoSalesFile);
    }

    [Theory]
    [InlineData(true, false, true, true)]    // Store Manager, open day without sales: shown
    [InlineData(true, true, true, true)]     // Owner: shown
    [InlineData(false, false, true, false)]  // Viewer: never
    [InlineData(true, true, false, false)]   // sales file present: nothing to mark
    public void The_no_sale_day_button_is_for_the_owner_and_store_manager_on_an_open_day_without_sales(bool canImport, bool owner, bool noSales, bool shown)
    {
        var state = new DailyWorkflowPresentationSession().Show(State(DailyWorkflowStatus.NotReady, noSales ? new[] { "R025" } : new[] { "R011" }), []);
        Assert.Equal(shown, DailyWorkflowWorkspaceView.ShowsNoSaleDayButton(state, new(true, canImport, owner)));
        var locked = new DailyWorkflowPresentationSession().Show(State(DailyWorkflowStatus.Locked, "R025"), []);
        Assert.False(DailyWorkflowWorkspaceView.ShowsNoSaleDayButton(locked, new(true, true, true)));
    }

    [Fact]
    public void A_mark_that_no_longer_applies_offers_withdraw_on_close_day_even_with_the_sales_file()
    {
        // Review M-1: sales were imported after the mark; R025 is no longer missing, but the Owner / Store Manager must be able to
        // withdraw the mark from Close day too. Never for a Viewer or on a finalised day.
        var voided = State(DailyWorkflowStatus.NotReady, "R011") with { NoSaleMarkNoLongerApplies = true };
        var state = new DailyWorkflowPresentationSession().Show(voided, []);
        Assert.True(state.NoSaleMarkNoLongerApplies);
        Assert.True(DailyWorkflowWorkspaceView.ShowsNoSaleDayButton(state, new(true, true, false)));
        Assert.False(DailyWorkflowWorkspaceView.ShowsNoSaleDayButton(state, new(true, false, false)));
        var locked = new DailyWorkflowPresentationSession().Show(State(DailyWorkflowStatus.Locked) with { NoSaleMarkNoLongerApplies = true }, []);
        Assert.False(DailyWorkflowWorkspaceView.ShowsNoSaleDayButton(locked, new(true, true, true)));
    }

    [Fact]
    public void A_staff_target_period_the_user_typed_survives_a_business_date_change()
    {
        // Review m-7: re-default only empty fields or fields still holding the previous default.
        (DateOnly From, DateOnly To) october = (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.True(DailyWorkflowWorkspaceView.ShouldReplaceStaffTargetPeriod(new DateTime(2026, 9, 3), new DateTime(2026, 9, 9), null));
        Assert.True(DailyWorkflowWorkspaceView.ShouldReplaceStaffTargetPeriod(null, null, october));
        Assert.True(DailyWorkflowWorkspaceView.ShouldReplaceStaffTargetPeriod(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), october));
        Assert.False(DailyWorkflowWorkspaceView.ShouldReplaceStaffTargetPeriod(new DateTime(2026, 10, 5), new DateTime(2026, 10, 31), october));
        Assert.False(DailyWorkflowWorkspaceView.ShouldReplaceStaffTargetPeriod(new DateTime(2026, 10, 1), null, october));
        // 1.12.1 P4: a period the Owner picked is kept even when it equals the default.
        Assert.False(DailyWorkflowWorkspaceView.ShouldReplaceStaffTargetPeriod(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), october, typed: true));
        Assert.True(DailyWorkflowWorkspaceView.ShouldReplaceStaffTargetPeriod(null, null, october, typed: true));
    }

    [Fact]
    public void The_staff_target_period_follows_the_header_until_the_Owner_picks_one_and_again_once_both_are_cleared()
    {
        RunSta(() =>
        {
            var view = new DailyWorkflowWorkspaceView(new DailyWorkflowPresentationSession(),
                () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => throw new InvalidOperationException(),
                () => new(true, true, true), (_, _, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
            var header = (DatePicker)view.FindName("BusinessDateInput");
            var from = (DatePicker)view.FindName("StaffTargetFromInput");
            var to = (DatePicker)view.FindName("StaffTargetToInput");
            Assert.False(view.StaffTargetPeriodTyped);
            void Period(DateTime first, DateTime last)
            {
                Assert.Equal((DateTime?)first, from.SelectedDate);
                Assert.Equal((DateTime?)last, to.SelectedDate);
            }

            header.SelectedDate = new DateTime(2026, 10, 15);
            Period(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));
            Assert.False(view.StaffTargetPeriodTyped);

            // The Owner picks 2 Oct, then 1 Oct again: the same dates as the default, but chosen.
            from.SelectedDate = new DateTime(2026, 10, 2);
            from.SelectedDate = new DateTime(2026, 10, 1);
            Assert.True(view.StaffTargetPeriodTyped);
            header.SelectedDate = new DateTime(2026, 11, 3);
            Period(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

            // Both cleared: the next date change defaults them again.
            from.SelectedDate = null;
            to.SelectedDate = null;
            header.SelectedDate = new DateTime(2026, 11, 4);
            Period(new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
            Assert.False(view.StaffTargetPeriodTyped);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Finalise_is_in_the_verdict_band_and_the_root_still_has_nineteen_children()
    {
        RunSta(() =>
        {
            var view = new DailyWorkflowWorkspaceView(new DailyWorkflowPresentationSession(),
                () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => throw new InvalidOperationException(),
                () => new(true, true, true), (_, _, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
            var root = (StackPanel)((Border)view.Content).Child;
            // TaskNavigator picks this panel's children by index until U1's by-name selection lands.
            Assert.Equal(19, root.Children.Count);
            var band = (Border)root.Children[3];
            var finalise = (Button)view.FindName("FinaliseDayButton");
            Assert.True(IsInside(finalise, band));
            Assert.False(finalise.IsEnabled);
            Assert.NotNull(finalise.ToolTip); // B6: a disabled Finalise says why
            Assert.True(IsInside((FrameworkElement)view.FindName("ReopenDayButton"), (FrameworkElement)root.Children[15]));
            Assert.False(view.NoSaleDayButtonVisible);
            return Task.CompletedTask;
        });
    }

    private static bool IsInside(FrameworkElement element, FrameworkElement container)
    {
        for (DependencyObject? current = element; current is not null; current = LogicalTreeHelper.GetParent(current))
            if (ReferenceEquals(current, container)) return true;
        return false;
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action().GetAwaiter().GetResult(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA test did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
