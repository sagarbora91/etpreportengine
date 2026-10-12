using System.Windows;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Modules.Imports;

public partial class ImportWorkspaceView
{
    /// <summary>Asks for the store, kind and date. Tests replace it; the default shows <see cref="BlankDailySheetDialog"/>.</summary>
    internal Func<BlankDailySheetRequest?>? BlankSheetChooser { get; set; }

    /// <summary>Asks where to save (suggested file name in, path or null out). Tests replace it; the default is the shared export Save dialog.</summary>
    internal Func<string, string?>? BlankSheetSavePath { get; set; }

    /// <summary>Gets the blank daily sheet. Owner and Store Manager only (the people who can enter manual inputs).</summary>
    public bool GetBlankDailySheet()
    {
        if (IsBusy) return false;
        if (!accessProvider().CanImport)
        {
            ValidationResult.Text = "Owner or Store Manager permission is required to get a blank daily sheet.";
            return false;
        }
        try
        {
            var request = BlankSheetChooser is { } choose
                ? choose()
                : BlankDailySheetDialog.Choose(ExportSaveDialog.Owner(this), DateOnly.FromDateTime(DateTime.Today));
            if (request is null) return false;
            var name = BlankDailySheet.SuggestedFileName(request);
            var path = BlankSheetSavePath is { } pick ? pick(name) : ExportSaveDialog.Show(this, ExportSaveDialog.ExcelFilter, name);
            if (string.IsNullOrWhiteSpace(path)) return false;
            BlankDailySheet.Save(path, request);
            ValidationResult.Text = $"Blank daily sheet saved: {path}. Fill it in, then save it in the daily-inputs folder.";
            return true;
        }
        catch (Exception exception)
        {
            ValidationResult.Text = DesktopDiagnostics.WithReference(
                "The blank daily sheet could not be saved. " + DesktopFriendlyError.Describe(exception),
                DesktopDiagnostics.Record(exception, "Imports.Workspace", "BLANK_DAILY_SHEET_FAILED", operation: "Blank daily sheet failed"));
            return false;
        }
    }

    private void GetBlankSheet_Click(object sender, RoutedEventArgs e) => GetBlankDailySheet();
}
