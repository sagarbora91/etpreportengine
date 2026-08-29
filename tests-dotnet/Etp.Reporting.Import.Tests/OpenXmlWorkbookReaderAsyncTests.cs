using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class OpenXmlWorkbookReaderAsyncTests
{
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
