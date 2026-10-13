using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Desktop.Modules.Imports;

namespace Etp.Reporting.Desktop.Composition;

// Imports: the import workspace (single file, batch, folder) and Import history.
public sealed partial class DesktopCompositionRoot
{
    private static ImportWorkspaceView CreateImportWorkspaceView(Func<string> connection)
    {
        var importCoordinator = new DesktopImportCoordinator(
            () => new SqlServerImportPersistenceUseCase(connection()),
            reportImportFailure: ImportFailureDiagnostics.Record);
        return new ImportWorkspaceView(importCoordinator);
    }

    private static void AttachImportHistory(MainWindow window, Func<string> connection) =>
        window.importHistoryView = new ImportHistoryView(scope =>
            new SqlServerImportHistoryQuery(connection()).LoadAsync(scope));
}
