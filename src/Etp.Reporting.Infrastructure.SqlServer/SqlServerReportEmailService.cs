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
        // Decision 45 #4 (OUT-13): the name the recipient sees on the phone, without a GUID; the generation id keeps it
        // unique and a second copy gets " (2)" rather than overwriting a file that may be open.
        var path = ReportPackShareText.UniquePath(folder, ReportPackShareText.SharePdfFileName(document, generationId));
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
        var attachmentPath = string.IsNullOrWhiteSpace(command.AttachmentPath) ? command.AttachmentPath : Path.GetFullPath(command.AttachmentPath);
        await ValidateEmailAttachmentAsync(attachmentPath, cancellationToken).ConfigureAwait(false);
        var productSettings = await gateway.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var settings = SmtpSettings(productSettings);
        _ = MailKitReportEmailTransport.ParseRecipients(command.To);
        if (!string.IsNullOrWhiteSpace(command.Cc)) _ = MailKitReportEmailTransport.ParseRecipients(command.Cc);
        // The path check above can be outrun by a swap in the sharing folder. What is sent is
        // read now from one held handle that is itself proved to be inside the folder (S-02).
        var attachment = HeldReportAttachment.Read(attachmentPath!, productSettings.ShareFolderPath, productSettings.MaximumAttachmentMb);
        var key = Guid.NewGuid();
        var (subject, body) = await EmailTextAsync(command.GenerationId, cancellationToken).ConfigureAwait(false);
        await Record("INITIATED", "SMTP submission started. If no final outcome follows, check the mail server before retrying.");
        SmtpTransportResult result;
        try
        {
            result = await emailTransport.SendAsync(settings, new(command.To, command.Cc,
                subject, body, attachment), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { result = new("UNKNOWN", "SMTP outcome could not be confirmed. Check the mail server before retrying."); }
        try { await Record(result.Outcome, result.Message); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { throw new InvalidOperationException($"{result.Message} The final history entry could not be saved; do not retry until the mail server has been checked.", ex); }
        return new(result.Outcome, result.Message);

        Task Record(string outcome, string message) => gateway.RecordShareAttemptAsync(command.GenerationId, null, "EMAIL", "Configured recipient",
            Path.GetFileName(attachmentPath), outcome, message, CancellationToken.None, key);
    }

    /// <summary>
    /// Decision 45 #4 (OUT-15): subject and body name the shop, the day and the verdict, read from the archived pack. When
    /// the pack cannot be read here the e-mail still goes, with the generation number only.
    /// </summary>
    private async Task<(string Subject, string Body)> EmailTextAsync(long generationId, CancellationToken cancellationToken)
    {
        try
        {
            var document = await loadDocument(generationId, cancellationToken).ConfigureAwait(false);
            return (ReportPackShareText.EmailSubject(document), ReportPackShareText.EmailBody(document, generationId));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return ($"ETP daily pack · generation {generationId}", $"The ETP daily reporting pack (generation {generationId}) is the attached PDF.");
        }
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
