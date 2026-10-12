using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.IO.Compression;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class OpenXmlWorkbookReaderAsyncTests
{
    [Theory]
    [InlineData("20260825")]
    [InlineData("46259")]
    [InlineData("25/08/2026")]
    public async Task Date_styled_numeric_etp_serial_and_text_dates_have_the_same_business_date(string raw)
    {
        var path = CreateWorkbook();
        try
        {
            using (var document = SpreadsheetDocument.Open(path, true))
            {
                var workbook = document.WorkbookPart!;
                var styles = workbook.AddNewPart<WorkbookStylesPart>();
                styles.Stylesheet = new Stylesheet(new CellFormats(new CellFormat { NumberFormatId = 14 }));
                var worksheet = workbook.WorksheetParts.Single();
                var cell = worksheet.Worksheet!.Descendants<Cell>().Single(c => c.CellReference == "B2");
                cell.CellValue = new CellValue(raw); cell.StyleIndex = 0;
                cell.DataType = raw.Contains('/') ? CellValues.String : CellValues.Number;
                worksheet.Worksheet!.Save(); styles.Stylesheet!.Save();
            }
            var snapshot = await new OpenXmlWorkbookReader().ReadAsync(path);
            var converted = new Etp.Reporting.Import.Conversion.TypedCellConverter().Convert(
                snapshot.Sheets.Single().Rows.Single().Cells[1].Value,
                Etp.Reporting.Domain.Imports.CanonicalDataType.Date, true);
            Assert.True(converted.IsSuccess);
            Assert.Equal(new DateOnly(2026, 8, 25), converted.Value);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Read_does_not_capture_the_callers_synchronization_context()
    {
        var path = CreateWorkbook();
        var context = new RecordingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        Task<WorkbookSnapshot> read;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            read = new OpenXmlWorkbookReader().ReadAsync(path);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        try
        {
            var snapshot = await read;
            Assert.Single(snapshot.Sheets);
            Assert.Equal(0, context.PostCount);
            Assert.Equal(0, context.SendCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Read_propagates_preexisting_cancellation()
    {
        var path = CreateWorkbook();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new OpenXmlWorkbookReader().ReadAsync(path, cancellation.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Async_materialization_preserves_snapshot_hash_size_sheet_rows_and_typed_cells()
    {
        var path = CreateWorkbook();
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(bytes));

            var snapshot = await new OpenXmlWorkbookReader().ReadAsync(path);

            Assert.Equal(Path.GetFileName(path), snapshot.FileName);
            Assert.Equal(bytes.LongLength, snapshot.FileSizeBytes);
            Assert.Equal(expectedHash, snapshot.Sha256);
            var sheet = Assert.Single(snapshot.Sheets);
            Assert.Equal("Sheet0", sheet.Name);
            Assert.Equal(1, sheet.HeaderRowNumber);
            Assert.Equal(["ITEM", "AMOUNT", "ENABLED"], sheet.Headers);
            var row = Assert.Single(sheet.Rows);
            Assert.Equal(2, row.RowNumber);
            Assert.Equal("SKU-1", row.Cells[0].Value);
            Assert.Equal(12.5m, row.Cells[1].Value);
            Assert.Equal(true, row.Cells[2].Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Workbook_whose_zip_has_an_unsafe_compression_ratio_is_refused_before_parsing()
    {
        // 1.11.2 L4 (S-10): an xlsx is a zip; a part that expands by far more than the policy ratio is a bomb.
        var path = CreateWorkbook();
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            using (var stream = zip.CreateEntry("xl/padding.bin", CompressionLevel.SmallestSize).Open())
                stream.Write(new byte[8 * 1024 * 1024]);
            var ex = await Assert.ThrowsAsync<ImportSourceException>(() => new OpenXmlWorkbookReader().ReadAsync(path));
            Assert.Equal("IMPORT_ARCHIVE_RATIO", ex.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Workbook_with_an_oversized_part_is_refused_before_parsing()
    {
        var path = CreateWorkbook();
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            using (var stream = zip.CreateEntry("xl/big.bin", CompressionLevel.NoCompression).Open())
            {
                var chunk = new byte[1024 * 1024];
                new Random(7).NextBytes(chunk);
                for (var i = 0; i < 101; i++) stream.Write(chunk); // above the 100 MB per-entry limit
            }
            var ex = await Assert.ThrowsAsync<ImportSourceException>(() => new OpenXmlWorkbookReader().ReadAsync(path));
            Assert.Equal("IMPORT_ARCHIVE_ENTRY_SIZE", ex.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Shared_strings_resolve_by_index_including_rich_text_and_out_of_range()
    {
        var path = CreateWorkbook();
        try
        {
            using (var document = SpreadsheetDocument.Open(path, true))
            {
                var workbook = document.WorkbookPart!;
                var table = workbook.AddNewPart<SharedStringTablePart>();
                table.SharedStringTable = new SharedStringTable(
                    new SharedStringItem(new Text("plain")),
                    new SharedStringItem(new Run(new Text("rich ")), new Run(new Text("text"))),
                    new SharedStringItem(new Text("third")));
                table.SharedStringTable!.Save();
                var worksheet = workbook.WorksheetParts.Single();
                var row = worksheet.Worksheet!.Descendants<Row>().Single(r => r.RowIndex?.Value == 2);
                row.RemoveAllChildren<Cell>();
                row.Append(Shared("A2", 1), Shared("B2", 2), Shared("C2", 9));
                worksheet.Worksheet!.Save();
            }
            var snapshot = await new OpenXmlWorkbookReader().ReadAsync(path);
            var cells = Assert.Single(snapshot.Sheets.Single().Rows).Cells;
            Assert.Equal("rich text", cells[0].Value);
            Assert.Equal("third", cells[1].Value);
            Assert.Null(cells[2].Value);
        }
        finally { File.Delete(path); }
    }

    // Gate: the sample exports are tiny, so this proves the default limits do not reject the known-good set;
    // the integrator also runs it against a real sparse daily export copy when one is at hand.
    [Fact]
    public async Task Every_etp_sample_export_passes_the_default_archive_policy_and_reads()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample");
        var files = Directory.GetFiles(folder, "*.xlsx");
        Assert.NotEmpty(files);
        var policy = new ImportPathPolicy();
        foreach (var file in files)
        {
            using (var zip = ZipFile.OpenRead(file)) policy.ValidateArchive(zip);
            var snapshot = await new OpenXmlWorkbookReader().ReadAsync(file);
            Assert.NotEmpty(snapshot.Sheets);
        }
    }

    private static Cell Shared(string reference, int index) => new()
    {
        CellReference = reference,
        DataType = CellValues.SharedString,
        CellValue = new CellValue(index.ToString())
    };

    private static string CreateWorkbook()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etp-reader-{Guid.NewGuid():N}.xlsx");
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var worksheet = workbook.AddNewPart<WorksheetPart>();
        worksheet.Worksheet = new Worksheet(new SheetData(
            new Row(
                Inline("A1", "ITEM"),
                Inline("B1", "AMOUNT"),
                Inline("C1", "ENABLED")) { RowIndex = 1 },
            new Row(
                Inline("A2", "SKU-1"),
                new Cell { CellReference = "B2", DataType = CellValues.Number, CellValue = new CellValue("12.5") },
                new Cell { CellReference = "C2", DataType = CellValues.Boolean, CellValue = new CellValue("1") }) { RowIndex = 2 }));
        workbook.Workbook.AppendChild(new Sheets(new Sheet
        {
            Id = workbook.GetIdOfPart(worksheet),
            SheetId = 1,
            Name = "Sheet0"
        }));
        workbook.Workbook.Save();
        return path;
    }

    private static Cell Inline(string reference, string value) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value))
    };

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int postCount;
        private int sendCount;

        public int PostCount => Volatile.Read(ref postCount);
        public int SendCount => Volatile.Read(ref sendCount);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref postCount);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        public override void Send(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref sendCount);
            callback(state);
        }
    }
}
