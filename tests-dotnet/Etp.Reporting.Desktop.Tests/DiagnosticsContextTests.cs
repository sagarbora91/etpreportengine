using System.Diagnostics;
using System.Text.Json;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.9 lane DIAG-CONTEXT. IE-CODE-03: an entry says where it failed (operation, report code, store, business date,
/// type and method names of the failing frames) without personal data, and the user sees its reference. IE-CODE-11:
/// fire-and-forget work is observed. IE-RT-04: a typing mistake in Settings > Users is a Warning.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class DiagnosticsContextTests
{
    [Fact]
    public void Record_returns_a_short_reference_that_is_the_entrys_correlation_id()
    {
        WithDirectory(directory =>
        {
            var reference = DesktopDiagnostics.Record(null, "Tests", "REFERENCE_CHECK", logDirectory: directory);

            Assert.Matches("^[0-9A-F]{12}$", reference);
            Assert.Equal(reference, Single(directory).GetProperty("CorrelationId").GetString());
            Assert.Equal("correlation-42", DesktopDiagnostics.Record(null, "Tests", "REFERENCE_CHECK", correlationId: "correlation-42", logDirectory: directory));
        });
    }

    [Fact]
    public void Entry_carries_operation_context_inner_types_and_frames_but_never_the_message()
    {
        WithDirectory(directory =>
        {
            Exception exception;
            try { ThrowWrapped(); throw new UnreachableException(); }
            catch (Exception caught) { exception = caught; }

            DesktopDiagnostics.Record(exception, "Reports.Workspace", "SALES_REPORT_FAILED", logDirectory: directory,
                context: new("sales-brand", "WLMHW", new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 1)),
                operation: "Sales report failed");

            var line = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "diagnostics-*.jsonl")));
            var root = JsonDocument.Parse(line).RootElement;
            Assert.Equal("Sales report failed", root.GetProperty("Operation").GetString());
            Assert.Equal("sales-brand", root.GetProperty("ReportCode").GetString());
            Assert.Equal("WLMHW", root.GetProperty("StoreCode").GetString());
            Assert.Equal("2026-10-09", root.GetProperty("BusinessDate").GetString());
            Assert.Equal("2026-10-01", root.GetProperty("DateFrom").GetString());
            Assert.Equal(typeof(NullReferenceException).FullName, Assert.Single(root.GetProperty("InnerExceptionTypes").EnumerateArray()).GetString());
            // The frames are those of the innermost exception, where the fault was raised.
            Assert.Contains(root.GetProperty("Stack").EnumerateArray(), frame => frame.GetString()!.EndsWith(nameof(ThrowInner), StringComparison.Ordinal));
            Assert.DoesNotContain("Customer Ramesh 9876543210", line, StringComparison.Ordinal);
            Assert.DoesNotContain("INV-0042", line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Unsafe_operation_text_and_context_tokens_are_left_out_or_redacted()
    {
        WithDirectory(directory =>
        {
            DesktopDiagnostics.Record(new ArgumentException("bad", "identity"), "Tests", "UNSAFE_CONTEXT", logDirectory: directory,
                context: new("report with spaces", "customer@example.com"), operation: "Invoice 42 for customer@example.com failed");

            var line = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "diagnostics-*.jsonl")));
            var root = JsonDocument.Parse(line).RootElement;
            Assert.False(root.TryGetProperty("Operation", out _));
            Assert.Equal("redacted", root.GetProperty("ReportCode").GetString());
            Assert.Equal("redacted", root.GetProperty("StoreCode").GetString());
            Assert.Equal("identity", root.GetProperty("ParameterName").GetString());
            Assert.False(root.TryGetProperty("BusinessDate", out _));
            Assert.DoesNotContain("customer@example.com", line, StringComparison.Ordinal);
            Assert.DoesNotContain("Invoice 42", line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_entry_without_context_keeps_its_existing_shape()
    {
        WithDirectory(directory =>
        {
            DesktopDiagnostics.Record(null, "Tests", "NO_CONTEXT", logDirectory: directory);
            var root = Single(directory);
            foreach (var name in new[] { "Operation", "ReportCode", "StoreCode", "BusinessDate", "DateFrom", "ParameterName", "InnerExceptionTypes", "Stack" })
                Assert.False(root.TryGetProperty(name, out _), name);
        });
    }

    [Theory]
    [InlineData(null, "ALL")]
    [InlineData(new string[0], "ALL")]
    [InlineData(new[] { "WLMHW" }, "WLMHW")]
    [InlineData(new[] { "WLMHW", "AW330" }, "MULTIPLE")]
    public void Store_scope_is_one_code_ALL_or_MULTIPLE(string[]? stores, string expected) =>
        Assert.Equal(expected, DesktopDiagnosticContext.StoreOf(stores));

    [Fact]
    public void The_reference_is_appended_to_the_user_text() =>
        Assert.Equal("Stock report failed: try again. Ref: 0A1B2C3D4E5F", DesktopDiagnostics.WithReference("Stock report failed: try again. ", "0A1B2C3D4E5F"));

    [Fact]
    public async Task An_observed_task_that_faults_is_recorded_and_shown_with_its_reference()
    {
        string? shown = null;
        await DesktopDiagnostics.ObserveAsync(Task.FromException(new InvalidOperationException("Sequence contains no elements")),
            "Shell.Navigation", "OBSERVE_TEST_FAILED", "Navigation failed", message => shown = message);

        Assert.NotNull(shown);
        Assert.StartsWith("Navigation failed: ", shown, StringComparison.Ordinal);
        var reference = shown![(shown.LastIndexOf("Ref: ", StringComparison.Ordinal) + 5)..];
        var entry = EntryWithReference(reference);
        Assert.Equal("OBSERVE_TEST_FAILED", entry.GetProperty("EventId").GetString());
        Assert.Equal("Navigation failed", entry.GetProperty("Operation").GetString());
    }

    [Fact]
    public async Task An_observed_task_that_succeeds_or_is_cancelled_shows_nothing()
    {
        var shown = 0;
        await DesktopDiagnostics.ObserveAsync(Task.CompletedTask, "Tests", "OBSERVE_OK", "Nothing", _ => shown++);
        await DesktopDiagnostics.ObserveAsync(Task.FromCanceled(new CancellationToken(true)), "Tests", "OBSERVE_CANCELLED", "Nothing", _ => shown++);
        Assert.Equal(0, shown);
        foreach (var log in Directory.GetFiles(DiagnosticsIsolation.LogDirectory, "diagnostics-*.jsonl"))
            Assert.DoesNotContain("OBSERVE_CANCELLED", ReadShared(log), StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_run_that_fails_before_its_own_try_is_shown_with_a_reference_and_logged_with_its_report()
    {
        RunSta(async () =>
        {
            var view = SyntheticReportView.Create(out _);
            view.AttachHost(_ => throw new InvalidOperationException("focused workspace could not open"), (_, _, _) => Task.CompletedTask, (_, _, _) => { }, _ => { }, _ => { });

            await view.RunReportObservedAsync("sales-brand");

            var text = ((TextBlock)view.FindName("ReportResult")).Text;
            Assert.StartsWith("Report failed: ", text, StringComparison.Ordinal);
            var entry = EntryWithReference(text[(text.LastIndexOf("Ref: ", StringComparison.Ordinal) + 5)..]);
            Assert.Equal("REPORT_RUN_FAILED", entry.GetProperty("EventId").GetString());
            Assert.Equal("sales-brand", entry.GetProperty("ReportCode").GetString());
            Assert.Equal("2026-08-25", entry.GetProperty("BusinessDate").GetString());
        });
    }

    [Theory]
    [InlineData(typeof(ArgumentException), DesktopDiagnosticSeverity.Warning)]
    [InlineData(typeof(ArgumentNullException), DesktopDiagnosticSeverity.Warning)]
    [InlineData(typeof(InvalidOperationException), DesktopDiagnosticSeverity.Error)]
    [InlineData(typeof(UnauthorizedAccessException), DesktopDiagnosticSeverity.Error)]
    public void A_typing_mistake_in_Settings_Users_is_a_warning(Type exceptionType, DesktopDiagnosticSeverity expected) =>
        Assert.Equal(expected, AdministrationWorkspaceView.UserAccessFailureSeverity((Exception)Activator.CreateInstance(exceptionType)!));

    [Fact]
    public void A_sql_refusal_in_Settings_Users_stays_an_error() =>
        Assert.Equal(DesktopDiagnosticSeverity.Error, AdministrationWorkspaceView.UserAccessFailureSeverity(
            SqlExceptionFactory.Create(new SqlExceptionFactory.Error(4613, "Grantor does not have GRANT permission."))));

    private static void ThrowWrapped()
    {
        try { ThrowInner(); }
        catch (NullReferenceException inner) { throw new InvalidOperationException("Customer Ramesh 9876543210 INV-0042", inner); }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowInner() => throw new NullReferenceException("INV-0042");

    private static JsonElement EntryWithReference(string reference)
    {
        foreach (var log in Directory.GetFiles(DiagnosticsIsolation.LogDirectory, "diagnostics-*.jsonl"))
            foreach (var line in ReadShared(log).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var root = JsonDocument.Parse(line).RootElement;
                if (root.GetProperty("CorrelationId").GetString() == reference) return root;
            }
        throw new Xunit.Sdk.XunitException($"No diagnostics entry has reference {reference}.");
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonElement Single(string directory) =>
        JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "diagnostics-*.jsonl")))).RootElement;

    private static void WithDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etp-diagnostics-{Guid.NewGuid():N}");
        try { test(directory); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action().GetAwaiter().GetResult(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Diagnostics context test failed", failure);
    }
}
