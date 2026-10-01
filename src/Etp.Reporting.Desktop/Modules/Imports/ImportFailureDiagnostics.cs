extern alias EtpApplication;
using EtpApplication::Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Modules.Imports;

/// <summary>
/// Writes an unexpected import failure to the desktop diagnostics log. The log keeps only
/// tokens, never free text, so the stage, report family, SQL error number and commit state travel
/// in the event id and the import batch in the correlation id; the file name and message stay out.
/// </summary>
internal static class ImportFailureDiagnostics
{
    public static void Record(FolderImportFailure failure) => Record(failure, null);

    internal static void Record(FolderImportFailure failure, string? logDirectory)
    {
        ArgumentNullException.ThrowIfNull(failure);
        DesktopDiagnostics.Record(failure.Exception, "Imports.Folder", EventId(failure), DesktopDiagnosticSeverity.Error,
            failure.BatchId?.ToString("N"), logDirectory);
    }

    internal static string EventId(FolderImportFailure failure)
    {
        var eventId = $"IMPORT_{failure.Stage.ToDatabaseCode()}_FAILED";
        if (!string.IsNullOrWhiteSpace(failure.ReportCode)) eventId += "." + failure.ReportCode.Trim().ToUpperInvariant();
        if (failure.SqlErrorNumber is { } number) eventId += ".SQL" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (failure.CommitState is { } state) eventId += "." + state.ToDatabaseCode();
        return eventId;
    }
}
