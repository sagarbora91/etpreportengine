using App = Etp.Reporting.Application.Distribution;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class ReportEmailServiceTests
{
    [Theory]
    [InlineData("SMTP_ACCEPTED")]
    [InlineData("FAILED")]
    [InlineData("UNKNOWN")]
    public async Task Email_records_correlated_start_and_final_outcome_with_saved_settings(string outcome)
    {
        var gateway = new Gateway(); var transport = new Transport(outcome);
        var service = Service(gateway, transport);
        var result = await service.SendEmailAsync(new(42, "pack.pdf", "owner@example.invalid", "reviewer@example.invalid"));
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(new[] { "INITIATED", outcome }, gateway.Attempts.Select(row => row.Outcome));
        Assert.NotNull(gateway.Attempts[0].AttemptKey);
        Assert.Equal(gateway.Attempts[0].AttemptKey, gateway.Attempts[1].AttemptKey);
        Assert.All(gateway.Attempts, row => Assert.Equal(42, row.GenerationId));
        Assert.Equal("smtp.example.invalid", transport.Connection!.Host);
        Assert.Equal(587, transport.Connection.Port); Assert.True(transport.Connection.UseTls);
        Assert.Equal("sender@example.invalid", transport.Connection.FromAddress);
        Assert.Equal("pack.pdf", transport.Email!.AttachmentPath);
        Assert.Equal("reviewer@example.invalid", transport.Email.Cc);
        Assert.DoesNotContain(gateway.Attempts, row => row.SafeMessage.Contains("owner@example.invalid"));
    }

    [Fact]
    public async Task Email_will_not_send_unless_start_history_is_durable()
    {
        var gateway = new Gateway { FailRecord = true }; var transport = new Transport("SMTP_ACCEPTED");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(gateway, transport).SendEmailAsync(new(42, "pack.pdf", "owner@example.invalid")));
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Transport_exception_has_unknown_outcome_and_is_never_labelled_delivered()
    {
        var gateway = new Gateway(); var transport = new Transport("SMTP_ACCEPTED") { Throw = true };
        var result = await Service(gateway, transport).SendEmailAsync(new(42, "pack.pdf", "owner@example.invalid"));
        Assert.Equal("UNKNOWN", result.Outcome);
        Assert.Equal("UNKNOWN", gateway.Attempts.Last().Outcome);
        Assert.DoesNotContain("private failure", result.Message);
    }

    [Fact]
    public async Task No_access_invalid_recipient_or_oversize_attachment_cannot_send()
    {
        var gateway = new Gateway(); var transport = new Transport("SMTP_ACCEPTED");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(gateway, transport, ApplicationRole.None).SendEmailAsync(new(1, "pack.pdf", "owner@example.invalid")));
        await Assert.ThrowsAsync<ArgumentException>(() => Service(gateway, transport).SendEmailAsync(new(1, "pack.pdf", "owner@example.invalid\r\nBcc: stolen@example.invalid")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(gateway, transport, size: 21 * 1024L * 1024).SendEmailAsync(new(1, "pack.pdf", "owner@example.invalid")));
        Assert.Equal(0, transport.Calls); Assert.Empty(gateway.Attempts);
    }

    [Fact]
    public async Task Test_send_is_owner_only_and_never_attaches_business_data()
    {
        var gateway = new Gateway(); var transport = new Transport("SMTP_ACCEPTED");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(gateway, transport).TestEmailAsync("owner@example.invalid"));
        await Service(gateway, transport, ApplicationRole.Owner).TestEmailAsync("owner@example.invalid");
        Assert.Equal(1, transport.Calls); Assert.Null(transport.Email!.AttachmentPath); Assert.Empty(gateway.Attempts);
    }

    private static SqlServerReportDistributionService Service(Gateway gateway, Transport transport, ApplicationRole role = ApplicationRole.Viewer, long size = 5) =>
        new(gateway, _ => Task.FromResult(new ApplicationAccess("test", "test", role, true)),
            (_, _, _, _, _, _, _) => throw new NotSupportedException(), _ => true, _ => size, transport);
    private sealed class Transport(string outcome) : IReportEmailTransport
    {
        public int Calls; public bool Throw; public SmtpConnection? Connection; public ReportEmail? Email;
        public Task<SmtpTransportResult> SendAsync(SmtpConnection settings, ReportEmail email, CancellationToken token)
        {
            Calls++; Connection = settings; Email = email;
            if (Throw) throw new IOException("private failure with host details");
            return Task.FromResult(new SmtpTransportResult(outcome, "Safe transport result."));
        }
    }
    private sealed class Gateway : IDistributionSqlGateway
    {
        public bool FailRecord; public List<App.RecordDistributionAttempt> Attempts { get; } = [];
        public Task<IReadOnlyList<InvestigationResult>> SearchAsync(string term, int limit, CancellationToken token) => throw new NotSupportedException();
        public Task<ProductSettings> LoadSettingsAsync(CancellationToken token) => Task.FromResult(new ProductSettings("documents", "share", "smtp.example.invalid", 587, true, "sender@example.invalid", 20, DateTime.UtcNow, "Owner"));
        public Task RecordPackageAsync(long generationId, string packageType, string path, string manifest, string hash, bool final, CancellationToken token) => throw new NotSupportedException();
        public Task RecordShareAttemptAsync(long generationId, long? packageId, string channel, string? destination, string path, string outcome, string message, CancellationToken token, Guid? attemptKey = null)
        {
            if (FailRecord) throw new InvalidOperationException("Synthetic history failure");
            Attempts.Add(new(generationId, packageId, channel, destination, path, outcome, message, attemptKey));
            return Task.CompletedTask;
        }
    }
}
