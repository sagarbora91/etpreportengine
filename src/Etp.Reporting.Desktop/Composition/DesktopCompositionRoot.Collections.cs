using Etp.Reporting.Desktop.Modules.Collections;
using Etp.Reporting.Infrastructure.SqlServer.Collections;

namespace Etp.Reporting.Desktop.Composition;

// 1.12 wave 2, lane U4: the Collections screens. One registration per task id (the task rows are U1's); each factory runs on
// every visit and builds its services from the window's current connection factory, so a connection changed in Settings is the
// one used. The screens never see a connection; roles come from the request's ShellAccess and are checked again by the services.
public sealed partial class DesktopCompositionRoot
{
    private CollectionsServices CreateCollectionsServices() => new(
        Setup: () => new SqlServerCollectionsSetupService(CurrentConnections()),
        Import: () => new SqlServerCollectionsImportService(CurrentConnections()),
        Deposits: () => new SqlServerCashDepositService(CurrentConnections()),
        Statement: () => new SqlServerCollectionsStatementQuery(CurrentConnections()));

    partial void RegisterCollections(ScreenRegistry registry) => RegisterCollectionScreens(registry);

    /// <summary>Registers the four screens by task id. Internal so a test can register them without building a window.</summary>
    internal void RegisterCollectionScreens(ScreenRegistry registry)
    {
        var services = CreateCollectionsServices();

        registry.Register(CollectionsScreens.SetupTask, request =>
        {
            System.Windows.Controls.UserControl? view = null;
            var dialogs = new WindowsCollectionsDialogs(() => view);
            view = new CollectionsSetupView(new CollectionsSetupModel(services.Setup, request.Access, dialogs));
            return view;
        });

        registry.Register(CollectionsScreens.BankFilesTask, request =>
        {
            System.Windows.Controls.UserControl? view = null;
            var dialogs = new WindowsCollectionsDialogs(() => view);
            view = new BankFilesView(new BankFilesModel(services.Import, request.Access), dialogs);
            return view;
        });

        registry.Register(CollectionsScreens.StatementTask, request =>
            new CollectionsStatementView(new CollectionsStatementModel(services.Statement, request.Access, request.StoreCode, request.BusinessDate)));

        registry.Register(CollectionsScreens.DepositsTask, request =>
        {
            System.Windows.Controls.UserControl? view = null;
            var dialogs = new WindowsCollectionsDialogs(() => view);
            view = new DepositsInTransitView(new DepositsInTransitModel(services.Deposits, request.Access, request.StoreCode), dialogs);
            return view;
        });
    }
}
