using System.Runtime.CompilerServices;
using Etp.Reporting.Desktop;

namespace Etp.Reporting.TestSupport;

/// <summary>
/// Keeps test runs out of the Owner's diagnostics log under %LOCALAPPDATA%. Before any test
/// in the assembly runs, DesktopDiagnostics is pointed at a folder of this run's own under
/// %TEMP%, which is deleted when the test process exits. Processes the tests start - the
/// real application, the history restart host, PowerShell - inherit the variable.
/// Linked into every test assembly that can reach DesktopDiagnostics.
/// </summary>
internal static class DiagnosticsIsolation
{
    public static string LogDirectory { get; } = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"EtpTestDiagnostics_{Guid.NewGuid():N}")).FullName;

    [ModuleInitializer]
    internal static void Redirect()
    {
        Environment.SetEnvironmentVariable(DesktopDiagnostics.DirectoryVariable, LogDirectory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(LogDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
