using System.Windows;
using Etp.Reporting.Desktop.Modules.Morning;
using Etp.Reporting.Desktop.Modules.NoSaleDays;
using Etp.Reporting.Infrastructure.SqlServer.NoSaleDays;

namespace Etp.Reporting.Desktop.Composition;

// No-sale days (1.12 lane N1, decision 52): builds the mark / withdraw buttons once. They have no screen of their own; the
// Today checklist and Morning page (U1), Close day (U2) and Today sales (N1) place them through NoSaleDayActions.Current,
// exposed by the shell as MainWindow.NoSaleDayButtons. The service reads the window's current connection when a button is used.
public sealed partial class DesktopCompositionRoot
{
    partial void RegisterNoSaleDays(ScreenRegistry registry)
    {
        var actions = new NoSaleDayActions(
            () => new SqlServerNoSaleDayService(CurrentConnections()),
            () => (System.Windows.Application.Current?.MainWindow as MainWindow)?.CurrentShellAccess ?? ShellAccess.DatabaseSetup);
        NoSaleDayActions.Current = actions;
        // Integration (U1 x N1): the Morning page and the Today checklist take the buttons from the window. A role that may
        // not mark gets an empty collapsed element (the pages add whatever they are given to a panel).
        if (registry.Window is { } window)
        {
            window.NoSaleDayButtons = new NoSaleDayButtons(
                (store, date, done) => (UIElement?)actions.CreateMarkButton(store, date, done) ?? Nothing(),
                (store, date, done) => (UIElement?)actions.CreateWithdrawButton(store, date, done) ?? Nothing());
        }
    }

    private static UIElement Nothing() => new FrameworkElement { Visibility = Visibility.Collapsed };

    /// <summary>
    /// Integration (U2 x N1): Close day's verdict band asks for one button for an open store-day without a sales file.
    /// Withdraw when the day is marked, Mark when it is not, null for a role that may not change it.
    /// </summary>
    private static async Task<UIElement?> CreateNoSaleDayButtonAsync(string storeCode, DateOnly day, Func<Task> afterChange)
    {
        if (NoSaleDayActions.Current is not { } actions || !actions.CanChange) return null;
        var marks = await actions.LoadAsync(day);
        var marked = marks.Any(mark => string.Equals(mark.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase));
        void Done() => _ = afterChange();
        return marked ? actions.CreateWithdrawButton(storeCode, day, Done) : actions.CreateMarkButton(storeCode, day, Done);
    }
}
