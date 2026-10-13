using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop.Composition;

/// <summary>
/// 1.12 startup-sql-wait. Right after a boot or restore the unattended task can start before SQL Server accepts
/// connections; that used to be recorded as a Critical STARTUP_FAILED and failed the next health check (ERR-02,
/// ERR-04). This waits for the first connection, retrying with backoff for up to <see cref="DefaultBudget"/>, but only
/// for errors that mean "the server is not ready yet". Permission and configuration errors are never retried.
/// </summary>
public static class SqlStartupWait
{
    /// <summary>Total time the unattended task waits for SQL Server before giving up.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    /// -1/-2/2/53/233/64/121/10053/10054/10060/10061: server or network not reachable yet. 921/922/927/945: database
    /// starting, recovering or not yet available. 4060: cannot open database (SQL Server raises it, with 18456, while a
    /// database is still recovering). 18456 on its own is a refused login and is NOT retried; neither is any 229, 230,
    /// 262, 297, 300 permission error. A real permission fault on 4060 only costs one wait of the budget.
    /// </summary>
    private static readonly HashSet<int> TransientNumbers = [-1, -2, 2, 53, 64, 121, 233, 921, 922, 927, 945, 4060, 10053, 10054, 10060, 10061];

    public static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not SqlException sql) continue;
            var numbers = sql.Errors.Cast<SqlError>().Select(error => error.Number).Append(sql.Number).ToList();
            return numbers.Count > 0 && numbers.Any(TransientNumbers.Contains);
        }
        return false;
    }

    /// <param name="probe">Opens and closes one connection; throws if SQL Server is not ready.</param>
    /// <param name="delay">Sleeps (fake in tests).</param>
    /// <param name="now">Monotonic clock (fake in tests).</param>
    /// <param name="log">Called once per wait with the SQL error number text and seconds waited so far.</param>
    public static async Task WaitAsync(
        Func<CancellationToken, Task> probe,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<TimeSpan> now,
        Action<string> log,
        TimeSpan? budget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(log);
        var limit = budget ?? DefaultBudget;
        var started = now();
        var wait = FirstDelay;
        while (true)
        {
            try { await probe(cancellationToken).ConfigureAwait(false); return; }
            catch (Exception exception) when (IsTransient(exception))
            {
                var elapsed = now() - started;
                if (elapsed + wait > limit) throw;
                log($"SQL Server is not ready yet (SQL error {FirstNumber(exception)}); waited {(int)elapsed.TotalSeconds}s of {(int)limit.TotalSeconds}s, trying again in {(int)wait.TotalSeconds}s.");
                await delay(wait, cancellationToken).ConfigureAwait(false);
                wait = TimeSpan.FromTicks(Math.Min(MaxDelay.Ticks, wait.Ticks * 2));
            }
        }
    }

    private static int FirstNumber(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql) return sql.Number;
        return 0;
    }

    public static Task ProbeAsync(string connectionString, CancellationToken cancellationToken) =>
        ProbeCoreAsync(new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 10 }.ConnectionString, cancellationToken);

    private static async Task ProbeCoreAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }
}
