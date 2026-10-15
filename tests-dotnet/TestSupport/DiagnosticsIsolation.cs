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
        // 1.12 U1 saves the recent screens on every navigation: keep the walks out of the Owner's ui-preferences.json too.
        Environment.SetEnvironmentVariable(UiPreferenceStore.DirectoryVariable, Path.Combine(LogDirectory, "ui-preferences"));
        // 1.12.1 P7: the readiness checks read the shop-live file; a test run must never see the Owner's real one.
        Environment.SetEnvironmentVariable("ETP_SHOP_LIVE_FILE", Path.Combine(LogDirectory, "no-shop-live-file.json"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(LogDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
