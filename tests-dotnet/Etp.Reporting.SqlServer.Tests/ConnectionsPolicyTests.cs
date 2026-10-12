using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.12 A1: the endpoint policy, the factory purposes and the set-once rule. No database is touched: every refused
/// string fails inside the factory's constructor, before a SqlConnection exists.
/// </summary>
public sealed class ConnectionsPolicyTests
{
    private const string Local = @"Server=.\SQLEXPRESS;Database=Test;Integrated Security=True";

    [Theory]
    [InlineData(@".\SQLEXPRESS")]
    [InlineData("(local)")]
    [InlineData("localhost")]
    [InlineData(@"lpc:.\SQLEXPRESS")]
    [InlineData(@"np:.\SQLEXPRESS")]
    [InlineData(@"np:\\localhost\pipe\MSSQL$SQLEXPRESS\sql\query")]
    [InlineData(@"(localdb)\MSSQLLocalDB")]
    public void LocalOnly_allows_this_computer(string server)
    {
        var decision = LocalOnlySqlEndpointPolicy.Instance.Evaluate(new($"Server={server};Database=Test;Integrated Security=True"));
        Assert.True(decision.Allowed);
        Assert.Null(decision.RefusalMessage);
    }

    [Theory]
    [InlineData("Server=remotehost;Database=Test;Integrated Security=True", "Choose a SQL Server instance on this computer.")]
    [InlineData(@"Server=np:\\remotehost\pipe\sql\query;Database=Test;Integrated Security=True", "Choose a SQL Server instance on this computer.")]
    [InlineData("Server=tcp:localhost,1433;Database=Test;Integrated Security=True", "Choose a SQL Server instance on this computer.")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=False", "Only Windows integrated security without stored credentials can be used.")]
    [InlineData("Server=localhost;Database=Test;User ID=someone;Password=x", "Only Windows integrated security without stored credentials can be used.")]
    [InlineData("Server=localhost;Integrated Security=True", "The SQL Server connection must name a database.")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;AttachDbFilename=C:\\x.mdf", "Attached files, user instances and failover servers cannot be used.")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;Failover Partner=localhost", "Attached files, user instances and failover servers cannot be used.")]
    public void LocalOnly_refuses_with_the_1_11_sentence(string value, string message)
    {
        var decision = LocalOnlySqlEndpointPolicy.Instance.Evaluate(new SqlConnectionStringBuilder(value));
        Assert.False(decision.Allowed);
        Assert.Equal(message, decision.RefusalMessage);
    }

    [Theory]
    [InlineData("Server=remotehost;Database=Test;Integrated Security=True")]
    [InlineData("Server=tcp:localhost,1433;Database=Test;Integrated Security=True")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;UID=secret")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;Encrypt=False")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;Encrypt=no")]
    [InlineData("")]
    [InlineData("not a connection string")]
    public void Factory_refuses_before_any_connection_exists(string value)
    {
        var policy = new CountingPolicy(LocalOnlySqlEndpointPolicy.Instance);
        var error = Assert.Throws<ArgumentException>(() => new EtpConnectionFactory(value, policy));
        Assert.DoesNotContain("secret", error.Message);
        // The facade gives the same refusal, so the 1.11 call sites and new code agree.
        Assert.Throws<ArgumentException>(() => LocalSqlConnectionPolicy.Validate(value));
    }

    [Fact]
    public void Factory_purposes_set_timeouts_pooling_and_catalog()
    {
        var factory = new EtpConnectionFactory(Local + ";Connect Timeout=300", LocalOnlySqlEndpointPolicy.Instance);
        Assert.Equal("Test", factory.DatabaseName);

        var normal = new SqlConnectionStringBuilder(factory.ConnectionStringFor(EtpConnectionPurpose.Default));
        Assert.Equal(5, normal.ConnectTimeout);
        Assert.True(normal.Pooling);
        Assert.Equal(SqlConnectionEncryptOption.Optional, normal.Encrypt);
        Assert.False(normal.TrustServerCertificate);

        var budget = new SqlConnectionStringBuilder(factory.ConnectionStringFor(EtpConnectionPurpose.CommitBudget));
        Assert.Equal(LocalSqlConnectionPolicy.CommitBudgetSeconds, budget.ConnectTimeout);
        Assert.True(budget.Pooling);

        // The commit check is never pooled: it must not reuse the session whose COMMIT reply was lost.
        var check = new SqlConnectionStringBuilder(factory.ConnectionStringFor(EtpConnectionPurpose.CommitCheck));
        Assert.False(check.Pooling);
        Assert.Equal(LocalSqlConnectionPolicy.CommitCheckConnectSeconds, check.ConnectTimeout);
        Assert.True(check.ConnectTimeout < LocalSqlConnectionPolicy.CommitBudgetSeconds);

        var maintenance = new SqlConnectionStringBuilder(factory.ConnectionStringFor(EtpConnectionPurpose.Maintenance));
        Assert.Equal("master", maintenance.InitialCatalog);
        Assert.False(maintenance.Pooling);
        Assert.Equal(5, maintenance.ConnectTimeout);
        Assert.Equal("Test", factory.DatabaseName);

        using var created = factory.Create(EtpConnectionPurpose.CommitCheck);
        Assert.Equal(System.Data.ConnectionState.Closed, created.State);
        Assert.False(new SqlConnectionStringBuilder(created.ConnectionString).Pooling);
    }

    [Fact]
    public void Factory_strings_match_the_facade()
    {
        var factory = new EtpConnectionFactory(Local, LocalOnlySqlEndpointPolicy.Instance);
        Assert.Equal(LocalSqlConnectionPolicy.Validate(Local), factory.ConnectionString);
        Assert.Equal(LocalSqlConnectionPolicy.ValidateWithCommitBudget(Local), factory.ConnectionStringFor(EtpConnectionPurpose.CommitBudget));
        Assert.Equal(LocalSqlConnectionPolicy.ValidateForCommitCheck(Local), factory.ConnectionStringFor(EtpConnectionPurpose.CommitCheck));
        // A validated string validates to itself (settings are saved validated and read back).
        Assert.Equal(factory.ConnectionString, new EtpConnectionFactory(factory.ConnectionString, LocalOnlySqlEndpointPolicy.Instance).ConnectionString);
    }

    [Fact]
    public void Factory_asks_its_own_policy_and_obeys_a_refusal()
    {
        var refusing = new CountingPolicy(new RefuseAll());
        var error = Assert.Throws<ArgumentException>(() => new EtpConnectionFactory(Local, refusing));
        Assert.Equal(RefuseAll.Message, error.Message);
        Assert.Equal(1, refusing.Calls);
    }

    [Fact]
    public void Configure_twice_throws()
    {
        var slot = new SqlEndpointPolicySlot();
        Assert.Same(LocalOnlySqlEndpointPolicy.Instance, slot.Policy);
        var chosen = new CountingPolicy(LocalOnlySqlEndpointPolicy.Instance);
        slot.Configure(chosen);
        Assert.Same(chosen, slot.Policy);
        Assert.Throws<InvalidOperationException>(() => slot.Configure(LocalOnlySqlEndpointPolicy.Instance));
        Assert.Same(chosen, slot.Policy);
        Assert.Throws<ArgumentNullException>(() => new SqlEndpointPolicySlot().Configure(null!));
    }

    [Fact]
    public void Process_default_is_LocalOnly()
    {
        // Nothing in a test process configures the policy.
        Assert.Equal(LocalOnlySqlEndpointPolicy.PolicyName, EtpSqlEndpoints.Policy.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LocalOnly")]
    [InlineData(" localonly ")]
    public void Setting_LocalOnly_or_blank_is_LocalOnly(string? name) =>
        Assert.Same(LocalOnlySqlEndpointPolicy.Instance, EtpSqlEndpoints.FromSetting(name));

    [Theory]
    [InlineData("ApprovedServer")]
    [InlineData("Any")]
    public void Any_other_setting_is_refused_in_1_12(string name)
    {
        var error = Assert.Throws<InvalidOperationException>(() => EtpSqlEndpoints.FromSetting(name));
        Assert.Equal("This ETP build allows only this PC's SQL Server.", error.Message);
    }

    private sealed class CountingPolicy(ISqlEndpointPolicy inner) : ISqlEndpointPolicy
    {
        public int Calls { get; private set; }
        public string Name => inner.Name;
        public SqlEndpointDecision Evaluate(SqlConnectionStringBuilder candidate) { Calls++; return inner.Evaluate(candidate); }
    }

    private sealed class RefuseAll : ISqlEndpointPolicy
    {
        public const string Message = "Refused by the test policy.";
        public string Name => "RefuseAll";
        public SqlEndpointDecision Evaluate(SqlConnectionStringBuilder candidate) => SqlEndpointDecision.Refuse(Message);
    }
}
