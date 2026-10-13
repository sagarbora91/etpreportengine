using Etp.Reporting.Desktop.Modules.NoSaleDays;
using Etp.Reporting.Infrastructure.SqlServer.NoSaleDays;

namespace Etp.Reporting.Desktop.Composition;

// No-sale days (1.12 lane N1, decision 52): builds the mark / withdraw buttons once. They have no screen of their own; the
// Today checklist and Morning page (U1), Close day (U2) and Today sales (N1) place them through NoSaleDayActions.Current,
// exposed by the shell as MainWindow.NoSaleDays. The service reads the window's current connection when a button is used.
public sealed partial class DesktopCompositionRoot
{
    partial void RegisterNoSaleDays(ScreenRegistry registry)
    {
        NoSaleDayActions.Current = new NoSaleDayActions(
            () => new SqlServerNoSaleDayService(CurrentConnections()),
            () => (System.Windows.Application.Current?.MainWindow as MainWindow)?.CurrentShellAccess ?? ShellAccess.DatabaseSetup);
    }
}
