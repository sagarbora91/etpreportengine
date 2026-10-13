using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Etp.Reporting.Application.Accounting;

/// <summary>Decision 48: the one Tally company ETP may talk to over HTTP. Decision 49: it lives in TallyPrime EDUCATIONAL
/// mode inside the acceptance VM; the real Tally stays untouched until the accountant visit.</summary>
public static class TallyTestCompany
{
    public const string Name = "Saagar Traders Titan";

    /// <summary>True only for the exact name (ordinal, surrounding spaces ignored). "saagar traders titan" is a different company to Tally.</summary>
    public static bool IsTheTestCompany(string? companyName) =>
        companyName is not null && string.Equals(companyName.Trim(), Name, StringComparison.Ordinal);
}

/// <summary>When ETP may open a connection to Tally (1.12 D1, decision 49). Connected mode is off by default: a profile has no
/// address until the Owner enters one. Even then it is refused unless every rule below holds; the plain sentence says which.</summary>
public static class TallyConnectedMode
{
    public const int LowestPort = 9000;
    public const int HighestPort = 9999;

    public const string NotConfigured = "Connecting to Tally is switched off for this company. Enter the address where the test Tally answers (for example http://127.0.0.1:9000/) in Settings → Tally companies.";
    public const string OnlyTestCompany = "ETP talks to Tally only for the test company '" + TallyTestCompany.Name + "' in the test PC (decision 49). Use the file steps for any other company.";
    public const string OnlyThisPc = "Only a Tally on this PC can be used: http://127.0.0.1:<port>/ or http://localhost:<port>/ with a port from 9000 to 9999.";

    /// <summary>True for http://127.0.0.1:&lt;9000-9999&gt;/ or http://localhost:&lt;9000-9999&gt;/ and nothing else.</summary>
    public static bool IsAllowedEndpoint(string? endpointUrl, out Uri? endpoint)
    {
        endpoint = null;
        if (!TallyProfileRules.IsLoopbackEndpoint(endpointUrl)) return false;
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback) return false;
        if (uri.Port is < LowestPort or > HighestPort || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.UserInfo.Length > 0) return false;
        endpoint = uri;
        return true;
    }

    /// <summary>Null when ETP may connect to this profile's Tally; otherwise the sentence to show. Nothing here opens a socket.</summary>
    public static string? Refusal(TallyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsEnabled) return "This Tally company is switched off in Settings → Tally companies.";
        if (profile.Environment != "TEST" || profile.ProductionEnabledUtc is not null) return OnlyTestCompany;
        if (!TallyTestCompany.IsTheTestCompany(profile.CompanyName)) return OnlyTestCompany;
        if (string.IsNullOrWhiteSpace(profile.EndpointUrl)) return NotConfigured;
        return IsAllowedEndpoint(profile.EndpointUrl.Trim(), out _) ? null : OnlyThisPc;
    }
}

/// <param name="VoucherTypes">The voucher types the caller expects; Tally's Day Book returns every type and the reconciliation
/// reports the rest as extra, so this is recorded with the request only.</param>
public sealed record TallyReadbackRequest(string CompanyName, DateOnly From, DateOnly To, IReadOnlyList<string> VoucherTypes);

/// <summary>Why a call to Tally gave no usable answer. Each has one plain sentence in <see cref="TallyGatewayResponse.Message"/>.</summary>
public static class TallyGatewayFailure
{
    /// <summary>Nothing listens on the port: Tally is closed or its HTTP server is off. Nothing was received.</summary>
    public const string NotAnswering = "NOT_ANSWERING";
    /// <summary>The request was sent and no answer came in time. For an import the outcome is unknown.</summary>
    public const string TimedOut = "TIMED_OUT";
    public const string CompanyNotOpen = "COMPANY_NOT_OPEN";
    public const string Redirected = "REDIRECTED";
    public const string HttpError = "HTTP_ERROR";
    public const string Truncated = "TRUNCATED";
    public const string NotXml = "NOT_XML";
    public const string TallyError = "TALLY_ERROR";
    /// <summary>The address broke a rule of <see cref="TallyConnectedMode"/>; no socket was opened.</summary>
    public const string EndpointRefused = "ENDPOINT_REFUSED";

    /// <summary>True when the request may have reached Tally, so an import's outcome must be proven by a read-back.</summary>
    public static bool MayHaveReachedTally(string? failure) => failure is null or TimedOut or Truncated or NotXml or TallyError or HttpError or Redirected or CompanyNotOpen;
}

/// <summary>One HTTP exchange with Tally: the bytes sent, the bytes received (null when nothing came back), and why it failed.</summary>
public sealed record TallyGatewayResponse(byte[] Request, byte[]? Response, int? HttpStatus, string? Failure, string? Message)
{
    public bool Succeeded => Failure is null;
}

public sealed record TallyOpenCompanies(IReadOnlyList<string> Names, TallyGatewayResponse Exchange);

/// <summary>Read-back over HTTP (master plan 7a task 9). Application never references System.Net.Http; the adapter lives in
/// Etp.Reporting.Infrastructure.Http and only ever talks to this PC.</summary>
public interface ITallyReadbackGateway
{
    /// <summary>The Day Book of <see cref="TallyReadbackRequest.CompanyName"/> for the dates, as Tally's XML.</summary>
    Task<TallyGatewayResponse> FetchVouchersAsync(TallyReadbackRequest request, CancellationToken cancellationToken = default);

    /// <summary>The companies open in Tally right now.</summary>
    Task<TallyOpenCompanies> FetchOpenCompaniesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Sends a written Tally file to the test Tally (decision 49 (2): the VM test company only).</summary>
public interface ITallyImportGateway
{
    Task<TallyGatewayResponse> ImportAsync(string companyName, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}

/// <summary>Tally's answer to an import. Counts Tally did not return stay null. <paramref name="LineErrors"/> are Tally's own
/// words for each object it refused.</summary>
public sealed record TallyImportResult(
    int? Created,
    int? Altered,
    int? Deleted,
    int? Ignored,
    int? Errors,
    int? Cancelled,
    int? Exceptions,
    long? LastVoucherId,
    IReadOnlyList<string> LineErrors,
    string? Failure)
{
    /// <summary>Everything Tally was given was created, and it reported no error or exception.</summary>
    public bool AllCreated(int expected) => Failure is null && Created == expected && (Errors ?? 0) == 0 && (Exceptions ?? 0) == 0 && LineErrors.Count == 0;

    /// <summary>Tally said that at least one voucher already exists: a duplicate it refused.</summary>
    public bool ReportsDuplicate => LineErrors.Any(error =>
        error.Contains("already exist", StringComparison.OrdinalIgnoreCase) || error.Contains("duplicate", StringComparison.OrdinalIgnoreCase));

    /// <summary>One sentence for the Owner. It never claims the vouchers are in Tally: only the read-back proves that.</summary>
    public string Describe(int sent)
    {
        if (Failure is not null) return "Tally's answer could not be read. Read back from Tally before doing anything else.";
        var refused = Math.Max(Errors ?? 0, LineErrors.Count) + (Exceptions ?? 0);
        var text = string.Create(CultureInfo.InvariantCulture,
            $"Tally says {Created ?? 0} of {sent} voucher(s) created, {Altered ?? 0} changed, {refused} refused.");
        if (ReportsDuplicate) text += " Tally says some vouchers already exist: do not send again.";
        if (LineErrors.Count > 0) text += " Tally's words: " + string.Join("; ", LineErrors.Take(5)) + (LineErrors.Count > 5 ? "; …" : ".");
        return text + " Read back from Tally and compare to prove what it holds.";
    }
}

/// <summary>Reads Tally's import answer: <c>&lt;RESPONSE&gt;&lt;CREATED&gt;…</c> (Tally.ERP 9) or the TallyPrime envelope with
/// <c>IMPORTRESULT</c>. Hardened like every Tally reader: no DTD, no resolver, Tally's control characters allowed.</summary>
public static class TallyImportResponseParser
{
    public static TallyImportResult Parse(byte[]? content)
    {
        if (content is null || content.Length == 0) return new(null, null, null, null, null, null, null, null, [], TallyGatewayFailure.NotXml);
        XDocument document;
        try { document = TallyXml.Load(content); }
        catch (XmlException) { return new(null, null, null, null, null, null, null, null, [], TallyGatewayFailure.NotXml); }

        var elements = document.Descendants().ToArray();
        int? Count(string name) => elements.FirstOrDefault(element => element.Name.LocalName == name) is { } found
            && int.TryParse(found.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
        var lastVoucher = elements.FirstOrDefault(element => element.Name.LocalName == "LASTVCHID") is { } last
            && long.TryParse(last.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : (long?)null;
        var lineErrors = elements.Where(element => element.Name.LocalName == "LINEERROR")
            .Select(element => element.Value.Trim()).Where(text => text.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var created = Count("CREATED");
        if (created is null && lineErrors.Length == 0)
            return new(null, null, null, null, null, null, null, null, [], TallyGatewayFailure.TallyError);
        return new(created, Count("ALTERED"), Count("DELETED"), Count("IGNORED"), Count("ERRORS"), Count("CANCELLED"), Count("EXCEPTIONS"), lastVoucher, lineErrors, null);
    }
}

/// <summary>The request bodies ETP sends to Tally, and the hardened XML loading every Tally answer goes through.</summary>
public static class TallyXml
{
    public const long MaxBytes = 50_000_000;

    /// <summary>Tally writes control characters such as &amp;#4; into its XML, which strict XML 1.0 refuses, so character
    /// checking is off; DTDs and external resolution stay forbidden.</summary>
    public static XmlReaderSettings ReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes,
        IgnoreComments = true, IgnoreProcessingInstructions = true, CheckCharacters = false, CloseInput = false
    };

    public static XDocument Load(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var reader = XmlReader.Create(stream, ReaderSettings());
        return XDocument.Load(reader, LoadOptions.None);
    }

    /// <summary>Tally's export of the Day Book for one company and period.</summary>
    public static byte[] DayBookRequest(TallyReadbackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.From > request.To) throw new ArgumentException("The first date must not be after the last.", nameof(request));
        return Build(writer =>
        {
            Header(writer, "Export", "Data", "Day Book");
            writer.WriteStartElement("BODY"); writer.WriteStartElement("DESC"); writer.WriteStartElement("STATICVARIABLES");
            writer.WriteElementString("SVEXPORTFORMAT", "$$SysName:XML");
            writer.WriteElementString("SVCURRENTCOMPANY", request.CompanyName.Trim());
            DateVariable(writer, "SVFROMDATE", request.From);
            DateVariable(writer, "SVTODATE", request.To);
            writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
        });
    }

    /// <summary>A small collection of the companies open in Tally (name only).</summary>
    public static byte[] OpenCompaniesRequest() => Build(writer =>
    {
        Header(writer, "Export", "Collection", "ETP Open Companies");
        writer.WriteStartElement("BODY"); writer.WriteStartElement("DESC");
        writer.WriteStartElement("STATICVARIABLES"); writer.WriteElementString("SVEXPORTFORMAT", "$$SysName:XML"); writer.WriteEndElement();
        writer.WriteStartElement("TDL"); writer.WriteStartElement("TDLMESSAGE");
        writer.WriteStartElement("COLLECTION"); writer.WriteAttributeString("NAME", "ETP Open Companies"); writer.WriteAttributeString("ISMODIFY", "No");
        writer.WriteElementString("TYPE", "Company"); writer.WriteElementString("FETCH", "NAME");
        writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteEndElement(); writer.WriteEndElement();
    });

    /// <summary>The names in an open-companies answer: <c>COMPANY NAME="…"</c> or its <c>NAME</c> child. Null when the answer is not XML.</summary>
    public static IReadOnlyList<string>? CompanyNames(byte[]? content)
    {
        if (content is null || content.Length == 0) return null;
        XDocument document;
        try { document = Load(content); }
        catch (XmlException) { return null; }
        return document.Descendants().Where(element => element.Name.LocalName == "COMPANY")
            .Select(element => element.Attribute("NAME")?.Value.Trim() is { Length: > 0 } name ? name
                : element.Elements().FirstOrDefault(child => child.Name.LocalName == "NAME")?.Value.Trim() ?? "")
            .Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Tally's own error text in an answer (LINEERROR), or null.</summary>
    public static string? ErrorText(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = document.Descendants().Where(element => element.Name.LocalName == "LINEERROR")
            .Select(element => element.Value.Trim()).Where(text => text.Length > 0).ToArray();
        return errors.Length == 0 ? null : string.Join(" ", errors);
    }

    private static void Header(XmlWriter writer, string request, string type, string id)
    {
        writer.WriteStartElement("HEADER");
        writer.WriteElementString("VERSION", "1");
        writer.WriteElementString("TALLYREQUEST", request);
        writer.WriteElementString("TYPE", type);
        writer.WriteElementString("ID", id);
        writer.WriteEndElement();
    }

    private static void DateVariable(XmlWriter writer, string name, DateOnly date)
    {
        writer.WriteStartElement(name);
        writer.WriteAttributeString("TYPE", "Date");
        writer.WriteString(date.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    private static byte[] Build(Action<XmlWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, OmitXmlDeclaration = true }))
        {
            writer.WriteStartElement("ENVELOPE");
            body(writer);
            writer.WriteEndElement();
        }
        return stream.ToArray();
    }
}
