using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>RA-EXPORT-02: the export Save dialog starts in the folder "Open export folder" opens, owned by the main window.</summary>
public sealed class ExportSaveDialogTests : IDisposable
{
    private readonly string documents = Path.Combine(Path.GetTempPath(), "etp-export-dialog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(documents)) Directory.Delete(documents, recursive: true);
    }

    [Fact]
    public void Export_folder_is_documents_etp_reporting_engine_exports()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ETP Reporting Engine", "Exports");
        Assert.Equal(expected, ExportSaveDialog.ExportFolder());
        Assert.Equal(Path.Combine(documents, "ETP Reporting Engine", "Exports"), ExportSaveDialog.ExportFolder(documents));
    }

    [Fact]
    public void Ensure_creates_the_missing_export_folder_and_is_repeatable()
    {
        var folder = ExportSaveDialog.ExportFolder(documents);
        Assert.False(Directory.Exists(folder));
        Assert.Equal(folder, ExportSaveDialog.EnsureExportFolder(documents));
        Assert.True(Directory.Exists(folder));
        Assert.Equal(folder, ExportSaveDialog.EnsureExportFolder(documents));
    }

    [Theory]
    [InlineData(false, "Daily_Sales_20261001_20261009.xlsx")]
    [InlineData(true, "Daily_Sales_20261001_20261009.pdf")]
    public void Dialog_starts_in_the_export_folder_with_the_proposed_name(bool pdf, string fileName)
    {
        RunSta(() =>
        {
            var filter = pdf ? ExportSaveDialog.PdfFilter : ExportSaveDialog.ExcelFilter;
            var dialog = ExportSaveDialog.Create(filter, fileName, documents);
            Assert.Equal(ExportSaveDialog.ExportFolder(documents), dialog.InitialDirectory);
            Assert.True(Directory.Exists(dialog.InitialDirectory));
            Assert.Equal(filter, dialog.Filter);
            Assert.Equal(fileName, dialog.FileName);
            Assert.True(dialog.AddExtension);
        });
    }

    [Fact]
    public void Dialog_still_opens_when_the_export_folder_cannot_be_created()
    {
        Directory.CreateDirectory(documents);
        var blocker = Path.Combine(documents, "not-a-folder");
        File.WriteAllText(blocker, "file in place of the Documents folder");
        RunSta(() =>
        {
            var dialog = ExportSaveDialog.Create(ExportSaveDialog.ExcelFilter, "Report.xlsx", blocker);
            Assert.Equal("", dialog.InitialDirectory);
            Assert.Equal("Report.xlsx", dialog.FileName);
        });
    }

    [Fact]
    public void Owner_is_the_window_holding_the_view()
    {
        RunSta(() =>
        {
            var view = new Border();
            var window = new Window { Content = new Grid { Children = { view } } };
            Assert.Same(window, ExportSaveDialog.Owner(view));
            window.Close();
        });
    }

    [Fact]
    public void Owner_of_a_view_outside_any_window_falls_back_to_the_main_window()
    {
        // The hidden report view is never in a window; with no running Application there is no main window either.
        RunSta(() =>
        {
            Assert.Null(Application.Current);
            Assert.Null(ExportSaveDialog.Owner(new Border()));
            Assert.Null(ExportSaveDialog.Owner(null));
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
