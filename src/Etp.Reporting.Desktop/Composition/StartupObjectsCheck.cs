using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop.Composition;

/// <summary>
/// 1.12 S1. The interactive start refuses a database that setup has not brought up to this installation (a pending
/// migration or object file), with A3's message, instead of opening onto screens that fail one by one. The headless
/// verbs and setup already refuse (<c>--plan-database-changes</c>). The check never blocks a start it cannot judge:
/// an unreachable server, a missing right to read object definitions (the planner would see every object as changed),
/// a time-out or any error lets the window open, where the welcome overlay explains connection problems.
/// </summary>
public static class StartupObjectsCheck
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private const string CanReadDefinitionsSql =
        "SELECT CONVERT(bit,CASE WHEN HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION')=1 THEN 1 ELSE 0 END);";

    /// <summary>The refusal text for a plan, or null when nothing is pending.</summary>
    public static string? Describe(DatabaseChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.HasChanges) return null;
        var parts = new List<string>();
        if (plan.PendingMigrations.Count > 0) parts.Add(Count(plan.PendingMigrations.Count, "database change"));
        if (plan.PendingObjects.Count > 0) parts.Add(Count(plan.PendingObjects.Count, "report or view update"));
        return SchemaObjectsNotUpToDateException.Reason + " (" + string.Join(" and ", parts) + " waiting.)";
    }

    private static string Count(int count, string noun) =>
        count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? "" : "s");

    /// <param name="canInspect">True when this login may read object definitions.</param>
    /// <param name="plan">A3's planner (read-only).</param>
    /// <param name="skipped">Told why the check could not judge (not for time-outs by the caller's own token).</param>
    public static async Task<string?> RefusalAsync(
        Func<CancellationToken, Task<bool>> canInspect,
        Func<CancellationToken, Task<DatabaseChangePlan>> plan,
        Action<Exception>? skipped = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canInspect);
        ArgumentNullException.ThrowIfNull(plan);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout ?? Timeout);
        try
        {
            if (!await canInspect(limit.Token).ConfigureAwait(false)) return null;
            return Describe(await plan(limit.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            skipped?.Invoke(exception);
            return null;
        }
    }

    public static Task<string?> RefusalAsync(string connectionString, string migrationDirectory,
        Action<Exception>? skipped = null, CancellationToken cancellationToken = default) =>
        RefusalAsync(
            async token =>
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(token).ConfigureAwait(false);
                await using var command = new SqlCommand(CanReadDefinitionsSql, connection);
                return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
            },
            token => DatabaseChangePlanner.PlanAsync(connectionString, migrationDirectory, token),
            skipped, null, cancellationToken);
}
