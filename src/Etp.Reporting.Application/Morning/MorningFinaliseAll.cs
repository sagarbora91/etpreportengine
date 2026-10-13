using Etp.Reporting.Application.DailyWorkflow;

namespace Etp.Reporting.Application.Morning;

/// <summary>
/// "Finalise all ready stores" (wave plan C-a, decision 2). For each store, in the order given, it does exactly what
/// Close day's Finalise does: generate the day's pack, refuse when a pack section is Blocked, then
/// <see cref="IDailyWorkflowCommands.FinaliseAsync"/> and the <c>DayFinalised</c> audit row. Every store is its own
/// transaction (FinaliseAsync's), so one refusal never undoes or stops another store; the result list says which stores
/// were locked and why the others were not. Cancellation stops before the next store and is rethrown.
/// </summary>
public sealed class MorningFinaliseAll<TDocument> where TDocument : notnull
{
    private readonly Func<IDailyReportPackGenerator<TDocument>> packs;
    private readonly Func<IDailyWorkflowCommands> commands;
    private readonly Func<string, string, string, Task>? audit;
    private readonly Func<Exception, string> describe;

    /// <param name="audit">Records (event type, outcome, detail); a failure to audit never fails the store (as on Close day).</param>
    /// <param name="describe">The plain sentence for a refusal (DesktopFriendlyError in the app).</param>
    public MorningFinaliseAll(
        Func<IDailyReportPackGenerator<TDocument>> packs,
        Func<IDailyWorkflowCommands> commands,
        Func<string, string, string, Task>? audit,
        Func<Exception, string> describe)
    {
        this.packs = packs ?? throw new ArgumentNullException(nameof(packs));
        this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        this.audit = audit;
        this.describe = describe ?? throw new ArgumentNullException(nameof(describe));
    }

    public const string AuditDetail = "Business day finalised from the Morning page";

    public async Task<IReadOnlyList<MorningFinaliseResult>> RunAsync(
        DateOnly businessDate,
        IReadOnlyList<string> storeCodes,
        string user,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storeCodes);
        if (string.IsNullOrWhiteSpace(user)) throw new ArgumentException("The finalising user is required.", nameof(user));
        var results = new List<MorningFinaliseResult>();
        foreach (var store in storeCodes.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(store);
            results.Add(await FinaliseOneAsync(new DailyWorkflowScope(store, businessDate), user, cancellationToken).ConfigureAwait(false));
        }
        return results;
    }

    private async Task<MorningFinaliseResult> FinaliseOneAsync(DailyWorkflowScope scope, string user, CancellationToken cancellationToken)
    {
        try
        {
            var pack = await packs().GenerateAsync(scope, user, cancellationToken).ConfigureAwait(false);
            var blocked = pack.Sections.Where(section => section.Status == DailyControlStatus.Blocked).Select(section => section.Report).ToArray();
            if (blocked.Length > 0)
                return new(scope.StoreCode, false, "Not locked: blocked by " + string.Join(", ", blocked) + ". Open Close day for this store.");
            await commands().FinaliseAsync(new FinaliseDailyWorkflow(scope, user, false), cancellationToken).ConfigureAwait(false);
            if (audit is not null)
            {
                try { await audit("DayFinalised", "Succeeded", AuditDetail).ConfigureAwait(false); }
                catch (Exception exception) when (exception is not OperationCanceledException) { /* the day is locked; the audit write reports itself */ }
            }
            var warnings = pack.Sections.Count(section => section.Status is DailyControlStatus.Failed);
            return new(scope.StoreCode, true, warnings == 0 ? "Locked." : warnings == 1 ? "Locked with 1 warning (see Close day)." : $"Locked with {warnings} warnings (see Close day).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            return new(scope.StoreCode, false, "Not locked: " + describe(exception));
        }
    }
}
