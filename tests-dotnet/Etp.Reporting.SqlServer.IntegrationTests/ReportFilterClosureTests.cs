using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class ReportFilterClosureTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task All_roles_can_filter_real_sales_and_export_the_applied_scope_without_changing_facts()
    {
        await database.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'sanitised-filters.xlsx',REPLICATE('c',64),1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            VALUES(@file,'Sales',1,'sale'),(@file,'Sales',2,'sale'),(@file,'Sales',3,'sale'),(@file,'Sales',4,'sale');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date)
            VALUES('SCOPE-A','I1',2027,'20260825'),('SCOPE-A','I2',2027,'20260825'),('SCOPE-A','B3',2027,'20260825'),('SCOPE-B','I4',2027,'20260825');
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_brand_name,brand_segment,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_tax_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,'1',v.item,v.brand,v.segment,v.kind,v.qty,v.amount,v.amount/1.18,v.amount-v.amount/1.18,'INR',s.source_lineage_id
            FROM (VALUES('I1','ITEM1','BRAND-A','SEG-X','INV',1,118.00,1),('I2','ITEM2','BRAND-B','SEG-Y','INV',2,236.00,2),('B3','ITEM1','BRAND-A','SEG-X','BC',-1,-59.00,3),('I4','ITEM3','BRAND-C','SEG-X','INV',4,472.00,4)) v(doc,item,brand,segment,kind,qty,amount,rowno)
            JOIN dbo.sales_invoices i ON i.document_number=v.doc
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.source_row_number=v.rowno;
            CREATE USER closure_filter_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER closure_filter_viewer;
            CREATE USER closure_filter_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER closure_filter_manager;
            CREATE USER closure_filter_owner WITHOUT LOGIN; ALTER ROLE etp_owner ADD MEMBER closure_filter_owner;
            ALTER ROLE db_owner ADD MEMBER closure_filter_owner; -- Mirrors configure_application_role for Owner.
            """);
        var all = Convert.ToDecimal(await database.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines"));
        var segment = Convert.ToDecimal(await database.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines WHERE brand_segment='SEG-X'"));
        Assert.Equal(767m, all);
        Assert.Equal(531m, segment);
        foreach (var (access, user) in new[] { (ShellAccess.Viewer, "closure_filter_viewer"), (ShellAccess.StoreManager, "closure_filter_manager"), (ShellAccess.Owner, "closure_filter_owner") })
        {
            // The actual SQL filter query remains readable under each database role.
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"EXECUTE AS USER='{user}'; " + SqlReportingQueries.Sales + " REVERT;", connection);
            command.Parameters.AddWithValue("@dateFrom", new DateOnly(2026, 8, 25));
            command.Parameters.AddWithValue("@dateTo", new DateOnly(2026, 8, 25));
            command.Parameters.AddWithValue("@storesJson", DBNull.Value);
            command.Parameters.AddWithValue("@segmentsJson", "[\"SEG-X\"]");
            command.Parameters.AddWithValue("@typesJson", DBNull.Value);
            command.Parameters.AddWithValue("@itemsJson", DBNull.Value);
            decimal actual = 0;
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) actual += reader.GetDecimal(9);
            Assert.Equal(segment, actual);
            if (!access.CanAdminister)
            {
                var denied = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"EXECUTE AS USER='{user}'; BEGIN TRY UPDATE dbo.sales_lines SET source_gross_amount=0; END TRY BEGIN CATCH REVERT; THROW; END CATCH; REVERT;"));
                Assert.Equal(229, denied.Number);
            }
            RunSta(() => VerifyScreenAndExports(access, all, segment));
        }
        Assert.Equal(all, Convert.ToDecimal(await database.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines")));
    }

    private void VerifyScreenAndExports(ShellAccess access, decimal all, decimal segment)
    {
        var folder = Directory.CreateTempSubdirectory("EtpReportFilter-").FullName;
        try
        {
            var query = new SqlServerApplicationReportQuery(database.ConnectionString);
            var initialQuery = query.RunSalesSummaryAsync(new(new(2026, 8, 25), new(2026, 8, 25)), Etp.Reporting.Application.Reports.ReportSalesDimension.Brand);
            Await(initialQuery);
            Assert.Equal(all, initialQuery.Result.Rows.Sum(row => row.SourceSignedNetAmount));
            var view = new ReportsWorkspaceView(() => database.ConnectionString, _ => query, _ => query, _ => query, new ReportExportCoordinator(), new ReportingTenderVarianceDiagnostic());
            var session = new ReportWorkspaceSession();
            ReportWorkspaceControl? workspace = null;
            ReportPresentationSnapshot? snapshot = null;
            view.ApplyScope(new(2026, 8, 25), new(2026, 8, 25), "Both stores");
            view.AttachHost(code =>
            {
                workspace = (ReportWorkspaceControl)session.Activate(code, view.DateFrom, view.DateTo, view.DateTo!.Value, (_, _) => { }, (_, _) => { });
                view.AttachQueryFilters(workspace);
                return true;
            }, (_, _, _) => Task.CompletedTask, (value, rows, status) => { snapshot = value; session.UpdatePreview(value, rows, status); }, _ => { }, _ => { });
            Task? running = null;
            var list = new ReportListView(access, destination => running = view.RunReportAsync(destination.ReportCode!));
            var route = TaskNavigation.Find("report-sales-brand")!;
            Find<Button>(list, item => item.Content?.ToString() == route.Title).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(running);
            Await(running);
            Assert.Equal(all, Total());
            var filters = Find<Expander>(workspace!, item => Equals(item.Header, "Filters"));
            Assert.Equal(Visibility.Visible, filters.Visibility);
            filters.IsExpanded = true;
            var segmentInput = (TextBox)view.FindName("BrandSegmentFilterInput");
            Assert.Contains(segmentInput, Children(workspace!));
            segmentInput.Text = "SEG-X";
            Assert.False(workspace!.HasCurrentPreview);
            Assert.False(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            ClickAndWait("ApplyQueryFiltersButton");
            Assert.Equal(segment, Total());
            Assert.Equal(segment, RowsTotal());
            Assert.Contains("Sales incl. GST 531.00", Find<TextBlock>(workspace!, item => item.Name == "ReportTaskStatus").Text);
            Assert.True(workspace.HasCurrentPreview);
            var scopeLine = Find<TextBlock>(workspace, item => AutomationProperties.GetName(item) == "Applied report scope").Text;
            Assert.Contains("Brand segments: SEG-X", scopeLine);
            // Visible-list search is separate from the query, metadata and export.
            var detailFilter = Find<ReportDetailFilter>(workspace, _ => true);
            detailFilter.Search.Text = "no matching detail";
            Assert.Empty(Find<DataGrid>(workspace, _ => true).Items.Cast<object>());
            Assert.Equal(segment, Total());
            var excel = Path.Combine(folder, "filtered.xlsx");
            var pdf = Path.Combine(folder, "filtered.pdf");
            Await(view.ExportReportToPathAsync(excel, false));
            Await(view.ExportReportToPathAsync(pdf, true));
            using (var book = SpreadsheetDocument.Open(excel, false))
            {
                var rows = book.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Row>().ToArray();
                Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(book));
                Assert.Contains(rows.SelectMany(row => row.Elements<Cell>()), cell => cell.InnerText == scopeLine);
                Assert.Equal(segment, decimal.Parse(rows.Last().Elements<Cell>().ElementAt(2).CellValue!.Text, CultureInfo.InvariantCulture));
                Assert.DoesNotContain(rows.SelectMany(row => row.Elements<Cell>()), cell => cell.InnerText == "BRAND-B");
            }
            var pdfText = PdfText.Read(pdf);
            Assert.Contains("Applied scope:", pdfText);
            Assert.Contains("Brand segments: SEG-X", pdfText);
            Assert.Contains("531.00", pdfText);
            Assert.DoesNotContain("BRAND-B", pdfText);
            ((TextBox)view.FindName("StoreFilterInput")).Text = "SCOPE-A";
            ClickAndWait("ApplyQueryFiltersButton"); Assert.Equal(59m, Total());
            ((TextBox)view.FindName("TransactionTypeFilterInput")).Text = "INV";
            ClickAndWait("ApplyQueryFiltersButton"); Assert.Equal(118m, Total());
            ((TextBox)view.FindName("ItemFilterInput")).Text = "ITEM2";
            ClickAndWait("ApplyQueryFiltersButton"); Assert.Equal(0m, Total());
            ClickAndWait("ClearQueryFiltersButton"); Assert.Equal(all, Total());
            Assert.Contains("Brand segments: All", snapshot!.ExportMetadata!.AppliedScope);
            Await(view.RunReportAsync("sales-item"));
            Await(view.RunReportAsync("sales-brand"));
            Assert.Contains(segmentInput, Children(workspace!));
            Assert.Equal(all, Total());

            decimal Total()
            {
                Assert.True(snapshot?.ExportData?.Totals is not null, ((TextBlock)view.FindName("ReportResult")).Text);
                return Convert.ToDecimal(snapshot!.ExportData!.Totals![2]);
            }
            decimal RowsTotal() => snapshot!.ExportData!.Rows.Sum(row => Convert.ToDecimal(row[2]));
            void ClickAndWait(string name)
            {
                ((Button)view.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => ((Button)view.FindName("ExportExcelButton")).IsEnabled);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    private static T Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject => Children(root).OfType<T>().First(match);
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var descendant in Children(child)) yield return descendant; }
    }
    private static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30), "Report filter operation timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(5);
        }
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { action(); } catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "Report filter test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    // Decode the exported PDF's text operators using its embedded ToUnicode maps;
    // checking metadata or raw file bytes would not verify rendered export content.
    private static class PdfText
    {
        public static string Read(string path)
        {
            using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var output = new StringBuilder();
            foreach (var page in document.Pages)
            {
                var fonts = page.Resources.Elements.GetDictionary("/Font")!;
                var mappings = fonts.Elements.Keys.ToDictionary(key => key, key =>
                {
                    var map = fonts.Elements.GetDictionary(key)!.Elements.GetDictionary("/ToUnicode");
                    // PDFsharp uses WinAnsi text directly when every character fits.
                    if (map is null) return null;
                    var cmap = Encoding.ASCII.GetString(map.Stream.UnfilteredValue);
                    var decoded = new Dictionary<int, char>();
                    foreach (Match range in Regex.Matches(cmap, @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>"))
                    {
                        var first = Convert.ToInt32(range.Groups[1].Value, 16);
                        var last = Convert.ToInt32(range.Groups[2].Value, 16);
                        var unicode = Convert.ToInt32(range.Groups[3].Value, 16);
                        for (var glyph = first; glyph <= last; glyph++) decoded[glyph] = (char)(unicode + glyph - first);
                    }
                    return decoded;
                });
                Dictionary<int, char>? current = null;
                foreach (var operation in ContentReader.ReadContent(page).OfType<COperator>())
                {
                    if (operation.OpCode.Name == "Tf") current = mappings[((CName)operation.Operands[0]).Name];
                    if (operation.OpCode.Name is not ("Tj" or "TJ")) continue;
                    foreach (var text in Strings(operation.Operands))
                    {
                        if (current is null) { output.Append(text.Value); continue; }
                        for (var i = 0; i + 1 < text.Value.Length; i += 2)
                            output.Append(current[(text.Value[i] << 8) | text.Value[i + 1]]);
                    }
                    output.Append(' ');
                }
            }
            return output.ToString();
        }
        private static IEnumerable<CString> Strings(CSequence sequence)
        {
            foreach (var item in sequence)
                if (item is CString text) yield return text;
                else if (item is CSequence nested) foreach (var value in Strings(nested)) yield return value;
        }
    }
}
