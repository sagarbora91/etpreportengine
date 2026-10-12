using System.Globalization;
using System.Text;
using System.Xml;
using Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <summary>Master plan 7a task 8 (1.12 D1): writes approved Tally Sales vouchers as Tally's standard import envelope, one
/// <c>VOUCHER VCHTYPE="Sales" ACTION="Create"</c> per invoice, accounting view only (D17). Amounts keep Tally's sign: a debit is
/// negative with ISDEEMEDPOSITIVE Yes. The tender ledger is the party (D14, D16). The voucher total is what the customer paid,
/// GST included; revenue and each GST ledger are credited separately. The correspondence key goes into the narration so the
/// read-back finds the voucher again. Tags follow TallyPrime's published XML; the installed build is checked at the VM visit.</summary>
public static class TallySalesVoucherXmlWriter
{
    public const string PrimaryCostCategory = "Primary Cost Category";
    public const string AccountingView = "Accounting Voucher View";

    public static async Task WriteAsync(Stream stream, TallyProfile profile, IReadOnlyList<AccountingVoucherDraft> vouchers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var bytes = Write(profile, vouchers);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The file's bytes (UTF-8, no byte-order mark). Throws <see cref="InvalidOperationException"/> for anything Tally
    /// could misread: an unbalanced voucher, a line with both sides or neither, more than two decimals, a missing key.</summary>
    public static byte[] Write(TallyProfile profile, IReadOnlyList<AccountingVoucherDraft> vouchers)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(vouchers);
        if (TallySalesVoucherComposer.Unsupported(profile) is { } unsupported) throw new InvalidOperationException(unsupported);
        if (profile.VoucherView != "ACCOUNTING")
            throw new InvalidOperationException("Only the accounting voucher view is written (D17). Change the voucher view of this Tally company to accounting.");
        var company = (profile.CompanyName ?? "").Trim();
        if (company.Length == 0) throw new InvalidOperationException("The Tally company has no name.");
        if (vouchers.Count == 0) throw new InvalidOperationException("There is no voucher to write.");
        foreach (var voucher in vouchers) Check(voucher);
        var keys = vouchers.GroupBy(voucher => voucher.CorrespondenceKey, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (keys.Length > 0) throw new InvalidOperationException($"The same voucher appears twice in the file: {string.Join(", ", keys)}.");

        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, NewLineChars = "\r\n", CheckCharacters = true };
        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartElement("ENVELOPE");
            writer.WriteStartElement("HEADER");
            writer.WriteElementString("TALLYREQUEST", "Import Data");
            writer.WriteEndElement();
            writer.WriteStartElement("BODY");
            writer.WriteStartElement("IMPORTDATA");
            writer.WriteStartElement("REQUESTDESC");
            writer.WriteElementString("REPORTNAME", "Vouchers");
            writer.WriteStartElement("STATICVARIABLES");
            writer.WriteElementString("SVCURRENTCOMPANY", company);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement("REQUESTDATA");
            foreach (var voucher in vouchers.OrderBy(item => item.Sequence))
            {
                writer.WriteStartElement("TALLYMESSAGE");
                WriteVoucher(writer, voucher);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        return stream.ToArray();
    }

    /// <summary>RULE-PAY-001: reads <paramref name="content"/> back with <see cref="TallyVoucherXmlReader"/> and checks it carries
    /// the company, the same number of vouchers, each key exactly once, and the same type, date and signed ledger lines.
    /// Null when it does; otherwise the first difference in one sentence.</summary>
    public static string? Verify(byte[] content, string companyName, IReadOnlyList<AccountingVoucherDraft> vouchers)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(vouchers);
        var document = TallyVoucherXmlReader.Read(content);
        if (document.FailureReason is not null) return $"The written file cannot be read back ({document.FailureReason}).";
        if (!string.Equals(document.CompanyNameReported, (companyName ?? "").Trim(), StringComparison.Ordinal))
            return $"The written file names company '{document.CompanyNameReported}' instead of '{companyName}'.";
        if (document.Vouchers.Count != vouchers.Count)
            return $"The written file holds {document.Vouchers.Count} voucher(s) instead of {vouchers.Count}.";
        foreach (var expected in vouchers)
        {
            var found = document.Vouchers.Where(parsed => parsed.Keys.Count == 1 && parsed.Keys[0] == expected.CorrespondenceKey).ToArray();
            if (found.Length != 1) return $"The written file carries {expected.CorrespondenceKey} {found.Length} time(s) instead of once.";
            var parsed = found[0];
            if (parsed.VoucherType != expected.VoucherType) return $"{expected.DocumentNumber}: the voucher type was written as '{parsed.VoucherType}'.";
            if (parsed.VoucherDate != expected.VoucherDate) return $"{expected.DocumentNumber}: the date was written as {parsed.VoucherDate:yyyy-MM-dd}.";
            var written = parsed.Lines.Select(line => (line.LedgerName, line.Amount, line.IsDeemedPositive)).ToArray();
            var planned = expected.Lines.Select(line => (line.LedgerName, (decimal?)TallyAmount(line), (bool?)(line.Debit > 0))).ToArray();
            if (!written.SequenceEqual(planned)) return $"{expected.DocumentNumber}: the ledger lines in the file differ from the approved vouchers.";
        }
        return null;
    }

    /// <summary>Tally's signed amount: a debit is negative.</summary>
    public static decimal TallyAmount(AccountingVoucherLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Debit > 0 ? -line.Debit : line.Credit;
    }

    private static void WriteVoucher(XmlWriter writer, AccountingVoucherDraft voucher)
    {
        var party = voucher.PartyLedger;
        writer.WriteStartElement("VOUCHER");
        writer.WriteAttributeString("VCHTYPE", voucher.VoucherType);
        writer.WriteAttributeString("ACTION", "Create");
        writer.WriteAttributeString("OBJVIEW", AccountingView);
        writer.WriteElementString("DATE", voucher.VoucherDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        writer.WriteElementString("VOUCHERTYPENAME", voucher.VoucherType);
        writer.WriteElementString("VOUCHERNUMBER", voucher.DocumentNumber);
        writer.WriteElementString("REFERENCE", voucher.DocumentNumber);
        writer.WriteElementString("PARTYLEDGERNAME", party);
        writer.WriteElementString("PERSISTEDVIEW", AccountingView);
        writer.WriteElementString("ISINVOICE", "No");
        writer.WriteElementString("NARRATION", voucher.Narration);
        var partyWritten = false;
        foreach (var line in voucher.Lines)
        {
            var debit = line.Debit > 0;
            var amount = Amount(TallyAmount(line));
            writer.WriteStartElement("ALLLEDGERENTRIES.LIST");
            writer.WriteElementString("LEDGERNAME", line.LedgerName);
            writer.WriteElementString("ISDEEMEDPOSITIVE", debit ? "Yes" : "No");
            var isParty = !partyWritten && debit && string.Equals(line.LedgerName, party, StringComparison.Ordinal);
            partyWritten |= isParty;
            writer.WriteElementString("ISPARTYLEDGER", isParty ? "Yes" : "No");
            writer.WriteElementString("AMOUNT", amount);
            if (!string.IsNullOrWhiteSpace(line.CostCentre))
            {
                // D12: the store's cost centre carries the whole line.
                writer.WriteStartElement("CATEGORYALLOCATIONS.LIST");
                writer.WriteElementString("CATEGORY", PrimaryCostCategory);
                writer.WriteElementString("ISDEEMEDPOSITIVE", debit ? "Yes" : "No");
                writer.WriteStartElement("COSTCENTREALLOCATIONS.LIST");
                writer.WriteElementString("NAME", line.CostCentre.Trim());
                writer.WriteElementString("AMOUNT", amount);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void Check(AccountingVoucherDraft voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        var label = string.IsNullOrWhiteSpace(voucher.DocumentNumber) ? $"voucher {voucher.Sequence}" : $"invoice {voucher.DocumentNumber}";
        if (!TallyCorrespondenceKey.IsDocumentNumberSafe(voucher.DocumentNumber))
            throw new InvalidOperationException($"{label}: the number cannot be traced in Tally (KEY_UNSAFE).");
        var keys = TallyCorrespondenceKey.FindAll(voucher.Narration);
        if (keys.Count != 1 || keys[0] != voucher.CorrespondenceKey)
            throw new InvalidOperationException($"{label}: the narration must carry its ETP key exactly once.");
        if (string.IsNullOrWhiteSpace(voucher.VoucherType)) throw new InvalidOperationException($"{label}: the voucher type is missing.");
        if (voucher.Lines.Count < 2) throw new InvalidOperationException($"{label}: a voucher needs at least two ledger lines.");
        foreach (var line in voucher.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.LedgerName) || line.LedgerName.Length > 200 || line.LedgerName.Any(char.IsControl))
                throw new InvalidOperationException($"{label}: a ledger name is empty, too long or holds a control character.");
            if (line.Debit < 0 || line.Credit < 0 || (line.Debit > 0) == (line.Credit > 0))
                throw new InvalidOperationException($"{label}: each ledger line must be either a debit or a credit above zero.");
            if (decimal.Round(line.Debit, 2) != line.Debit || decimal.Round(line.Credit, 2) != line.Credit)
                throw new InvalidOperationException($"{label}: an amount has more than two decimals, which Tally would round.");
            if (line.CostCentre is { } centre && (centre.Length > 100 || centre.Any(char.IsControl)))
                throw new InvalidOperationException($"{label}: the cost centre name is too long or holds a control character.");
        }
        if (voucher.Lines.Sum(line => line.Debit) != voucher.Lines.Sum(line => line.Credit))
            throw new InvalidOperationException($"{label}: debits and credits do not balance.");
        _ = voucher.PartyLedger;
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
