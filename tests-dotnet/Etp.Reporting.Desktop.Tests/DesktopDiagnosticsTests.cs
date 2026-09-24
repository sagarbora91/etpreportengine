using System.Text.Json;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopDiagnosticsTests
{
    private static readonly string OwnersLogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "Logs");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\Logs")]
    [InlineData(@"\rooted\but\no\drive")]
    [InlineData(@"C:drive\relative")]
    public void The_Owners_log_folder_is_kept_unless_the_variable_names_a_full_path(string? configured) =>
        Assert.Equal(OwnersLogDirectory, DesktopDiagnostics.DefaultDirectory(configured));

    [Fact]
    public void A_full_path_in_the_variable_replaces_the_Owners_log_folder()
    {
        var configured = Path.Combine(Path.GetTempPath(), "etp-diagnostics-elsewhere");

        Assert.Equal(configured, DesktopDiagnostics.DefaultDirectory(configured));
    }

    [Fact]
    public void This_test_run_writes_diagnostics_to_its_own_temporary_folder_and_not_the_Owners_log()
    {
        // Views under test record failures without naming a folder. They must land here.
        var correlationId = Guid.NewGuid().ToString("N");

        DesktopDiagnostics.Record(null, "Tests", "TEST_RUN_ISOLATION_CHECK", correlationId: correlationId);

        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), DiagnosticsIsolation.LogDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DiagnosticsIsolation.LogDirectory, Environment.GetEnvironmentVariable(DesktopDiagnostics.DirectoryVariable));
        Assert.Contains(Directory.GetFiles(DiagnosticsIsolation.LogDirectory, "diagnostics-*.jsonl"),
            path => ReadWhileOthersAppend(path).Contains(correlationId, StringComparison.Ordinal));
    }

    // Other tests in this run append to the same file concurrently.
    private static string ReadWhileOthersAppend(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Record_writes_structured_privacy_safe_diagnostic()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etp-diagnostics-{Guid.NewGuid():N}");
        try
        {
            var exception = new InvalidOperationException("restricted source row and customer name");
            var timestamp = new DateTimeOffset(2026, 8, 29, 12, 30, 0, TimeSpan.Zero);

            DesktopDiagnostics.Record(
                exception,
                "Import",
                "IMPORT_FAILED",
                DesktopDiagnosticSeverity.Error,
                "correlation-42",
                directory,
                timestamp);

            var path = Assert.Single(Directory.GetFiles(directory, "diagnostics-202608.jsonl"));
            var line = File.ReadAllText(path);
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            Assert.Equal("Error", root.GetProperty("Severity").GetString());
            Assert.Equal("IMPORT_FAILED", root.GetProperty("EventId").GetString());
            Assert.Equal("Import", root.GetProperty("Source").GetString());
            Assert.Equal("correlation-42", root.GetProperty("CorrelationId").GetString());
            Assert.Equal(typeof(InvalidOperationException).FullName, root.GetProperty("ExceptionType").GetString());
            Assert.DoesNotContain(exception.Message, line, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Record_generates_a_correlation_id_when_no_activity_exists()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etp-diagnostics-{Guid.NewGuid():N}");
        try
        {
            DesktopDiagnostics.Record(null, "Startup", "STARTUP_CHECK", logDirectory: directory);

            using var json = JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(directory))));
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("CorrelationId").GetString()));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Record_prunes_expired_logs_and_rolls_over_before_the_size_limit()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etp-diagnostics-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var timestamp = new DateTimeOffset(2026, 8, 29, 12, 30, 0, TimeSpan.Zero);
            var expired = Path.Combine(directory, "diagnostics-202501.jsonl");
            File.WriteAllText(expired, "expired");
            File.SetLastWriteTimeUtc(expired, timestamp.UtcDateTime.AddDays(-DesktopDiagnostics.RetentionDays - 1));
            var full = Path.Combine(directory, "diagnostics-202608.jsonl");
            using (var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.SetLength(DesktopDiagnostics.MaxLogFileBytes);

            DesktopDiagnostics.Record(null, "Startup", "STARTUP_CHECK", logDirectory: directory, timestampUtc: timestamp);

            Assert.False(File.Exists(expired));
            Assert.Equal(DesktopDiagnostics.MaxLogFileBytes, new FileInfo(full).Length);
            var rolled = Assert.Single(Directory.GetFiles(directory, "diagnostics-202608-*.jsonl"));
            using var json = JsonDocument.Parse(File.ReadAllText(rolled));
            Assert.Equal("STARTUP_CHECK", json.RootElement.GetProperty("EventId").GetString());
            Assert.InRange(new FileInfo(rolled).Length, 1, DesktopDiagnostics.MaxLogFileBytes);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Record_keeps_the_global_file_count_bounded_and_redacts_unsafe_tokens()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etp-diagnostics-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var timestamp = new DateTimeOffset(2026, 8, 29, 12, 30, 0, TimeSpan.Zero);
            for (var index = 0; index < DesktopDiagnostics.MaxRetainedFiles; index++)
            {
                var path = Path.Combine(directory, $"diagnostics-202607-{index:D3}.jsonl");
                File.WriteAllText(path, string.Empty);
                File.SetLastWriteTimeUtc(path, timestamp.UtcDateTime.AddDays(-index - 1));
            }

            DesktopDiagnostics.Record(
                null,
                "source with customer@example.com",
                "event with invoice 42",
                correlationId: "customer@example.com",
                logDirectory: directory,
                timestampUtc: timestamp);

            Assert.Equal(DesktopDiagnostics.MaxRetainedFiles, Directory.GetFiles(directory, "diagnostics-*.jsonl").Length);
            var current = Assert.Single(Directory.GetFiles(directory, "diagnostics-202608.jsonl"));
            var line = File.ReadAllText(current);
            using var json = JsonDocument.Parse(line);
            Assert.Equal("Unknown", json.RootElement.GetProperty("Source").GetString());
            Assert.Equal("UNCLASSIFIED", json.RootElement.GetProperty("EventId").GetString());
            Assert.Equal("redacted", json.RootElement.GetProperty("CorrelationId").GetString());
            Assert.DoesNotContain("customer@example.com", line, StringComparison.Ordinal);
            Assert.DoesNotContain("invoice 42", line, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
