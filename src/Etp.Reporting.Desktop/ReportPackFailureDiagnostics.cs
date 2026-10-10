using System.Globalization;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop;

/// <summary>
/// IE-CODE-09. A scheduled or auto-import report pack that failed in the unattended run. The
/// automation history row says "Details are in the application diagnostics log"; this writes them.
/// </summary>
internal static class ReportPackFailureDiagnostics
{
    public static void Record(AutomatedReportPackFailure failure) => Record(failure, null);

    internal static void Record(AutomatedReportPackFailure failure, string? logDirectory)
    {
        ArgumentNullException.ThrowIfNull(failure);
        DesktopDiagnostics.Record(failure.Exception, "Automation.ReportPack", EventId(failure), DesktopDiagnosticSeverity.Error,
            failure.BusinessDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture), logDirectory);
    }

    internal static string EventId(AutomatedReportPackFailure failure)
    {
        var eventId = string.Concat(failure.RunType.Trim().ToUpperInvariant().Select(character =>
            char.IsAsciiLetterOrDigit(character) ? character : '_')) + "_FAILED";
        if (failure.SqlErrorNumber is { } number) eventId += ".SQL" + number.ToString(CultureInfo.InvariantCulture);
        return eventId;
    }
}
