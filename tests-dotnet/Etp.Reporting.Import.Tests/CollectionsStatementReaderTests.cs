using System.Text;
using System.Text.Json;
using Etp.Reporting.Import.Collections;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// 1.12 lane D2: the Owner-configured statement layouts (master plan 8a task 2) over the synthetic fixtures in
/// tests-dotnet/fixtures/collections-synthetic (two columns, signed amount with Cr/Dr, DR/CR marker, dd-MMM-yy, Indian
/// grouping, preamble and footer, provider gross/fee/net). The expected values are worked by hand in the fixtures' README.
/// </summary>
public sealed class CollectionsStatementReaderTests
{
    private const string TwoColumns = """
        {"value_date":"Value Dt","posting_date":"Date","narration":"Narration","reference":"Chq./Ref.No.",
         "debit":"Withdrawal Amt.","credit":"Deposit Amt.","running_balance":"Closing Balance","date_format":"dd/MM/yyyy"}
        """;

    private const string Signed = """
        {"value_date":"Txn Date","narration":"Description","reference":"Ref No","signed_amount":"Amount","running_balance":"Balance","date_format":"dd-MMM-yy"}
        """;

    private const string Flag = """
        {"value_date":"Value Date","narration":"Particulars","reference":"Reference","signed_amount":"Amount","dr_cr_flag":"Dr/Cr",
         "credit_flag_values":["CR","C"],"date_format":"dd/MM/yy"}
        """;

    private const string Card = """
        {"txn_datetime":"Txn Date Time","merchant_id":"MID","terminal_id":"TID","txn_reference":"RRN","invoice_reference":"Invoice",
         "line_kind_column":"Type","line_kind_values":{"SALE":"SALE","REFUND":"REFUND"},"status":"Status","status_failed_values":["FAILED"],
         "gross":"Gross Amount","fee":"MDR","tax_on_fee":"GST on MDR","net":"Net Amount","settlement_date":"Settlement Date",
         "payer_name":"Customer Name","payer_vpa":"VPA","datetime_format":"dd/MM/yyyy HH:mm:ss","date_format":"dd/MM/yyyy"}
        """;

    [Fact]
    public void Two_column_bank_export_with_preamble_footer_and_indian_grouping_reads_every_line()
    {
        var snapshot = Fixture("bank-two-columns.csv");
        var layout = StatementLayout.Parse(TwoColumns, StatementLayoutKind.Bank);

        var result = StatementLayoutReader.Read(snapshot, layout, null, 3, 1, "SYN_MAIN");

        Assert.Empty(result.Problems);
        Assert.Equal(5, result.Lines.Count);
        Assert.All(result.Lines, line => Assert.Null(line.ParseError));
        Assert.Equal(new[] { 12500.00m, 123456.78m, 8000.00m }, result.Lines.Where(line => line.Credit is not null).Select(line => line.Credit!.Value));
        Assert.Equal(new[] { 118.00m, 25000.00m }, result.Lines.Where(line => line.Debit is not null).Select(line => line.Debit!.Value));
        Assert.All(result.Lines, line => Assert.False(line.Credit is not null && line.Debit is not null));
        var deposit = result.Lines[0];
        Assert.Equal(new DateOnly(2026, 8, 1), deposit.LineDate);
        Assert.Equal("DEP0001", deposit.Reference);
        Assert.Equal(112500.00m, deposit.RunningBalance);
        // The value date counts, not the posting date.
        Assert.Equal(new DateOnly(2026, 8, 4), result.Lines[3].LineDate);
        Assert.Equal(new DateOnly(2026, 8, 3), result.Lines[3].PostingDate);
        // The header signature is the ETP profile signature of the header cells.
        Assert.Equal(ImportProfileMatcher.CreateHeaderSignature(
            new[] { "Date", "Narration", "Chq./Ref.No.", "Value Dt", "Withdrawal Amt.", "Deposit Amt.", "Closing Balance" }), result.Header!.HeaderSignature);
        // raw_json keeps every cell by header.
        using var raw = JsonDocument.Parse(deposit.RawJson);
        Assert.Equal("CASH DEPOSIT BY SELF", raw.RootElement.GetProperty("Narration").GetString());
        Assert.Equal("12,500.00", raw.RootElement.GetProperty("Deposit Amt.").GetString());
    }

    [Fact]
    public void Signed_amounts_read_cr_and_dr_suffixes_and_a_minus_and_an_unreadable_amount_is_an_error_never_zero()
    {
        var result = StatementLayoutReader.Read(Fixture("bank-signed-amount.csv"), StatementLayout.Parse(Signed, StatementLayoutKind.Bank), null, 1, 0, "SYN_SIGNED");

        Assert.Equal(4, result.Lines.Count);
        Assert.Equal(5000.00m, result.Lines[0].Credit);
        Assert.Null(result.Lines[0].Debit);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Lines[0].LineDate);
        Assert.Equal(1200.50m, result.Lines[1].Debit);
        Assert.Equal(new DateOnly(2026, 8, 2), result.Lines[1].LineDate);
        var bad = result.Lines[2];
        Assert.Equal("The amount could not be read.", bad.ParseError);
        Assert.Null(bad.Credit);
        Assert.Null(bad.Debit);
        Assert.DoesNotContain("not a number", bad.ParseError, StringComparison.Ordinal);
        Assert.Equal(75.25m, result.Lines[3].Debit);
        Assert.Equal(3, result.ReadableLines);
        Assert.Equal(1, result.UnreadableLines);
    }

    [Fact]
    public void A_dr_cr_marker_sets_the_direction_and_two_identical_lines_get_ordinals_1_and_2()
    {
        var result = StatementLayoutReader.Read(Fixture("bank-drcr-flag.csv"), StatementLayout.Parse(Flag, StatementLayoutKind.Bank), null, 1, 0, "SYN_FLAG");

        Assert.Equal(3, result.Lines.Count);
        Assert.Equal(2500m, result.Lines[0].Credit);
        Assert.Equal(2500m, result.Lines[1].Credit);
        Assert.Equal(10.5m, result.Lines[2].Debit);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Lines[0].LineDate);
        var payload = LineIdentity.BankPayload("SYN_FLAG", new DateOnly(2026, 8, 1), "X1", "SYNTHETIC DEPOSIT", 2500m, null);
        Assert.Equal(LineIdentity.Compute(payload, 1), result.Lines[0].LineIdentity);
        Assert.Equal(LineIdentity.Compute(payload, 2), result.Lines[1].LineIdentity);
        Assert.Equal(3, result.Lines.Select(line => line.LineIdentity).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Identities_ignore_the_preamble_balance_and_posting_date_but_not_the_reference_amount_or_date()
    {
        var layout = StatementLayout.Parse(TwoColumns, StatementLayoutKind.Bank);
        var first = StatementLayoutReader.Read(Fixture("bank-two-columns.csv"), layout, null, 3, 1, "SYN_MAIN");
        var again = StatementLayoutReader.Read(Fixture("bank-two-columns-reexport.csv"), layout, null, 3, 1, "SYN_MAIN");
        var superset = StatementLayoutReader.Read(Fixture("bank-two-columns-superset.csv"), layout, null, 3, 1, "SYN_MAIN");

        Assert.Equal(first.Lines.Select(line => line.LineIdentity), again.Lines.Select(line => line.LineIdentity));
        Assert.Equal(8, superset.Lines.Count);
        Assert.Equal(first.Lines.Select(line => line.LineIdentity), superset.Lines.Take(5).Select(line => line.LineIdentity));
        Assert.Equal(3, superset.Lines.Skip(5).Select(line => line.LineIdentity).Except(first.Lines.Select(line => line.LineIdentity)).Count());

        var day = new DateOnly(2026, 8, 1);
        var basis = LineIdentity.BankPayload("SYN_MAIN", day, "DEP0001", "CASH DEPOSIT BY SELF", 12500m, null);
        Assert.Equal(basis, LineIdentity.BankPayload("SYN_MAIN", day, " dep0001 ", "other words", 12500.0000m, null));
        Assert.NotEqual(basis, LineIdentity.BankPayload("SYN_MAIN", day, "DEP0002", "CASH DEPOSIT BY SELF", 12500m, null));
        Assert.NotEqual(basis, LineIdentity.BankPayload("SYN_MAIN", day.AddDays(1), "DEP0001", "CASH DEPOSIT BY SELF", 12500m, null));
        Assert.NotEqual(basis, LineIdentity.BankPayload("SYN_MAIN", day, "DEP0001", "CASH DEPOSIT BY SELF", null, 12500m));
        Assert.NotEqual(basis, LineIdentity.BankPayload("SYN_OTHER", day, "DEP0001", "CASH DEPOSIT BY SELF", 12500m, null));
        // Without a reference the narration identifies the line, spaces collapsed and upper-cased.
        Assert.Equal(LineIdentity.BankPayload("A", day, null, "cash  deposit by self", 1m, null), LineIdentity.BankPayload("A", day, null, "CASH DEPOSIT BY SELF", 1m, null));
    }

    [Fact]
    public void Provider_card_lines_keep_failed_status_positive_refunds_fees_and_payer_fields()
    {
        var result = StatementLayoutReader.Read(Fixture("provider-card-gross.csv"), StatementLayout.Parse(Card, StatementLayoutKind.Provider), null, 1, 0, "CARD_ACQUIRER");

        Assert.Empty(result.Problems);
        Assert.Equal(4, result.Lines.Count);
        var sale = result.Lines[0];
        Assert.Equal("SALE", sale.LineKind);
        Assert.Equal(1000m, sale.Gross);
        Assert.Equal(18m, sale.Fee);
        Assert.Equal(3.24m, sale.TaxOnFee);
        Assert.Equal(978.76m, sale.Net);
        Assert.Equal(new DateTime(2026, 8, 1, 10, 15, 0), sale.TxnDateTime);
        Assert.Equal(new DateOnly(2026, 8, 1), sale.TxnBusinessDate);
        Assert.Equal(new DateOnly(2026, 8, 2), sale.SettlementDate);
        Assert.Equal("MID-SYN-1", sale.MerchantId);
        Assert.Equal("Synthetic Payer One", sale.PayerName);
        Assert.Equal("payer1@synthetic", sale.PayerVpa);
        Assert.False(sale.IsFailed);
        Assert.True(result.Lines[1].IsFailed);
        Assert.Equal("SALE", result.Lines[1].LineKind);
        var refund = result.Lines[2];
        Assert.Equal("REFUND", refund.LineKind);
        Assert.Equal(200m, refund.Gross);
        Assert.Equal(200m, refund.Net);
    }

    [Fact]
    public void A_negative_sale_is_an_error_unless_the_layout_reads_the_sign_as_a_refund()
    {
        var csv = Snapshot("negative.csv", "Txn Date Time,Gross Amount\n01/08/2026 10:00:00,-50.00\n01/08/2026 11:00:00,70.00\n");
        const string strict = """{"txn_datetime":"Txn Date Time","gross":"Gross Amount","line_kind_default":"SALE"}""";
        const string bySign = """{"txn_datetime":"Txn Date Time","gross":"Gross Amount","line_kind_default":"SALE","line_kind_from_sign":true}""";

        var refused = StatementLayoutReader.Read(csv, StatementLayout.Parse(strict, StatementLayoutKind.Provider), null, 1, 0, "SYN");
        var refunds = StatementLayoutReader.Read(csv, StatementLayout.Parse(bySign, StatementLayoutKind.Provider), null, 1, 0, "SYN");

        Assert.NotNull(refused.Lines[0].ParseError);
        Assert.Null(refused.Lines[0].Gross);
        Assert.Equal("SALE", refused.Lines[1].LineKind);
        Assert.Equal("REFUND", refunds.Lines[0].LineKind);
        Assert.Equal(50m, refunds.Lines[0].Gross);
    }

    [Fact]
    public void A_layout_column_the_file_lacks_is_a_file_problem_with_no_lines()
    {
        const string missing = """{"value_date":"Value Dt","credit":"Deposit Amt.","debit":"Withdrawal Amt.","reference":"UTR Number"}""";
        var result = StatementLayoutReader.Read(Fixture("bank-two-columns.csv"), StatementLayout.Parse(missing, StatementLayoutKind.Bank), null, 3, 1, "SYN");

        Assert.Empty(result.Lines);
        Assert.Contains(result.Problems, problem => problem.Contains("UTR Number", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"value_date":"A","credit":"B","debit":"C","colour":"D"}""", "not a bank layout key")]
    [InlineData("""{"narration":"A","credit":"B","debit":"C"}""", "value date")]
    [InlineData("""{"value_date":"A","credit":"B"}""", "both the credit")]
    [InlineData("""{"value_date":"A","credit":"B","debit":"C","signed_amount":"D"}""", "not both")]
    [InlineData("""{"value_date":"A"}""", "amount columns")]
    [InlineData("""{"value_date":"A","signed_amount":"B","dr_cr_flag":"C"}""", "marker words")]
    [InlineData("""{"value_date":"A","signed_amount":"B","date_format":7}""", "text")]
    [InlineData("""{"value_date":"","signed_amount":"B"}""", "must name a column")]
    [InlineData("""[1,2]""", "one JSON object")]
    [InlineData("""not json""", "not valid JSON")]
    public void A_bank_layout_that_cannot_be_read_safely_is_refused_in_plain_words(string json, string expected)
    {
        var error = Assert.Throws<StatementLayoutException>(() => StatementLayout.Parse(json, StatementLayoutKind.Bank));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"txn_datetime":"A"}""", "gross amount or the net")]
    [InlineData("""{"gross":"A"}""", "date column")]
    [InlineData("""{"gross":"A","settlement_date":"B","fee_source":"COLUMN"}""", "fee column")]
    [InlineData("""{"gross":"A","settlement_date":"B","line_kind_values":{"S":"SALE"}}""", "line type column")]
    [InlineData("""{"gross":"A","settlement_date":"B","line_kind_column":"C","line_kind_values":{"S":"GIFT"}}""", "must map to one of")]
    [InlineData("""{"gross":"A","settlement_date":"B","line_kind_default":"REFUND"}""", "SALE or UNKNOWN")]
    [InlineData("""{"gross":"A","settlement_date":"B","credit":"C"}""", "not a provider layout key")]
    public void A_provider_layout_that_cannot_be_read_safely_is_refused_in_plain_words(string json, string expected)
    {
        var error = Assert.Throws<StatementLayoutException>(() => StatementLayout.Parse(json, StatementLayoutKind.Provider));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1,23,456.78", 123456.78)]
    [InlineData("₹ 1,000", 1000)]
    [InlineData("Rs.250.50", 250.50)]
    [InlineData("(12.50)", -12.50)]
    [InlineData("12.50-", -12.50)]
    [InlineData("-0.75", -0.75)]
    public void Amounts_read_indian_grouping_currency_marks_and_negative_forms(string text, double expected)
    {
        Assert.True(StatementValues.TryAmount(text, '.', ',', out var amount, out _));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("  ")]
    public void A_blank_amount_is_no_value_not_zero(string text)
    {
        Assert.True(StatementValues.TryAmount(text, '.', ',', out var amount, out _));
        Assert.Null(amount);
    }

    [Theory]
    [InlineData("12a.50")]
    [InlineData("1.2.3")]
    [InlineData("1.23456")]
    public void An_amount_that_is_not_a_number_is_unreadable(string text) =>
        Assert.False(StatementValues.TryAmount(text, '.', ',', out _, out _));

    [Theory]
    [InlineData("05-Aug-26", 2026, 8, 5)]
    [InlineData("05-AUG-2026", 2026, 8, 5)]
    [InlineData("05/08/2026", 2026, 8, 5)]
    [InlineData("2026-08-05", 2026, 8, 5)]
    [InlineData("05/08/2026 23:59", 2026, 8, 5)]
    public void Dates_default_to_day_first_indian_forms(string text, int year, int month, int day)
    {
        Assert.True(StatementValues.TryDate(text, [], out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Fact]
    public void A_utc_transaction_time_becomes_the_india_business_date()
    {
        Assert.True(StatementValues.TryDateTime("2026-08-01 20:00:00", [], utc: true, out var moment));
        Assert.Equal(new DateTime(2026, 8, 2, 1, 30, 0), moment);
    }

    private static WorkbookSnapshot Fixture(string name) => CsvWorkbookReader.Materialize(name, File.ReadAllBytes(FixturePath(name)));

    private static WorkbookSnapshot Snapshot(string name, string csv) => CsvWorkbookReader.Materialize(name, Encoding.UTF8.GetBytes(csv));

    /// <summary>The fixture folder next to the test binaries when a project links it, else found in the repository.</summary>
    internal static string FixturePath(string name)
    {
        var copied = Path.Combine(AppContext.BaseDirectory, "fixtures", "collections-synthetic", name);
        if (File.Exists(copied)) return copied;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "tests-dotnet", "fixtures", "collections-synthetic", name);
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(folder.FullName, "fixtures", "collections-synthetic", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Collections fixture not found.", name);
    }
}
