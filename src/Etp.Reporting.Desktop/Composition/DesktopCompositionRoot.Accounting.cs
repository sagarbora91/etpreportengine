using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Desktop.Modules.Accounting;

namespace Etp.Reporting.Desktop.Composition;

// Accounting: the accounting batches workspace. The Tally screens (Settings > Integrations) are built by the Settings
// workspace in 1.12; U3 moves them here in wave 2 (DesktopCompositionRoot.Accounting.cs is U3's then).
public sealed partial class DesktopCompositionRoot
{
    private static AccountingWorkspaceView CreateAccountingWorkspaceView(Func<string> connection) =>
        new(new AccountingPresentationSession(() => new SqlServerAccountingService(connection())));
}
