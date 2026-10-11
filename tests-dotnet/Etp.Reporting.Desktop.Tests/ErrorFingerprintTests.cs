using Etp.Reporting.Application.ErrorRegister;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.11.0 lane L2: fingerprint rule v1 of the error register (design 4.2).</summary>
public sealed class ErrorFingerprintTests
{
    internal static DesktopDiagnosticEntry Entry(
        string eventId = "REPORT_STORE_REQUIRED",
        string source = "Reports.Workspace",
        string exceptionType = "System.InvalidOperationException",
        IReadOnlyList<string>? stack = null,
        IReadOnlyList<DesktopDiagnosticSqlError>? sql = null,
        DesktopDiagnosticSeverity severity = DesktopDiagnosticSeverity.Error,
        string reference = "1A2B3C4D5E6F",
        string version = "1.9.9.0",
        DateTimeOffset? at = null,
        string? reportCode = null,
        string? storeCode = null,
        string? businessDate = null,
        string? dateFrom = null,
        string? operation = null) =>
        new(at ?? new DateTimeOffset(2026, 10, 9, 10, 15, 2, TimeSpan.Zero), severity, eventId, source, reference, exceptionType,
            0, version, sql, operation, reportCode, storeCode, businessDate, dateFrom, null, null, stack);

    [Theory]
    [InlineData("DATABASE_HEALTH_CHECK_FAILED.SQL18456", "DATABASE_HEALTH_CHECK_FAILED", "", 18456)]
    [InlineData("BACKUP_RUN_FAILED:LOW_DISK_SPACE", "BACKUP_RUN_FAILED", "LOW_DISK_SPACE", 0)]
    [InlineData("BACKUP_RUN_FAILED:RECOVERY_KEYS_MISSING.SQL-2", "BACKUP_RUN_FAILED", "RECOVERY_KEYS_MISSING", -2)]
    [InlineData("CASH_BOOK_FAILED", "CASH_BOOK_FAILED", "", 0)]
    [InlineData("SOMETHING.ELSE", "SOMETHING", "", 0)]
    public void Event_id_splits_into_base_sub_code_and_a_sql_suffix_that_is_not_a_sub_code(string eventId, string eventBase, string subCode, int sql)
    {
        var parts = ErrorFingerprint.PartsOf(Entry(eventId));

        Assert.Equal(eventBase, parts.EventBase);
        Assert.Equal(subCode, parts.SubCode);
        Assert.Equal(sql, parts.SqlNumber);
    }

    [Fact]
    public void Sql_number_is_the_first_error_of_class_eleven_or_more_before_any_suffix()
    {
        var errors = new[]
        {
            new DesktopDiagnosticSqlError(5701, 2, 10, "", 0),
            new DesktopDiagnosticSqlError(51402, 1, 16, "usp_x", 12),
            new DesktopDiagnosticSqlError(18456, 1, 14, "", 0)
        };

        Assert.Equal(51402, ErrorFingerprint.PartsOf(Entry("X_FAILED.SQL1", sql: errors)).SqlNumber);
        Assert.Equal(1, ErrorFingerprint.PartsOf(Entry("X_FAILED.SQL1", sql: [new DesktopDiagnosticSqlError(5701, 2, 10, "", 0)])).SqlNumber);
        Assert.Equal(0, ErrorFingerprint.PartsOf(Entry("X_FAILED")).SqlNumber);
    }

    [Theory]
    [InlineData("Etp.Reporting.Desktop.Modules.Reports.ReportsWorkspaceView+<RunDsrAsync>d__12.MoveNext", "ReportsWorkspaceView.RunDsrAsync")]
    [InlineData("Etp.Reporting.Desktop.Modules.Reports.ReportsWorkspaceView+<>c__DisplayClass3_0.<Run>b__1", "ReportsWorkspaceView.Run")]
    [InlineData("Etp.Reporting.Desktop.Modules.Reports.ReportsWorkspaceView+<>c.<Run>b__3_0", "ReportsWorkspaceView.Run")]
    [InlineData("Etp.Reporting.Desktop.Modules.Reports.ReportsWorkspaceView+<>c__DisplayClass3_0+<<Run>b__0>d.MoveNext", "ReportsWorkspaceView.Run")]
    [InlineData("Etp.Reporting.Desktop.Modules.Reports.ReportsWorkspaceView.<Run>g__Local|3_0", "ReportsWorkspaceView.Run")]
    [InlineData("Etp.Reporting.Infrastructure.SqlServer.SqlServerApplicationReportQuery.LoadStaffPerformanceAsync", "SqlServerApplicationReportQuery.LoadStaffPerformanceAsync")]
    [InlineData("Etp.Reporting.Application.Repo`2.Load", "Repo.Load")]
    [InlineData("Etp.Reporting.Application.Repo`1+Inner.Load", "Repo+Inner.Load")]
    [InlineData("Etp.Reporting.Desktop.Foo..ctor", "Foo..ctor")]
    [InlineData("Etp.Reporting.Desktop.Foo.MoveNext", "Foo.MoveNext")]
    public void Frames_reduce_to_Type_Method_without_namespace_arity_or_compiler_names(string frame, string expected) =>
        Assert.Equal(expected, ErrorFingerprint.NormaliseFrame(frame));

    [Theory]
    [InlineData("System.Threading.Tasks.Task.Wait")]
    [InlineData("Microsoft.Data.SqlClient.SqlCommand.ExecuteReader")]
    [InlineData("")]
    [InlineData(null)]
    public void Frames_outside_Etp_have_no_site(string? frame) =>
        Assert.Null(ErrorFingerprint.NormaliseFrame(frame));

    [Fact]
    public void Site_is_the_innermost_Etp_frame_and_empty_without_a_stack()
    {
        string[] stack =
        [
            "Microsoft.Data.SqlClient.SqlCommand.ExecuteReader",
            "Etp.Reporting.Infrastructure.SqlServer.SqlServerApplicationReportQuery.LoadStaffPerformanceAsync",
            "Etp.Reporting.Desktop.Modules.Reports.ReportsWorkspaceView+<RunAsync>d__4.MoveNext"
        ];

        Assert.Equal("SqlServerApplicationReportQuery.LoadStaffPerformanceAsync", ErrorFingerprint.SiteOf(stack));
        Assert.Equal(string.Empty, ErrorFingerprint.SiteOf(null));
        Assert.Equal(string.Empty, ErrorFingerprint.SiteOf(["System.Foo.Bar"]));
    }

    [Fact]
    public void Fingerprint_is_sixty_four_lowercase_hex_and_matches_the_documented_legacy_value()
    {
        var parts = ErrorFingerprint.PartsOf(Entry());
        var fingerprint = ErrorFingerprint.Compute(parts);

        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
        // ER-0003 of the design (REPORT_STORE_REQUIRED, no stack, no SQL number); also the line in ErrorFixManifest.json.
        Assert.Equal("a46f2ccbec1c1b4f6c1f26ba228ec5527cae851e8443662c7bed03a4172658c4", fingerprint);
    }

    [Fact]
    public void Context_and_version_are_not_identity_but_the_site_and_exception_type_are()
    {
        string[] stack = ["Etp.Reporting.Desktop.Foo.Run"];
        var a = ErrorFingerprint.Compute(ErrorFingerprint.PartsOf(Entry(stack: stack, reportCode: "dsr", storeCode: "AAA", businessDate: "2026-10-01", version: "1.9.6.0", reference: "AAAAAAAAAAAA")));
        var b = ErrorFingerprint.Compute(ErrorFingerprint.PartsOf(Entry(stack: stack, reportCode: "cash", storeCode: "MULTIPLE", businessDate: "2026-10-09", version: "1.9.9.0", reference: "BBBBBBBBBBBB", operation: "Run report")));
        var otherSite = ErrorFingerprint.Compute(ErrorFingerprint.PartsOf(Entry(stack: ["Etp.Reporting.Desktop.Foo.Other"])));
        var otherType = ErrorFingerprint.Compute(ErrorFingerprint.PartsOf(Entry(stack: stack, exceptionType: "System.ArgumentException")));

        Assert.Equal(a, b);
        Assert.NotEqual(a, otherSite);
        Assert.NotEqual(a, otherType);
    }

    [Fact]
    public void Fields_are_joined_with_separators_so_a_shifted_part_does_not_collide()
    {
        var one = ErrorFingerprint.Compute(new ErrorFingerprintParts("A", "B", "C", "", "D", 0));
        var two = ErrorFingerprint.Compute(new ErrorFingerprintParts("A", "", "B", "C", "D", 0));

        Assert.NotEqual(one, two);
    }

    private static ErrorRegisterEntry Registered(string fingerprint, string? site, string eventCode = "REPORT_STORE_REQUIRED",
        string source = "Reports.Workspace", string exceptionType = "System.InvalidOperationException", int sql = 0, int? mergedInto = null) =>
        new(1, fingerprint, 1, eventCode, null, source, site, exceptionType, sql, ErrorRegisterSeverity.Error, ErrorStatus.New,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, "1.9.9.0", "1.9.9.0", null, null, null, null, null, [], null,
            DateTimeOffset.UnixEpoch, "x", mergedInto);

    [Fact]
    public void Old_entry_without_a_stack_attaches_to_the_only_entry_with_the_same_other_parts()
    {
        var legacy = ErrorFingerprint.PartsOf(Entry());
        var match = Registered(new string('a', 64), "ReportsWorkspaceView.RunAsync");

        var found = ErrorFingerprint.SoftMatch(legacy, [match, Registered(new string('b', 64), "X.Y", exceptionType: "System.ArgumentException")]);

        Assert.Same(match, found);
    }

    [Fact]
    public void Old_entry_keeps_its_legacy_fingerprint_for_zero_or_several_matches_a_merged_entry_or_an_exact_legacy_entry()
    {
        var legacy = ErrorFingerprint.PartsOf(Entry());
        var own = ErrorFingerprint.Compute(legacy);

        Assert.Null(ErrorFingerprint.SoftMatch(legacy, []));
        Assert.Null(ErrorFingerprint.SoftMatch(legacy, [Registered(new string('a', 64), "A.B"), Registered(new string('b', 64), "C.D")]));
        Assert.Null(ErrorFingerprint.SoftMatch(legacy, [Registered(new string('a', 64), "A.B", mergedInto: 7)]));
        Assert.Null(ErrorFingerprint.SoftMatch(legacy, [Registered(own, ""), Registered(new string('a', 64), "A.B")]));
        Assert.Null(ErrorFingerprint.SoftMatch(legacy, [Registered(new string('a', 64), "A.B", sql: 51402)]));
    }

    [Fact]
    public void Two_rows_with_the_same_fingerprint_count_as_one_match()
    {
        var legacy = ErrorFingerprint.PartsOf(Entry());
        var first = Registered(new string('a', 64), "A.B");

        Assert.NotNull(ErrorFingerprint.SoftMatch(legacy, [first, first]));
    }
}
