using System.Windows;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Modules.Imports;

public partial class ImportWorkspaceView
{
    /// <summary>The sheets offered, from the store list (<see cref="SetBlankSheetStores"/>); empty until the stores load.</summary>
    internal IReadOnlyList<BlankDailySheetOption> BlankSheetOptions { get; private set; } = [];

    /// <summary>Called with the store catalogue whenever the stores load or change.</summary>
    public void SetBlankSheetStores(IEnumerable<StoreCatalogEntry> stores) => BlankSheetOptions = BlankDailySheet.OptionsFor(stores);

    /// <summary>Asks for the store, kind and date. Tests replace it; the default shows <see cref="BlankDailySheetDialog"/>.</summary>
    internal Func<BlankDailySheetRequest?>? BlankSheetChooser { get; set; }

    /// <summary>Asks where to save (suggested file name in, path or null out). Tests replace it; the default is the shared export Save dialog.</summary>
    internal Func<string, string?>? BlankSheetSavePath { get; set; }

    /// <summary>Asks for the folder when "Save all sheets" is ticked (null = cancelled). Tests replace it; the default is the folder picker.</summary>
    internal Func<string?>? BlankSheetFolderPath { get; set; }

    /// <summary>The folder the last blank sheet(s) went to; "Open folder" shows it.</summary>
    internal string? LastBlankSheetFolder { get; private set; }

    private static string? PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder for the blank daily sheets" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>Opens the folder the last blank sheet(s) were saved to.</summary>
    internal bool OpenBlankSheetFolder()
    {
        if (string.IsNullOrWhiteSpace(LastBlankSheetFolder) || !System.IO.Directory.Exists(LastBlankSheetFolder)) return false;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + LastBlankSheetFolder + "\"") { UseShellExecute = true });
        return true;
    }

    /// <summary>Gets the blank daily sheet. Owner and Store Manager only (the people who can enter manual inputs).</summary>
    public bool GetBlankDailySheet()
    {
        if (IsBusy) return false;
        if (!accessProvider().CanImport)
        {
            ValidationResult.Text = "Owner or Store Manager permission is required to get a blank daily sheet.";
            return false;
        }
        if (BlankSheetChooser is null && BlankSheetOptions.Count == 0)
        {
            ValidationResult.Text = "No active shop store is set up yet, so there is no daily sheet to get. Check Settings > Stores.";
            return false;
        }
        try
        {
            var request = BlankSheetChooser is { } choose
                ? choose()
                : BlankDailySheetDialog.Choose(ExportSaveDialog.Owner(this), BlankSheetOptions, DateOnly.FromDateTime(DateTime.Today));
            if (request is null) return false;
            if (request.AllSheets)
            {
                var folder = BlankSheetFolderPath is { } chooseFolder ? chooseFolder() : PickFolder();
                if (string.IsNullOrWhiteSpace(folder)) return false;
                var written = BlankDailySheet.SaveAll(folder, BlankSheetOptions, request.BusinessDate);
                LastBlankSheetFolder = folder; OpenBlankSheetFolderButton.Visibility = Visibility.Visible;
                ValidationResult.Text = $"{written.Count} blank daily sheets saved in {folder}. Copy each one to its shop PC's daily-inputs folder.";
                return true;
            }
            var name = BlankDailySheet.SuggestedFileName(request);
            var path = BlankSheetSavePath is { } pick ? pick(name) : ExportSaveDialog.Show(this, ExportSaveDialog.ExcelFilter, name);
            if (string.IsNullOrWhiteSpace(path)) return false;
            BlankDailySheet.Save(path, request);
            LastBlankSheetFolder = System.IO.Path.GetDirectoryName(path); OpenBlankSheetFolderButton.Visibility = Visibility.Visible;
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

    private void OpenBlankSheetFolder_Click(object sender, RoutedEventArgs e) => OpenBlankSheetFolder();

    private void GetBlankSheet_Click(object sender, RoutedEventArgs e) => GetBlankDailySheet();
}
