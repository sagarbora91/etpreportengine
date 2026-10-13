using System.IO;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop;

/// <summary>
/// 1.12 A3: the headless verb <c>--plan-database-changes</c>. Prints one line of JSON to stdout,
/// <c>{"pendingMigrations":[...],"pendingObjects":[...]}</c>, and exits 0; changes nothing. Setup
/// (<c>bootstrap-etp-prerequisites.ps1</c>) takes the verified pre-migration backup when either list is non-empty, and
/// after the bootstrap requires both to be empty. The target is the protected machine database (as for
/// <c>--initialize-configured-database</c>), or the one given with <c>--connection-string</c>. A failure writes the
/// usual <c>ETP-STARTUP-FAILED mode=plan-database-changes ...</c> line to stderr and exits 1.
/// </summary>
internal static class DatabaseChangePlanCommand
{
    internal const string Verb = "--plan-database-changes";
    internal const string ModeToken = "plan-database-changes";

    internal static bool IsRequested(IReadOnlyList<string> arguments) =>
        arguments.Any(argument => string.Equals(argument, Verb, StringComparison.Ordinal));

    internal static async Task<int> RunAsync(IReadOnlyList<string> arguments, TextWriter output, Action<string> writeFailure,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = arguments.Contains("--connection-string")
                ? DesktopCompositionRoot.CreateForArguments(arguments).LoadConnectionString()
                : DesktopCompositionRoot.LoadAutomationConnectionString();
            var migrationDirectory = Path.Combine(AppContext.BaseDirectory, "database", "migrations");
            var plan = await DatabaseChangePlanner.PlanAsync(connectionString, migrationDirectory, cancellationToken).ConfigureAwait(false);
            output.WriteLine(plan.ToJson());
            output.Flush();
            return 0;
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "DatabaseChangePlan", "STARTUP_FAILED", DesktopDiagnosticSeverity.Critical);
            writeFailure(StartupFailureText.HeadlessLine(ModeToken, exception));
            return 1;
        }
    }
}
