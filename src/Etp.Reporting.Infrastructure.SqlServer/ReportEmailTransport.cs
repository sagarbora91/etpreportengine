using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record SmtpConnection(string Host, int Port, bool UseTls, string FromAddress);
public sealed record ReportEmail(string To, string? Cc, string Subject, string Body, ReportEmailAttachment? Attachment);
public sealed record SmtpCredential(string UserName, string Password);
public sealed record SmtpTransportResult(string Outcome, string Message);
public interface IReportEmailTransport
{
    Task<SmtpTransportResult> SendAsync(SmtpConnection settings, ReportEmail email, CancellationToken token);
}

public sealed class MailKitReportEmailTransport(Func<SmtpConnection, SmtpCredential?>? loadCredential = null) : IReportEmailTransport
{
    public async Task<SmtpTransportResult> SendAsync(SmtpConnection settings, ReportEmail email, CancellationToken token)
    {
        using var message = new MimeMessage();
        message.From.Add(ParseAddress(settings.FromAddress));
        message.To.AddRange(ParseRecipients(email.To));
        if (!string.IsNullOrWhiteSpace(email.Cc)) message.Cc.AddRange(ParseRecipients(email.Cc));
        message.Subject = email.Subject;
        var body = new BodyBuilder { TextBody = email.Body };
        // The bytes the service read from the validated file; the path is never reopened here.
        if (email.Attachment is { } attachment) body.Attachments.Add(attachment.FileName, attachment.Content);
        message.Body = body.ToMessageBody();
        using var smtp = new SmtpClient { Timeout = 30000 };
        var sending = false;
        try
        {
            var security = SecurityFor(settings);
            await smtp.ConnectAsync(settings.Host, settings.Port, security, token).ConfigureAwait(false);
            var credential = (loadCredential ?? WindowsSmtpCredentials.Load)(settings);
            if (credential is not null)
            {
                if (!smtp.IsSecure) throw new InvalidOperationException("SMTP credentials require TLS.");
                await smtp.AuthenticateAsync(credential.UserName, credential.Password, token).ConfigureAwait(false);
            }
            sending = true;
            await smtp.SendAsync(message, token).ConfigureAwait(false);
            // The SMTP server accepted the message; an inbox receipt is outside this protocol.
            return new("SMTP_ACCEPTED", "SMTP server accepted the email. Recipient delivery has not been confirmed.");
        }
        catch (SmtpCommandException)
        { return new("FAILED", "The SMTP server refused the message. Check the sender, recipient and server settings."); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return sending
                ? new("UNKNOWN", "SMTP confirmation was lost. Check the recipient or mail server before retrying to avoid duplicates.")
                : new("FAILED", "Email was not submitted. Check SMTP settings, TLS and this Windows user's saved SMTP credentials.");
        }
        finally
        {
            if (smtp.IsConnected)
                try { await smtp.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { /* Acceptance is unaffected by QUIT failure. */ }
        }
    }

    /// <summary>
    /// S-15: TLS is always used (SSL on connect for port 465, otherwise STARTTLS required). The only exception is a
    /// server on this PC (localhost, 127.0.0.1, ::1) with <c>UseTls=false</c>, where nothing crosses a network.
    /// </summary>
    internal static SecureSocketOptions SecurityFor(SmtpConnection settings)
    {
        if (!settings.UseTls)
        {
            if (IsLoopbackHost(settings.Host)) return SecureSocketOptions.None;
            throw new InvalidOperationException("SMTP without TLS is only allowed for a server on this PC.");
        }
        return settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
    }

    internal static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var value = host.Trim().Trim('[', ']');
        if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return System.Net.IPAddress.TryParse(value, out var address) && System.Net.IPAddress.IsLoopback(address);
    }

    public static MailboxAddress ParseAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['\r', '\n']) >= 0 || !MailboxAddress.TryParse(value, out var address))
            throw new ArgumentException("Enter a valid email address.");
        return address;
    }

    public static InternetAddressList ParseRecipients(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['\r', '\n']) >= 0 || !InternetAddressList.TryParse(value, out var addresses)
            || addresses.Count == 0 || addresses.Any(address => address is not MailboxAddress))
            throw new ArgumentException("Enter valid email recipients separated by commas.");
        return addresses;
    }
}
