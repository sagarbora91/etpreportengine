using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Etp.Reporting.Desktop.Modules.Reports;

/// <summary>
/// RA-EXPORT-02 (1.9.8): one place for the export folder and the report Save dialog. The Save dialog starts in the
/// same Documents\ETP Reporting Engine\Exports folder that "Open export folder" opens, and is owned by the main
/// window, so exports land where the user looks for them.
/// </summary>
internal static class ExportSaveDialog
{
    public const string ExcelFilter = "Excel workbook (*.xlsx)|*.xlsx";
    public const string PdfFilter = "PDF report (*.pdf)|*.pdf";
    public const string ZipFilter = "ZIP report package (*.zip)|*.zip";

    /// <summary>The export folder under the given Documents folder (the user's Documents by default).</summary>
    public static string ExportFolder(string? documentsFolder = null) =>
        Path.Combine(documentsFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ETP Reporting Engine", "Exports");

    /// <summary>Creates the export folder when it is missing and returns it.</summary>
    public static string EnsureExportFolder(string? documentsFolder = null)
    {
        var folder = ExportFolder(documentsFolder);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// The Save dialog for an export: proposed name, filter, and the export folder as its starting folder. If the
    /// folder cannot be created (Documents unavailable) the dialog still opens, in the Windows default folder.
    /// </summary>
    public static SaveFileDialog Create(string filter, string fileName, string? documentsFolder = null)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = fileName, AddExtension = true };
        try { dialog.InitialDirectory = EnsureExportFolder(documentsFolder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { }
        return dialog;
    }

    /// <summary>The dialog owner: the view's window, else the application's main window (the hidden report view is not in a window).</summary>
    public static Window? Owner(DependencyObject? view) =>
        (view is null ? null : Window.GetWindow(view)) ?? Application.Current?.MainWindow;

    /// <summary>Shows the export Save dialog owned by the main window; the chosen path, or null when dismissed.</summary>
    public static string? Show(DependencyObject? view, string filter, string fileName)
    {
        var dialog = Create(filter, fileName);
        return ShowDialog(dialog, view) ? dialog.FileName : null;
    }

    /// <summary>Shows a Save dialog owned by <see cref="Owner"/>; true when the user chose a file.</summary>
    public static bool ShowDialog(SaveFileDialog dialog, DependencyObject? view)
    {
        var owner = Owner(view);
        return (owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true;
    }
}
