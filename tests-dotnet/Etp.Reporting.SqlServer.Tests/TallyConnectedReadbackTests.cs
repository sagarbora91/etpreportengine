using System.Text;
using System.Xml.Linq;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer.Tally;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

// 1.12 D1, decision 49: when ETP may talk to Tally, what it asks, and how it reads the answers. The answers are the synthetic
// recordings in tests-dotnet/fixtures/tally-golden/responses (no real Tally is ever contacted).
public sealed class TallyConnectedReadbackTests
{
    private static TallyProfile Connected(string company = TallyTestCompany.Name, string? endpoint = "http://127.0.0.1:9000/") =>
        TallyProfile.NewTest("TTSAAGAR", company, ["TTGOLD"]) with { EndpointUrl = endpoint };

    [Fact]
    public void Connecting_is_off_until_an_address_is_entered_and_only_for_the_test_company_on_this_PC()
    {
        Assert.Equal(TallyConnectedMode.NotConfigured, TallyConnectedMode.Refusal(Connected(endpoint: null)));
        Assert.Null(TallyConnectedMode.Refusal(Connected()));
        Assert.Null(TallyConnectedMode.Refusal(Connected(endpoint: "http://localhost:9000/")));
        // Decision 53 (review M-3): port 9000 only.
        Assert.Equal(TallyConnectedMode.OnlyThisPc, TallyConnectedMode.Refusal(Connected(endpoint: "http://localhost:9999/")));
        Assert.Equal(TallyConnectedMode.OnlyTestCompany, TallyConnectedMode.Refusal(Connected("TEST - ETP Golden")));
        Assert.Equal(TallyConnectedMode.OnlyTestCompany, TallyConnectedMode.Refusal(Connected("saagar traders titan")));
        Assert.Equal(TallyConnectedMode.OnlyTestCompany, TallyConnectedMode.Refusal(Connected() with { Environment = "PRODUCTION" }));
        Assert.Equal(TallyConnectedMode.OnlyTestCompany, TallyConnectedMode.Refusal(Connected() with { ProductionEnabledUtc = new DateTime(2026, 10, 1) }));
        Assert.Equal(TallyConnectedMode.OnlyThisPc, TallyConnectedMode.Refusal(Connected(endpoint: "http://127.0.0.1:8000/")));
        Assert.Equal(TallyConnectedMode.OnlyThisPc, TallyConnectedMode.Refusal(Connected(endpoint: "http://10.0.0.5:9000/")));
        Assert.Contains("switched off", TallyConnectedMode.Refusal(Connected() with { IsEnabled = false }), StringComparison.Ordinal);
        Assert.True(TallyTestCompany.IsTheTestCompany("  Saagar Traders Titan "));
        Assert.False(TallyTestCompany.IsTheTestCompany("Saagar Traders"));
    }

    [Fact]
    public void The_day_book_request_names_the_company_and_the_dates()
    {
        var request = XDocument.Parse(Encoding.UTF8.GetString(TallyXml.DayBookRequest(new(TallyTestCompany.Name, new(2026, 8, 1), new(2026, 8, 2), ["Sales"]))));
        Assert.Equal(new[] { "1", "Export", "Data", "Day Book" }, request.Root!.Element("HEADER")!.Elements().Select(element => element.Value));
        var variables = request.Descendants("STATICVARIABLES").Single();
        Assert.Equal(TallyTestCompany.Name, variables.Element("SVCURRENTCOMPANY")!.Value);
        Assert.Equal(("20260801", "20260802", "Date"), (variables.Element("SVFROMDATE")!.Value, variables.Element("SVTODATE")!.Value, variables.Element("SVFROMDATE")!.Attribute("TYPE")!.Value));
        Assert.Equal("$$SysName:XML", variables.Element("SVEXPORTFORMAT")!.Value);
        Assert.Throws<ArgumentException>(() => TallyXml.DayBookRequest(new(TallyTestCompany.Name, new(2026, 8, 2), new(2026, 8, 1), [])));

        var companies = XDocument.Parse(Encoding.UTF8.GetString(TallyXml.OpenCompaniesRequest()));
        Assert.Equal("Company", companies.Descendants("COLLECTION").Single().Element("TYPE")!.Value);
    }

    [Fact]
    public void A_recorded_day_book_with_Tally_s_control_characters_is_read_with_its_key_and_lines()
    {
        var document = TallyVoucherXmlReader.Read(Fixture("daybook-saagar.xml"));
        Assert.Null(document.FailureReason);
        Assert.Null(document.CompanyNameReported);   // Tally's Day Book does not repeat the company; the HTTP request names it.
        var voucher = Assert.Single(document.Vouchers);
        Assert.Equal("ETP:TTGOLD:2027:T260801-001:SALES:1", Assert.Single(voucher.Keys));
        Assert.Equal(("Sales", new DateOnly(2026, 8, 1), 1201L, 12L), (voucher.VoucherType!, voucher.VoucherDate!.Value, voucher.MasterId!.Value, voucher.AlterId!.Value));
        Assert.Equal(new[] { "Cash - TTGOLD|-1180.00", "Sales - GST Retail|1000.00", "Output CGST 9%|90.00", "Output SGST 9%|90.00" },
            voucher.Lines.Select(line => $"{line.LedgerName}|{line.Amount:0.00}"));
        Assert.Empty(TallyVoucherXmlReader.Read(Fixture("daybook-empty.xml")).Vouchers);
        Assert.Null(TallyVoucherXmlReader.Read(Fixture("daybook-empty.xml")).FailureReason);
    }

    [Theory]
    [InlineData("dtd.xml", "NOT_XML")]
    [InlineData("malformed.xml", "NOT_XML")]
    [InlineData("company-not-open.xml", "TALLY_ERROR")]
    public void Unusable_answers_are_never_read_as_vouchers(string fixture, string failure)
    {
        var document = TallyVoucherXmlReader.Read(Fixture(fixture));
        Assert.Equal(failure, document.FailureReason);
        Assert.Empty(document.Vouchers);
    }

    [Fact]
    public void Open_companies_are_read_from_both_answer_shapes()
    {
        Assert.Equal(new[] { "Saagar Traders Titan", "TEST - ETP Golden" }, TallyXml.CompanyNames(Fixture("open-companies.xml")));
        Assert.Equal(new[] { "Some Other Company" }, TallyXml.CompanyNames(Fixture("open-companies-other.xml")));
        Assert.Null(TallyXml.CompanyNames(Fixture("dtd.xml")));
        Assert.Null(TallyXml.CompanyNames(null));
    }

    [Fact]
    public void Import_answers_are_read_and_described_without_claiming_success()
    {
        var created = TallyImportResponseParser.Parse(Fixture("import-created.xml"));
        Assert.True(created.AllCreated(1));
        Assert.False(created.ReportsDuplicate);
        Assert.Equal(1201L, created.LastVoucherId);
        Assert.Equal("Tally says 1 of 1 voucher(s) created, 0 changed, 0 refused. Read back from Tally and compare to prove what it holds.", created.Describe(1));

        var duplicate = TallyImportResponseParser.Parse(Fixture("import-duplicate.xml"));
        Assert.False(duplicate.AllCreated(1));
        Assert.True(duplicate.ReportsDuplicate);
        Assert.Contains("already exist: do not send again", duplicate.Describe(1), StringComparison.Ordinal);

        var rejected = TallyImportResponseParser.Parse(Fixture("import-rejected-ledger.xml"));
        Assert.Equal((0, 1), (rejected.Created!.Value, rejected.Errors!.Value));
        Assert.Equal("Ledger 'Cash - TTGOLD' does not exist!", Assert.Single(rejected.LineErrors));
        Assert.Contains("1 refused", rejected.Describe(1), StringComparison.Ordinal);
        Assert.Contains("Tally's words: Ledger 'Cash - TTGOLD' does not exist!", rejected.Describe(1), StringComparison.Ordinal);

        var prime = TallyImportResponseParser.Parse(Fixture("import-prime-envelope.xml"));
        Assert.True(prime.AllCreated(2));
        Assert.False(prime.AllCreated(3));

        Assert.Equal(TallyGatewayFailure.NotXml, TallyImportResponseParser.Parse(Fixture("dtd.xml")).Failure);
        Assert.Equal(TallyGatewayFailure.NotXml, TallyImportResponseParser.Parse(null).Failure);
        Assert.Equal(TallyGatewayFailure.TallyError, TallyImportResponseParser.Parse(Fixture("daybook-empty.xml")).Failure);
        Assert.StartsWith("Tally's answer could not be read", TallyImportResponseParser.Parse(null).Describe(1), StringComparison.Ordinal);
    }

    [Fact]
    public void Read_back_requests_have_their_own_evidence_file_name()
    {
        Assert.Equal(@"TTSAAGAR\TTGOLD\2026-08\batch-7\actuals\readback-1-request.xml",
            TallyEvidencePaths.Validate(@"TTSAAGAR\TTGOLD\2026-08\batch-7\actuals\readback-1-request.xml"));
        Assert.Throws<ArgumentException>(() => TallyEvidencePaths.Validate(@"TTSAAGAR\TTGOLD\2026-08\batch-7\actuals\readback-1-answer.xml"));
    }

    private static byte[] Fixture(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            foreach (var candidate in new[]
                     {
                         Path.Combine(directory.FullName, "fixtures", "tally-golden", "responses", name),
                         Path.Combine(directory.FullName, "tests-dotnet", "fixtures", "tally-golden", "responses", name)
                     })
                if (File.Exists(candidate)) return File.ReadAllBytes(candidate);
        throw new FileNotFoundException($"Tally golden fixture {name} was not found.", name);
    }
}
