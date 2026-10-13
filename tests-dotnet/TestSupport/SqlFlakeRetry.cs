#pragma warning disable CS0618 // XunitTestCase's parameterless constructor exists only for the de-serializer.
#pragma warning disable CA2255 // Module initializer: the SqlClient networking switch must be set before any connection exists.
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit.Abstractions;
using Xunit.Sdk;

// Linked into Etp.Reporting.SqlServer.IntegrationTests only (the discoverer attribute names that assembly).
namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// The known SQL Server 2025 / shared-memory transport flake (about 1 run in 4, a different test each time, raised while a
/// connection is being opened). A test marked [SqlFact] or [SqlTheory] is run again ONCE, and only when it failed with a
/// SqlException whose message says "transport-level error" raised inside SqlConnection.Open. Nothing else is retried: a wrong
/// answer, a timeout or a different SQL error fails at once. Every retry is logged as "ETP-FLAKE-RETRY &lt;test&gt;" to
/// etp-flake-retry.log (directory: ETP_FLAKE_LOG_DIRECTORY, default the test output folder) and to the console; the gate fails
/// a run with more than 2 of them (architecture A2).
/// </summary>
[XunitTestCaseDiscoverer("Etp.Reporting.SqlServer.IntegrationTests.SqlFactDiscoverer", "Etp.Reporting.SqlServer.IntegrationTests")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlFactAttribute : FactAttribute { }

[XunitTestCaseDiscoverer("Etp.Reporting.SqlServer.IntegrationTests.SqlTheoryDiscoverer", "Etp.Reporting.SqlServer.IntegrationTests")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlTheoryAttribute : TheoryAttribute { }

public sealed class SqlFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink diagnosticMessageSink;
    public SqlFactDiscoverer(IMessageSink diagnosticMessageSink) => this.diagnosticMessageSink = diagnosticMessageSink;

    public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
    {
        yield return new SqlRetryTestCase(diagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
    }
}

public sealed class SqlTheoryDiscoverer : TheoryDiscoverer
{
    public SqlTheoryDiscoverer(IMessageSink diagnosticMessageSink) : base(diagnosticMessageSink) { }

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow)
    {
        yield return new SqlRetryTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, dataRow);
    }

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute)
    {
        yield return new SqlRetryTheoryTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
    }
}

public sealed class SqlRetryTestCase : XunitTestCase
{
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public SqlRetryTestCase() { }

    public SqlRetryTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, object[]? testMethodArguments = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments) { }

    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        SqlFlakeRetry.RunAsync(DisplayName, messageBus, aggregator,
            (bus, copy) => RunOnceAsync(diagnosticMessageSink, bus, constructorArguments, copy, cancellationTokenSource));

    private Task<RunSummary> RunOnceAsync(IMessageSink sink, IMessageBus bus, object[] arguments, ExceptionAggregator aggregator, CancellationTokenSource cancellation) =>
        base.RunAsync(sink, bus, arguments, aggregator, cancellation);
}

public sealed class SqlRetryTheoryTestCase : XunitTheoryTestCase
{
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public SqlRetryTheoryTestCase() { }

    public SqlRetryTheoryTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod) { }

    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        SqlFlakeRetry.RunAsync(DisplayName, messageBus, aggregator,
            (bus, copy) => RunOnceAsync(diagnosticMessageSink, bus, constructorArguments, copy, cancellationTokenSource));

    private Task<RunSummary> RunOnceAsync(IMessageSink sink, IMessageBus bus, object[] arguments, ExceptionAggregator aggregator, CancellationTokenSource cancellation) =>
        base.RunAsync(sink, bus, arguments, aggregator, cancellation);
}

/// <summary>The retry rule, the retry log and the counter, shared by the test cases and the SQL fixture.</summary>
public static class SqlFlakeRetry
{
    private static readonly Regex OpenFrame = new(@"SqlConnection\.\W*(Try)?Open|SqlInternalConnectionTds", RegexOptions.Compiled);
    private static readonly object LogLock = new();
    private static int retries;

    /// <summary>ETP_FLAKE_NO_RETRY=1 turns the retry off, so the flake experiments count raw failures.</summary>
    public static bool Disabled => Environment.GetEnvironmentVariable("ETP_FLAKE_NO_RETRY") == "1";

    /// <summary>Number of ETP-FLAKE-RETRY lines written by this process.</summary>
    public static int RetryCount => Volatile.Read(ref retries);

    /// <summary>True for a SqlException "transport-level error" raised while a connection was being opened.</summary>
    public static bool IsOpenTransportError(IEnumerable<string?> messages, IEnumerable<string?> stackTraces) =>
        messages.Any(m => m is not null && m.Contains("transport-level error", StringComparison.OrdinalIgnoreCase)) &&
        stackTraces.Any(s => s is not null && OpenFrame.IsMatch(s));

    public static bool IsOpenTransportError(Exception exception)
    {
        var text = exception.ToString();
        return IsOpenTransportError(new[] { text }, new[] { text });
    }

    public static void LogRetry(string what)
    {
        Interlocked.Increment(ref retries);
        var line = $"{DateTime.UtcNow:O} ETP-FLAKE-RETRY {what}";
        try { Console.Error.WriteLine(line); } catch (Exception) { /* a closed console must not fail a test */ }
        try
        {
            var directory = Environment.GetEnvironmentVariable("ETP_FLAKE_LOG_DIRECTORY");
            if (string.IsNullOrWhiteSpace(directory)) directory = AppContext.BaseDirectory;
            Directory.CreateDirectory(directory);
            lock (LogLock) File.AppendAllText(Path.Combine(directory, "etp-flake-retry.log"), line + Environment.NewLine);
        }
        catch (Exception) { /* the console line above is still in the test log */ }
    }

    internal static async Task<RunSummary> RunAsync(string testName, IMessageBus realBus, ExceptionAggregator aggregator,
        Func<IMessageBus, ExceptionAggregator, Task<RunSummary>> runOnce)
    {
        var first = new BufferingMessageBus();
        var summary = await runOnce(first, new ExceptionAggregator(aggregator)).ConfigureAwait(false);
        if (summary.Failed == 0 || !first.SawOpenTransportError || Disabled)
        {
            first.FlushTo(realBus);
            return summary;
        }
        LogRetry(testName);
        var second = new BufferingMessageBus();
        summary = await runOnce(second, new ExceptionAggregator(aggregator)).ConfigureAwait(false);
        second.FlushTo(realBus);
        return summary;
    }

    /// <summary>Holds one attempt's messages so a failed first attempt can be dropped without a trace in the results.</summary>
    private sealed class BufferingMessageBus : IMessageBus
    {
        private readonly List<IMessageSinkMessage> messages = new();
        public bool SawOpenTransportError { get; private set; }

        public bool QueueMessage(IMessageSinkMessage message)
        {
            lock (messages)
            {
                messages.Add(message);
                if (message is ITestFailed failed && IsOpenTransportError(failed.Messages, failed.StackTraces)) SawOpenTransportError = true;
            }
            return true;
        }

        public void FlushTo(IMessageBus bus)
        {
            lock (messages) foreach (var message in messages) bus.QueueMessage(message);
        }

        public void Dispose() { }
    }
}

/// <summary>
/// Evidence for the flake experiments (ETP_FLAKE_TRACE=1): thread id at every fixture open, the SqlClient pool counters,
/// and the test that was running. Off by default; one line per event in etp-flake-trace.log beside the retry log.
/// </summary>
public static class SqlFlakeTrace
{
    private static readonly object TraceLock = new();
    private static PoolCounters? counters;

    public static bool Enabled => Environment.GetEnvironmentVariable("ETP_FLAKE_TRACE") == "1";

    /// <summary>The test now running, for tests that opt in (the integration collections run one at a time).</summary>
    public static string CurrentTest { get; set; } = "";

    public static void Write(string what)
    {
        if (!Enabled) return;
        try
        {
            counters ??= new PoolCounters();
            var directory = Environment.GetEnvironmentVariable("ETP_FLAKE_LOG_DIRECTORY");
            if (string.IsNullOrWhiteSpace(directory)) directory = AppContext.BaseDirectory;
            Directory.CreateDirectory(directory);
            var line = $"{DateTime.UtcNow:O} thread={Environment.CurrentManagedThreadId} pool={(Thread.CurrentThread.IsThreadPoolThread ? "y" : "n")} test=[{CurrentTest}] {what} counters={counters.Snapshot()}";
            lock (TraceLock) File.AppendAllText(Path.Combine(directory, "etp-flake-trace.log"), line + Environment.NewLine);
        }
        catch (Exception) { /* tracing must never fail a test */ }
    }

    private sealed class PoolCounters : EventListener
    {
        private readonly ConcurrentDictionary<string, string> latest = new();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Microsoft.Data.SqlClient.EventSource")
                EnableEvents(eventSource, EventLevel.Informational, EventKeywords.All,
                    new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "1" });
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName != "EventCounters" || eventData.Payload is null) return;
            foreach (var item in eventData.Payload)
            {
                if (item is not IDictionary<string, object> fields || !fields.TryGetValue("Name", out var name)) continue;
                var value = fields.TryGetValue("Mean", out var mean) ? mean : fields.TryGetValue("Increment", out var increment) ? increment : null;
                latest[name?.ToString() ?? "?"] = value?.ToString() ?? "";
            }
        }

        public string Snapshot() => string.Join(',', latest.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value));
    }
}

internal static class SqlClientSwitches
{
    // Variant (d) of the flake experiment: managed SNI instead of the native one (needs TCP, so ETP_TEST_SQL_CONNECTION must point at
    // a local instance with TCP enabled, such as localhost\ETPTEST). Tests only; the shipped app never sets it.
    [ModuleInitializer]
    internal static void ApplyFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("ETP_TEST_MANAGED_SNI") == "1")
            AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);
    }
}
