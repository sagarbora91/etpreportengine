using System.Security.Cryptography;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

/// <summary>The import keeps the bytes the reader parsed, from its single in-memory snapshot (IF-023, spec 11.2).</summary>
public sealed class WorkbookEvidenceBytesTests
{
    [Fact]
    public async Task Reader_snapshot_bytes_reach_the_accepted_import_unchanged()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
        var bytes = await File.ReadAllBytesAsync(path);

        var snapshot = await new OpenXmlWorkbookReader().ReadAsync(path);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), snapshot.Sha256);
        Assert.Equal(snapshot.Sha256, snapshot.Content!.Sha256);
        Assert.Equal(bytes, snapshot.EvidenceBytes.ToArray());

        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(snapshot);
        Assert.Equal(bytes, accepted.Workbook.EvidenceBytes.ToArray());
    }

    [Fact]
    public async Task Bytes_are_not_offered_once_the_hash_no_longer_describes_them()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
        var snapshot = await new OpenXmlWorkbookReader().ReadAsync(path);

        Assert.True((snapshot with { Sha256 = new string('0', 64) }).EvidenceBytes.IsEmpty);
        Assert.False((snapshot with { Sha256 = snapshot.Sha256.ToUpperInvariant() }).EvidenceBytes.IsEmpty);
        Assert.True((snapshot with { Content = null }).EvidenceBytes.IsEmpty);
        Assert.True(new WorkbookSnapshot("synthetic.xlsx", 1, new string('a', 64), []).EvidenceBytes.IsEmpty);
    }
}
