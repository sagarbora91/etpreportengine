using System.Windows.Controls;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.8, lane EXPORT-TESTS (RA-EXPORT-01/19). The dialog export path that the focused buttons, Ctrl+E / Ctrl+P and the
/// Actions menu use: a failure before the staged write reaches the status line and the diagnostics log instead of
/// dying as an unobserved task; a dismissed dialog exports nothing; a report that changed while the dialog was open is
/// not exported; a failed write leaves an existing destination untouched; the Excel headers are the 1.9.7 grid headers.
/// Files go under the test output folder, never the user's Documents.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ReportExportPathTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dialog_failure_reaches_the_status_line_and_the_diagnostics_log(bool pdf)
    {
        RunSta(async () =>
        {
            var exporter = new CountingExporter();
            var statuses = new List<string>();
            var view = SyntheticReportView.Create(out _, (_, _, status) => statuses.Add(status), exporter: exporter);
            await view.RunReportAsync("sales-brand");
            view.saveFileChooser = (_, _) => throw new InvalidOperationException("Synthetic dialog failure");

            await view.ExportObservedAsync(pdf);

            var expected = pdf ? "PDF export failed" : "Excel export failed";
            Assert.StartsWith(expected, Status(view), StringComparison.Ordinal);
            Assert.Equal(Status(view), statuses[^1]);
            Assert.Contains(pdf ? "REPORT_PDF_EXPORT_FAILED" : "REPORT_EXCEL_EXPORT_FAILED", DiagnosticsLog());
            Assert.Equal(0, exporter.Calls);
            Assert.False(view.IsExportInProgress);
            Assert.Equal("sales-brand", view.CurrentReportCode);
            Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            Assert.True(((Button)view.FindName("ExportPdfButton")).IsEnabled);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dismissed_dialog_exports_nothing_and_keeps_the_status(bool pdf)
    {
        RunSta(async () =>
        {
            var exporter = new CountingExporter();
            var view = SyntheticReportView.Create(out var latest, exporter: exporter);
            await view.RunReportAsync("sales-brand");
            var before = Status(view);
            string? proposed = null;
            view.saveFileChooser = (isPdf, fileName) => { Assert.Equal(pdf, isPdf); proposed = fileName; return null; };

            await view.ExportObservedAsync(pdf);

            Assert.Equal(ReportsWorkspaceView.ProposedFileName(latest().ExportMetadata!, pdf), proposed);
            Assert.Equal(0, exporter.Calls);
            Assert.Equal(before, Status(view));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Chosen_path_is_written_through_the_coordinator(bool pdf)
    {
        RunSta(async () =>
        {
            var folder = OutputFolder();
            try
            {
                var exporter = new CountingExporter();
                var view = SyntheticReportView.Create(out var latest, exporter: exporter);
                await view.RunReportAsync("sales-brand");
                var path = Path.Combine(folder, ReportsWorkspaceView.ProposedFileName(latest().ExportMetadata!, pdf));
                view.saveFileChooser = (_, fileName) => Path.Combine(folder, fileName);

                await view.ExportObservedAsync(pdf);

                Assert.Equal(1, exporter.Calls);
                Assert.Equal("Synthetic export", File.ReadAllText(path));
                Assert.Equal($"{(pdf ? "PDF" : "Excel")} report saved to {path}", Status(view));
                Assert.Empty(Directory.GetFiles(folder, ".etp-*"));
            }
            finally { Directory.Delete(folder, recursive: true); }
        });
    }

    [Fact]
    public void A_report_that_changes_while_the_dialog_is_open_is_not_exported()
    {
        RunSta(async () =>
        {
            var exporter = new CountingExporter();
            var view = SyntheticReportView.Create(out _, exporter: exporter);
            await view.RunReportAsync("sales-brand");
            view.saveFileChooser = (_, fileName) => { view.SetBusinessDate(new DateTime(2026, 8, 24)); return Path.Combine(AppContext.BaseDirectory, "export-path-tests", fileName); };

            await view.ExportObservedAsync(pdf: false);

            Assert.Equal(0, exporter.Calls);
            Assert.Contains("Filters changed", Status(view), StringComparison.Ordinal);
            Assert.False(((Button)view.FindName("ExportExcelButton")).IsEnabled);
        });
    }

    [Fact]
    public void Export_is_disabled_without_rows_and_enabled_with_rows()
    {
        RunSta(async () =>
        {
            var exporter = new CountingExporter();
            var view = SyntheticReportView.Create(out var latest, empty: true, exporter: exporter);
            view.saveFileChooser = (_, _) => throw new InvalidOperationException("The dialog must not open for a report without rows.");
            await view.RunReportAsync("sales-brand");
            Assert.False(latest().CanExportReport);
            Assert.False(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            Assert.False(((Button)view.FindName("ExportPdfButton")).IsEnabled);
            await view.ExportObservedAsync(pdf: false);
            await view.ExportObservedAsync(pdf: true);
            Assert.Equal(0, exporter.Calls);
            Assert.DoesNotContain("export failed", Status(view), StringComparison.Ordinal);

            var populated = SyntheticReportView.Create(out latest, exporter: exporter);
            await populated.RunReportAsync("sales-brand");
            Assert.True(latest().CanExportReport);
            Assert.True(((Button)populated.FindName("ExportExcelButton")).IsEnabled);
            Assert.True(((Button)populated.FindName("ExportPdfButton")).IsEnabled);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_write_leaves_an_existing_destination_untouched_and_reports_the_error(bool pdf)
    {
        RunSta(async () =>
        {
            var folder = OutputFolder();
            try
            {
                var view = SyntheticReportView.Create(out _, exporter: new InlineExporter(fail: true));
                await view.RunReportAsync("sales-brand");
                var destination = Path.Combine(folder, pdf ? "existing.pdf" : "existing.xlsx");
                File.WriteAllText(destination, "original");
                view.saveFileChooser = (_, _) => destination;

                await view.ExportObservedAsync(pdf);

                Assert.Equal("original", File.ReadAllText(destination));
                Assert.Empty(Directory.GetFiles(folder, ".etp-*"));
                Assert.StartsWith(pdf ? "PDF export failed" : "Excel export failed", Status(view), StringComparison.Ordinal);
                Assert.Contains(pdf ? "REPORT_PDF_EXPORT_FAILED" : "REPORT_EXCEL_EXPORT_FAILED", DiagnosticsLog());
                Assert.False(view.IsExportInProgress);
                Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            }
            finally { Directory.Delete(folder, recursive: true); }
        });
    }

    [Fact]
    public async Task Staging_discards_the_temporary_file_and_keeps_the_destination_on_failure_or_cancellation()
    {
        var folder = OutputFolder();
        try
        {
            var destination = Path.Combine(folder, "report.xlsx");
            File.WriteAllText(destination, "original");

            await Assert.ThrowsAsync<IOException>(() => ExportStaging.WriteAsync(destination, temporary =>
            {
                Assert.Equal(folder, Path.GetDirectoryName(temporary));
                Assert.EndsWith(".xlsx", temporary, StringComparison.Ordinal);
                File.WriteAllText(temporary, "partial");
                throw new IOException("Synthetic render failure");
            }, CancellationToken.None));
            Assert.Equal("original", File.ReadAllText(destination));
            Assert.Empty(Directory.GetFiles(folder, ".etp-*"));

            using var cancellation = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExportStaging.WriteAsync(destination, temporary =>
            {
                File.WriteAllText(temporary, "rendered");
                cancellation.Cancel();
                return Task.CompletedTask;
            }, cancellation.Token));
            Assert.Equal("original", File.ReadAllText(destination));
            Assert.Empty(Directory.GetFiles(folder, ".etp-*"));

            await ExportStaging.WriteAsync(destination, temporary => { File.WriteAllText(temporary, "rendered"); return Task.CompletedTask; }, CancellationToken.None);
            Assert.Equal("rendered", File.ReadAllText(destination));
            Assert.Empty(Directory.GetFiles(folder, ".etp-*"));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Coordinator_surfaces_exporter_failures_and_rejects_blank_paths()
    {
        var coordinator = new ReportExportCoordinator(
            (_, _) => throw new NotSupportedException(), (_, _) => throw new NotSupportedException(),
            (_, _, _) => throw new IOException("tabular excel"), (_, _) => throw new IOException("visual excel"),
            (_, _, _) => throw new IOException("tabular pdf"), (_, _) => throw new IOException("visual pdf"),
            (_, _) => throw new IOException("dsr pdf"));
        var metadata = new ExcelReportMetadata("Synthetic", new(2026, 8, 25), new(2026, 8, 25), "Passed", "test", "Synthetic", DateTimeOffset.UtcNow);
        var data = new ExcelReportData([new("Store")], [["WLMHW"]]);
        var visual = VisualReportComposer.Compose(metadata, data);
        var dsr = DailySalesReportBuilder.Build(new(2026, 8, 25), [], [], new Dictionary<string, decimal?>());

        Assert.Equal("tabular excel", (await Assert.ThrowsAsync<IOException>(() => coordinator.ExportReportExcelAsync("a.xlsx", metadata, data, null))).Message);
        Assert.Equal("visual excel", (await Assert.ThrowsAsync<IOException>(() => coordinator.ExportReportExcelAsync("a.xlsx", metadata, data, visual))).Message);
        Assert.Equal("tabular pdf", (await Assert.ThrowsAsync<IOException>(() => coordinator.ExportReportPdfAsync("a.pdf", metadata, data, null, null))).Message);
        Assert.Equal("visual pdf", (await Assert.ThrowsAsync<IOException>(() => coordinator.ExportReportPdfAsync("a.pdf", metadata, data, visual, null))).Message);
        Assert.Equal("dsr pdf", (await Assert.ThrowsAsync<IOException>(() => coordinator.ExportReportPdfAsync("a.pdf", metadata, data, visual, dsr))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.ExportReportExcelAsync(" ", metadata, data, null));
        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.ExportReportPdfAsync("", metadata, data, null, null));
        await Assert.ThrowsAsync<ArgumentNullException>(() => coordinator.ExportReportExcelAsync("a.xlsx", metadata, null!, null));
    }

    [Theory]
    [InlineData("sales-brand")]
    [InlineData("invoice")]
    [InlineData("invoice-lineage")]
    [InlineData("staff")]
    [InlineData("service")]
    [InlineData("tender")]
    [InlineData("tender-diagnostic")]
    [InlineData("stock-variance")]
    [InlineData("stock-movement")]
    [InlineData("stock-physical")]
    [InlineData("stock-closing")]
    [InlineData("stock-brand")]
    [InlineData("exceptions")]
    [InlineData("management-trend")]
    public void Excel_export_writes_every_row_under_the_grid_headers(string code)
    {
        RunSta(async () =>
        {
            var folder = OutputFolder();
            try
            {
                var view = SyntheticReportView.Create(out var latest, exporter: new InlineExporter());
                await view.RunReportAsync(code);
                var data = latest().ExportData!;
                var path = Path.Combine(folder, ReportsWorkspaceView.ProposedFileName(latest().ExportMetadata!, pdf: false));
                view.saveFileChooser = (_, fileName) => Path.Combine(folder, fileName);

                await view.ExportObservedAsync(pdf: false);

                Assert.Equal($"Excel report saved to {path}", Status(view));
                using var workbook = SpreadsheetDocument.Open(path, false);
                var rows = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Row>().ToDictionary(row => row.RowIndex!.Value);
                var headers = rows[8].Elements<Cell>().Select(cell => cell.InnerText).ToArray();
                Assert.Equal(data.Columns.Select(column => column.Header), headers);
                Assert.DoesNotContain(headers, header => header.Contains('₹'));
                Assert.Equal(data.Rows.Count + (data.Totals is null ? 0 : 1), rows.Keys.Count(index => index > 8));
                for (var i = 0; i < data.Rows.Count; i++) Assert.Equal(data.Columns.Count, rows[(uint)(9 + i)].Elements<Cell>().Count());

                // The 1.9.7 grid map (RA-EXPORT-06/07): the export headers are the mapped headers in order; ₹ is on screen only.
                var rowType = ((DataGrid)view.FindName("ReportGrid")).ItemsSource!.Cast<object>().First().GetType();
                if (ReportGridColumns.For(rowType) is { } mapped)
                {
                    var mappedHeaders = mapped.Select(column => column.Header).ToArray();
                    Assert.True(IsSubsequence(headers, mappedHeaders), $"{code}: export [{string.Join(" | ", headers)}] is not the grid map [{string.Join(" | ", mappedHeaders)}] in order");
                    var grid = (DataGrid)view.FindName("ReportGrid");
                    TablePresentation.Configure(grid);
                    foreach (var column in mapped.Where(column => column.Money && headers.Contains(column.Header)))
                        Assert.Contains(grid.Columns, gridColumn => (string)gridColumn.Header == column.Header + " ₹");
                }
            }
            finally { Directory.Delete(folder, recursive: true); }
        });
    }

    private static bool IsSubsequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var position = 0;
        foreach (var header in expected)
        {
            while (position < actual.Count && actual[position] != header) position++;
            if (position++ >= actual.Count) return false;
        }
        return true;
    }

    private static string Status(ReportsWorkspaceView view) => ((TextBlock)view.FindName("ReportResult")).Text;

    private static string DiagnosticsLog() =>
        string.Concat(Directory.GetFiles(DiagnosticsIsolation.LogDirectory, "diagnostics-*.jsonl").Select(File.ReadAllText));

    private static string OutputFolder() =>
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "export-path-tests", Guid.NewGuid().ToString("N"))).FullName;

    private sealed class CountingExporter : IReportExportCoordinator
    {
        public int Calls { get; private set; }
        private Task WriteAsync(string path) { Calls++; File.WriteAllText(path, "Synthetic export"); return Task.CompletedTask; }
        public Task ExportReportExcelAsync(string path, ExcelReportMetadata metadata, ExcelReportData data, VisualReportModel? visual, CancellationToken token = default) => WriteAsync(path);
        public Task ExportReportPdfAsync(string path, ExcelReportMetadata metadata, ExcelReportData data, VisualReportModel? visual, DailySalesReportDocument? dsr, CancellationToken token = default) => WriteAsync(path);
        public Task ExportPackExcelAsync(string path, ReportPackDocument document, CancellationToken token = default) => throw new NotSupportedException();
        public Task ExportPackPdfAsync(string path, ReportPackDocument document, CancellationToken token = default) => throw new NotSupportedException();
        public Task ExportManagementSummaryPdfAsync(string path, ExcelReportMetadata metadata, ExcelReportData data, CancellationToken token = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// The production exporters on the production route, run inline. The real coordinator moves them to a worker
    /// thread; in the app the dispatcher brings the continuation back to the UI thread, but a test STA thread has no
    /// dispatcher loop, so the view would be touched from the worker. Faults surface as a faulted task, as in production.
    /// </summary>
    private sealed class InlineExporter(bool fail = false) : IReportExportCoordinator
    {
        private Task Run(Action export)
        {
            if (fail) return Task.FromException(new IOException("Synthetic disk failure"));
            try { export(); return Task.CompletedTask; }
            catch (Exception ex) { return Task.FromException(ex); }
        }
        public Task ExportReportExcelAsync(string path, ExcelReportMetadata metadata, ExcelReportData data, VisualReportModel? visual, CancellationToken token = default) =>
            Run(() => { if (ReportExportCoordinator.SelectExcelRoute(visual) == ReportExcelExportRoute.Visual) new OpenXmlVisualReportExporter().Export(path, visual!); else new OpenXmlReportExporter().Export(path, metadata, data); });
        public Task ExportReportPdfAsync(string path, ExcelReportMetadata metadata, ExcelReportData data, VisualReportModel? visual, DailySalesReportDocument? dsr, CancellationToken token = default) =>
            Run(() => { if (dsr is not null) new DailySalesReportPdfExporter().Export(path, dsr); else if (visual is not null) new SimplePdfVisualReportExporter().Export(path, visual); else new SimplePdfReportExporter().Export(path, metadata, data); });
        public Task ExportPackExcelAsync(string path, ReportPackDocument document, CancellationToken token = default) => throw new NotSupportedException();
        public Task ExportPackPdfAsync(string path, ReportPackDocument document, CancellationToken token = default) => throw new NotSupportedException();
        public Task ExportManagementSummaryPdfAsync(string path, ExcelReportMetadata metadata, ExcelReportData data, CancellationToken token = default) => throw new NotSupportedException();
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action().GetAwaiter().GetResult(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Report export path test failed", failure);
    }
}
