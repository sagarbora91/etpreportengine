extern alias EtpApplication;

using System.Windows;
using Etp.Reporting.Infrastructure.SqlServer.ImportSourceLocations;
using FolderImportFileResult = EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult;
using ImportDiagnosticCatalogue = EtpApplication::Etp.Reporting.Application.Imports.ImportDiagnosticCatalogue;
using ImportFixKind = EtpApplication::Etp.Reporting.Application.Imports.ImportFixKind;

namespace Etp.Reporting.Desktop.Modules.Imports;

/// <summary>
/// What a pressed Fix button does on screen (1.12 U2). Each action reports one sentence through <c>report</c> and, after a
/// re-import, asks the view to load again (<c>reload</c>), so a fixed row leaves Problems and History shows the new attempt.
/// The dialogs and the clipboard are replaceable for tests.
/// </summary>
public sealed class ImportFixHandlers(DependencyObject owner, Action<string> report, Func<Task> reload)
{
    /// <summary>Asks the Owner for the day and the reason; null when the Owner keeps working.</summary>
    internal Func<ReopenAndReapplyRequest, ReopenAndReapplyAnswer?> AskReopen { get; set; } = request => ReopenAndReapplyDialog.Ask(owner, request);

    /// <summary>Confirms a retry (one ConfirmationSheet).</summary>
    internal Func<string, bool> ConfirmRetry { get; set; } = fileName =>
        ConfirmationSheet.Show(owner, "Retry", $"Import {fileName} again from where ETP keeps it? Nothing in the file changes.");

    internal Action<string> CopyText { get; set; } = text => Clipboard.SetText(text);

    public async Task RunAsync(ImportFixKind kind, FolderImportFileResult result, ImportFixLocation? location, ImportFixServices services)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(services);
        try
        {
            switch (kind)
            {
                case ImportFixKind.ReopenDayAndReapply:
                {
                    if (location is null) { report(SqlServerImportFixService.MissingFileMessage); return; }
                    var answer = AskReopen(new(result.FileName, result.StoreCode, result.PeriodStart, result.PeriodEnd));
                    if (answer is null) { report("Nothing changed. The day stays finalised."); return; }
                    var outcome = await services.Fixes().ReopenAndReapplyAsync(location, ImportFixRows.TargetFor(result), answer.StoreCode,
                        answer.BusinessDate, answer.Reason);
                    report(Join(outcome.Message, outcome.ShopResultNote));
                    await reload();
                    return;
                }
                case ImportFixKind.Retry:
                {
                    if (location is null) { report(SqlServerImportFixService.MissingFileMessage); return; }
                    if (!ConfirmRetry(result.FileName)) return;
                    var outcome = await services.Fixes().RetryAsync(location, ImportFixRows.TargetFor(result));
                    report(Join(outcome.Message, outcome.ShopResultNote));
                    await reload();
                    return;
                }
                case ImportFixKind.TellShop:
                    Copy(ImportDiagnosticCatalogue.ShopSentence(result.Failure?.Code), "Copied the sentence for the shop.");
                    return;
                case ImportFixKind.OpenFolder:
                    if (location is not { Exists: true, UnderConfiguredRoot: true }) { report("ETP opens only its own import folders."); return; }
                    services.OpenFolder(location.Location.RoutedPath);
                    return;
                case ImportFixKind.CopyRef:
                    if (location?.Location.AttemptRef is { } reference) Copy(reference, $"Copied Ref {reference}.");
                    return;
                case ImportFixKind.OpenErrorRegister:
                    if (location?.Location.AttemptRef is { } at && services.OpenErrorRegister is { } open) open(at);
                    return;
            }
        }
        catch (Exception exception)
        {
            report(DesktopDiagnostics.WithReference(DesktopFriendlyError.Describe(exception),
                DesktopDiagnostics.Record(exception, "Imports.Fix", "IMPORT_FIX_FAILED", operation: "Import fix failed")));
        }
    }

    private void Copy(string text, string done)
    {
        try { CopyText(text); report(done); }
        catch (System.Runtime.InteropServices.ExternalException) { report("Could not copy: another program is using the clipboard. Try again."); }
    }

    private static string Join(string message, string note) => string.IsNullOrWhiteSpace(note) ? message : $"{message} {note.Trim()}";
}
