using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class AccountingBatchComposer
{
    public AccountingBatchDraft Compose(IReadOnlyList<AccountingBusinessEvent> events, IReadOnlyList<AccountingMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(events); ArgumentNullException.ThrowIfNull(mappings);
        var map = mappings.GroupBy(x => x.BusinessEvent, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);
        var entries = new List<AccountingEntryDraft>(); var missing = new List<string>(); var line = 1;
        foreach (var item in events.Where(x => x.Amount != 0))
        {
            if (!map.TryGetValue(item.EventCode, out var mapping)) { missing.Add(item.EventCode); continue; }
            var amount = Math.Abs(item.Amount);
            var narration = mapping.NarrationTemplate.Replace("{description}", item.Description, StringComparison.OrdinalIgnoreCase)
                .Replace("{reference}", item.SourceReference, StringComparison.OrdinalIgnoreCase);
            var debitLedger = item.Amount >= 0 ? mapping.DebitLedger : mapping.CreditLedger;
            var creditLedger = item.Amount >= 0 ? mapping.CreditLedger : mapping.DebitLedger;
            entries.Add(new(line++, item.EventCode, debitLedger, amount, 0, narration, mapping.CostCentre, item.SourceReference));
            entries.Add(new(line++, item.EventCode, creditLedger, 0, amount, narration, mapping.CostCentre, item.SourceReference));
        }
        var debit = entries.Sum(x => x.DebitAmount); var credit = entries.Sum(x => x.CreditAmount);
        return new(entries, debit, credit, debit == credit && missing.Count == 0, missing.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray());
    }
}

public sealed class TallyXmlExportService
{
    public async Task<string> ExportAsync(string path, string companyName, DateOnly businessDate,
        AccountingBatchDraft batch, CancellationToken cancellationToken = default)
    {
        if (!batch.IsBalanced || batch.DebitTotal != batch.CreditTotal) throw new InvalidOperationException("Only a balanced accounting batch with complete mappings can be exported.");
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); var temporary = full + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var settings = new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false), Indent = true };
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            await using (var writer = XmlWriter.Create(stream, settings))
            {
                await writer.WriteStartDocumentAsync(); await writer.WriteStartElementAsync(null, "ENVELOPE", null);
                await writer.WriteStartElementAsync(null, "HEADER", null); await writer.WriteElementStringAsync(null, "TALLYREQUEST", null, "Import Data"); await writer.WriteEndElementAsync();
                await writer.WriteStartElementAsync(null, "BODY", null); await writer.WriteStartElementAsync(null, "IMPORTDATA", null);
                await writer.WriteStartElementAsync(null, "REQUESTDESC", null); await writer.WriteElementStringAsync(null, "REPORTNAME", null, "Vouchers");
                await writer.WriteStartElementAsync(null, "STATICVARIABLES", null); await writer.WriteElementStringAsync(null, "SVCURRENTCOMPANY", null, companyName); await writer.WriteEndElementAsync(); await writer.WriteEndElementAsync();
                await writer.WriteStartElementAsync(null, "REQUESTDATA", null); await writer.WriteStartElementAsync(null, "TALLYMESSAGE", null);
                await writer.WriteStartElementAsync(null, "VOUCHER", null); await writer.WriteAttributeStringAsync(null, "VCHTYPE", null, "Journal"); await writer.WriteAttributeStringAsync(null, "ACTION", null, "Create");
                await writer.WriteElementStringAsync(null, "DATE", null, businessDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                await writer.WriteElementStringAsync(null, "NARRATION", null, $"ETP controlled accounting batch {businessDate:dd-MMM-yyyy}");
                foreach (var entry in batch.Entries)
                {
                    await writer.WriteStartElementAsync(null, "ALLLEDGERENTRIES.LIST", null);
                    await writer.WriteElementStringAsync(null, "LEDGERNAME", null, entry.LedgerName);
                    await writer.WriteElementStringAsync(null, "ISDEEMEDPOSITIVE", null, entry.DebitAmount > 0 ? "Yes" : "No");
                    var tallyAmount = entry.DebitAmount > 0 ? -entry.DebitAmount : entry.CreditAmount;
                    await writer.WriteElementStringAsync(null, "AMOUNT", null, tallyAmount.ToString("0.00", CultureInfo.InvariantCulture));
                    await writer.WriteEndElementAsync();
                }
                await writer.WriteEndElementAsync(); await writer.WriteEndElementAsync(); await writer.WriteEndElementAsync(); await writer.WriteEndElementAsync(); await writer.WriteEndElementAsync(); await writer.WriteEndElementAsync();
                await writer.WriteEndDocumentAsync(); await writer.FlushAsync();
            }
            File.Move(temporary, full, true); return await HashAsync(full, cancellationToken).ConfigureAwait(false);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }

    private static async Task<string> HashAsync(string path,CancellationToken token){await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,64*1024,true);return Convert.ToHexString(await SHA256.HashDataAsync(stream,token)).ToLowerInvariant();}
}

public static class SafeShareLauncher
{
    public static Uri CreateWhatsAppUri(string message, string? phoneE164 = null)
    {
        var encoded = Uri.EscapeDataString(message ?? string.Empty); var phone = new string((phoneE164 ?? string.Empty).Where(char.IsDigit).ToArray());
        return new Uri(phone.Length == 0 ? $"https://wa.me/?text={encoded}" : $"https://wa.me/{phone}?text={encoded}");
    }

    public static void OpenWhatsApp(string message, string? phoneE164 = null) =>
        Process.Start(new ProcessStartInfo(CreateWhatsAppUri(message, phoneE164).AbsoluteUri) { UseShellExecute = true });

    public static string CreateEmailDraft(string shareFolder, string attachmentPath, string to, string? cc, string subject, string message)
    {
        var attachment = Path.GetFullPath(attachmentPath); if (!File.Exists(attachment)) throw new FileNotFoundException("The report attachment was not found.", attachment);
        var folder = Path.GetFullPath(shareFolder); Directory.CreateDirectory(folder); var path = Path.Combine(folder, $"ETP_Email_Draft_{DateTime.Now:yyyyMMdd_HHmmss}.eml");
        var boundary = "=_EtpReporting_" + Guid.NewGuid().ToString("N"); var builder = new StringBuilder();
        builder.AppendLine($"To: {SanitizeHeader(to)}"); if (!string.IsNullOrWhiteSpace(cc)) builder.AppendLine($"Cc: {SanitizeHeader(cc)}");
        builder.AppendLine($"Subject: {SanitizeHeader(subject)}"); builder.AppendLine("MIME-Version: 1.0"); builder.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\""); builder.AppendLine();
        builder.AppendLine($"--{boundary}"); builder.AppendLine("Content-Type: text/plain; charset=utf-8"); builder.AppendLine(); builder.AppendLine(message);
        builder.AppendLine($"--{boundary}"); builder.AppendLine($"Content-Type: application/octet-stream; name=\"{SanitizeHeader(Path.GetFileName(attachment))}\"");
        builder.AppendLine("Content-Transfer-Encoding: base64"); builder.AppendLine($"Content-Disposition: attachment; filename=\"{SanitizeHeader(Path.GetFileName(attachment))}\""); builder.AppendLine();
        builder.AppendLine(Convert.ToBase64String(File.ReadAllBytes(attachment), Base64FormattingOptions.InsertLineBreaks)); builder.AppendLine($"--{boundary}--");
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false)); return path;
    }

    private static string SanitizeHeader(string value)
    {
        if (value.Contains('\r') || value.Contains('\n')) throw new SecurityException("Email headers cannot contain line breaks.");
        return value.Trim();
    }
}
