using System.Text.Json;
using Etp.Reporting.Application.ErrorRegister;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Desktop.Modules.HealthCheck;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.11.0 self-diagnostics, design sections 8 and 10: no customer data ever leaves the PC in a health export.
/// Planted names, phone numbers, long numbers, e-mail addresses, paths, quotes and invoice numbers go into every
/// field a probe or the register can fill; none of them may appear in the written file, each is counted as a
/// redaction, and the safe values next to them stay readable. All values here are synthetic.
/// </summary>
public sealed class HealthExportPrivacyTests
{
    // Synthetic look-alikes of what must never leave the PC.
    private const string FullName = "Ramesh Kumar";
    private const string Phone = "9876543210";
    private const string PhoneWithDashes = "98765-43210";
    private const string LongNumber = "123456789012";
    private const string Email = "ramesh.kumar@example.com";
    private const string WindowsPath = @"C:\Users\Ramesh\Desktop\R025 HEMW.xlsx";
    private const string Quoted = "\"Ramesh\"";
    private const string SingleQuoted = "'Ramesh'";
    private const string InvoiceSlash = "HE/24-25/001234";
    private const string InvoiceDash = "INV-0012345";
    private const string Hidden = "PLANTEDSECRET";

    private static readonly string[] Planted =
        [FullName, Phone, PhoneWithDashes, LongNumber, Email, WindowsPath, Quoted, SingleQuoted, InvoiceSlash, InvoiceDash];

    private static readonly DateTimeOffset Now = new(2026, 10, 11, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_planted_value_reaches_the_export_and_every_one_is_counted()
    {
        var root = NewDirectory();
        try
        {
            var writer = new HealthExportWriter(root, [], new FixedClock(Now));
            var input = PlantedInput();
            var expectedRedactions = writer.CountRedactions(input);

            var result = writer.Write(input);

            Assert.Null(result.FailureCode);
            Assert.NotNull(result.FileName);
            var text = File.ReadAllText(result.FullPath!);
            foreach (var value in Planted)
                Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
            foreach (var fragment in new[] { "@", "\\", "Ramesh", Phone, "43210", "001234", "0012345", "9876543210", "123456789012" })
                Assert.DoesNotContain(fragment, text, StringComparison.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(text);
            var strings = AllStrings(document.RootElement).ToArray();
            foreach (var value in strings)
            {
                Assert.DoesNotContain("@", value, StringComparison.Ordinal);
                Assert.DoesNotContain("\\", value, StringComparison.Ordinal);
                Assert.DoesNotContain("\"", value, StringComparison.Ordinal);
                Assert.DoesNotMatch(@"[0-9]{10,}", value);
            }
            Assert.DoesNotContain(AllNumbers(document.RootElement), number => Math.Abs(number) >= 1_000_000_000m);

            var privacy = document.RootElement.GetProperty("privacy");
            Assert.Equal(HealthExportPrivacyGuard.Name, privacy.GetProperty("guard").GetString());
            Assert.Equal(expectedRedactions, privacy.GetProperty("redactions").GetInt32());
            Assert.Equal(expectedRedactions, result.Redactions);
            Assert.True(result.Redactions >= 30, $"Only {result.Redactions} values were redacted.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Safe_values_stay_readable_beside_the_redacted_ones()
    {
        // Serialize only: the folder is never touched.
        var writer = new HealthExportWriter(Path.Combine(Path.GetTempPath(), "EtpHealthExportUnused"), [], new FixedClock(Now));
        var bytes = writer.Serialize(PlantedInput(), writer.CreateGuard());
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;

        Assert.Equal(HealthExportWriter.Schema, root.GetProperty("schema").GetString());
        var run = root.GetProperty("run");
        Assert.Equal("Nightly", run.GetProperty("trigger").GetString());
        Assert.Equal("2026-10-10", run.GetProperty("businessDate").GetString());
        Assert.Equal("1.11.0.0", run.GetProperty("appVersion").GetString());
        Assert.Equal("Automation", run.GetProperty("runByRole").GetString());

        var safe = root.GetProperty("results").EnumerateArray().Single(result => result.GetProperty("id").GetString() == "DB-04");
        Assert.Equal("AUTO_CLOSE_ON", safe.GetProperty("finding").GetString());
        Assert.Equal("AUTO_CLOSE is on for EtpReporting. Run ETP setup again as administrator.", safe.GetProperty("explanation").GetString());
        Assert.Equal("2026-10-04", safe.GetProperty("sameSince").GetString());

        var planted = root.GetProperty("results").EnumerateArray().Single(result => result.GetProperty("id").GetString() == "DATA-01");
        Assert.Equal(HealthExportPrivacyGuard.Redacted, planted.GetProperty("explanation").GetString());
        var detail = planted.GetProperty("detail").EnumerateArray().First();
        Assert.Equal("HEMW", detail.GetProperty("store").GetString());
        Assert.Equal("R022, R025", detail.GetProperty("codes").GetString());

        var entry = root.GetProperty("register").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "ER-0006");
        Assert.Equal("ER-0006", entry.GetProperty("id").GetString());
        Assert.Equal(new string('a', 64), entry.GetProperty("fingerprint").GetString());
        Assert.Equal("1A2B3C4D5E6F", entry.GetProperty("sampleRefs").EnumerateArray().First().GetString());
        Assert.Contains("SqlServerApplicationReportQuery.LoadStaffPerformanceAsync",
            entry.GetProperty("stack").EnumerateArray().Select(frame => frame.GetString()));

        Assert.Equal(1, root.GetProperty("previousRun").GetProperty("changed").GetArrayLength());
        Assert.Equal(3, root.GetProperty("warningsByEvent").GetProperty("USER_ACCESS_SAVE_FAILED").GetInt32());
    }

    [Theory]
    [InlineData("R025")]
    [InlineData("HEMW")]
    [InlineData("1.11.0.0")]
    [InlineData("2026-10-09")]
    [InlineData("2026-10-10T21:00:04Z")]
    [InlineData("SqlServerApplicationReportQuery.LoadStaffPerformanceAsync")]
    [InlineData("<RunDsrAsync>d__12.MoveNext")]
    [InlineData("Microsoft.Data.SqlClient.SqlException")]
    [InlineData("INSERT, UPDATE")]
    [InlineData("ER-0006")]
    [InlineData("SQL18456")]
    public void Tokens_the_probes_use_are_safe(string value) => Assert.True(HealthExportPrivacyGuard.IsSafeToken(value), value);

    [Theory]
    [InlineData(FullName)]
    [InlineData(Phone)]
    [InlineData(PhoneWithDashes)]
    [InlineData("987 654 3210")]
    [InlineData(LongNumber)]
    [InlineData(Email)]
    [InlineData(WindowsPath)]
    [InlineData(Quoted)]
    [InlineData(SingleQuoted)]
    [InlineData(InvoiceSlash)]
    [InlineData(InvoiceDash)]
    [InlineData("BILL00042")]
    [InlineData("")]
    public void Customer_looking_values_are_never_tokens(string value) => Assert.False(HealthExportPrivacyGuard.IsSafeToken(value), value);

    [Theory]
    [InlineData("AUTO_CLOSE is on for EtpReporting.", true)]
    [InlineData("SQL Server 2025 is on its first release build (17.0.1000.7, no cumulative update). Install CU9 with the SQL CU runbook.", true)]
    [InlineData("4 exports missing: HEMW R025, R022; the Owner's inputs are not entered.", true)]
    [InlineData("Skipped: the database is not reachable (see DB-01).", true)]
    [InlineData("Customer ramesh.kumar@example.com has an open invoice.", false)]
    [InlineData("Call 98765-43210 about the stock.", false)]
    [InlineData("Invoice INV-0012345 failed.", false)]
    [InlineData("Invoice HE/24-25/001234 failed.", false)]
    [InlineData("The file C:\\Users\\Ramesh\\R025.xlsx was not read.", false)]
    [InlineData("Customer \"Ramesh\" is missing.", false)]
    [InlineData("Customer 'Ramesh' is missing.", false)]
    [InlineData("Account 123456789012 was refused.", false)]
    public void Explanations_are_plain_sentences_without_customer_data(string text, bool safe) =>
        Assert.Equal(safe, new HealthExportPrivacyGuard([]).IsSafeText(text));

    [Fact]
    public void A_template_accepts_tokens_and_refuses_quotes_in_a_placeholder()
    {
        var guard = new HealthExportPrivacyGuard(["{role} has {permissions} on {object}; the design ({decision}) says Owner only."]);
        Assert.True(guard.IsSafeText("etp_store_manager has INSERT, UPDATE on dbo.staff_sales_targets; the design (D22) says Owner only."));
        Assert.False(guard.IsSafeText("etp_store_manager has INSERT on dbo.staff_sales_targets; the design (\"x\") says Owner only."));
    }

    [Fact]
    public void Numbers_of_ten_or_more_digits_are_dropped_and_counted()
    {
        var guard = new HealthExportPrivacyGuard([]);
        Assert.Equal(41250L, guard.Number(41250L));
        Assert.Equal(12.5m, guard.Number(12.5m));
        Assert.Null(guard.Number(9876543210L));
        Assert.Null(guard.Number(9876543210m));
        Assert.Equal(2, guard.Redactions);
    }

    [Fact]
    public void A_redacting_export_turns_err01_into_a_warning_and_recounts_the_run()
    {
        var run = PlantedInput().Run;
        Assert.Equal(HealthOutcome.Pass, run.Results.Single(result => result.CheckId == HealthCheckId.Err01).Outcome);

        var warned = HealthExportWriter.WithRedactionWarning(run, 7);

        var err01 = warned.Results.Single(result => result.CheckId == HealthCheckId.Err01);
        Assert.Equal(HealthOutcome.Warning, err01.Outcome);
        Assert.Equal(HealthExportWriter.RedactionFinding, err01.FindingCode);
        Assert.Equal("The health export replaced 7 value(s) that were not safe to export.", err01.Explanation);
        Assert.True(new HealthExportPrivacyGuard([]).IsSafeText(err01.Explanation));
        Assert.Equal(run.Counts.Warning + 1, warned.Counts.Warning);
        Assert.Equal(run.Counts.Pass - 1, warned.Counts.Pass);
        Assert.Same(run, HealthExportWriter.WithRedactionWarning(run, 0));
    }

    // ------------------------------------------------------------ files

    [Fact]
    public void The_file_is_named_by_local_start_time_and_trigger()
    {
        var india = TimeZoneInfo.CreateCustomTimeZone("ETP-Test-IST", TimeSpan.FromMinutes(330), "IST", "IST");
        Assert.Equal("health-20261011-023104-Nightly.json",
            HealthExportWriter.FileNameFor(HealthCheckTrigger.Nightly, new DateTimeOffset(2026, 10, 10, 21, 1, 4, TimeSpan.Zero), india));
        Assert.Equal(new DateOnly(2026, 10, 11), HealthExportWriter.ExportDateOf("health-20261011-023104-Nightly.json"));
        Assert.Equal(new DateOnly(2026, 10, 11), HealthExportWriter.ExportDateOf("health-20261011-023104-Manual-2.json"));
        Assert.Null(HealthExportWriter.ExportDateOf("health-20261011-023104-Nightly.json.bak"));
        Assert.Null(HealthExportWriter.ExportDateOf("notes.json"));
    }

    [Fact]
    public void Writing_keeps_90_days_and_deletes_only_its_own_old_files()
    {
        var root = NewDirectory();
        try
        {
            var clock = new FixedClock(Now);
            void Touch(string name) => File.WriteAllText(Path.Combine(root, name), "{}");
            // Now is 11 Oct 21:00 UTC = 12 Oct 02:30 in the test zone (IST).
            Touch("health-20260601-023000-Nightly.json");   // older than 90 days: deleted
            Touch("health-20260713-023000-Nightly.json");   // 91 days: deleted
            Touch("health-20260714-023000-Manual.json");    // exactly 90 days before 12 Oct (local): kept
            Touch("health-20261001-023000-Nightly.json");
            Touch("notes.txt");                             // not ours: kept
            Touch("health-old.json");                       // not an export name: kept

            var result = new HealthExportWriter(root, [], clock).Write(PlantedInput());

            Assert.Null(result.FailureCode);
            var names = Directory.GetFiles(root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            Assert.DoesNotContain("health-20260601-023000-Nightly.json", names);
            Assert.DoesNotContain("health-20260713-023000-Nightly.json", names);
            Assert.Contains("health-20260714-023000-Manual.json", names);
            Assert.Contains("health-20261001-023000-Nightly.json", names);
            Assert.Contains("notes.txt", names);
            Assert.Contains("health-old.json", names);
            Assert.Contains(result.FileName, names);
            Assert.DoesNotContain(names, name => name!.EndsWith(".tmp", StringComparison.Ordinal));

            // A second run in the same second does not overwrite the first.
            var second = new HealthExportWriter(root, [], clock).Write(PlantedInput());
            Assert.NotEqual(result.FileName, second.FileName);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_missing_folder_is_reported_not_created_and_nothing_throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "EtpHealthExportMissing-" + Guid.NewGuid().ToString("N"));
        var result = new HealthExportWriter(missing, [], new FixedClock(Now)).Write(PlantedInput());
        Assert.Equal("EXPORT_FOLDER_MISSING", result.FailureCode);
        Assert.Null(result.FileName);
        Assert.False(Directory.Exists(missing));
    }

    // ------------------------------------------------------------ register selection

    [Fact]
    public void The_register_holds_open_entries_recent_changes_and_fixed_entries_for_45_days_never_merged_ones()
    {
        var open = Entry(1, ErrorStatus.New, changed: Now.AddDays(-200), lastSeen: Now.AddDays(-200));
        var fixedRecently = Entry(2, ErrorStatus.Fixed, changed: Now.AddDays(-40), lastSeen: Now.AddDays(-90));
        var fixedLongAgo = Entry(3, ErrorStatus.Fixed, changed: Now.AddDays(-50), lastSeen: Now.AddDays(-90));
        var merged = Entry(4, ErrorStatus.New, changed: Now, lastSeen: Now) with { MergedIntoId = 1 };

        var selected = HealthExportWriter.SelectRegister([open, fixedRecently, fixedLongAgo, merged], Now);

        Assert.Equal(new[] { 1, 2 }, selected.Select(entry => entry.ErrorRegisterId));
    }

    [Fact]
    public void A_fix_is_verified_after_30_quiet_days_and_5_nightly_runs_on_the_fixed_version()
    {
        var fixedEntry = Entry(1, ErrorStatus.Fixed, changed: Now.AddDays(-31), lastSeen: Now.AddDays(-40)) with
        {
            FixedInVersion = "1.11.1",
            FixedUtc = Now.AddDays(-31)
        };
        HealthCheckRunSummary Nightly(int daysAgo, string version) => new(daysAgo, HealthCheckTrigger.Nightly, DateOnly.FromDateTime(Now.AddDays(-daysAgo - 1).Date),
            Now.AddDays(-daysAgo), Now.AddDays(-daysAgo).AddSeconds(40), 40000, version, "WORKPC", "Automation", HealthOutcome.Pass, new(51, 0, 0, 0, 0), null);

        var fiveOnFixed = Enumerable.Range(1, 5).Select(day => Nightly(day, "1.11.1.0")).ToArray();
        var olderVersion = Enumerable.Range(1, 5).Select(day => Nightly(day, "1.11.0.0")).ToArray();

        Assert.True(HealthExportWriter.IsVerified(fixedEntry, Now, fiveOnFixed));
        Assert.False(HealthExportWriter.IsVerified(fixedEntry, Now, fiveOnFixed.Take(4)));
        Assert.False(HealthExportWriter.IsVerified(fixedEntry, Now, olderVersion));
        Assert.False(HealthExportWriter.IsVerified(fixedEntry with { FixedUtc = Now.AddDays(-10) }, Now, fiveOnFixed));
        Assert.False(HealthExportWriter.IsVerified(fixedEntry with { Status = ErrorStatus.Recurred }, Now, fiveOnFixed));
    }

    // ------------------------------------------------------------ planted input

    private static HealthExportInput PlantedInput()
    {
        var plantedFields = new Dictionary<string, string>
        {
            ["store"] = "HEMW",
            ["codes"] = "R022, R025",
            ["customer"] = FullName,
            ["phone"] = Phone,
            ["mobile"] = PhoneWithDashes,
            ["email"] = Email,
            ["file"] = WindowsPath,
            ["invoice"] = InvoiceSlash,
            ["bill"] = InvoiceDash,
            ["account"] = LongNumber,
            [Email] = "1",
            [FullName] = "2"
        };
        var results = new List<HealthCheckResult>
        {
            new(HealthCheckId.Db04, HealthCheckArea.Database, HealthOutcome.Warning, "AUTO_CLOSE_ON",
                "AUTO_CLOSE is on for EtpReporting. Run ETP setup again as administrator.", null, TimeSpan.FromMilliseconds(12),
                UnchangedSince: new DateOnly(2026, 10, 4)),
            new(HealthCheckId.Data01, HealthCheckArea.Data, HealthOutcome.Warning, "DAY_INCOMPLETE",
                $"Missing exports for {FullName} ({Phone}, {Email}), invoice {InvoiceSlash} in {WindowsPath}.", "import-files",
                TimeSpan.FromMilliseconds(400), MetricValue: 9876543210m,
                SubResults:
                [
                    new("HEMW", HealthOutcome.Warning, "EXPORTS_MISSING", 2m, plantedFields),
                    new(FullName, HealthOutcome.Warning, Quoted, 98765432101m, new Dictionary<string, string> { ["k"] = SingleQuoted })
                ]),
            new(HealthCheckId.Rpt16, HealthCheckArea.Reports, HealthOutcome.Fail, InvoiceDash,
                $"Report failed for {Quoted} {SingleQuoted} {InvoiceDash} {LongNumber}.", $"call {PhoneWithDashes}", TimeSpan.FromSeconds(1)),
            new(HealthCheckId.Sec02, HealthCheckArea.Security, HealthOutcome.Pass, "OK", "Role memberships are as expected.", null,
                TimeSpan.FromMilliseconds(30), SubResults: [new(@"WORKPC\Ramesh", HealthOutcome.Pass, Fields: new Dictionary<string, string> { ["member"] = @"WORKPC\Ramesh" })]),
            new(HealthCheckId.Err01, HealthCheckArea.Errors, HealthOutcome.Pass, "OK", "Ingested 3 entries.", null, TimeSpan.FromMilliseconds(50))
        };
        var counts = HealthExportWriter.Recount(results);
        var run = new HealthCheckRun(HealthCheckTrigger.Nightly, new DateOnly(2026, 10, 10), Now.AddMinutes(-1), Now, "1.11.0.0",
            "WORKPC", "Automation", HealthExportWriter.OverallOf(counts), counts, null, results);

        var previous = new HealthCheckPreviousRun(41, Now.AddDays(-1),
        [
            new(HealthCheckId.Db04, HealthCheckArea.Database, HealthOutcome.Warning, "AUTO_CLOSE_ON", "AUTO_CLOSE is on for EtpReporting.", null, TimeSpan.Zero),
            new(HealthCheckId.Data01, HealthCheckArea.Data, HealthOutcome.Pass, "OK", "Yesterday is complete.", null, TimeSpan.Zero)
        ]);

        var entry = Entry(6, ErrorStatus.New, changed: Now, lastSeen: Now) with
        {
            SubCode = FullName,
            Site = Email,
            FixedTicket = InvoiceDash,
            LatestStack = ["SqlServerApplicationReportQuery.LoadStaffPerformanceAsync", WindowsPath, $"Customer.{Phone}"]
        };
        var badFingerprint = Entry(7, ErrorStatus.Seen, changed: Now, lastSeen: Now) with
        {
            Fingerprint = "not-a-fingerprint " + FullName,
            EventCode = Email,
            Source = Quoted,
            ExceptionType = WindowsPath,
            FirstVersion = Phone,
            LastVersion = InvoiceSlash,
            OccurrenceCount = 12345678901L
        };
        var detail = new ErrorRegisterEntryDetail(entry,
            [new("1.11.0.0", 3, Now.AddHours(-2), Now), new(Phone, 1, Now, Now)],
            [
                Occurrence(1, "1A2B3C4D5E6F", "staff", "ALL"),
                Occurrence(2, "123456789012", InvoiceDash, FullName),
                Occurrence(3, Email, Quoted, Phone)
            ]);

        return new HealthExportInput(run, previous, [entry, badFingerprint],
            new Dictionary<int, ErrorRegisterEntryDetail> { [6] = detail },
            [],
            new Dictionary<string, int> { ["USER_ACCESS_SAVE_FAILED"] = 3, [Email] = 1, [FullName] = 2 });
    }

    private static ErrorRegisterEntry Entry(int id, ErrorStatus status, DateTimeOffset changed, DateTimeOffset lastSeen) => new(
        id, new string('a', 64), 1, "STAFF_REPORT_FAILED", null, "Reports.Workspace", "SqlServerApplicationReportQuery.LoadStaffPerformanceAsync",
        "Microsoft.Data.SqlClient.SqlException", -2, ErrorRegisterSeverity.Error, status, lastSeen.AddDays(-1), lastSeen, 3,
        "1.11.0.0", "1.11.0.0", null, null, null, null, null, [], null, changed, "WORKPC\\Owner", null);

    private static ErrorOccurrenceRecord Occurrence(long id, string reference, string report, string store) => new(
        id, Now.AddMinutes(-id), reference, "1.11.0.0", ErrorRegisterSeverity.Error, report, store, new DateOnly(2026, 10, 9), null,
        "RunReport", "Owner", Now);

    private static IEnumerable<string> AllStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var value in AllStrings(property.Value)) yield return value;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var value in AllStrings(item)) yield return value;
                break;
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
        }
    }

    private static IEnumerable<decimal> AllNumbers(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => AllNumbers(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(AllNumbers),
        JsonValueKind.Number => [element.GetDecimal()],
        _ => []
    };

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EtpHealthExport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FixedClock(DateTimeOffset utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utc;
        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("ETP-Test-IST", TimeSpan.FromMinutes(330), "IST", "IST");
    }
}
