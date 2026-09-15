using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class ImportProfilePersistenceContractTests
{
    [Fact]
    public void Null_legacy_report_code_derives_routing_from_the_exact_approved_profile()
    {
        var id = Guid.NewGuid();
        var file = new ImportFileRegistration(
            id,
            Etp.Reporting.Import.Profiles.RetailSalesProfiles.R025.Identity,
            "sales.xlsx",
            new string('a', 64),
            1);
        var package = new ImportPersistencePackage(
            new(id, null, null, null, DateTimeOffset.UtcNow),
            file,
            [], [], [], []);

        Assert.Null(file.ReportCode);
        PersistenceValidation.Validate(package);
        Assert.Equal("R025", PersistenceValidation.ResolveReportCode(file));
    }

    [Fact]
    public void Persistence_rejects_a_report_code_that_disagrees_with_the_exact_profile()
    {
        var id = Guid.NewGuid();
        var file = new ImportFileRegistration(
            id,
            Etp.Reporting.Import.Profiles.RetailSalesProfiles.R025.Identity,
            "sales.xlsx",
            new string('a', 64),
            1,
            "R022");
        var package = new ImportPersistencePackage(
            new(id, null, null, null, DateTimeOffset.UtcNow),
            file,
            [], [], [], []);

        Assert.Throws<ArgumentException>(() => PersistenceValidation.Validate(package));
        Assert.Throws<ArgumentException>(() => PersistenceValidation.ResolveReportCode(file));
    }

    [Fact]
    public void Persistence_package_rejects_an_identity_outside_the_approved_registry()
    {
        var id = Guid.NewGuid();
        var changed = new Etp.Reporting.Domain.Imports.ImportProfileIdentity(
            "R025", "ETP_2026_08", "1", new string('f', 64));
        var package = new ImportPersistencePackage(
            new(id, null, null, null, DateTimeOffset.UtcNow),
            new(id, changed, "sales.xlsx", new string('a', 64), 1),
            [], [], [], []);

        Assert.Throws<InvalidOperationException>(() => PersistenceValidation.Validate(package));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "src"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
