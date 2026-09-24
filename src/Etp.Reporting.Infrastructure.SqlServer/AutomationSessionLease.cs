using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>A session lock belongs to its physical connection, so release it before pooling that connection.</summary>
internal sealed class AutomationSessionLease(SqlConnection connection) : IAsyncDisposable
{
    private bool disposed;
    internal const string Resource = "ETP_PHASE2_AUTOMATION";
    public static async Task<AutomationSessionLease?> TryAcquireAsync(string connectionString, CancellationToken token)
    {
        var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using var command = new SqlCommand("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @result;", connection);
            command.Parameters.AddWithValue("@resource", Resource);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) >= 0) return new(connection);
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

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            await using var release = new SqlCommand("DECLARE @result int; EXEC @result=sys.sp_releaseapplock @Resource=@resource,@LockOwner='Session'; SELECT @result;", connection) { CommandTimeout = 10 };
            release.Parameters.AddWithValue("@resource", Resource);
            // Cancellation of the import must not cancel its cleanup.
            if (Convert.ToInt32(await release.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false)) < 0)
                SqlConnection.ClearPool(connection);
        }
        catch { SqlConnection.ClearPool(connection); }
        finally { await connection.DisposeAsync().ConfigureAwait(false); }
    }
}
