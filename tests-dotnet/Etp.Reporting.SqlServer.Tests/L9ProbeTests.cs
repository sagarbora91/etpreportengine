using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Audit;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using MailKit.Security;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>1.11.2 L9: PC-name tokens, SMTP TLS rule, DB-05 6 GB, SEC-04, DB-06 and OPS-10, without a database.</summary>
public sealed class L9ProbeTests
{
    [Theory]
    [InlineData("SHOP-PC-1", "SHOP-PC-1")]
    [InlineData("SAGAR'S PC", "SAGAR_S_PC")]
    [InlineData("A&B Jewellers", "A_B_Jewellers")]
    [InlineData("रमेश-PC", "____-PC")]
    [InlineData("pc,1;2=3", "pc_1_2_3")]
    [InlineData("  ", "unknown")]
    [InlineData(null, "unknown")]
    public void Machine_names_become_diag_tokens(string? name, string expected) =>
        Assert.Equal(expected, HealthMachineName.ToToken(name));

    [Fact]
    public void A_long_machine_name_is_cut_to_64_characters() =>
        Assert.Equal(64, HealthMachineName.ToToken(new string('x', 200)).Length);

    [Theory]
    [InlineData("smtp.example.com", 587, true, SecureSocketOptions.StartTls)]
    [InlineData("smtp.example.com", 465, true, SecureSocketOptions.SslOnConnect)]
    [InlineData("localhost", 25, false, SecureSocketOptions.None)]
    [InlineData("127.0.0.1", 25, false, SecureSocketOptions.None)]
    [InlineData("[::1]", 25, false, SecureSocketOptions.None)]
    public void Smtp_uses_tls_except_for_a_server_on_this_pc(string host, int port, bool tls, SecureSocketOptions expected) =>
        Assert.Equal(expected, MailKitReportEmailTransport.SecurityFor(new SmtpConnection(host, port, tls, "a@b.example")));

    [Theory]
    [InlineData("smtp.example.com")]
    [InlineData("192.168.1.20")]
    [InlineData("localhost.example.com")]
    public void Smtp_without_tls_is_refused_for_any_other_host(string host) =>
        Assert.Throws<InvalidOperationException>(() =>
            MailKitReportEmailTransport.SecurityFor(new SmtpConnection(host, 25, false, "a@b.example")));

    [Fact]
    public void DB05_warns_from_6_GB_on_a_10_GB_limit_only()
    {
        Assert.Equal("DB_SIZE_HIGH", DatabaseProbes.EvaluateSize(6200m, 10240m, 80m).FindingCode);
        Assert.Equal(HealthOutcome.Pass, DatabaseProbes.EvaluateSize(6000m, 10240m, 80m).Outcome);
        Assert.Equal(HealthOutcome.Pass, DatabaseProbes.EvaluateSize(6200m, 51200m, 80m).Outcome);
    }

    [Fact]
    public void L9_sql_texts_are_read_only_and_names_are_quoted()
    {
        const string hostile = "EtpAudit_X]; DROP TABLE y;--";
        Assert.Empty(ReadOnlySqlGuard.Violations(LandingCopyProbe.OtherDatabasesSql));
        Assert.Empty(ReadOnlySqlGuard.Violations(LandingCopyProbe.LandingCountSql("EtpAudit_HEMW")));
        Assert.Contains("[EtpAudit_X]]; DROP TABLE y;--]", LandingCopyProbe.LandingCountSql(hostile));
        foreach (var (_, view) in ServiceScreenTimingProbe.Views)
            Assert.Empty(ReadOnlySqlGuard.Violations(ServiceScreenTimingProbe.CountSql(view)));
    }

    [Fact]
    public void SEC04_warns_on_a_landing_copy_and_skips_what_it_cannot_read()
    {
        var warn = LandingCopyProbe.Evaluate([0, 12, null], TimeSpan.Zero);
        Assert.Equal(HealthOutcome.Warning, warn.Outcome);
        Assert.Equal(1m, warn.MetricValue);
        Assert.Equal(["DATABASE_2", "DATABASE_3"], warn.SubResults!.Select(sub => sub.Key));
        Assert.Equal(HealthOutcome.Pass, LandingCopyProbe.Evaluate([], TimeSpan.Zero).Outcome);
        Assert.Equal(HealthOutcome.Pass, LandingCopyProbe.Evaluate([0, 0], TimeSpan.Zero).Outcome);
        Assert.Equal(HealthOutcome.Skipped, LandingCopyProbe.Evaluate([null], TimeSpan.Zero).Outcome);
    }

    [Fact]
    public void DB06_warns_above_one_second_and_skips_missing_views()
    {
        var fast = ServiceScreenTimingProbe.Evaluate([("JOBS", TimeSpan.FromMilliseconds(40)), ("PARTS", null)], TimeSpan.Zero);
        Assert.Equal(HealthOutcome.Pass, fast.Outcome);
        var slow = ServiceScreenTimingProbe.Evaluate([("JOBS", TimeSpan.FromMilliseconds(1500))], TimeSpan.Zero);
        Assert.Equal(HealthOutcome.Warning, slow.Outcome);
        Assert.Equal(1500m, slow.MetricValue);
        Assert.Equal(HealthOutcome.Skipped, ServiceScreenTimingProbe.Evaluate([("JOBS", null)], TimeSpan.Zero).Outcome);
    }

    private sealed class Env(string? root, string key, bool running) : IOffPcCopyEnvironment
    {
        public string? OneDriveRoot() => root;
        public string KeyFilePath() => key;
        public bool OneDriveIsRunning() => running;
    }

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    private static readonly HealthCheckContext Context = new(new DateOnly(2026, 10, 11), HealthCheckTrigger.Manual,
        DateTimeOffset.UtcNow, [], null, HealthCheckBudget.Default, new Dictionary<string, object>());

    private static string TempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "etp-l9-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<HealthCheckResult> Run(OffPcCopyProbe probe) => Assert.Single(await probe.RunAsync(Context, default));

    [Fact]
    public async Task OPS10_is_skipped_without_onedrive_or_a_backup_folder()
    {
        Assert.Equal(HealthOutcome.Skipped, (await Run(new OffPcCopyProbe(new Env(null, "k", true)))).Outcome);
        var root = TempFolder();
        try { Assert.Equal(HealthOutcome.Skipped, (await Run(new OffPcCopyProbe(new Env(root, "k", true)))).Outcome); }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OPS10_checks_age_key_and_onedrive()
    {
        var root = TempFolder();
        var keys = TempFolder();
        try
        {
            var backups = Path.Combine(root, OffPcCopyProbe.BackupFolderName);
            Directory.CreateDirectory(backups);
            var copy = Path.Combine(backups, "EtpReporting-20261011-220000.bak.enc");
            File.WriteAllText(copy, "x");
            var key = Path.Combine(keys, "k.key");
            File.WriteAllText(key, "k");
            var now = File.GetLastWriteTimeUtc(copy);

            var good = await Run(new OffPcCopyProbe(new Env(root, key, true), new FixedClock(now.AddHours(20))));
            Assert.Equal(HealthOutcome.Pass, good.Outcome);

            var old = await Run(new OffPcCopyProbe(new Env(root, key, true), new FixedClock(now.AddHours(40))));
            Assert.Equal(HealthOutcome.Warning, old.Outcome);
            Assert.Equal("OFF_PC_COPY_OLD", old.FindingCode);

            var stopped = await Run(new OffPcCopyProbe(new Env(root, key, false), new FixedClock(now.AddHours(1))));
            Assert.Equal("ONEDRIVE_NOT_RUNNING", stopped.FindingCode);

            var missingKey = await Run(new OffPcCopyProbe(new Env(root, Path.Combine(keys, "none.key"), true), new FixedClock(now.AddHours(1))));
            Assert.Equal("KEY_MISSING", missingKey.FindingCode);

            var inside = Path.Combine(backups, "k.key");
            File.WriteAllText(inside, "k");
            var leaked = await Run(new OffPcCopyProbe(new Env(root, inside, true), new FixedClock(now.AddHours(1))));
            Assert.Equal(HealthOutcome.Fail, leaked.Outcome);
            Assert.Equal("KEY_IN_ONEDRIVE", leaked.FindingCode);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(keys, true);
        }
    }

    [Fact]
    public async Task OPS10_without_any_copy_is_a_warning()
    {
        var root = TempFolder();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, OffPcCopyProbe.BackupFolderName));
            var result = await Run(new OffPcCopyProbe(new Env(root, "missing.key", true)));
            Assert.Equal(HealthOutcome.Warning, result.Outcome);
            Assert.Equal("NO_OFF_PC_COPY", result.FindingCode);
        }
        finally { Directory.Delete(root, true); }
    }
}
