extern alias EtpApplication;

using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.ImportSourceLocations;
using HistoryScope = EtpApplication::Etp.Reporting.Application.Imports.ImportHistoryScope;

namespace Etp.Reporting.Desktop.Composition;

// 1.12 U2 (wave plan C-c): the import Fix buttons. Problems and History are built by the shell, so their services go through
// ImportFixServices.Current (as DiagnosticsScreens.Services does); every service reads the window's current connection when
// a button is pressed. "import-problems" is the screen a Fix link opens (U1's Morning card: TryNavigateTask("import-problems")):
// today's problems for the header store, filtered to the argument (a file name or its SHA-256) when one is given.
public sealed partial class DesktopCompositionRoot
{
    /// <summary>The task id the Morning page's Fix links open (frozen in 112-W2-PLAN section 4).</summary>
    public const string ImportProblemsTask = "import-problems";

    partial void RegisterImportFixes(ScreenRegistry registry)
    {
        ImportFixServices.Current = CreateImportFixServices(CurrentConnections);
        // Month start (C-d): staff names beside CRO numbers in the copied staff targets (Owner-only screens).
        StaffNameDirectory.Load = async token =>
            (await new DataTruthMasterRepository(CurrentConnections().LegacyConnectionString()).LoadStaffAsync(token).ConfigureAwait(true))
            .Select(staff => new StaffNameEntry(staff.StoreCode, staff.Code, staff.Name)).ToArray();
        registry.Register(ImportProblemsTask, request => new ImportProblemsView(null, async () =>
        {
            var scope = new HistoryScope(request.BusinessDate, request.BusinessDate, request.StoreCode);
            var history = await new SqlServerImportHistoryQuery(CurrentConnections().LegacyConnectionString()).LoadAsync(scope);
            var problems = ImportProblems.FromHistory(history.Select(entry => (entry.RecordedUtc, entry.Result)));
            return FilterProblems(problems, request.Argument);
        }));
    }

    /// <summary>The Fix buttons' services on the given connection (the window's current one, read on every use).</summary>
    internal static ImportFixServices CreateImportFixServices(Func<IEtpConnectionFactory> connections) => new()
    {
        Fixes = () => new SqlServerImportFixService(connections(), ImportFailureDiagnostics.Record),
        Locations = () => new SqlServerImportSourceLocationStore(connections()),
        LoadAccess = async token =>
        {
            var access = await new Phase2OperationsRepository(connections().LegacyConnectionString()).LoadCurrentAccessAsync(token).ConfigureAwait(true);
            return new ImportWorkspaceAccess(access.CanImport, access.CanAdminister);
        }
    };

    /// <summary>
    /// "Enter stock by brand" (1.12 U2: the window takes services, not a connection string). The core composition builds the
    /// window with <c>new BrandStockEntryWindow(CreateBrandStockEntryServices(() => connectionState.Factory), store, date)</c>.
    /// </summary>
    internal static BrandStockEntryServices CreateBrandStockEntryServices(Func<IEtpConnectionFactory> connections) => new(
        (store, date) => new OperationalReportRepository(connections().LegacyConnectionString()).LoadBrandStockEntryAsync(store, date),
        async () => (await new Phase2OperationsRepository(connections().LegacyConnectionString()).LoadCurrentAccessAsync().ConfigureAwait(true)).CanImport,
        count => new OperationalCompletionRepository(connections().LegacyConnectionString()).SaveManualStockCountAsync(count.StoreCode, count.BusinessDate,
            count.Brand, count.Display, count.Backstock, count.Defective, count.YLocation, count.Total, count.Remark, count.User, count.Reason));

    /// <summary>Only the rows of the file a Fix link names (by file name or source SHA-256); all rows when it names none.</summary>
    internal static IReadOnlyList<ImportProblem> FilterProblems(IReadOnlyList<ImportProblem> problems, string? argument) =>
        string.IsNullOrWhiteSpace(argument) ? problems
            : problems.Where(problem => string.Equals(problem.File, argument.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(problem.Source?.SourceSha256, argument.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
}
