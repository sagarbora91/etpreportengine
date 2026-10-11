using Etp.Reporting.Application.ErrorRegister;
using Etp.Reporting.Desktop.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.11.0 lane L2: the shipped fix manifest (design 4.4) and how it is applied.</summary>
public sealed class ErrorFixManifestTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"etp-manifest-{Guid.NewGuid():N}");

    public ErrorFixManifestTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string ShippedManifestPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Etp.Reporting.Desktop", "Diagnostics", ErrorFixManifest.FileName);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("ErrorFixManifest.json was not found above the test output folder.");
    }

    private static string Fingerprint(string eventBase, string source, string exceptionType) =>
        ErrorFingerprint.Compute(new ErrorFingerprintParts(eventBase, string.Empty, source, string.Empty, exceptionType, 0));

    [Fact]
    public void The_shipped_manifest_is_well_formed_and_holds_the_five_legacy_entries_of_the_design()
    {
        var lines = ErrorFixManifest.Parse(File.ReadAllText(ShippedManifestPath()), out var problems);

        Assert.Empty(problems);
        var expected = new Dictionary<string, (string FixedIn, string Ticket)>
        {
            [Fingerprint("USER_ACCESS_SAVE_FAILED", "OperationsAdministration.Administration", "Microsoft.Data.SqlClient.SqlException")] = ("1.9.3", "IE-RT-03"),
            [Fingerprint("USER_ACCESS_SAVE_FAILED", "OperationsAdministration.Administration", "System.ArgumentException")] = ("1.9.9", "IE-RT-04"),
            [Fingerprint("REPORT_STORE_REQUIRED", "Reports.Workspace", "System.InvalidOperationException")] = ("1.9.7", "IE-RT-05"),
            [Fingerprint("CASH_BOOK_FAILED", "Reports.Workspace", "System.ArgumentException")] = ("1.9.7", "IE-RT-06"),
            [Fingerprint("MANAGEMENT_TREND_REPORT_FAILED", "Reports.Workspace", "System.ArgumentException")] = ("1.9.8", "IE-RT-07")
        };
        foreach (var (fingerprint, fix) in expected)
        {
            var line = Assert.Single(lines, candidate => candidate.Fingerprint == fingerprint);
            Assert.Equal(fix.FixedIn, line.FixedIn);
            Assert.Equal(fix.Ticket, line.Ticket);
        }
        Assert.Equal(lines.Count, lines.Select(line => line.Fingerprint).Distinct().Count());
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[]", "root must be an object")]
    [InlineData("{ \"manifest\": 2, \"fixes\": [] }", "\"manifest\" must be 1")]
    [InlineData("{ \"manifest\": 1 }", "\"fixes\" must be an array")]
    [InlineData("{ \"manifest\": 1, \"fixes\": [ 5 ] }", "must be an object")]
    [InlineData("{ \"manifest\": 1, \"fixes\": [ { \"fingerprint\": \"ABC\", \"fixedIn\": \"1.11.1\", \"ticket\": \"FT-007\" } ] }", "64 lowercase hex")]
    [InlineData("{ \"manifest\": 1, \"fixes\": [ { \"fingerprint\": \"0000000000000000000000000000000000000000000000000000000000000000\", \"fixedIn\": \"soon\", \"ticket\": \"FT-007\" } ] }", "fixedIn must be a version")]
    [InlineData("{ \"manifest\": 1, \"fixes\": [ { \"fingerprint\": \"0000000000000000000000000000000000000000000000000000000000000000\", \"fixedIn\": \"1.11\", \"ticket\": \"FT-007\" } ] }", "fixedIn must be a version")]
    [InlineData("{ \"manifest\": 1, \"fixes\": [ { \"fingerprint\": \"0000000000000000000000000000000000000000000000000000000000000000\", \"fixedIn\": \"1.11.1\", \"ticket\": \"has space\" } ] }", "ticket must be")]
    public void Malformed_manifests_are_reported(string json, string expectedProblem)
    {
        var lines = ErrorFixManifest.Parse(json, out var problems);

        Assert.Contains(problems, problem => problem.Contains(expectedProblem, StringComparison.Ordinal));
        Assert.Empty(lines);
    }

    [Fact]
    public void A_duplicate_fingerprint_is_reported_and_only_the_first_line_is_kept()
    {
        var line = "{ \"fingerprint\": \"" + new string('a', 64) + "\", \"fixedIn\": \"1.11.1\", \"ticket\": \"FT-007\" }";

        var lines = ErrorFixManifest.Parse("{ \"manifest\": 1, \"fixes\": [ " + line + ", " + line + " ] }", out var problems);

        Assert.Single(lines);
        Assert.Contains(problems, problem => problem.Contains("twice", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_hands_the_raw_json_and_the_running_version_to_the_store()
    {
        var path = Path.Combine(_directory, ErrorFixManifest.FileName);
        File.Copy(ShippedManifestPath(), path);
        var store = new ErrorRegisterIngestTests.FakeStore();

        var result = await ErrorFixManifest.ApplyAsync(store, "1.11.0.0", path, _directory);

        Assert.Equal(new ErrorFixManifestResult(5, 5, 0, 0, 0), result);
        var call = Assert.Single(store.Manifests);
        Assert.Equal("1.11.0.0", call.Version);
        Assert.Equal(File.ReadAllText(path), call.Json);
    }

    [Fact]
    public async Task A_missing_manifest_does_nothing_and_a_broken_one_or_a_failing_store_is_logged_without_throwing()
    {
        var store = new ErrorRegisterIngestTests.FakeStore();
        Assert.Null(await ErrorFixManifest.ApplyAsync(store, "1.11.0.0", Path.Combine(_directory, "missing.json"), _directory));
        Assert.Empty(store.Manifests);
        Assert.Empty(Directory.GetFiles(_directory, "diagnostics-*.jsonl"));

        var broken = Path.Combine(_directory, "broken.json");
        File.WriteAllText(broken, "{ \"manifest\": 1, \"fixes\": [ { \"fingerprint\": \"x\" } ] }");
        Assert.Null(await ErrorFixManifest.ApplyAsync(store, "1.11.0.0", broken, _directory));
        Assert.Empty(store.Manifests);
        Assert.Contains("ERROR_FIX_MANIFEST_FAILED", string.Concat(Directory.GetFiles(_directory, "diagnostics-*.jsonl").Select(File.ReadAllText)));
    }

    [Fact]
    public async Task An_empty_manifest_does_not_call_the_store()
    {
        var path = Path.Combine(_directory, "empty.json");
        File.WriteAllText(path, "{ \"manifest\": 1, \"fixes\": [] }");
        var store = new ErrorRegisterIngestTests.FakeStore();

        var result = await ErrorFixManifest.ApplyAsync(store, "1.11.0.0", path, _directory);

        Assert.Equal(new ErrorFixManifestResult(0, 0, 0, 0, 0), result);
        Assert.Empty(store.Manifests);
    }
}
