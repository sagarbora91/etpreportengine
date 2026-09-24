using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class MailKitReportEmailTransportTests
{
    [Theory]
    [InlineData("ACCEPT", "SMTP_ACCEPTED")]
    [InlineData("REFUSE", "FAILED")]
    [InlineData("DROP", "UNKNOWN")]
    public async Task Actual_MailKit_transport_uses_loopback_SMTP_and_reports_observable_outcomes(string behavior, string expected)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var attachment = Path.Combine(Path.GetTempPath(), "smtp-test-" + Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllTextAsync(attachment, "%PDF-1.4 synthetic test attachment", timeout.Token);
        try
        {
            var server = ServeAsync(listener, behavior, timeout.Token);
            var result = await new MailKitReportEmailTransport(_ => null).SendAsync(new("127.0.0.1", port, false, "sender@example.invalid"),
                new("recipient@example.invalid", null, "Synthetic test", "No customer data", attachment), timeout.Token);
            Assert.Equal(expected, result.Outcome);
            var mime = await server;
            if (behavior != "REFUSE") { Assert.Contains("application/pdf", mime); Assert.Contains("Content-Transfer-Encoding: base64", mime); }
        }
        finally { File.Delete(attachment); }
    }

    private static async Task<string> ServeAsync(TcpListener listener, string behavior, CancellationToken token)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 loopback.test ESMTP".AsMemory(), token);
        var mime = new StringBuilder();
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (line.StartsWith("EHLO ") || line.StartsWith("HELO ")) await writer.WriteLineAsync("250 loopback.test".AsMemory(), token);
            else if (line.StartsWith("MAIL FROM")) await writer.WriteLineAsync((behavior == "REFUSE" ? "550 Sender refused" : "250 Sender accepted").AsMemory(), token);
            else if (line.StartsWith("RCPT TO")) await writer.WriteLineAsync("250 Recipient accepted".AsMemory(), token);
            else if (line == "DATA")
            {
                await writer.WriteLineAsync("354 Send data".AsMemory(), token);
                while (await reader.ReadLineAsync(token) is { } data && data != ".") mime.AppendLine(data);
                if (behavior == "DROP") return mime.ToString();
                await writer.WriteLineAsync("250 Accepted".AsMemory(), token);
            }
            else if (line == "QUIT") { await writer.WriteLineAsync("221 Bye".AsMemory(), token); break; }
            else await writer.WriteLineAsync("250 OK".AsMemory(), token);
        }
        return mime.ToString();
    }
}
