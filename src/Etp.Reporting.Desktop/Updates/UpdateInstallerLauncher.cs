extern alias EtpApplication;

using System.Diagnostics;
using System.IO;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Updates;

/// <summary>
/// 1.12 (B1). Starts the staged setup for an unattended update, after which ETP must exit (setup waits for ETP's
/// "running" mutex to go, then refuses as before if it does not).
///
/// Arguments: <c>/SILENT /SUPPRESSMSGBOXES /NORESTART /LOG="..." /MERGETASKS="!sqlprerequisites" /ETPUPDATE=1 /ETPATTEMPT=&lt;id&gt;</c>.
/// - /ETPUPDATE=1 makes setup wait for ETP to close, write <c>Updates\setup-result.json</c>, and start ETP again on success.
/// - The SQL media option is never chosen by an update (an existing database is upgraded either way).
///
/// Setup is started WITHOUT the "runas" verb, on purpose: the installer asks for elevation itself (PrivilegesRequired=admin,
/// a normal UAC prompt), and keeps its original unelevated process, so its restart entry
/// (<c>runasoriginaluser</c>) starts ETP as the Owner, not as an administrator (AUDIT-2 S-13: an elevated ETP is what
/// the DLL-planting finding was about). Started with "runas" there would be no unelevated original to return to.
/// </summary>
internal static class UpdateInstallerLauncher
{
    public static string SetupLogPath(StagedInstaller staged, string attemptId) =>
        Path.Combine(staged.Folder, $"setup-{attemptId}.log");

    internal static ProcessStartInfo BuildStartInfo(string installerPath, string logPath, string attemptId)
    {
        if (!UpdateAttemptRecord.IsAttemptIdText(attemptId)) throw new ArgumentException("The attempt id is 32 lower-case hex characters.", nameof(attemptId));
        if (!Path.IsPathFullyQualified(installerPath) || !Path.IsPathFullyQualified(logPath))
            throw new ArgumentException("The installer and its log need full paths.");
        if (installerPath.Contains('"') || logPath.Contains('"')) throw new ArgumentException("A path with a quote cannot be passed to setup.");
        return new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /LOG=\"{logPath}\" /MERGETASKS=\"!sqlprerequisites\" /ETPUPDATE=1 /ETPATTEMPT={attemptId}",
            // Shell execute: if Windows must elevate the stub itself it shows the UAC prompt instead of failing with 740.
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath)!,
        };
    }

    /// <summary>Starts setup. Throws (Win32Exception, InvalidOperationException) when it could not be started.</summary>
    public static void Launch(StagedInstaller staged, string attemptId)
    {
        ArgumentNullException.ThrowIfNull(staged);
        using var process = Process.Start(BuildStartInfo(staged.Path, SetupLogPath(staged, attemptId), attemptId))
            ?? throw new InvalidOperationException("Setup could not be started.");
    }
}
