using System.Net;
using System.Net.Http.Headers;
using System.Xml;
using Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.Http;

/// <summary>1.12 D1 (master plan 7a task 9; decision 49): the only code in ETP that opens a socket to Tally. It talks to one
/// address on this PC (http://127.0.0.1 or localhost, port 9000-9999), never follows a redirect, never uses a proxy, waits at
/// most 3 s to connect and 60 s in all, reads at most 50 MB, and checks every answer with a reader that refuses DTDs and
/// external entities. Every failure becomes a <see cref="TallyGatewayFailure"/> with one plain sentence; it never throws for
/// what Tally does, only for a wrong address or the caller's own cancellation.</summary>
public sealed class HttpTallyGateway : ITallyReadbackGateway, ITallyImportGateway, IDisposable
{
    public const long MaxResponseBytes = TallyXml.MaxBytes;
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(60);

    public const string NotAnsweringMessage = "Tally is not answering on this PC. Open TallyPrime in the test PC and check that it acts as a server on the port in Settings → Tally companies.";

    private readonly HttpClient client;
    private readonly Uri endpoint;
    private readonly TimeSpan totalTimeout;

    /// <summary>The production gateway: a <see cref="SocketsHttpHandler"/> with no redirects, no proxy, no cookies and a 3 s connect timeout.</summary>
    public HttpTallyGateway(Uri endpoint) : this(endpoint, CreateHandler(), TotalTimeout)
    {
    }

    /// <summary>For tests: any handler (a recorded-answer stub) and timeout. The address rules still apply.</summary>
    public HttpTallyGateway(Uri endpoint, HttpMessageHandler handler, TimeSpan totalTimeout)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(handler);
        if (!TallyConnectedMode.IsAllowedEndpoint(endpoint.OriginalString, out var allowed) || allowed is null)
            throw new ArgumentException(TallyConnectedMode.OnlyThisPc, nameof(endpoint));
        if (totalTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(totalTimeout));
        this.endpoint = allowed;
        this.totalTimeout = totalTimeout;
        client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = MaxResponseBytes
        };
    }

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        Proxy = null,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = ConnectTimeout,
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        MaxConnectionsPerServer = 1
    };

    public async Task<TallyGatewayResponse> FetchVouchersAsync(TallyReadbackRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var exchange = await PostAsync(TallyXml.DayBookRequest(request), cancellationToken).ConfigureAwait(false);
        return exchange.Succeeded ? Inspect(exchange, request.CompanyName) : exchange;
    }

    public async Task<TallyOpenCompanies> FetchOpenCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var exchange = await PostAsync(TallyXml.OpenCompaniesRequest(), cancellationToken).ConfigureAwait(false);
        if (!exchange.Succeeded) return new([], exchange);
        var names = TallyXml.CompanyNames(exchange.Response);
        return names is null
            ? new([], exchange with { Failure = TallyGatewayFailure.NotXml, Message = "Tally's list of open companies could not be read." })
            : new(names, exchange);
    }

    public async Task<TallyGatewayResponse> ImportAsync(string companyName, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (!TallyTestCompany.IsTheTestCompany(companyName))
            throw new InvalidOperationException(TallyConnectedMode.OnlyTestCompany);
        if (payload.IsEmpty) throw new ArgumentException("There is nothing to send.", nameof(payload));
        var exchange = await PostAsync(payload.ToArray(), cancellationToken).ConfigureAwait(false);
        return exchange.Succeeded ? Inspect(exchange, companyName) : exchange;
    }

    public void Dispose() => client.Dispose();

    /// <summary>A 200 answer that is not well-formed XML, holds a DTD, or carries Tally's "company not found" error.</summary>
    private static TallyGatewayResponse Inspect(TallyGatewayResponse exchange, string companyName)
    {
        System.Xml.Linq.XDocument document;
        try { document = TallyXml.Load(exchange.Response!); }
        catch (XmlException)
        {
            return exchange with { Failure = TallyGatewayFailure.NotXml, Message = "Tally's answer is not readable XML (or it holds a DTD, which ETP never reads). It is kept as evidence." };
        }
        var error = TallyXml.ErrorText(document);
        if (error is not null && error.Contains("company", StringComparison.OrdinalIgnoreCase)
            && (error.Contains("not find", StringComparison.OrdinalIgnoreCase) || error.Contains("not open", StringComparison.OrdinalIgnoreCase)
                || error.Contains("not loaded", StringComparison.OrdinalIgnoreCase) || error.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
            return exchange with { Failure = TallyGatewayFailure.CompanyNotOpen, Message = $"Tally is running but the company '{companyName.Trim()}' is not open. Open it in Tally, then try again." };
        return exchange;
    }

    private async Task<TallyGatewayResponse> PostAsync(byte[] body, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(totalTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(body, null, null, TallyGatewayFailure.TimedOut, "Tally did not answer in time.");
        }
        catch (HttpRequestException)
        {
            return new(body, null, null, TallyGatewayFailure.NotAnswering, NotAnsweringMessage);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400)
                return new(body, null, status, TallyGatewayFailure.Redirected, "Something on the Tally port tried to send ETP elsewhere. ETP never follows a redirect; check that the port is Tally's.");
            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
                return new(body, null, status, TallyGatewayFailure.Truncated, "Tally's answer is larger than 50 MB. Read back fewer days at a time.");
            byte[]? content;
            try
            {
                content = await ReadCappedAsync(response.Content, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(body, null, status, TallyGatewayFailure.TimedOut, "Tally stopped answering part-way.");
            }
            catch (HttpRequestException)
            {
                return new(body, null, status, TallyGatewayFailure.TimedOut, "Tally stopped answering part-way.");
            }
            if (content is null) return new(body, null, status, TallyGatewayFailure.Truncated, "Tally's answer is larger than 50 MB. Read back fewer days at a time.");
            if (status is < 200 or >= 300)
                return new(body, content.Length == 0 ? null : content, status, TallyGatewayFailure.HttpError, $"Tally answered with HTTP {status}.");
            if (content.Length == 0) return new(body, null, status, TallyGatewayFailure.TallyError, "Tally sent an empty answer.");
            return new(body, content, status, null, null);
        }
    }

    /// <summary>The body, or null when it passes the 50 MB cap: it is never cut short and kept.</summary>
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
