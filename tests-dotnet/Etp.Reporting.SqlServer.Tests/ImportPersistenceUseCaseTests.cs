using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class ImportPersistenceUseCaseTests
{
    public static TheoryData<string, ImportPersistenceRoute> Routes => new()
    {
        { "R022", ImportPersistenceRoute.Revenue },
        { "r022", ImportPersistenceRoute.Revenue },
        { "STOCK_LEDGER", ImportPersistenceRoute.Stock },
        { "CLOSING_STOCK", ImportPersistenceRoute.Stock },
        { "R003", ImportPersistenceRoute.Enrichment },
        { "R013", ImportPersistenceRoute.Enrichment },
        { "R025", ImportPersistenceRoute.Sales },
        { "UNKNOWN", ImportPersistenceRoute.Sales }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void Report_codes_preserve_the_existing_orchestrator_routing(string reportCode, ImportPersistenceRoute expected) =>
        Assert.Equal(expected, SqlServerImportPersistenceUseCase.SelectRoute(reportCode));

    [Fact]
    public void Batch_outcome_mapping_preserves_every_counter_and_duplicate_state()
    {
        var mapped = SqlServerImportPersistenceUseCase.Map(new WorkbookImportOutcome(10, 4, 3, 3, true));

        Assert.Equal(10, mapped.RowsProcessed);
        Assert.Equal(4, mapped.NewRows);
        Assert.Equal(3, mapped.AlreadyPresentRows);
        Assert.Equal(3, mapped.ConflictRows);
        Assert.True(mapped.ExactDuplicate);
    }

    [Fact]
    public void Restatement_mapping_preserves_identity_and_reason()
    {
        var mapped = SqlServerImportPersistenceUseCase.Map(new ImportRestatement(42, "owner", "Corrected source"));

        Assert.NotNull(mapped);
        Assert.Equal(42, mapped.PreviousImportFileId);
        Assert.Equal("owner", mapped.RequestedBy);
        Assert.Equal("Corrected source", mapped.Reason);
    }

    [Theory]
    [InlineData(0, "owner", "reason")]
    [InlineData(1, "", "reason")]
    [InlineData(1, "owner", "")]
    public void Invalid_restatement_is_rejected_before_sql_is_used(long previousId, string requestedBy, string reason) =>
        Assert.Throws<ArgumentException>(() => SqlServerImportPersistenceUseCase.Map(new ImportRestatement(previousId, requestedBy, reason)));

    [Fact]
    public void Empty_connection_string_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new SqlServerImportPersistenceUseCase("   "));

    [Fact]
    public void Sql_authentication_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new SqlServerImportPersistenceUseCase(
            "Server=localhost;Database=EtpReporting;User ID=reporter;Password=secret"));

    [Fact]
    public async Task Import_queries_and_persistence_fail_closed_before_SQL_for_unauthorized_accounts()
    {
        const string connection = @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";
        var viewer = new SqlServerImportPersistenceUseCase(
            connection,
            _ => Task.FromResult(new ApplicationAccess("STORE\\Viewer", "Viewer", ApplicationRole.Viewer, true)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewer.ExistsByHashAsync(new string('a', 64)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewer.PersistAsync(Request(restatement: null)));

        var manager = new SqlServerImportPersistenceUseCase(
            connection,
            _ => Task.FromResult(new ApplicationAccess("STORE\\Manager", "Manager", ApplicationRole.StoreManager, true)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.PersistAsync(Request(
            new ImportRestatement(7, "STORE\\Manager", "Corrected source"))));
    }

    [Fact]
    public async Task Import_authorization_observes_cancellation_before_touching_SQL()
    {
        const string connection = @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";
        var service = new SqlServerImportPersistenceUseCase(
            connection,
            token => Task.FromCanceled<ApplicationAccess>(token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExistsByHashAsync(new string('a', 64), cancellation.Token));
    }

    private static ImportPersistenceRequest<WorkbookSnapshot> Request(ImportRestatement? restatement) => new(
        new WorkbookSnapshot("sales.xlsx", 1, new string('a', 64), []),
        "R025",
        new DateOnly(2026, 8, 25),
        "WLMHW",
        "STORE\\Manager",
        restatement);
}
