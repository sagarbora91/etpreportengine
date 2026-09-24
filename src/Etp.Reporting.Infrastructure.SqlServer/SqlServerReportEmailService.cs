using App = Etp.Reporting.Application.Distribution;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed partial class SqlServerReportDistributionService
{
    public async Task<string> PreparePdfAsync(long generationId, CancellationToken cancellationToken = default)
    {
        if (generationId <= 0) throw new ArgumentOutOfRangeException(nameof(generationId));
        await SqlServerInvestigationQuery.RequireViewAsync(loadAccess, cancellationToken).ConfigureAwait(false);
        var document = await loadDocument(generationId, cancellationToken).ConfigureAwait(false);
        var settings = await gateway.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var folder = Path.GetFullPath(settings.ShareFolderPath);
        for (var candidate = folder; !string.IsNullOrEmpty(candidate); candidate = Path.GetDirectoryName(candidate))
            if (Directory.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The sharing folder cannot use linked folders.");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"ETP_Pack_{generationId}_{document.DateTo:yyyyMMdd}_{Guid.NewGuid():N}.pdf");
        await Task.Run(() => new SimplePdfReportPackExporter().Export(path, document), cancellationToken).ConfigureAwait(false);
        return path;
    }

    public async Task<IReadOnlyList<App.ShareDeliveryHistory>> LoadHistoryAsync(long generationId, CancellationToken cancellationToken = default)
    {
        await SqlServerInvestigationQuery.RequireViewAsync(loadAccess, cancellationToken).ConfigureAwait(false);
        return await gateway.LoadHistoryAsync(generationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<App.EmailSendResult> SendEmailAsync(App.SendReportEmail command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.GenerationId <= 0) throw new ArgumentOutOfRangeException(nameof(command.GenerationId));
        await ValidateEmailAttachmentAsync(command.AttachmentPath, cancellationToken).ConfigureAwait(false);
        var settings = SmtpSettings(await gateway.LoadSettingsAsync(cancellationToken).ConfigureAwait(false));
        _ = MailKitReportEmailTransport.ParseRecipients(command.To);
        if (!string.IsNullOrWhiteSpace(command.Cc)) _ = MailKitReportEmailTransport.ParseRecipients(command.Cc);
        var key = Guid.NewGuid();
        await Record("INITIATED", "SMTP submission started. If no final outcome follows, check the mail server before retrying.");
        SmtpTransportResult result;
        try
        {
            result = await emailTransport.SendAsync(settings, new(command.To, command.Cc,
                $"ETP report pack - generation {command.GenerationId}", "Please find the saved ETP report pack attached.", command.AttachmentPath), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { result = new("UNKNOWN", "SMTP outcome could not be confirmed. Check the mail server before retrying."); }
        try { await Record(result.Outcome, result.Message); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { throw new InvalidOperationException($"{result.Message} The final history entry could not be saved; do not retry until the mail server has been checked.", ex); }
        return new(result.Outcome, result.Message);

        Task Record(string outcome, string message) => gateway.RecordShareAttemptAsync(command.GenerationId, null, "EMAIL", "Configured recipient",
            Path.GetFileName(command.AttachmentPath), outcome, message, CancellationToken.None, key);
    }

    public async Task<App.EmailSendResult> TestEmailAsync(string recipient, CancellationToken cancellationToken = default)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
        _ = MailKitReportEmailTransport.ParseRecipients(recipient);
        var settings = SmtpSettings(await gateway.LoadSettingsAsync(cancellationToken).ConfigureAwait(false));
        var result = await emailTransport.SendAsync(settings, new(recipient, null, "ETP SMTP test",
            "This is the test message you requested from ETP Reporting. No report or business data is attached.", null), cancellationToken).ConfigureAwait(false);
        return new(result.Outcome, result.Message);
    }

    public async Task SaveSmtpCredentialsAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        await SqlServerInvestigationQuery.RequireViewAsync(loadAccess, cancellationToken).ConfigureAwait(false);
        WindowsSmtpCredentials.Save(SmtpSettings(await gateway.LoadSettingsAsync(cancellationToken).ConfigureAwait(false)), userName, password);
    }

    internal static SmtpConnection SmtpSettings(ProductSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) || settings.SmtpPort is not (>= 1 and <= 65535) || string.IsNullOrWhiteSpace(settings.SmtpFromAddress))
            throw new InvalidOperationException("Ask the Owner to save the SMTP host, port and sender in Settings > Email and sharing.");
        _ = MailKitReportEmailTransport.ParseAddress(settings.SmtpFromAddress);
        return new(settings.SmtpHost, settings.SmtpPort.Value, settings.SmtpUseTls, settings.SmtpFromAddress);
    }
}
