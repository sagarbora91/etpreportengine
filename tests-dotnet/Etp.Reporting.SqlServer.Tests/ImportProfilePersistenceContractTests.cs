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

    [Fact]
    public void Governed_file_persistence_resolves_profile_inside_the_same_transaction_and_never_backfills_history()
    {
        var root = FindRepositoryRoot();
        var repositories = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Infrastructure.SqlServer", "SqlServerRepositories.cs"));
        var resolver = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Infrastructure.SqlServer", "SqlServerImportProfileResolver.cs"));
        var enrichment = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Infrastructure.SqlServer", "RetailEnrichmentSqlImportOrchestrator.cs"));
        var completion = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Infrastructure.SqlServer", "OperationalCompletionRepository.cs"));
        var automation = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Infrastructure.SqlServer", "AutomatedOperationsService.cs"));
        var restatement = File.ReadAllText(Path.Combine(root, "database", "migrations", "0010_operational_completion.sql"));

        Assert.Contains("ResolveOrRegisterAsync(connection,transaction,package.File.Profile", repositories, StringComparison.Ordinal);
        Assert.Contains("InsertFile(connection,transaction,package.File,profileId", repositories, StringComparison.Ordinal);
        Assert.Contains("WITH(UPDLOCK,HOLDLOCK)", resolver, StringComparison.Ordinal);
        Assert.Contains("report_code=@report AND layout_version=@layout AND profile_version=@profile", resolver, StringComparison.Ordinal);
        Assert.Contains("header_signature_sha256", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE dbo.import_files SET import_profile_id", repositories, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("var reportCode=PersistenceValidation.ResolveReportCode(file)", repositories, StringComparison.Ordinal);
        Assert.Contains("var reportCode=PersistenceValidation.ResolveReportCode(x)", repositories, StringComparison.Ordinal);
        Assert.DoesNotContain("@report\",file.ReportCode", repositories, StringComparison.Ordinal);
        Assert.DoesNotContain("@report\",x.ReportCode", repositories, StringComparison.Ordinal);
        Assert.Contains("ResolveOrRegisterAsync(\n                connection, transaction, accepted.ProfileIdentity", enrichment.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("import_profile_id", enrichment, StringComparison.Ordinal);
        Assert.Contains("WHERE report_code=@report AND store_code=@store AND business_date=@date", completion, StringComparison.Ordinal);
        Assert.Contains("SELECT report_code,store_code,business_date FROM dbo.import_files", automation, StringComparison.Ordinal);
        Assert.Contains("SELECT @store=store_code,@date=business_date,@report=report_code FROM dbo.import_files", restatement, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "src"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
