using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record SmtpConnection(string Host, int Port, bool UseTls, string FromAddress);
public sealed record ReportEmail(string To, string? Cc, string Subject, string Body, string? AttachmentPath);
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
        if (email.AttachmentPath is { } attachment) await body.Attachments.AddAsync(attachment, token).ConfigureAwait(false);
        message.Body = body.ToMessageBody();
        using var smtp = new SmtpClient { Timeout = 30000 };
        var sending = false;
        try
        {
            var security = settings.UseTls ? settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls : SecureSocketOptions.None;
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
