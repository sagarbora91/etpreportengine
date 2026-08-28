using Etp.Reporting.Application.Registers;
using Etp.Reporting.Application.Sharing;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class SharingContactsAndDigitalRegistersAdapterTests
{
    private const string IntegratedConnection =
        @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";

    [Fact]
    public async Task Sharing_adapter_maps_all_read_fields()
    {
        var modified = new DateTime(2026, 8, 28, 10, 30, 0, DateTimeKind.Utc);
        IReadOnlyList<SharingContactRow> rows =
        [
            new(7, "Area Manager", "Management", "manager@example.test", "+919999999999", "DSR", true, @"STORE\Owner", modified)
        ];
        var service = new SqlServerSharingContactsService(
            _ => Task.FromResult(rows),
            (_, _, _) => Task.FromResult(0));

        var contact = Assert.Single(await service.LoadAsync());

        Assert.Equal(
            new SharingContact(7, "Area Manager", "Management", "manager@example.test", "+919999999999", "DSR", true, @"STORE\Owner", modified),
            contact);
    }

    [Fact]
    public async Task Sharing_save_preserves_draft_reason_cancellation_and_authorization_failure()
    {
        var draft = new SharingContactDraft(4, "Owner", "Owner", "owner@example.test", null, "Daily", true);
        using var cancellation = new CancellationTokenSource();
        SharingContactRow? observed = null;
        string? observedReason = null;
        CancellationToken observedToken = default;
        var service = new SqlServerSharingContactsService(
            _ => Task.FromResult<IReadOnlyList<SharingContactRow>>([]),
            (row, reason, token) =>
            {
                observed = row;
                observedReason = reason;
                observedToken = token;
                return Task.FromException<int>(new UnauthorizedAccessException("Owner permission is required."));
            });

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SaveAsync(draft, "Approved contact correction", cancellation.Token));

        Assert.Equal("Owner permission is required.", exception.Message);
        Assert.Equal(
            new SharingContactRow(4, "Owner", "Owner", "owner@example.test", null, "Daily", true, string.Empty, DateTime.MinValue),
            observed);
        Assert.Equal("Approved contact correction", observedReason);
        Assert.Equal(cancellation.Token, observedToken);
    }

    [Fact]
    public async Task Register_adapter_maps_all_read_fields()
    {
        var date = new DateOnly(2026, 8, 27);
        var modified = new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc);
        IReadOnlyList<RegisterEntryRow> rows =
        [
            new(12, "INWARD", 44, "WLMHW", date, "INV-12", date, "Vendor", 2m, 4999m, "PO-1", "Manager", "VERIFIED", "Received", @"STORE\Manager", modified)
        ];
        var service = new SqlServerDigitalRegisterService(
            (_, _, _) => Task.FromResult(rows),
            (_, _, _) => Task.FromResult(0L));

        var entry = Assert.Single(await service.LoadAsync());

        Assert.Equal(
            new DigitalRegisterEntry(12, "INWARD", 44, "WLMHW", date, "INV-12", date, "Vendor", 2m, 4999m, "PO-1", "Manager", "VERIFIED", "Received", @"STORE\Manager", modified),
            entry);
    }

    [Fact]
    public async Task Register_load_and_save_preserve_query_write_and_cancellation_inputs()
    {
        var draft = new DigitalRegisterEntryDraft(
            "INWARD", 44, "WLMHW", new(2026, 8, 27), "INV-12", null, "Vendor", 2m, 4999m,
            "PO-1", "Manager", "DRAFT", "Received");
        using var cancellation = new CancellationTokenSource();
        string? observedSearch = null;
        var observedLimit = 0;
        RegisterEntryRow? observedEntry = null;
        string? observedReason = null;
        var observedTokens = new List<CancellationToken>();
        var service = new SqlServerDigitalRegisterService(
            (search, limit, token) =>
            {
                observedSearch = search;
                observedLimit = limit;
                observedTokens.Add(token);
                return Task.FromResult<IReadOnlyList<RegisterEntryRow>>([]);
            },
            (entry, reason, token) =>
            {
                observedEntry = entry;
                observedReason = reason;
                observedTokens.Add(token);
                return Task.FromResult(91L);
            });

        await service.LoadAsync("INV-12", 75, cancellation.Token);
        var id = await service.SaveAsync(draft, "Document received", cancellation.Token);

        Assert.Equal("INV-12", observedSearch);
        Assert.Equal(75, observedLimit);
        Assert.Equal(91, id);
        Assert.Equal(
            new RegisterEntryRow(0, "INWARD", 44, "WLMHW", new(2026, 8, 27), "INV-12", null, "Vendor", 2m, 4999m,
                "PO-1", "Manager", "DRAFT", "Received", string.Empty, DateTime.MinValue),
            observedEntry);
        Assert.Equal("Document received", observedReason);
        Assert.Equal([cancellation.Token, cancellation.Token], observedTokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Server=.;Database=EtpReporting;User ID=reporter;Password=not-used")]
    [InlineData("Server=.;Database=EtpReporting;Integrated Security=True;User ID=reporter;Password=not-used")]
    public void Public_adapters_reject_missing_or_sql_authenticated_connections(string connectionString)
    {
        Assert.Throws<ArgumentException>(() => new SqlServerSharingContactsService(connectionString));
        Assert.Throws<ArgumentException>(() => new SqlServerDigitalRegisterService(connectionString));
    }

    [Fact]
    public void Public_adapters_accept_windows_integrated_connections()
    {
        _ = new SqlServerSharingContactsService(IntegratedConnection);
        _ = new SqlServerDigitalRegisterService(IntegratedConnection);
    }
}
