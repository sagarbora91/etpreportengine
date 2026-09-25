using Etp.Reporting.Application.Registers;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseFiveDigitalRegisterBoundaryTests(SqlDatabaseFixture db) : IClassFixture<SqlDatabaseFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inactive_account_is_refused_before_register_or_day_reads(bool dayOnly)
    {
        await SetAccessAsync("OWNER", false);
        if (dayOnly)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => SqlServerDigitalRegisterService.LoadDayAsync(db.ConnectionString, "F16", new(2026, 8, 25)));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SqlServerDigitalRegisterService(db.ConnectionString).LoadAsync());
    }

    [Theory]
    [InlineData("VIEWER", true, "DRAFT")]
    [InlineData("STORE_MANAGER", true, "VERIFIED")]
    [InlineData("STORE_MANAGER", true, " verified ")]
    [InlineData("OWNER", false, "DRAFT")]
    public async Task Register_save_rechecks_role_and_refuses_unauthorized_writes(string role, bool active, string status)
    {
        var number = Guid.NewGuid().ToString("N");
        await db.ExecuteAsync($"EXEC dbo.save_register_entry @type='INWARD',@store='F16',@date='20260825',@number='{number}',@counterparty=N'Private supplier',@verification='DRAFT',@reason=N'Fixture setup';");
        await SetAccessAsync(role, active);
        var service = new SqlServerDigitalRegisterService(db.ConnectionString);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(Draft(number, status), "Attempted change"));
        Assert.Equal("DRAFT", await db.ExecuteAsync($"SELECT verification_status FROM dbo.register_entries WHERE document_number='{number}'"));
        Assert.Equal("Private supplier", await db.ExecuteAsync($"SELECT counterparty FROM dbo.register_entries WHERE document_number='{number}'"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("PENDING")]
    [InlineData("VERIFIED            suffix")]
    [InlineData("DRAFT               suffix")]
    public async Task Unknown_or_SQL_truncated_status_is_rejected_before_authorization_and_storage(string status)
    {
        var number = Guid.NewGuid().ToString("N");
        await SetAccessAsync("STORE_MANAGER", true);
        await Assert.ThrowsAsync<ArgumentException>(() => new SqlServerDigitalRegisterService(db.ConnectionString).SaveAsync(Draft(number, status), "Invalid status"));
        Assert.Equal(0, await db.ExecuteAsync($"SELECT COUNT(*) FROM dbo.register_entries WHERE document_number='{number}'"));
    }

    [Fact]
    public async Task Viewer_reads_manager_saves_draft_and_Owner_verifies_with_fresh_access_each_time()
    {
        var number = Guid.NewGuid().ToString("N");
        var service = new SqlServerDigitalRegisterService(db.ConnectionString);
        await SetAccessAsync("STORE_MANAGER", true);
        var id = await service.SaveAsync(Draft(number, " draft "), "Draft received");
        await SetAccessAsync("VIEWER", true);
        Assert.Equal("DRAFT", Assert.Single(await service.LoadAsync(number)).VerificationStatus);
        Assert.Contains(await SqlServerDigitalRegisterService.LoadDayAsync(db.ConnectionString, "F16", new(2026, 8, 25)), entry => entry.Id == id);
        await SetAccessAsync("OWNER", true);
        Assert.Equal(id, await service.SaveAsync(Draft(number, " verified "), "Evidence reviewed"));
        Assert.Equal("VERIFIED", Assert.Single(await service.LoadAsync(number)).VerificationStatus);
        await SetAccessAsync("VIEWER", true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(Draft(Guid.NewGuid().ToString("N"), "DRAFT"), "Role revoked"));
    }

    private Task<object?> SetAccessAsync(string role, bool active) => db.ExecuteAsync($"""
        IF NOT EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=N'F16\FixtureOwner')
          INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
          VALUES(N'F16\FixtureOwner',N'Synthetic backup Owner','OWNER',1,SUSER_SNAME(),N'Preserve the last-Owner guard in the role fixture');
        UPDATE dbo.application_users SET role_code='{role}',is_active={(active ? 1 : 0)} WHERE windows_identity=SUSER_SNAME();
        """);

    private static DigitalRegisterEntryDraft Draft(string number, string status) =>
        new("INWARD", null, "F16", new(2026, 8, 25), number, null, "Changed supplier", 1m, 12m, null, "Fixture", status, null);
}
