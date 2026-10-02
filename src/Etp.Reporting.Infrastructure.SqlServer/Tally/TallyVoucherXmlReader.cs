using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <param name="FailureReason">Null when vouchers were read; otherwise NOT_XML, TALLY_ERROR or TRUNCATED (plan task 9).</param>
public sealed record TallyVoucherDocument(string? CompanyNameReported, IReadOnlyList<ParsedVoucher> Vouchers, string? FailureReason, string? TallyMessage = null);

/// <summary>Reads Tally voucher XML — the payload ETP writes (B) and a Day Book read-back (C) — with a hardened reader:
/// no DTD, no external resolver, a size cap that fails instead of truncating. Tags follow TallyPrime's published XML
/// (ENVELOPE, REQUESTDESC/STATICVARIABLES/SVCURRENTCOMPANY, VOUCHER, ALLLEDGERENTRIES.LIST / LEDGERENTRIES.LIST,
/// LEDGERNAME, AMOUNT, ISDEEMEDPOSITIVE); whether the installed build returns each of them is plan task 5 and is not
/// yet verified. A value Tally did not return stays null.</summary>
public static class TallyVoucherXmlReader
{
    public const long MaxBytes = 50_000_000;

    public static TallyVoucherDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.CanSeek && stream.Length - stream.Position > MaxBytes)
            return new(null, [], "TRUNCATED");
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes,
                IgnoreComments = true, IgnoreProcessingInstructions = true, CloseInput = false
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            return new(null, [], "NOT_XML");
        }

        var root = document.Root!;
        var errors = root.DescendantsAndSelf().Where(element => element.Name.LocalName is "LINEERROR" or "ERRORS" && !string.IsNullOrWhiteSpace(element.Value)).ToArray();
        var hasVouchers = root.Descendants().Any(element => element.Name.LocalName == "VOUCHER");
        if (root.Name.LocalName == "RESPONSE" || errors.Length > 0 && !hasVouchers || root.Name.LocalName != "ENVELOPE")
            return new(null, [], "TALLY_ERROR", Trim(string.Join(" ", errors.Select(error => error.Value.Trim())), 500) ?? Trim(root.Value.Trim(), 500));

        var company = root.Descendants().Where(element => element.Name.LocalName == "REQUESTDESC")
            .SelectMany(element => element.Descendants()).FirstOrDefault(element => element.Name.LocalName == "SVCURRENTCOMPANY")?.Value.Trim();
        var vouchers = root.Descendants().Where(element => element.Name.LocalName == "VOUCHER").Select(ReadVoucher).ToArray();
        return new(string.IsNullOrEmpty(company) ? null : company, vouchers, null);
    }

    public static TallyVoucherDocument Read(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        return Read(stream);
    }

    private static ParsedVoucher ReadVoucher(XElement voucher, int index)
    {
        string? Child(string name) => voucher.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;
        var type = Child("VOUCHERTYPENAME") ?? (voucher.Attribute("VCHTYPE")?.Value.Trim() is { Length: > 0 } attribute ? attribute : null);
        var lines = voucher.Elements()
            .Where(element => element.Name.LocalName is "ALLLEDGERENTRIES.LIST" or "LEDGERENTRIES.LIST")
            .Select(element => new ParsedLedgerLine(
                element.Elements().FirstOrDefault(e => e.Name.LocalName == "LEDGERNAME")?.Value.Trim() ?? "",
                Amount(element.Elements().FirstOrDefault(e => e.Name.LocalName == "AMOUNT")?.Value),
                YesNo(element.Elements().FirstOrDefault(e => e.Name.LocalName == "ISDEEMEDPOSITIVE")?.Value)))
            .Where(line => line.LedgerName.Length > 0 || line.Amount is not null)
            .ToArray();
        var fragment = Encoding.UTF8.GetBytes(voucher.ToString(SaveOptions.DisableFormatting));
        return new(index, type, TallyDate(Child("DATE")), Child("VOUCHERNUMBER"), Child("REFERENCE"), Child("NARRATION"),
            YesNo(Child("ISCANCELLED")), YesNo(Child("ISOPTIONAL")), Child("GUID"), Long(Child("MASTERID")), Long(Child("ALTERID")),
            Convert.ToHexString(SHA256.HashData(fragment)).ToLowerInvariant(), lines);
    }

    private static DateOnly? TallyDate(string? value) =>
        value is not null && DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static decimal? Amount(string? value) =>
        value is not null && decimal.TryParse(value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    private static long? Long(string? value) =>
        value is not null && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static bool? YesNo(string? value) => value?.Trim() switch
    {
        "Yes" => true,
        "No" => false,
        _ => null
    };

    private static string? Trim(string value, int length) => value.Length == 0 ? null : value.Length <= length ? value : value[..length];
}
