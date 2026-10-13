using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Application.Morning;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 U1 "Finalise all ready stores": Close day's steps per store, one store's refusal never stops another, a Blocked
/// pack section is refused before anything is locked, cancellation stops before the next store, audit failures never
/// undo a lock. The SQL proof (locks and the lock trigger) is MorningFinaliseAllSqlTests.
/// </summary>
public sealed class MorningFinaliseAllTests
{
    private static readonly DateOnly Day = new(2026, 10, 9);

    [Fact]
    public async Task Each_ready_store_is_generated_finalised_and_audited_in_order()
    {
        var fake = new FakeWorkflow();
        var audits = new List<string>();
        var finaliser = new MorningFinaliseAll<string>(() => fake, () => fake, (type, outcome, detail) => { audits.Add($"{type}|{outcome}|{detail}"); return Task.CompletedTask; }, e => e.Message);

        var results = await finaliser.RunAsync(Day, ["SC901", "SC902"], "SYNTH\\owner");

        Assert.Equal(["SC901", "SC902"], fake.Finalised.Select(command => command.Scope.StoreCode));
        Assert.All(fake.Finalised, command => Assert.Equal((Day, "SYNTH\\owner", false), (command.Scope.BusinessDate, command.User, command.HasBlockingReconciliationExceptions)));
        Assert.Equal(["SC901", "SC902"], fake.Generated);
        Assert.All(results, result => Assert.True(result.Locked));
        Assert.Equal("Locked.", results[0].Message);
        Assert.Equal(2, audits.Count);
        Assert.All(audits, audit => Assert.Equal("DayFinalised|Succeeded|" + MorningFinaliseAll<string>.AuditDetail, audit));
    }

    [Fact]
    public async Task A_refusal_or_a_blocked_section_leaves_that_store_open_and_the_others_still_lock()
    {
        var fake = new FakeWorkflow();
        fake.Sections["SC902"] = [new("Sales lines", DailyControlStatus.Blocked, null, null, "missing")];
        fake.Refuse["SC903"] = new InvalidOperationException("Required source reports and manual inputs must be complete before finalisation.");
        var finaliser = new MorningFinaliseAll<string>(() => fake, () => fake, null, e => "plain: " + e.Message);

        var results = await finaliser.RunAsync(Day, ["SC901", "SC902", "SC903", "SC904"], "SYNTH\\owner");

        Assert.Equal(["SC901", "SC904"], fake.Finalised.Select(command => command.Scope.StoreCode));
        Assert.Equal([true, false, false, true], results.Select(result => result.Locked));
        Assert.Equal("Not locked: blocked by Sales lines. Open Close day for this store.", results[1].Message);
        Assert.StartsWith("Not locked: plain: Required source reports", results[2].Message);
    }

    [Fact]
    public async Task Warnings_lock_and_are_counted_and_a_failed_audit_never_undoes_the_lock()
    {
        var fake = new FakeWorkflow();
        fake.Sections["SC901"] = [new("Tender", DailyControlStatus.Failed, 10m, 2m, "variance"), new("Cash", DailyControlStatus.Passed, 1m, 0m, "ok")];
        var finaliser = new MorningFinaliseAll<string>(() => fake, () => fake, (_, _, _) => throw new TimeoutException("audit"), e => e.Message);

        var result = Assert.Single(await finaliser.RunAsync(Day, ["SC901"], "SYNTH\\owner"));

        Assert.True(result.Locked);
        Assert.Equal("Locked with 1 warning (see Close day).", result.Message);
    }

    [Fact]
    public async Task Cancellation_stops_before_the_next_store_and_is_not_reported_as_a_refusal()
    {
        using var cancel = new CancellationTokenSource();
        var fake = new FakeWorkflow { AfterFinalise = cancel.Cancel };
        var finaliser = new MorningFinaliseAll<string>(() => fake, () => fake, null, e => e.Message);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => finaliser.RunAsync(Day, ["SC901", "SC902"], "SYNTH\\owner", null, cancel.Token));
        Assert.Equal(["SC901"], fake.Finalised.Select(command => command.Scope.StoreCode));
    }

    [Fact]
    public async Task Blank_and_repeated_store_codes_are_finalised_once_and_progress_names_each_store()
    {
        var fake = new FakeWorkflow();
        var seen = new List<string>();
        var finaliser = new MorningFinaliseAll<string>(() => fake, () => fake, null, e => e.Message);

        var results = await finaliser.RunAsync(Day, ["SC901", " ", "sc901", "SC902"], "SYNTH\\owner", new SynchronousProgress(seen));

        Assert.Equal(["SC901", "SC902"], results.Select(result => result.StoreCode));
        Assert.Equal(["SC901", "SC902"], seen);
        await Assert.ThrowsAsync<ArgumentException>(() => finaliser.RunAsync(Day, ["SC901"], " "));
    }

    private sealed class SynchronousProgress(List<string> seen) : IProgress<string>
    {
        public void Report(string value) => seen.Add(value);
    }

    private sealed class FakeWorkflow : IDailyWorkflowCommands, IDailyReportPackGenerator<string>
    {
        public List<string> Generated { get; } = [];
        public List<FinaliseDailyWorkflow> Finalised { get; } = [];
        public Dictionary<string, DailyPackSection[]> Sections { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Exception> Refuse { get; } = new(StringComparer.Ordinal);
        public Action? AfterFinalise { get; init; }

        public Task<DailyPackGeneration<string>> GenerateAsync(DailyWorkflowScope scope, string? generatedBy = null, CancellationToken cancellationToken = default)
        {
            Generated.Add(scope.StoreCode);
            var sections = Sections.TryGetValue(scope.StoreCode, out var configured) ? configured : [new DailyPackSection("Sales", DailyControlStatus.Passed, 1m, 0m, "ok")];
            return Task.FromResult(new DailyPackGeneration<string>(scope.StoreCode, scope.BusinessDate, DailyControlStatus.Passed, sections, "ok",
                DateTimeOffset.UnixEpoch, "pack", 1, new string('a', 64)));
        }

        public Task FinaliseAsync(FinaliseDailyWorkflow command, CancellationToken cancellationToken = default)
        {
            if (Refuse.TryGetValue(command.Scope.StoreCode, out var refusal)) return Task.FromException(refusal);
            Finalised.Add(command);
            AfterFinalise?.Invoke();
            return Task.CompletedTask;
        }

        public Task<string> GenerateCombinedAsync(DateOnly businessDate, string? generatedBy = null, CancellationToken cancellationToken = default) => Task.FromResult("combined");
        public Task SaveManualInputAsync(SaveDailyManualInput command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveStockCountAsync(SaveDailyStockCount command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveStaffTargetAsync(SaveDailyStaffTarget command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReopenAsync(ReopenDailyWorkflow command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
