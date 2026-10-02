using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>Source files kept inside the database as import evidence (IF-023, Owner decision OD-2; spec 5.1 B and 11.2).</summary>
public sealed class EvidenceSqlTests
{
    [Fact]
    public async Task Bytes_retained_in_import_transaction_and_deduplicated()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var path = Sample("R025");
            var bytes = await File.ReadAllBytesAsync(path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

            // A failure after the bytes were stored rolls them back with the import.
            await database.ExecuteAsync("""
                CREATE TRIGGER dbo.evidence_test_fail_completion ON dbo.import_batches AFTER UPDATE AS
                THROW 50999,'Synthetic failure after the evidence was stored.',1;
                """);
            var failed = Assert.Single((await Import(database, path)).Files);
            Assert.Equal("Failed", failed.Status);
            Assert.Equal(EvidenceState.NotAttempted, failed.Evidence);
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));
            await database.ExecuteAsync("DROP TRIGGER dbo.evidence_test_fail_completion");

            var imported = Assert.Single((await Import(database, path)).Files);
            Assert.Equal("Imported", imported.Status);
            Assert.Equal(EvidenceState.Retained, imported.Evidence);
            var fileId = Convert.ToInt64(await database.ExecuteAsync("SELECT import_file_id FROM dbo.import_files"));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));
            Assert.Equal(hash, await database.ExecuteAsync("SELECT source_sha256 FROM dbo.import_source_content"));
            Assert.Equal(fileId, await database.ExecuteAsync("SELECT first_import_file_id FROM dbo.import_source_content"));
            Assert.Equal((long)bytes.Length, await database.ExecuteAsync("SELECT size_bytes FROM dbo.import_source_content"));
            Assert.Equal(bytes, (byte[])(await database.ExecuteAsync("SELECT content FROM dbo.import_source_content"))!);

            // The same bytes are stored once, however often they are offered.
            Assert.Equal("ALREADY_HELD", await Retain(database, hash, bytes, fileId));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));

            // The procedure keeps only well-formed, matching bytes of an imported file, inside a transaction.
            Assert.Equal(51750, (await Assert.ThrowsAsync<SqlException>(() => Retain(database, hash.ToUpperInvariant(), bytes, fileId))).Number);
            Assert.Equal(51751, (await Assert.ThrowsAsync<SqlException>(() => Retain(database, hash, [.. bytes, 0], fileId))).Number);
            var other = new byte[] { 1, 2, 3 };
            Assert.Equal(51752, (await Assert.ThrowsAsync<SqlException>(() =>
                Retain(database, Convert.ToHexStringLower(SHA256.HashData(other)), other, fileId))).Number);
            Assert.Equal(51422, (await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                $"EXEC dbo.retain_import_source '{hash}',3,0x010203,{fileId}"))).Number);
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Duplicate_result_retains_missing_bytes()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var path = Sample("R025");
            // An import from before evidence was kept: its rows are stored, its bytes are not.
            var earlier = Assert.Single((await Import(database, path, new WithoutContentReader())).Files);
            Assert.Equal("Imported", earlier.Status);
            Assert.Equal(EvidenceState.NotAttempted, earlier.Evidence);
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));
            var fileId = Convert.ToInt64(await database.ExecuteAsync("SELECT import_file_id FROM dbo.import_files"));
            var rows = await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines");

            var duplicate = Assert.Single((await Import(database, path)).Files);
            Assert.Equal("Duplicate", duplicate.Status);
            Assert.Equal(EvidenceState.Retained, duplicate.Evidence);
            Assert.Equal(await File.ReadAllBytesAsync(path), (byte[])(await database.ExecuteAsync("SELECT content FROM dbo.import_source_content"))!);
            Assert.Equal(fileId, await database.ExecuteAsync("SELECT first_import_file_id FROM dbo.import_source_content"));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));
            Assert.Equal(rows, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));

            var again = Assert.Single((await Import(database, path)).Files);
            Assert.Equal("Duplicate", again.Status);
            Assert.Equal(EvidenceState.AlreadyHeld, again.Evidence);
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Viewer_cannot_read_evidence()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            Assert.Equal("Imported", Assert.Single((await Import(database, Sample("R025"))).Files).Status);
            await database.ExecuteAsync("""
                CREATE USER evidence_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER evidence_viewer;
                CREATE USER evidence_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER evidence_manager;
                """);
            foreach (var user in new[] { "evidence_viewer", "evidence_manager" })
            {
                var denied = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                    $"EXECUTE AS USER='{user}'; SELECT TOP(1) content FROM dbo.import_source_content; REVERT;"));
                Assert.Equal(229, denied.Number);
                var write = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                    $"EXECUTE AS USER='{user}'; DELETE dbo.import_source_content; REVERT;"));
                Assert.Equal(229, write.Number);
                // Every role sees what is held and its size, never the bytes.
                Assert.Equal(1, await database.ExecuteAsync(
                    $"EXECUTE AS USER='{user}'; SELECT COUNT(*) FROM dbo.v_import_source_evidence; REVERT;"));
            }
            var execute = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                "EXECUTE AS USER='evidence_viewer'; EXEC dbo.retain_import_source NULL,0,0x,1; REVERT;"));
            Assert.Equal(229, execute.Number);
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Keep_source_files_for_earlier_imports_stores_matching_files_and_imports_nothing()
    {
        var database = new SqlDatabaseFixture();
        var folder = Path.Combine(Path.GetTempPath(), "EtpEvidenceFolder_" + Guid.NewGuid().ToString("N"));
        try
        {
            await database.InitializeAsync();
            var imported = await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString),
                new WithoutContentReader()).RunFilesAsync([Sample("R025"), Sample("R020"), Sample("R024")], new("Synthetic Owner"));
            Assert.All(imported.Files, file => Assert.Equal("Imported", file.Status));
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));
            var files = await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files");
            var rows = await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines");

            // R025 as a plain file in a sub-folder, R020 inside a .zip, and files that match no import.
            var nested = Directory.CreateDirectory(Path.Combine(folder, "WLMHW", "2026-08-25")).FullName;
            File.Copy(Sample("R025"), Path.Combine(nested, Path.GetFileName(Sample("R025"))));
            using (var archive = ZipFile.Open(Path.Combine(folder, "exports.zip"), ZipArchiveMode.Create))
                archive.CreateEntryFromFile(Sample("R020"), "inner/" + Path.GetFileName(Sample("R020")));
            File.Copy(Sample("R030"), Path.Combine(folder, Path.GetFileName(Sample("R030"))));
            await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), "Synthetic note");

            var service = new SqlServerImportEvidenceService(database.ConnectionString);
            var before = await service.LoadSummaryAsync();
            Assert.Equal(0, before.FilesHeld);
            Assert.Equal(3, before.ImportedSourcesWithoutFile);

            var result = await service.RetainEarlierImportsAsync([folder]);
            Assert.Equal(3, result.FilesHashed);
            Assert.Equal(2, result.Matched);
            Assert.Equal(2, result.Retained);
            Assert.Equal(0, result.AlreadyHeld);
            Assert.Equal(0, result.Skipped);
            Assert.Equal(new FileInfo(Sample("R025")).Length + new FileInfo(Sample("R020")).Length, result.BytesRetained);
            Assert.Equal(2, await database.ExecuteAsync("""
                SELECT COUNT(*) FROM dbo.import_source_content c
                JOIN dbo.import_files f ON f.import_file_id=c.first_import_file_id AND f.source_sha256=c.source_sha256
                WHERE f.report_code IN ('R025','R020') AND HASHBYTES('SHA2_256',c.content)=CONVERT(binary(32),c.source_sha256,2);
                """));
            // Nothing is imported.
            Assert.Equal(files, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));
            Assert.Equal(rows, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));
            var after = await service.LoadSummaryAsync();
            Assert.Equal(2, after.FilesHeld);
            Assert.Equal(result.BytesRetained, after.BytesHeld);
            Assert.Equal(1, after.ImportedSourcesWithoutFile);
            Assert.True(after.DatabaseDataBytes > 0);

            var repeat = await service.RetainEarlierImportsAsync([folder]);
            Assert.Equal(0, repeat.Retained);
            Assert.Equal(2, repeat.AlreadyHeld);
            Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));

            // Only the Owner may run it; every role may see the size.
            var manager = new SqlServerImportEvidenceService(database.ConnectionString,
                _ => Task.FromResult(new ApplicationAccess("synthetic", "Synthetic manager", ApplicationRole.StoreManager, true)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.RetainEarlierImportsAsync([folder]));
            Assert.Equal(2, (await manager.LoadSummaryAsync()).FilesHeld);
        }
        finally
        {
            await database.DisposeAsync();
            if (!Path.GetFileName(folder).StartsWith("EtpEvidenceFolder_", StringComparison.Ordinal) ||
                !Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe synthetic folder cleanup path.");
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Retaining_new_bytes_does_not_block_an_import_of_another_file()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var paths = new[] { Sample("R025"), Sample("R020") };
            Assert.All((await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString),
                new WithoutContentReader()).RunFilesAsync(paths, new("Synthetic Owner"))).Files,
                file => Assert.Equal("Imported", file.Status));
            var first = await File.ReadAllBytesAsync(paths[0]);
            var second = await File.ReadAllBytesAsync(paths[1]);
            var firstHash = Convert.ToHexStringLower(SHA256.HashData(first));
            var secondHash = Convert.ToHexStringLower(SHA256.HashData(second));
            var firstId = Convert.ToInt64(await database.ExecuteAsync($"SELECT import_file_id FROM dbo.import_files WHERE source_sha256='{firstHash}'"));
            var secondId = Convert.ToInt64(await database.ExecuteAsync($"SELECT import_file_id FROM dbo.import_files WHERE source_sha256='{secondHash}'"));

            // An open import holds its new bytes uncommitted; a second import of a different file must not wait on it.
            await using var open = new SqlConnection(database.ConnectionString);
            await open.OpenAsync();
            await using var transaction = (SqlTransaction)await open.BeginTransactionAsync();
            Assert.Equal("RETAINED", await RetainIn(open, transaction, firstHash, first, firstId));

            await using var other = new SqlConnection(database.ConnectionString);
            await other.OpenAsync();
            await using (var timeout = new SqlCommand("SET LOCK_TIMEOUT 3000", other)) await timeout.ExecuteNonQueryAsync();
            await using var otherTransaction = (SqlTransaction)await other.BeginTransactionAsync();
            Assert.Equal("RETAINED", await RetainIn(other, otherTransaction, secondHash, second, secondId));
            await otherTransaction.CommitAsync();
            await transaction.CommitAsync();
            Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));
        }
        finally { await database.DisposeAsync(); }
    }

    // Review 1.9.3 finding 5: the rollback of the evidence is proven on every route, not only R025 (sales).
    // The file under test runs beside R025 so a snapshot without its own date takes the folder's date.
    [Theory]
    [InlineData("R025")] // sales
    [InlineData("R022")] // revenue
    [InlineData("R013")] // enrichment
    [InlineData("R030")] // stock
    [InlineData("R020")] // family
    public async Task A_failure_after_the_bytes_were_stored_rolls_them_back_on_every_route(string report)
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var path = Sample(report);
            var bytes = await File.ReadAllBytesAsync(path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var paths = report == "R025" ? new[] { path } : [Sample("R025"), path];

            // Fails the completion of this file's batch only, after its bytes were stored in the same transaction.
            await database.ExecuteAsync($"""
                CREATE TRIGGER dbo.evidence_test_fail_route ON dbo.import_batches AFTER UPDATE AS
                IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.import_files f ON f.import_batch_id=i.import_batch_id
                          WHERE f.source_sha256='{hash}')
                    THROW 50999,'Synthetic failure after the evidence was stored.',1;
                """);
            var first = await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString))
                .RunFilesAsync(paths, new("Synthetic Owner"));
            var failed = Assert.Single(first.Files, file => file.FileName == Path.GetFileName(path));
            Assert.Equal("Failed", failed.Status);
            Assert.Equal(EvidenceState.NotAttempted, failed.Evidence);
            Assert.Equal(CommitState.RolledBack, failed.CommitState);
            Assert.Equal(0, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.import_files WHERE source_sha256='{hash}'"));
            Assert.Equal(0, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.import_source_content WHERE source_sha256='{hash}'"));
            // The attempt still records its evidence state (IF-023), never NULL.
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_attempts WHERE evidence_state IS NULL"));
            await database.ExecuteAsync("DROP TRIGGER dbo.evidence_test_fail_route");

            var second = await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString))
                .RunFilesAsync(paths, new("Synthetic Owner"));
            var imported = Assert.Single(second.Files, file => file.FileName == Path.GetFileName(path));
            Assert.True(imported.Status is "Imported" or "empty export", $"{imported.FileName}: {imported.Status}; {imported.Message}");
            Assert.Equal(EvidenceState.Retained, imported.Evidence);
            Assert.Equal(bytes, (byte[])(await database.ExecuteAsync(
                $"SELECT content FROM dbo.import_source_content WHERE source_sha256='{hash}'"))!);
        }
        finally { await database.DisposeAsync(); }
    }

    // Review 1.9.3 finding 5: the use case's own duplicate check, before the import lock (ExistsInScope).
    [Fact]
    public async Task Use_case_duplicate_before_the_lock_keeps_missing_bytes()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var path = Sample("R025");
            var withBytes = await Accepted(path, keepContent: true);
            var withoutBytes = await Accepted(path, keepContent: false);
            var useCase = new SqlServerImportPersistenceUseCase(database.ConnectionString);

            var imported = await useCase.PersistAsync(Request(withoutBytes));
            Assert.Equal("Imported", imported.Status);
            Assert.Equal(EvidenceState.NotAttempted, imported.Evidence);
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));

            var duplicate = await useCase.PersistAsync(Request(withBytes));
            Assert.Equal("Duplicate", duplicate.Status);
            Assert.Equal(EvidenceState.Retained, duplicate.Evidence);
            Assert.Equal(await File.ReadAllBytesAsync(path), (byte[])(await database.ExecuteAsync("SELECT content FROM dbo.import_source_content"))!);
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));

            var again = await useCase.PersistAsync(Request(withBytes));
            Assert.Equal("Duplicate", again.Status);
            Assert.Equal(EvidenceState.AlreadyHeld, again.Evidence);
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));
        }
        finally { await database.DisposeAsync(); }
    }

    // Review 1.9.3 finding 5: the duplicate the use case finds only under the import lock (a second import of the
    // same file that waited on the first). Whichever wins, the bytes are held once and the states say who kept them.
    [Fact]
    public async Task Use_case_duplicate_found_under_the_import_lock_keeps_missing_bytes()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var path = Sample("R025");
            var withBytes = await Accepted(path, keepContent: true);
            var withoutBytes = await Accepted(path, keepContent: false);
            var builder = new SqlConnectionStringBuilder(database.ConnectionString)
                { ApplicationName = "EvidenceUnderLock_" + Guid.NewGuid().ToString("N"), Pooling = false };
            await using var gate = new SqlConnection(database.ConnectionString);
            await gate.OpenAsync();
            await using var transaction = (SqlTransaction)await gate.BeginTransactionAsync();
            await using (var hold = new SqlCommand("""
                DECLARE @lock int;
                EXEC @lock=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
                IF @lock<0 THROW 51997,'Could not arrange the import lock fixture.',1;
                """, gate, transaction))
            {
                hold.Parameters.AddWithValue("@resource", $"ETP_IMPORT:{withBytes.Scope.StoreCode}:{withBytes.ProfileIdentity.ReportCode}");
                await hold.ExecuteNonQueryAsync();
            }
            var without = new SqlServerImportPersistenceUseCase(builder.ConnectionString).PersistAsync(Request(withoutBytes));
            var with = new SqlServerImportPersistenceUseCase(builder.ConnectionString).PersistAsync(Request(withBytes));
            try
            {
                var clock = Stopwatch.StartNew();
                while (Convert.ToInt32(await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id WHERE s.program_name='{builder.ApplicationName}' AND r.wait_type LIKE 'LCK_M_%'")) < 2)
                {
                    Assert.False(without.IsCompleted || with.IsCompleted, "Both imports must pass the early duplicate check and wait on the import lock.");
                    Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "The imports did not reach the import lock.");
                    await Task.Delay(25);
                }
            }
            finally { await transaction.CommitAsync(); }
            var results = await Task.WhenAll(without, with).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(new[] { "Duplicate", "Imported" }, results.Select(result => result.Status).Order());
            var withoutWon = results[0].Status == "Imported";
            var duplicate = withoutWon ? results[1] : results[0];
            Assert.Equal(withoutWon ? EvidenceState.NotAttempted : EvidenceState.Retained, (withoutWon ? results[0] : results[1]).Evidence);
            // The duplicate offered bytes only when it is the import that read them.
            Assert.Equal(withoutWon ? EvidenceState.Retained : EvidenceState.NotAttempted, duplicate.Evidence);
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_content"));
            Assert.Equal(await File.ReadAllBytesAsync(path), (byte[])(await database.ExecuteAsync("SELECT content FROM dbo.import_source_content"))!);
        }
        finally { await database.DisposeAsync(); }
    }

    // Review 1.9.3 finding 3: Settings → Database and the walk never wait on an import in progress.
    [Fact]
    public async Task Summary_and_walk_do_not_wait_on_an_import_in_progress()
    {
        var database = new SqlDatabaseFixture();
        var folder = Path.Combine(Path.GetTempPath(), "EtpEvidenceFolder_" + Guid.NewGuid().ToString("N"));
        try
        {
            await database.InitializeAsync();
            Assert.All((await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString),
                new WithoutContentReader()).RunFilesAsync([Sample("R025"), Sample("R020")], new("Synthetic Owner"))).Files,
                file => Assert.Equal("Imported", file.Status));
            Directory.CreateDirectory(folder);
            File.Copy(Sample("R025"), Path.Combine(folder, Path.GetFileName(Sample("R025"))));
            File.Copy(Sample("R020"), Path.Combine(folder, Path.GetFileName(Sample("R020"))));
            var held = await File.ReadAllBytesAsync(Sample("R025"));
            var heldHash = Convert.ToHexStringLower(SHA256.HashData(held));
            var heldId = Convert.ToInt64(await database.ExecuteAsync($"SELECT import_file_id FROM dbo.import_files WHERE source_sha256='{heldHash}'"));
            var service = new SqlServerImportEvidenceService(database.ConnectionString, null, null, TimeSpan.FromMilliseconds(500));

            // An import in progress holds R025's new evidence row, uncommitted.
            await using (var open = new SqlConnection(database.ConnectionString))
            {
                await open.OpenAsync();
                await using var transaction = (SqlTransaction)await open.BeginTransactionAsync();
                Assert.Equal("RETAINED", await RetainIn(open, transaction, heldHash, held, heldId));

                var clock = Stopwatch.StartNew();
                var summary = await service.LoadSummaryAsync();
                Assert.Equal(0, summary.FilesHeld); // the uncommitted row is read past, not waited on
                Assert.Equal(2, summary.ImportedSourcesWithoutFile);
                var walk = await service.RetainEarlierImportsAsync([folder]);
                Assert.Equal(2, walk.Matched);
                Assert.Equal(1, walk.Retained); // R020
                Assert.Equal(1, walk.Busy);     // R025, held by the import
                Assert.Equal(0, walk.DatabaseFailures);
                Assert.Equal(0, walk.Skipped);
                Assert.Equal(EarlierImportStop.None, walk.Stop);
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"Waited {clock.Elapsed} on an import in progress.");
                await transaction.RollbackAsync();
            }
            var again = await service.RetainEarlierImportsAsync([folder]);
            Assert.Equal(1, again.Retained);
            Assert.Equal(1, again.AlreadyHeld);
            Assert.Equal(0, again.Busy);

            // A table lock cannot be read past: the summary and the walk report busy instead of hanging.
            await using (var locking = new SqlConnection(database.ConnectionString))
            {
                await locking.OpenAsync();
                await using var transaction = (SqlTransaction)await locking.BeginTransactionAsync();
                await using (var hold = new SqlCommand("SELECT COUNT(*) FROM dbo.import_files WITH (TABLOCKX, HOLDLOCK)", locking, transaction))
                    await hold.ExecuteScalarAsync();
                var clock = Stopwatch.StartNew();
                await Assert.ThrowsAsync<ImportEvidenceBusyException>(() => service.LoadSummaryAsync());
                await Assert.ThrowsAsync<ImportEvidenceBusyException>(() => service.RetainEarlierImportsAsync([folder]));
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"Waited {clock.Elapsed} on a table lock.");
                await transaction.RollbackAsync();
            }
            Assert.Equal(2, (await service.LoadSummaryAsync()).FilesHeld);
        }
        finally
        {
            await database.DisposeAsync();
            if (!Path.GetFileName(folder).StartsWith("EtpEvidenceFolder_", StringComparison.Ordinal) ||
                !Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe synthetic folder cleanup path.");
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<MatchedImportEnvelope> Accepted(string path, bool keepContent)
    {
        var workbook = await new OpenXmlWorkbookReader().ReadAsync(path);
        return new MatchedImportEnvelopeFactory().RequireAccepted(keepContent ? workbook : workbook with { Content = null });
    }

    private static ImportPersistenceRequest<MatchedImportEnvelope> Request(MatchedImportEnvelope accepted) =>
        new(accepted, accepted.Scope.PeriodEnd!.Value, accepted.Scope.StoreCode!, "Synthetic Owner");

    private static async Task<string> RetainIn(SqlConnection connection, SqlTransaction transaction, string hash, byte[] content, long fileId)
    {
        await using var command = new SqlCommand("EXEC dbo.retain_import_source @hash,@size,@content,@file,@state OUTPUT",
            connection, transaction);
        command.Parameters.Add("@hash", System.Data.SqlDbType.Char, 64).Value = hash;
        command.Parameters.Add("@size", System.Data.SqlDbType.BigInt).Value = (long)content.Length;
        command.Parameters.Add("@content", System.Data.SqlDbType.VarBinary, -1).Value = content;
        command.Parameters.Add("@file", System.Data.SqlDbType.BigInt).Value = fileId;
        var state = command.Parameters.Add("@state", System.Data.SqlDbType.VarChar, 16);
        state.Direction = System.Data.ParameterDirection.Output;
        await command.ExecuteNonQueryAsync();
        return (string)state.Value;
    }
    private static string Sample(string report) =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), report + "_*.xlsx").Single();

    private static Task<FolderImportSummary> Import(SqlDatabaseFixture database, string path, IWorkbookReader? reader = null) =>
        new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString), reader)
            .RunFilesAsync([path], new("Synthetic Owner"));

    private static async Task<string> Retain(SqlDatabaseFixture database, string hash, byte[] content, long fileId)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = new SqlCommand("EXEC dbo.retain_import_source @hash,@size,@content,@file,@state OUTPUT",
            connection, transaction);
        command.Parameters.Add("@hash", System.Data.SqlDbType.Char, 64).Value = hash;
        command.Parameters.Add("@size", System.Data.SqlDbType.BigInt).Value = (long)content.Length;
        command.Parameters.Add("@content", System.Data.SqlDbType.VarBinary, -1).Value = content;
        command.Parameters.Add("@file", System.Data.SqlDbType.BigInt).Value = fileId;
        var state = command.Parameters.Add("@state", System.Data.SqlDbType.VarChar, 16);
        state.Direction = System.Data.ParameterDirection.Output;
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return (string)state.Value;
    }

    // Reads like the importer of release 1.9.2 and earlier, which kept no bytes.
    private sealed class WithoutContentReader : IWorkbookReader
    {
        public async Task<WorkbookSnapshot> ReadAsync(string filePath, CancellationToken cancellationToken = default) =>
            (await new OpenXmlWorkbookReader().ReadAsync(filePath, cancellationToken)) with { Content = null };
    }
}
