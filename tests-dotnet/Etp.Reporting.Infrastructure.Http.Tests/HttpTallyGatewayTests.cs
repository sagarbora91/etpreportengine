using System.Net;
using System.Text;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.Http;

namespace Etp.Reporting.Infrastructure.Http.Tests;

// 1.12 D1: the HTTP gateway against a stub handler that replays the synthetic Tally answers in
// tests-dotnet/fixtures/tally-golden/responses (modelled on TallyPrime's published XML; replace with real recordings from
// the VM visit). No test opens a socket and none talks to a real Tally.
public sealed class HttpTallyGatewayTests
{
    private static readonly Uri Endpoint = new("http://127.0.0.1:9000/");
    private static readonly TallyReadbackRequest DayBook = new(TallyTestCompany.Name, new(2026, 8, 1), new(2026, 8, 1), ["Sales"]);

    [Theory]
    [InlineData("http://192.168.1.20:9000/")]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://127.0.0.1:10000/")]
    [InlineData("https://127.0.0.1:9000/")]
    [InlineData("http://tally.example:9000/")]
    [InlineData("http://127.0.0.1:9000/path/")]
    public void Only_a_Tally_on_this_PC_with_a_port_from_9000_to_9999_is_accepted(string address)
    {
        var stub = new StubHandler(_ => throw new InvalidOperationException("No request may be made."));
        var refused = Assert.Throws<ArgumentException>(() => new HttpTallyGateway(new Uri(address), stub, TimeSpan.FromSeconds(5)));
        Assert.Contains("9000 to 9999", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, stub.Calls);
    }

    [Theory]
    [InlineData("http://127.0.0.1:9000/")]
    [InlineData("http://localhost:9999/")]
    public void Loopback_addresses_in_range_are_accepted(string address)
    {
        using var gateway = new HttpTallyGateway(new Uri(address), new StubHandler(_ => Ok("open-companies.xml")), TimeSpan.FromSeconds(5));
        Assert.NotNull(gateway);
    }

    [Fact]
    public async Task The_day_book_is_requested_for_the_company_and_dates_by_POST_and_Tally_control_characters_are_read()
    {
        HttpRequestMessage? seen = null;
        string? body = null;
        string? mediaType = null;
        var stub = new StubHandler(request =>
        {
            seen = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            mediaType = request.Content.Headers.ContentType?.MediaType;
            return Ok("daybook-saagar.xml");
        });
        using var gateway = new HttpTallyGateway(Endpoint, stub, TimeSpan.FromSeconds(5));

        var answer = await gateway.FetchVouchersAsync(DayBook);

        Assert.True(answer.Succeeded, answer.Message);
        Assert.Equal(200, answer.HttpStatus);
        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal(Endpoint, seen.RequestUri);
        Assert.Equal("text/xml", mediaType);
        Assert.Contains("<SVCURRENTCOMPANY>Saagar Traders Titan</SVCURRENTCOMPANY>", body, StringComparison.Ordinal);
        Assert.Contains("<SVFROMDATE TYPE=\"Date\">20260801</SVFROMDATE>", body, StringComparison.Ordinal);
        Assert.Contains("<ID>Day Book</ID>", body, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(body!), answer.Request);
        Assert.Equal(Fixture("daybook-saagar.xml"), answer.Response);
    }

    [Fact]
    public async Task Open_companies_are_read_from_Tally_s_collection()
    {
        using var gateway = new HttpTallyGateway(Endpoint, new StubHandler(_ => Ok("open-companies.xml")), TimeSpan.FromSeconds(5));
        var open = await gateway.FetchOpenCompaniesAsync();
        Assert.True(open.Exchange.Succeeded);
        Assert.Equal(new[] { "Saagar Traders Titan", "TEST - ETP Golden" }, open.Names);

        using var other = new HttpTallyGateway(Endpoint, new StubHandler(_ => Ok("open-companies-other.xml")), TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "Some Other Company" }, (await other.FetchOpenCompaniesAsync()).Names);
    }

    [Fact]
    public async Task A_refused_connection_says_Tally_is_not_answering()
    {
        using var gateway = new HttpTallyGateway(Endpoint, new StubHandler(_ => throw new HttpRequestException("No connection could be made")), TimeSpan.FromSeconds(5));
        var answer = await gateway.FetchVouchersAsync(DayBook);
        Assert.Equal(TallyGatewayFailure.NotAnswering, answer.Failure);
        Assert.Null(answer.Response);
        Assert.Equal(HttpTallyGateway.NotAnsweringMessage, answer.Message);
        Assert.StartsWith("Tally is not answering on this PC", answer.Message, StringComparison.Ordinal);

        var open = await gateway.FetchOpenCompaniesAsync();
        Assert.Empty(open.Names);
        Assert.Equal(TallyGatewayFailure.NotAnswering, open.Exchange.Failure);
    }

    [Fact]
    public async Task A_silent_Tally_times_out_but_the_caller_s_own_cancellation_is_passed_on()
    {
        var stub = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var gateway = new HttpTallyGateway(Endpoint, stub, TimeSpan.FromMilliseconds(200));
        var answer = await gateway.FetchVouchersAsync(DayBook);
        Assert.Equal(TallyGatewayFailure.TimedOut, answer.Failure);
        Assert.Null(answer.Response);
        Assert.True(TallyGatewayFailure.MayHaveReachedTally(answer.Failure));

        using var slow = new HttpTallyGateway(Endpoint, stub, TimeSpan.FromSeconds(30));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow.FetchVouchersAsync(DayBook, cancel.Token));
    }

    [Fact]
    public async Task A_redirect_is_never_followed()
    {
        var stub = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("http://203.0.113.9:9000/");
            return response;
        });
        using var gateway = new HttpTallyGateway(Endpoint, stub, TimeSpan.FromSeconds(5));
        var answer = await gateway.FetchVouchersAsync(DayBook);
        Assert.Equal(TallyGatewayFailure.Redirected, answer.Failure);
        Assert.Equal(302, answer.HttpStatus);
        Assert.Equal(1, stub.Calls);
        Assert.False(HttpTallyGateway.CreateHandler().AllowAutoRedirect);
        Assert.False(HttpTallyGateway.CreateHandler().UseProxy);
        Assert.Equal(TimeSpan.FromSeconds(3), HttpTallyGateway.CreateHandler().ConnectTimeout);
    }

    [Fact]
    public async Task A_company_that_is_not_open_is_named_plainly()
    {
        using var gateway = new HttpTallyGateway(Endpoint, new StubHandler(_ => Ok("company-not-open.xml")), TimeSpan.FromSeconds(5));
        var answer = await gateway.FetchVouchersAsync(DayBook);
        Assert.Equal(TallyGatewayFailure.CompanyNotOpen, answer.Failure);
        Assert.Equal("Tally is running but the company 'Saagar Traders Titan' is not open. Open it in Tally, then try again.", answer.Message);
        Assert.NotNull(answer.Response);
    }

    [Fact]
    public async Task An_answer_over_50_MB_is_refused_not_cut_short()
    {
        var stub = new StubHandler(_ =>
        {
            var content = new ByteArrayContent(Fixture("daybook-saagar.xml"));
            content.Headers.ContentLength = 60_000_000;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var gateway = new HttpTallyGateway(Endpoint, stub, TimeSpan.FromSeconds(5));
        var answer = await gateway.FetchVouchersAsync(DayBook);
        Assert.Equal(TallyGatewayFailure.Truncated, answer.Failure);
        Assert.Null(answer.Response);
    }

    [Theory]
    [InlineData("dtd.xml")]
    [InlineData("malformed.xml")]
    public async Task A_DTD_or_broken_XML_is_kept_as_evidence_and_never_parsed(string fixture)
    {
        using var gateway = new HttpTallyGateway(Endpoint, new StubHandler(_ => Ok(fixture)), TimeSpan.FromSeconds(5));
        var answer = await gateway.FetchVouchersAsync(DayBook);
        Assert.Equal(TallyGatewayFailure.NotXml, answer.Failure);
        Assert.Equal(Fixture(fixture), answer.Response);
        Assert.DoesNotContain("[fonts]", answer.Message ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_server_error_is_reported_with_its_status()
    {
        using var gateway = new HttpTallyGateway(Endpoint,
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("busy") }), TimeSpan.FromSeconds(5));
        var answer = await gateway.FetchVouchersAsync(DayBook);
        Assert.Equal(TallyGatewayFailure.HttpError, answer.Failure);
        Assert.Equal(500, answer.HttpStatus);
    }

    [Fact]
    public async Task Import_goes_only_to_the_test_company_and_returns_Tally_s_answer()
    {
        var stub = new StubHandler(_ => Ok("import-created.xml"));
        using var gateway = new HttpTallyGateway(Endpoint, stub, TimeSpan.FromSeconds(5));
        var payload = Encoding.UTF8.GetBytes("<ENVELOPE><HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER></ENVELOPE>");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.ImportAsync("TEST - ETP Golden", payload));
        Assert.Contains(TallyTestCompany.Name, refused.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.ImportAsync("saagar traders titan", payload));
        Assert.Equal(0, stub.Calls);

        var answer = await gateway.ImportAsync(TallyTestCompany.Name, payload);
        Assert.True(answer.Succeeded);
        Assert.Equal(payload, answer.Request);
        var result = TallyImportResponseParser.Parse(answer.Response);
        Assert.True(result.AllCreated(1));
        Assert.Equal(1201L, result.LastVoucherId);
    }

    private static HttpResponseMessage Ok(string fixture) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(Fixture(fixture)) };

    internal static byte[] Fixture(string name) => File.ReadAllBytes(TallyGoldenFixtures.Path(name));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : this((request, _) => Task.FromResult(answer(request))) { }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) => this.answer = answer;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return answer(request, cancellationToken);
        }
    }
}

/// <summary>Finds a file in tests-dotnet/fixtures/tally-golden/responses: next to the test assembly when the project copies the
/// folder, otherwise by walking up to the repository's tests-dotnet folder.</summary>
internal static class TallyGoldenFixtures
{
    public static string Path(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            foreach (var candidate in new[]
                     {
                         System.IO.Path.Combine(directory.FullName, "fixtures", "tally-golden", "responses", name),
                         System.IO.Path.Combine(directory.FullName, "tests-dotnet", "fixtures", "tally-golden", "responses", name)
                     })
                if (File.Exists(candidate)) return candidate;
        throw new FileNotFoundException($"Tally golden fixture {name} was not found.", name);
    }
}
