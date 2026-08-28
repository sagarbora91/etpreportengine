using App = Etp.Reporting.Application.Distribution;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class DistributionServiceBoundaryTests
{
    [Fact]
    public void Production_adapters_require_windows_integrated_security()
    {
        const string integrated = @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";
        _ = new SqlServerInvestigationQuery(integrated);
        _ = new SqlServerReportDistributionService(integrated);

        const string sqlLogin = @"Server=.\SQLEXPRESS;Database=EtpReporting;User ID=sa;Password=secret";
        Assert.Throws<ArgumentException>(() => new SqlServerInvestigationQuery(sqlLogin));
        Assert.Throws<ArgumentException>(() => new SqlServerReportDistributionService(sqlLogin));
    }

    [Fact]
    public async Task Investigation_is_view_authorized_and_maps_the_productisation_query()
    {
        var gateway = new FakeGateway
        {
            SearchResults = [new("Invoice", "INV-1", "WLMHW", new(2026, 8, 28), "Canonical sales invoice", "Reports")]
        };

        var rows = await new SqlServerInvestigationQuery(gateway, Access(ApplicationRole.Viewer)).SearchAsync("INV", 25);

        Assert.Equal("INV-1", Assert.Single(rows).PrimaryReference);
        Assert.Equal(25, gateway.SearchLimit);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new SqlServerInvestigationQuery(gateway, Access(ApplicationRole.None)).SearchAsync("INV"));
    }

    [Fact]
    public async Task Package_creation_preserves_hash_manifest_and_records_audit_after_creation()
    {
        var gateway = new FakeGateway();
        var expectedHash = new string('a', 64);
        var expectedManifest = "{\"generation\":7}";
        var service = Distribution(gateway, ApplicationRole.Viewer, (_, _, generation, store, final, createdBy, _) =>
        {
            gateway.Calls.Add($"create:{generation}:{store}:{final}:{createdBy}");
            return Task.FromResult(new ReportPackageResult(
                @"C:\reports\pack.zip", expectedHash, expectedManifest,
                [new ReportPackageFile("ETP-Reports/Report-Pack.xlsx", 10, new string('b', 64))]));
        });

        var result = await service.CreatePackageAsync(new(
            91, @"C:\reports\pack.zip", Document(), 7, "COMBINED", true, "Owner"));

        Assert.Equal(expectedHash, result.Sha256);
        Assert.Equal(expectedManifest, result.ManifestJson);
        Assert.Equal("ETP-Reports/Report-Pack.xlsx", Assert.Single(result.Files).RelativePath);
        Assert.Equal(["create:7:COMBINED:True:Owner", "package:91:COMBINED:True"], gateway.Calls);
        Assert.Equal(expectedHash, gateway.PackageHash);
        Assert.Equal(expectedManifest, gateway.PackageManifest);
    }

    [Fact]
    public async Task Email_attachment_policy_enforces_the_configured_limit_before_UI_draft_creation()
    {
        var gateway = new FakeGateway { Settings = Settings(maximumAttachmentMb: 5) };
        var allowed = Distribution(gateway, ApplicationRole.Viewer, PackageBuilder(), _ => true, _ => 5 * 1024L * 1024L);
        var policy = await allowed.ValidateEmailAttachmentAsync("pack.zip");
        Assert.Equal("share", policy.ShareFolderPath);
        Assert.Equal(5, policy.MaximumAttachmentMb);

        var oversized = Distribution(gateway, ApplicationRole.Viewer, PackageBuilder(), _ => true, _ => 5 * 1024L * 1024L + 1);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => oversized.ValidateEmailAttachmentAsync("pack.zip"));
        Assert.Equal("The attachment exceeds the configured 5 MB email limit.", error.Message);
    }

    [Fact]
    public async Task Share_attempt_preserves_safe_audit_wording_and_requires_view_access()
    {
        var gateway = new FakeGateway();
        var command = new App.RecordDistributionAttempt(
            91, null, "EMAIL", "Configured recipient", @"C:\reports\pack.zip", "INITIATED",
            "Email draft opened; delivery is not claimed.");

        await Distribution(gateway, ApplicationRole.Viewer, PackageBuilder()).RecordAttemptAsync(command);
        Assert.Equal("share:91:EMAIL:Configured recipient:pack.zip:INITIATED:Email draft opened; delivery is not claimed.", Assert.Single(gateway.Calls));

        gateway.Calls.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Distribution(gateway, ApplicationRole.None, PackageBuilder()).RecordAttemptAsync(command));
        Assert.Empty(gateway.Calls);
    }

    private static SqlServerReportDistributionService Distribution(
        FakeGateway gateway,
        ApplicationRole role,
        Func<string, ReportPackDocument, int, string, bool, string, CancellationToken, Task<ReportPackageResult>> create,
        Func<string, bool>? exists = null,
        Func<string, long>? length = null) =>
        new(gateway, Access(role), create, exists, length);

    private static Func<CancellationToken, Task<ApplicationAccess>> Access(ApplicationRole role) =>
        _ => Task.FromResult(new ApplicationAccess(@"STORE\User", "User", role, true));

    private static Func<string, ReportPackDocument, int, string, bool, string, CancellationToken, Task<ReportPackageResult>> PackageBuilder() =>
        (_, _, _, _, _, _, _) => Task.FromResult(new ReportPackageResult("pack.zip", new string('a', 64), "{}", []));

    private static ReportPackDocument Document() =>
        new("Daily Pack", new(2026, 8, 28), new(2026, 8, 28), "Passed", "rule", "Complete", DateTimeOffset.UtcNow, []);

    private static ProductSettings Settings(int maximumAttachmentMb) =>
        new("documents", "share", null, null, null, null, true, null, maximumAttachmentMb, DateTime.MinValue, "owner");

    private sealed class FakeGateway : IDistributionSqlGateway
    {
        public IReadOnlyList<InvestigationResult> SearchResults { get; set; } = [];
        public ProductSettings Settings { get; set; } = DistributionServiceBoundaryTests.Settings(20);
        public List<string> Calls { get; } = [];
        public int SearchLimit { get; private set; }
        public string? PackageHash { get; private set; }
        public string? PackageManifest { get; private set; }

        public Task<IReadOnlyList<InvestigationResult>> SearchAsync(string term, int limit, CancellationToken token)
        {
            SearchLimit = limit;
            return Task.FromResult(SearchResults);
        }

        public Task<ProductSettings> LoadSettingsAsync(CancellationToken token) => Task.FromResult(Settings);

        public Task RecordPackageAsync(long generationId, string packageType, string path, string manifestJson, string sha256, bool isFinal, CancellationToken token)
        {
            PackageHash = sha256;
            PackageManifest = manifestJson;
            Calls.Add($"package:{generationId}:{packageType}:{isFinal}");
            return Task.CompletedTask;
        }

        public Task RecordShareAttemptAsync(long generationId, long? packageId, string channel, string? destinationSafe, string attachmentName, string outcome, string message, CancellationToken token)
        {
            Calls.Add($"share:{generationId}:{channel}:{destinationSafe}:{Path.GetFileName(attachmentName)}:{outcome}:{message}");
            return Task.CompletedTask;
        }
    }
}
