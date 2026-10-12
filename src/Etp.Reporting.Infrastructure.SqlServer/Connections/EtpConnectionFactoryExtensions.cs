namespace Etp.Reporting.Infrastructure.SqlServer.Connections;

/// <summary>Bridges from the factory to the 1.11 adapters that still take a connection string (they validate it again).</summary>
public static class EtpConnectionFactoryExtensions
{
    /// <summary>
    /// The factory's validated Default string. Use it only to construct a 1.11 adapter whose constructor takes a
    /// string; new code calls <see cref="IEtpConnectionFactory.Create"/> or <see cref="IEtpConnectionFactory.OpenAsync"/>.
    /// </summary>
    public static string LegacyConnectionString(this IEtpConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (factory is EtpConnectionFactory concrete) return concrete.ConnectionString;
        using var connection = factory.Create();
        return connection.ConnectionString;
    }
}
