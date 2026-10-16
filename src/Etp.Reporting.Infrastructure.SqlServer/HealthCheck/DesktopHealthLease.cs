using System.Globalization;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

/// <summary>
/// The automation lease for health runs: the SQL application lock <c>ETP_PHASE2_AUTOMATION</c> that
/// <c>AutomatedOperationsService</c> takes for each import run (<c>AutomationSessionLease</c>), session-owned, never
/// waited for. The manual "Run health check" takes it too, so a manual run started while the nightly run is active is
/// refused ("A health check is already running"). Moved from the Desktop in 1.12 (A1); the schedule stays there.
/// </summary>
public static class NightlyHealthLease
{
    /// <summary>Must equal <c>AutomationSessionLease.Resource</c>.</summary>
    public const string Resource = "ETP_PHASE2_AUTOMATION";

    /// <summary>
    /// True when an import batch started within the last 4 hours is still Pending or Processing
    /// (<c>open_imports</c> of <c>dbo.load_database_operational_health</c>, which the automation account may run). A batch
    /// older than 4 hours is stale (a crashed import) and never blocks. No row means no import.
    /// <para>
    /// 1.12.1 review M-2: any failure (timeout behind a running import's locks, connection, permission) <b>throws</b>. It is
    /// never "no import": the update gate maps the throw to <c>DatabaseUnreadable</c> ("try again in a minute"), and the
    /// nightly health schedule catches it itself and goes ahead, as before.
    /// </para>
    /// </summary>
    public static async Task<bool> ImportIsOpenAsync(IEtpConnectionFactory connections, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connections);
        await using var connection = await connections.OpenAsync(EtpConnectionPurpose.Default, cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand("EXEC dbo.load_database_operational_health;", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return false;
        var ordinal = reader.GetOrdinal("open_imports");
        return !reader.IsDBNull(ordinal) && Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>The lease, or null when another run holds it. Disposing releases it.</summary>
    public static async Task<IAsyncDisposable?> TryAcquireAsync(IEtpConnectionFactory connections, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var connection = connections.Create(EtpConnectionPurpose.Default);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @result;",
                connection);
            command.Parameters.Add(new SqlParameter("@resource", System.Data.SqlDbType.NVarChar, 255) { Value = Resource });
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) >= 0)
                return new Lease(connection);
            await connection.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch
        {
            SqlConnection.ClearPool(connection);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Lease(SqlConnection connection) : IAsyncDisposable
    {
        private bool disposed;

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                await using var release = new SqlCommand(
                    "DECLARE @result int; EXEC @result=sys.sp_releaseapplock @Resource=@resource,@LockOwner='Session'; SELECT @result;",
                    connection) { CommandTimeout = 10 };
                release.Parameters.Add(new SqlParameter("@resource", System.Data.SqlDbType.NVarChar, 255) { Value = Resource });
                // A cancelled health run must still release the lock before the connection goes back to the pool.
                if (Convert.ToInt32(await release.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false), CultureInfo.InvariantCulture) < 0)
                    SqlConnection.ClearPool(connection);
            }
            catch { SqlConnection.ClearPool(connection); }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
