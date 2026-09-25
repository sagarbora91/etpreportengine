using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class StoreCatalogConnectionPolicyTests
{
    [Theory]
    [InlineData("Server=.;Database=EtpPhase0Test_Policy;User ID=reporter;Password=not-used")]
    [InlineData("Server=.;Database=EtpPhase0Test_Policy;Integrated Security=True;User ID=reporter;Password=not-used")]
    [InlineData("Server=remote.example.invalid;Database=EtpPhase0Test_Policy;Integrated Security=True")]
    [InlineData("Server=.;Database=EtpPhase0Test_Policy;Integrated Security=True;AttachDbFilename=C:\\unapproved.mdf")]
    [InlineData("Server=.;Database=EtpPhase0Test_Policy;Integrated Security=True;User Instance=True")]
    [InlineData("Server=.;Database=EtpPhase0Test_Policy;Integrated Security=True;Failover Partner=other")]
    [InlineData("Server=.;Database=EtpPhase0Test_Policy;Integrated Security=True;Encrypt=False")]
    public void Catalogue_rejects_unsafe_connection_options_before_any_query(string connectionString) =>
        Assert.Throws<ArgumentException>(() => new StoreCatalogRepository(connectionString));

    [Fact]
    public void Catalogue_accepts_local_integrated_security_with_explicit_optional_encryption() =>
        _ = new StoreCatalogRepository(@"Server=.\SQLEXPRESS;Database=EtpPhase0Test_Policy;Integrated Security=True;Encrypt=Optional");
}
