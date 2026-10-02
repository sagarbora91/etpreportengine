using Etp.Reporting.Infrastructure.SqlServer.Tally;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class TallyEvidenceStoreSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>, IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "EtpTallyEvidenceSql", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task A_written_file_is_registered_and_verified_until_it_changes()
    {
        var batch = await database.ExecuteAsync("""
            INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
            VALUES('TEVSTORE','20260825',1,REPLICATE('a',64),N'{}',SUSER_SNAME(),1);
            INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,created_by)
            SELECT store_code,business_date,daily_report_generation_id,1,1,1,'DRAFT',SUSER_SNAME() FROM dbo.daily_report_generations WHERE store_code='TEVSTORE';
            SELECT CONVERT(bigint,SCOPE_IDENTITY());
            """);
        var store = new TallyEvidenceStore(database.ConnectionString, root);
        var path = $@"GOLDEN\TEVSTORE\2026-08\batch-{batch}\validation.json";

        var artifact = await store.WriteAsync((long)batch!, "VALIDATION", path, "{\"findings\":[]}"u8.ToArray());

        Assert.Equal(path, await database.ExecuteAsync($"SELECT relative_path FROM dbo.tally_artifacts WHERE tally_artifact_id={artifact.Id}"));
        Assert.Equal(artifact.Sha256, await database.ExecuteAsync($"SELECT sha256 FROM dbo.tally_artifacts WHERE tally_artifact_id={artifact.Id}"));
        Assert.Equal(TallyEvidenceState.Ok, Assert.Single(await store.VerifyAsync((long)batch)).State);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync((long)batch, "VALIDATION", path, "{}"u8.ToArray()));
        await File.WriteAllTextAsync(Path.Combine(root, path.Replace('\\', Path.DirectorySeparatorChar)), "{\"findings\":[1]}");
        Assert.Equal(TallyEvidenceState.Changed, Assert.Single(await store.VerifyAsync((long)batch)).State);
    }

    [Fact]
    public async Task A_file_that_cannot_be_registered_is_removed_and_paths_stay_in_their_batch()
    {
        var store = new TallyEvidenceStore(database.ConnectionString, root);
        const string orphan = @"GOLDEN\TEVNONE\2026-08\batch-999999\payload.xml";
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => store.WriteAsync(999999, "PAYLOAD_XML", orphan, "<ENVELOPE/>"u8.ToArray()));
        Assert.False(File.Exists(Path.Combine(root, orphan.Replace('\\', Path.DirectorySeparatorChar))));

        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(1, "PAYLOAD_XML", @"GOLDEN\TEVNONE\2026-08\batch-2\payload.xml", "x"u8.ToArray()));
        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(1, "INVENTED", @"GOLDEN\TEVNONE\2026-08\batch-1\payload.xml", "x"u8.ToArray()));
    }

    [Fact]
    public async Task A_file_left_on_disk_without_registration_is_set_aside_and_does_not_block_its_name()
    {
        var batch = await database.ExecuteAsync("""
            INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
            VALUES('TEVORPH','20260825',1,REPLICATE('a',64),N'{}',SUSER_SNAME(),1);
            INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,created_by)
            SELECT store_code,business_date,daily_report_generation_id,1,1,1,'DRAFT',SUSER_SNAME() FROM dbo.daily_report_generations WHERE store_code='TEVORPH';
            SELECT CONVERT(bigint,SCOPE_IDENTITY());
            """);
        var store = new TallyEvidenceStore(database.ConnectionString, root);
        var path = $@"GOLDEN\TEVORPH\2026-08\batch-{batch}\manifest.json";
        var full = Path.Combine(root, path.Replace('\\', Path.DirectorySeparatorChar));
        // A crash between writing and registering leaves this behind.
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, "{\"left\":true}");

        var artifact = await store.WriteAsync((long)batch!, "MANIFEST", path, "{\"files\":[]}"u8.ToArray());

        Assert.Equal(TallyEvidenceState.Ok, Assert.Single(await store.VerifyAsync((long)batch)).State);
        Assert.Equal(artifact.Sha256, await TallyEvidenceFiles.HashAsync(full));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(full)!, "manifest.json.unregistered-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
