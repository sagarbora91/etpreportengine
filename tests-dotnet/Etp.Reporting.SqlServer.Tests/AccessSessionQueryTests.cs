using Etp.Reporting.Application.Access;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class AccessSessionQueryTests
{
    public static TheoryData<AccessRole, bool, bool, bool, bool> Permissions => new()
    {
        { AccessRole.None, true, false, false, false },
        { AccessRole.Viewer, true, true, false, false },
        { AccessRole.StoreManager, true, true, true, false },
        { AccessRole.Owner, true, true, true, true },
        { AccessRole.Owner, false, false, false, false }
    };

    [Theory]
    [MemberData(nameof(Permissions))]
    public void Application_contract_centralizes_effective_permissions(
        AccessRole role,
        bool active,
        bool canView,
        bool canImport,
        bool canAdminister)
    {
        var session = new AccessSession(@"STORE\User", "User", role, active);

        Assert.Equal(role is not AccessRole.None, session.HasAssignedRole);
        Assert.Equal(canView, session.CanView);
        Assert.Equal(canImport, session.CanImport);
        Assert.Equal(canImport, session.CanEnterOperations);
        Assert.Equal(canAdminister, session.CanAdminister);
    }

    public static TheoryData<ApplicationRole, AccessRole> RoleMappings => new()
    {
        { ApplicationRole.None, AccessRole.None },
        { ApplicationRole.Viewer, AccessRole.Viewer },
        { ApplicationRole.StoreManager, AccessRole.StoreManager },
        { ApplicationRole.Owner, AccessRole.Owner },
        { (ApplicationRole)999, AccessRole.None }
    };

    [Theory]
    [MemberData(nameof(RoleMappings))]
    public void Sql_adapter_maps_infrastructure_roles_centrally(
        ApplicationRole infrastructureRole,
        AccessRole expected)
    {
        Assert.Equal(expected, SqlServerAccessSessionQuery.MapRole(infrastructureRole));
    }

    [Fact]
    public void Sql_adapter_preserves_identity_display_name_and_active_state()
    {
        var result = SqlServerAccessSessionQuery.Map(
            new ApplicationAccess(@"STORE\Manager", "Store Manager", ApplicationRole.StoreManager, true));

        Assert.Equal(@"STORE\Manager", result.WindowsIdentity);
        Assert.Equal("Store Manager", result.DisplayName);
        Assert.Equal(AccessRole.StoreManager, result.Role);
        Assert.True(result.IsActive);
    }

    [Theory]
    [InlineData(@"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True")]
    [InlineData(@"Server=.\SQLEXPRESS;Database=EtpReporting;Trusted_Connection=True;TrustServerCertificate=True")]
    public void Sql_adapter_accepts_windows_integrated_connections(string connectionString)
    {
        _ = new SqlServerAccessSessionQuery(connectionString);
    }

    [Fact]
    public void Sql_adapter_rejects_sql_authentication()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SqlServerAccessSessionQuery(
            @"Server=.\SQLEXPRESS;Database=EtpReporting;User ID=reporter;Password=not-used"));

        Assert.Contains("Windows-integrated", exception.Message, StringComparison.Ordinal);
    }
}
