using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class ScopedImportDuplicateSqlTests
{
    [Fact]
    public async Task Shared_bytes_keep_source_row_outcomes_separate_and_direct_reimports_return_zero_new_rows()
    {
        var database = new SqlDatabaseFixture();
        var paths = new List<string>();
        try
        {
            await database.InitializeAsync();
            var source = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"fixtures","etp-sample"),"R022_*.xlsx").Single();
            var bytes = await File.ReadAllBytesAsync(source);
            foreach (var report in new[] { "R001", "R022" })
            {
                var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{report}_source.xlsx");
                paths.Add(path);
                await File.WriteAllBytesAsync(path, bytes);
            }
            var persistence = new SqlServerImportPersistenceUseCase(database.ConnectionString);
            var first = await new FolderImportService(persistence).RunFilesAsync(paths,new("Scoped outcome SQL test"));
            Assert.Equal(2,first.Files.Count);
            Assert.All(first.Files,file =>
            {
                Assert.True(file.Status=="Imported",$"{file.FileName}: {file.Status}; {file.Message}");
                Assert.True(file.RowsProcessed>0);
                Assert.Equal(file.RowsProcessed,file.NewRows);
            });
            Assert.Equal(1,await database.ExecuteAsync("SELECT COUNT(DISTINCT source_sha256) FROM dbo.import_files"));
            foreach(var path in paths)
            {
                var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(await new OpenXmlWorkbookReader().ReadAsync(path));
                var repeated = await persistence.PersistAsync(new(accepted,accepted.Scope.PeriodEnd!.Value,accepted.Scope.StoreCode!,"Scoped outcome SQL test"));
                Assert.Equal("Duplicate",repeated.Status);
                Assert.Equal(0,repeated.PersistedRows);
                Assert.Equal(accepted.Staging.Rows.Count,repeated.AlreadyPresentRows);
                var outcome = await persistence.LoadOutcomeInScopeAsync(accepted.Workbook.Sha256,accepted.ProfileIdentity.ReportCode,
                    accepted.Scope.StoreCode!,accepted.Scope.PeriodStart!.Value,accepted.Scope.PeriodEnd.Value);
                Assert.Equal(accepted.Staging.Rows.Count,outcome.NewRows);
            }
            Assert.Equal(2,await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));

            // Several normalized outcomes belong to the same physical source row; conflict takes precedence.
            await database.ExecuteAsync("""
                DECLARE @file bigint=(SELECT import_file_id FROM dbo.import_files WHERE report_code='R022');
                DECLARE @lineage bigint=(SELECT TOP(1) source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file ORDER BY source_row_number,source_lineage_id);
                INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message)
                VALUES(@file,@lineage,'TEST_PRESENT','ALREADY_PRESENT',REPLICATE('a',64),'Test row outcome'),
                      (@file,@lineage,'TEST_CONFLICT','CONFLICT',REPLICATE('b',64),'Test row outcome');
                """);
            foreach(var path in paths)
            {
                var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(await new OpenXmlWorkbookReader().ReadAsync(path));
                var outcome = await persistence.LoadOutcomeInScopeAsync(accepted.Workbook.Sha256,accepted.ProfileIdentity.ReportCode,
                    accepted.Scope.StoreCode!,accepted.Scope.PeriodStart!.Value,accepted.Scope.PeriodEnd!.Value);
                var expectedConflicts = accepted.ProfileIdentity.ReportCode=="R022" ? 1 : 0;
                Assert.Equal(expectedConflicts,outcome.ConflictRows);
                Assert.Equal(accepted.Staging.Rows.Count-expectedConflicts,outcome.NewRows);
                Assert.Equal(0,outcome.AlreadyPresentRows);
            }
        }
        finally
        {
            foreach (var path in paths) File.Delete(path);
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Identical_empty_workbook_bytes_preserve_each_report_store_and_date_and_reimport_as_duplicates()
    {
        var database = new SqlDatabaseFixture();
        var paths = new List<string>();
        try
        {
            await database.InitializeAsync();
            var headers = EtpReportFamilyRegistry.Resolve("R022").Headers;
            Assert.Equal(headers, EtpReportFamilyRegistry.Resolve("R001").Headers);
            var bytes = EmptyWorkbook(headers);
            foreach (var report in new[] { "R001", "R022" })
            foreach (var store in new[] { "HEMW", "WLMHW" })
            foreach (var date in new[] { "20260825", "20260826" })
            {
                var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{report}_{store}_{date}.xlsx");
                paths.Add(path);
                await File.WriteAllBytesAsync(path, bytes);
            }

            var persistence = new SqlServerImportPersistenceUseCase(database.ConnectionString);
            var service = new FolderImportService(persistence);
            var first = await service.RunFilesAsync(paths, new("Scoped duplicate SQL test"));
            Assert.Equal(8, first.Files.Count);
            Assert.All(first.Files, file => Assert.True(file.Status == "empty export", $"{file.FileName}: {file.Status}; {file.Message}"));
            Assert.Equal(8, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files WHERE data_truth_version=1 AND is_superseded=0"));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(DISTINCT source_sha256) FROM dbo.import_files"));
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_invoices"));
            Assert.Equal(0, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.{EtpReportFamilyRegistry.Resolve("R001").TableName}"));
            var hash = (await new OpenXmlWorkbookReader().ReadAsync(paths[0])).Sha256;
            foreach (var report in new[] { "R001", "R022" })
            foreach (var store in new[] { "HEMW", "WLMHW" })
            foreach (var date in new[] { new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 26) })
            {
                Assert.True(await persistence.ExistsInScopeAsync(hash, report, store, date, date));
                Assert.False(await persistence.ExistsInScopeAsync(hash, report, store, date.AddDays(-1), date));
                var outcome = await persistence.LoadOutcomeInScopeAsync(hash, report, store, date, date);
                Assert.Equal(0, outcome.RowsProcessed);
                Assert.Equal(0, outcome.NewRows);
                var day = await new DailyReportingWorkflowRepository(database.ConnectionString).LoadAsync(store, date);
                Assert.Contains(report, day.ImportedReports);
            }

            var repeated = await service.RunFilesAsync(paths, new("Scoped duplicate SQL test"));
            Assert.Equal(8, repeated.Files.Count);
            Assert.All(repeated.Files, file => Assert.Equal("Duplicate", file.Status));
            Assert.Equal(0, repeated.NewRows);
            Assert.Equal(8, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));

            var documents = new ProductisationRepository(database.ConnectionString);
            var document = await documents.RegisterDocumentAsync("synthetic-empty.xlsx",
                Path.Combine(Path.GetTempPath(),"synthetic-managed-evidence.xlsx"),hash,bytes.Length,
                "ETP_WORKBOOK","R001","HEMW",new(2026,8,25),"VALIDATED","Synthetic SQL test source metadata");
            foreach (var report in new[] { "R001", "R022" })
            foreach (var store in new[] { "HEMW", "WLMHW" })
            foreach (var date in new[] { new DateOnly(2026,8,25), new DateOnly(2026,8,26) })
                await documents.LinkDocumentToImportAsync(document.Id,hash,report,store,date);
            Assert.Equal(1,await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.source_documents"));
            Assert.Equal(8,await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.source_document_import_links"));
            await documents.LinkDocumentToImportAsync(document.Id,hash,"R022","WLMHW",new(2026,8,26));
            Assert.Equal(8,await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.source_document_import_links"));
            var latest = await documents.LoadDocumentAsync(document.Id);
            Assert.Equal("R022",latest.ReportCode);
            Assert.Equal("WLMHW",latest.StoreCode);
            Assert.Equal(new DateOnly(2026,8,26),latest.BusinessDate);
            Assert.Equal(await database.ExecuteAsync("SELECT import_file_id FROM dbo.import_files WHERE report_code='R022' AND store_code='WLMHW' AND period_end='20260826'"),latest.ImportFileId);

            // A legacy version in one scope must not inherit current-version identity from another scope.
            await database.ExecuteAsync("UPDATE dbo.import_files SET data_truth_version=0 WHERE report_code='R001' AND store_code='HEMW' AND business_date='20260825'");
            Assert.False(await persistence.ExistsInScopeAsync(hash, "R001", "HEMW", new(2026,8,25), new(2026,8,25)));
            Assert.True(await persistence.ExistsInScopeAsync(hash, "R022", "HEMW", new(2026,8,25), new(2026,8,25)));
            Assert.True(await persistence.ExistsInScopeAsync(hash, "R001", "WLMHW", new(2026,8,25), new(2026,8,25)));
        }
        finally
        {
            foreach (var path in paths) File.Delete(path);
            await database.DisposeAsync();
        }
    }

    private static byte[] EmptyWorkbook(IReadOnlyList<string> headers)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var worksheet = workbook.AddNewPart<WorksheetPart>();
            var row = new Row { RowIndex = 1 };
            for (var index = 0; index < headers.Count; index++)
                row.Append(new Cell { CellReference = Column(index + 1) + "1", DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(headers[index])) });
            worksheet.Worksheet = new Worksheet(new SheetData(row));
            workbook.Workbook.AppendChild(new Sheets(new Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = "Data" }));
            workbook.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static string Column(int index)
    {
        var result = "";
        while (index > 0) { index--; result = (char)('A' + index % 26) + result; index /= 26; }
        return result;
    }
}
