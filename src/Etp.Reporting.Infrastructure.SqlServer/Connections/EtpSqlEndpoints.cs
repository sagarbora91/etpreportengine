namespace Etp.Reporting.Infrastructure.SqlServer.Connections;

/// <summary>
/// The process-wide endpoint policy. The composition root sets it once at start, before any connection is made;
/// until then, and in every process that never sets it (tests, headless verbs), it is <see cref="LocalOnlySqlEndpointPolicy"/>.
/// A second <see cref="Configure"/> throws, so no screen or adapter can widen the rule later.
/// </summary>
public static class EtpSqlEndpoints
{
    private static readonly SqlEndpointPolicySlot Slot = new();

    public static ISqlEndpointPolicy Policy => Slot.Policy;

    public static void Configure(ISqlEndpointPolicy policy) => Slot.Configure(policy);

    /// <summary>The settings value (<c>"database": { "endpointPolicy": ... }</c>) as a policy; blank means LocalOnly.</summary>
    public static ISqlEndpointPolicy FromSetting(string? name) => SqlEndpointPolicySlot.FromSetting(name);
}

/// <summary>The set-once holder behind <see cref="EtpSqlEndpoints"/>; separate so its rule can be tested per instance.</summary>
internal sealed class SqlEndpointPolicySlot
{
    internal const string OnlyThisPc = "This ETP build allows only this PC's SQL Server";

    private readonly object gate = new();
    private ISqlEndpointPolicy policy = LocalOnlySqlEndpointPolicy.Instance;
    private bool configured;

    public ISqlEndpointPolicy Policy { get { lock (gate) return policy; } }

    public void Configure(ISqlEndpointPolicy value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (gate)
        {
            if (configured) throw new InvalidOperationException("The SQL endpoint policy is already set for this process.");
            policy = value;
            configured = true;
        }
    }

    public static ISqlEndpointPolicy FromSetting(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Equals(LocalOnlySqlEndpointPolicy.PolicyName, StringComparison.OrdinalIgnoreCase))
            return LocalOnlySqlEndpointPolicy.Instance;
        // 1.12 ships no other policy (decision 44 #6). A later "ApprovedServer" is one class and one case here.
        throw new InvalidOperationException(OnlyThisPc + ".");
    }
}
