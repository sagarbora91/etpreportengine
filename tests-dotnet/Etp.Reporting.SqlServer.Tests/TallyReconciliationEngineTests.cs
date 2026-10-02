using System.Text;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer.Tally;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

// Synthetic G01: a cash sale of 1,180.00 (1,000.00 sales, CGST 9% 90.00, SGST 9% 90.00) in the TEST company.
// The XML follows TallyPrime's published voucher format; it is not an export from the installed build.
public sealed class TallyReconciliationEngineTests
{
    private const string Company = "TEST - ETP Golden";
    private const string Key = "ETP:WLMHW:2027:INV-1:SALES:1";
    private static readonly DateOnly Day = new(2026, 8, 25);

    private static readonly (string Ledger, decimal Signed)[] G01 =
        [("Cash", 1180m), ("Sales", -1000m), ("Output CGST 9%", -90m), ("Output SGST 9%", -90m)];

    private static ExpectedVoucher Expected(string status = "EXPORTED") => new(1, Key, "Sales", Day, "INV-1", status,
    [
        new("TENDER_CASH", "Cash", 1180m, 0m), new("SALES_REVENUE", "Sales", 0m, 1000m),
        new("OUTPUT_CGST_9", "Output CGST 9%", 0m, 90m), new("OUTPUT_SGST_9", "Output SGST 9%", 0m, 90m)
    ]);

    private static ReconciliationInput Input(bool source = true, bool payload = true, bool numbers = false, params ExpectedVoucher[] expected) =>
        new(Company, expected.Length == 0 ? new[] { Expected() } : expected, source, payload, numbers, HasHttpAttempt: false);

    // Debit-positive amounts in, Tally's sign out (a debit is a negative AMOUNT).
    private static string Voucher(string narration, (string Ledger, decimal Signed)[] lines, string date = "20260825", string extra = "", bool omitAmountFor = false) =>
        $"""<VOUCHER VCHTYPE="Sales" ACTION="Create"><DATE>{date}</DATE><VOUCHERTYPENAME>Sales</VOUCHERTYPENAME><NARRATION>{narration}</NARRATION>{extra}"""
        + string.Concat(lines.Select((line, i) =>
            $"<ALLLEDGERENTRIES.LIST><LEDGERNAME>{line.Ledger}</LEDGERNAME><ISDEEMEDPOSITIVE>{(line.Signed > 0 ? "Yes" : "No")}</ISDEEMEDPOSITIVE>"
            + (omitAmountFor && i == 1 ? "" : $"<AMOUNT>{(-line.Signed).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}</AMOUNT>")
            + "</ALLLEDGERENTRIES.LIST>"))
        + "</VOUCHER>";

    private static string Envelope(string? company, params string[] vouchers) =>
        "<ENVELOPE><HEADER><TALLYREQUEST>Export Data</TALLYREQUEST></HEADER><BODY><EXPORTDATA><REQUESTDESC><REPORTNAME>Day Book</REPORTNAME><STATICVARIABLES>"
        + (company is null ? "" : $"<SVCURRENTCOMPANY>{company}</SVCURRENTCOMPANY>")
        + "</STATICVARIABLES></REQUESTDESC><REQUESTDATA><TALLYMESSAGE>" + string.Concat(vouchers) + "</TALLYMESSAGE></REQUESTDATA></EXPORTDATA></BODY></ENVELOPE>";

    private static TallyVoucherDocument Parse(string xml) => TallyVoucherXmlReader.Read(Encoding.UTF8.GetBytes(xml));

    private static ReadbackSnapshot Readback(string xml)
    {
        var document = Parse(xml);
        Assert.Null(document.FailureReason);
        return new(1, document.CompanyNameReported, true, null, Day, Day, document.Vouchers);
    }

    private static IReadOnlyList<ParsedVoucher> Payload() => Parse(Envelope(Company, Voucher($"{Key} | ETP WLMHW invoice INV-1 dated 25-Aug-2026", G01))).Vouchers;

    private static TallyReconciliationResult Run(string readbackXml, ReconciliationInput? input = null, ToleranceSet? tolerances = null) =>
        TallyReconciliationEngine.Run(input ?? Input(), Payload(), Readback(readbackXml), tolerances ?? ToleranceSet.None);

    [Fact]
    public void G01_matching_readback_reconciles()
    {
        var result = Run(Envelope(Company, Voucher($"{Key} | ETP WLMHW invoice INV-1 dated 25-Aug-2026", G01)));
        Assert.Empty(result.Differences);
        Assert.Equal("RECONCILED", result.BatchStatus);
        Assert.Equal("RECONCILED", result.RunOutcome);
        var voucher = Assert.Single(result.Vouchers);
        Assert.Equal("RECONCILED", voucher.Status);
        Assert.Equal(0, voucher.ActualIndex);
        Assert.Equal("7a.1", result.RuleSetVersion);
    }

    [Fact]
    public void A_missing_voucher_fails_with_the_fixed_action()
    {
        // Another voucher of the same day (keyed by hand, no ETP key) shows the file covers the day.
        var result = Run(Envelope(Company, Voucher("Shop rent paid in cash", G01)));
        var difference = Assert.Single(result.Differences);
        Assert.Equal(("COVERAGE", "MISSING", "FAIL"), (difference.CheckLevel, difference.DifferenceType, difference.Severity));
        Assert.StartsWith("Voucher not found in Tally. Do not write the file again", difference.RequiredAction, StringComparison.Ordinal);
        Assert.Equal("FAILED_RECONCILIATION", result.BatchStatus);
        Assert.Equal("DIFFERENCE", Assert.Single(result.Vouchers).Status);
    }

    [Fact]
    public void An_invoice_view_voucher_is_read_with_the_sales_ledger_inside_its_stock_item()
    {
        var inventory = Voucher(Key, G01.Where(line => line.Ledger != "Sales").ToArray())
            .Replace("</VOUCHER>", "<ALLINVENTORYENTRIES.LIST><STOCKITEMNAME>Saree</STOCKITEMNAME><ACTUALQTY>1 Nos</ACTUALQTY>"
                + "<ACCOUNTINGALLOCATIONS.LIST><LEDGERNAME>Sales</LEDGERNAME><ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><AMOUNT>1000.00</AMOUNT></ACCOUNTINGALLOCATIONS.LIST>"
                + "</ALLINVENTORYENTRIES.LIST></VOUCHER>", StringComparison.Ordinal);
        var result = Run(Envelope(Company, inventory));
        Assert.Empty(result.Differences);
        Assert.Equal("RECONCILED", Assert.Single(result.Vouchers).Status);
    }

    [Fact]
    public void A_file_with_nothing_on_the_vouchers_day_never_proves_it_missing()
    {
        var result = Run(Envelope(Company, Voucher("Shop rent paid in cash", G01, date: "20260824")));
        var difference = Assert.Single(result.Differences);
        Assert.Equal(("NOT_VERIFIABLE", "RECO-COV-008"), (difference.DifferenceType, difference.RuleId));
        var voucher = Assert.Single(result.Vouchers);
        Assert.Equal("EXPORTED", voucher.Status);
        Assert.False(voucher.CoveredByReadback);
        Assert.Equal("RECONCILIATION_INCOMPLETE", result.BatchStatus);
    }

    [Fact]
    public void Without_a_registered_written_file_a_located_voucher_is_not_reconciled()
    {
        var input = Input() with { PayloadRecorded = false };
        var result = TallyReconciliationEngine.Run(input, null, Readback(Envelope(Company, Voucher(Key, G01))), ToleranceSet.None);
        Assert.Equal("RECO-INT-006", Assert.Single(result.Differences).RuleId);
        Assert.Equal("ACTUAL_LOCATED", Assert.Single(result.Vouchers).Status);
        Assert.Equal("RECONCILIATION_INCOMPLETE", result.BatchStatus);
    }

    [Fact]
    public void A_blocked_voucher_found_in_Tally_is_reported()
    {
        const string blockedKey = "ETP:WLMHW:2027:INV-2:SALES:1";
        var input = Input(expected: [Expected(), Expected("BLOCKED") with { Sequence = 2, CorrespondenceKey = blockedKey, DocumentNumber = "INV-2" }]);
        var result = TallyReconciliationEngine.Run(input, Payload(), Readback(Envelope(Company, Voucher(Key, G01), Voucher(blockedKey, G01))), ToleranceSet.None);
        var extra = Assert.Single(result.Differences);
        Assert.Equal(("EXTRA", "RECO-COV-009", (int?)2, (string?)"BLOCKED"), (extra.DifferenceType, extra.RuleId, extra.VoucherSequence, extra.ValueA));
        Assert.Equal("RECONCILED", Assert.Single(result.Vouchers).Status);
    }

    [Fact]
    public void A_voucher_imported_twice_is_a_duplicate_and_none_is_picked()
    {
        var result = Run(Envelope(Company, Voucher(Key, G01), Voucher(Key, G01)));
        Assert.Equal("DUPLICATE", Assert.Single(result.Differences).DifferenceType);
        Assert.Null(Assert.Single(result.Vouchers).ActualIndex);
        Assert.Equal("FAILED_RECONCILIATION", result.BatchStatus);
    }

    [Fact]
    public void Another_company_matches_nothing()
    {
        var result = Run(Envelope("TEST - Other", Voucher(Key, G01)));
        var difference = Assert.Single(result.Differences);
        Assert.Equal("WRONG_COMPANY", difference.DifferenceType);
        Assert.Equal("TEST - Other", difference.ValueC);
        Assert.Equal("FAILED_RECONCILIATION", result.BatchStatus);
    }

    [Fact]
    public void A_readback_that_does_not_name_its_company_proves_nothing()
    {
        var result = Run(Envelope(null, Voucher(Key, G01)));
        Assert.Equal("NOT_VERIFIABLE", Assert.Single(result.Differences).DifferenceType);
        Assert.Equal("EXPORTED", Assert.Single(result.Vouchers).Status);
        Assert.Equal("RECONCILIATION_INCOMPLETE", result.BatchStatus);
    }

    [Fact]
    public void One_paisa_off_fails_unless_a_tolerance_allows_it_and_then_still_needs_acceptance()
    {
        var off = G01.Select(line => line.Ledger == "Output CGST 9%" ? (line.Ledger, -90.01m) : line).ToArray();
        var strict = Run(Envelope(Company, Voucher(Key, off)));
        var difference = Assert.Single(strict.Differences);
        Assert.Equal(("TAX", "TAX_MISMATCH", "FAIL"), (difference.CheckLevel, difference.DifferenceType, difference.Severity));
        Assert.Equal(-0.01m, difference.Delta);
        Assert.Equal("FAILED_RECONCILIATION", strict.BatchStatus);

        var tolerant = Run(Envelope(Company, Voucher(Key, off)), tolerances: new(new Dictionary<string, decimal> { ["TAX"] = 0.01m }));
        Assert.Equal("WARN", Assert.Single(tolerant.Differences).Severity);
        Assert.Equal("RECONCILED_WITH_WARNINGS", Assert.Single(tolerant.Vouchers).Status);
        Assert.Equal("RECONCILIATION_INCOMPLETE", tolerant.BatchStatus);
    }

    [Fact]
    public void A_renamed_ledger_shows_both_the_missing_and_the_unplanned_line()
    {
        var renamed = G01.Select(line => line.Ledger == "Cash" ? ("Cash in Hand", line.Signed) : line).ToArray();
        var result = Run(Envelope(Company, Voucher(Key, renamed)));
        Assert.Equal(new[] { "RECO-ACC-002", "RECO-ACC-004" }, result.Differences.Select(d => d.RuleId));
        Assert.All(result.Differences, d => Assert.Equal("LEDGER_MISMATCH", d.DifferenceType));
        Assert.Equal("FAILED_RECONCILIATION", result.BatchStatus);
    }

    [Fact]
    public void An_amount_Tally_did_not_return_is_never_zero()
    {
        var result = Run(Envelope(Company, Voucher(Key, G01, omitAmountFor: true)));
        var difference = Assert.Single(result.Differences);
        Assert.Equal("NOT_VERIFIABLE", difference.DifferenceType);
        Assert.Equal("ACTUAL_LOCATED", Assert.Single(result.Vouchers).Status);
        Assert.Equal("RECONCILIATION_INCOMPLETE", result.BatchStatus);
    }

    [Fact]
    public void A_voucher_with_two_keys_matches_neither()
    {
        var result = Run(Envelope(Company, Voucher($"{Key} ETP:WLMHW:2027:INV-2:SALES:1", G01)));
        Assert.Equal(new[] { "AMBIGUOUS_MATCH", "MISSING" }, result.Differences.Select(d => d.DifferenceType));
    }

    [Fact]
    public void An_unplanned_ETP_voucher_for_the_store_is_reported_without_failing_the_batch()
    {
        var result = Run(Envelope(Company, Voucher(Key, G01), Voucher("ETP:WLMHW:2027:INV-9:SALES:1", G01), Voucher("ETP:HEMW:2027:INV-9:SALES:1", G01)));
        var extra = Assert.Single(result.Differences);
        Assert.Equal(("EXTRA", "WARN", "ETP:WLMHW:2027:INV-9:SALES:1"), (extra.DifferenceType, extra.Severity, extra.ValueC));
        Assert.Equal("RECONCILED", result.BatchStatus);
    }

    [Fact]
    public void A_voucher_dated_outside_the_readback_is_not_judged()
    {
        var input = Input(expected: Expected() with { VoucherDate = Day.AddDays(1) });
        var result = TallyReconciliationEngine.Run(input, Payload(), Readback(Envelope(Company)), ToleranceSet.None);
        Assert.Empty(result.Differences);
        Assert.False(Assert.Single(result.Vouchers).CoveredByReadback);
        Assert.Equal("RECONCILIATION_INCOMPLETE", result.BatchStatus);
    }

    [Fact]
    public void Changed_source_or_written_file_is_flagged_even_when_Tally_matches()
    {
        var xml = Envelope(Company, Voucher(Key, G01));
        Assert.Contains(Run(xml, Input(source: false)).Differences, d => d.DifferenceType == "SOURCE_CHANGED");
        Assert.Equal("FAILED_RECONCILIATION", Run(xml, Input(source: false)).BatchStatus);
        var changed = Run(xml, Input(payload: false));
        Assert.Contains(changed.Differences, d => d.RuleId == "RECO-INT-002");
        Assert.Equal("RECONCILIATION_INCOMPLETE", changed.BatchStatus);
    }

    [Fact]
    public void A_written_file_that_differs_from_the_plan_is_an_integrity_failure()
    {
        var wrongFile = Parse(Envelope(Company, Voucher(Key, G01.Select(line => line.Ledger == "Sales" ? (line.Ledger, -999m) : line).ToArray()))).Vouchers;
        var result = TallyReconciliationEngine.Run(Input(), wrongFile, Readback(Envelope(Company, Voucher(Key, G01))), ToleranceSet.None);
        var difference = Assert.Single(result.Differences);
        Assert.Equal(("INTEGRITY", "RECO-INT-005", "-999.00"), (difference.CheckLevel, difference.RuleId, difference.ValueB));
    }

    [Fact]
    public void Cancelled_vouchers_and_wrong_dates_are_header_failures()
    {
        var cancelled = Run(Envelope(Company, Voucher(Key, G01, extra: "<ISCANCELLED>Yes</ISCANCELLED>")));
        Assert.Equal("RECO-HDR-004", Assert.Single(cancelled.Differences).RuleId);
        var redated = TallyReconciliationEngine.Run(Input(), Payload(), Readback(Envelope(Company, Voucher(Key, G01, date: "20260826"))) with { ToDate = Day.AddDays(1) }, ToleranceSet.None);
        Assert.Equal("RECO-HDR-002", Assert.Single(redated.Differences).RuleId);
    }

    [Fact]
    public void Voucher_numbers_are_compared_only_when_Tally_keeps_ETPs_number()
    {
        var xml = Envelope(Company, Voucher(Key, G01, extra: "<VOUCHERNUMBER>17</VOUCHERNUMBER>"));
        Assert.Empty(Run(xml).Differences);
        Assert.Equal("RECO-HDR-003", Assert.Single(Run(xml, Input(numbers: true)).Differences).RuleId);
    }

    [Fact]
    public void Excluded_vouchers_are_ignored_and_a_batch_with_nothing_left_is_cancelled()
    {
        var result = TallyReconciliationEngine.Run(Input(expected: Expected("EXCLUDED")), Payload(), Readback(Envelope(Company)), ToleranceSet.None);
        Assert.Empty(result.Vouchers);
        Assert.Equal("CANCELLED", result.BatchStatus);
        Assert.Equal("RECONCILIATION_INCOMPLETE", result.RunOutcome);
    }
}

public sealed class TallyVoucherXmlReaderTests
{
    [Fact]
    public void Voucher_fields_and_company_are_read_and_absent_fields_stay_null()
    {
        var document = TallyVoucherXmlReader.Read(Encoding.UTF8.GetBytes(
            "<ENVELOPE><BODY><EXPORTDATA><REQUESTDESC><STATICVARIABLES><SVCURRENTCOMPANY> TEST - ETP Golden </SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC>"
            + "<REQUESTDATA><TALLYMESSAGE><VOUCHER VCHTYPE=\"Sales\"><DATE>20260825</DATE><GUID>abc-1</GUID><MASTERID>42</MASTERID><ALTERID>7</ALTERID>"
            + "<NARRATION>ETP:WLMHW:2027:INV-1:SALES:1</NARRATION><ISOPTIONAL>No</ISOPTIONAL>"
            + "<ALLLEDGERENTRIES.LIST><LEDGERNAME>Cash ₹ नकद</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-1180.00</AMOUNT></ALLLEDGERENTRIES.LIST>"
            + "</VOUCHER></TALLYMESSAGE></REQUESTDATA></EXPORTDATA></BODY></ENVELOPE>"));
        Assert.Null(document.FailureReason);
        Assert.Equal("TEST - ETP Golden", document.CompanyNameReported);
        var voucher = Assert.Single(document.Vouchers);
        Assert.Equal(("Sales", new DateOnly(2026, 8, 25), "abc-1", 42L, 7L), (voucher.VoucherType, voucher.VoucherDate!.Value, voucher.TallyGuid!, voucher.MasterId!.Value, voucher.AlterId!.Value));
        Assert.Null(voucher.VoucherNumber);
        Assert.Null(voucher.IsCancelled);
        Assert.False(voucher.IsOptional);
        Assert.Equal(new[] { "ETP:WLMHW:2027:INV-1:SALES:1" }, voucher.Keys);
        var line = Assert.Single(voucher.Lines);
        Assert.Equal(("Cash ₹ नकद", -1180m, true), (line.LedgerName, line.Amount!.Value, line.IsDeemedPositive!.Value));
        Assert.Equal(64, voucher.FragmentSha256.Length);
    }

    [Theory]
    [InlineData("<!DOCTYPE ENVELOPE [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]><ENVELOPE>&x;</ENVELOPE>", "NOT_XML")]
    [InlineData("<html><body>Tally is not running</body></html>", "TALLY_ERROR")]
    [InlineData("not xml at all", "NOT_XML")]
    [InlineData("<ENVELOPE><BODY><DATA><LINEERROR>Could not find Company 'TEST - ETP Golden'</LINEERROR></DATA></BODY></ENVELOPE>", "TALLY_ERROR")]
    [InlineData("<RESPONSE>Unknown Request</RESPONSE>", "TALLY_ERROR")]
    public void Unusable_answers_are_classified_not_parsed(string xml, string reason)
    {
        var document = TallyVoucherXmlReader.Read(Encoding.UTF8.GetBytes(xml));
        Assert.Equal(reason, document.FailureReason);
        Assert.Empty(document.Vouchers);
        Assert.Null(document.CompanyNameReported);
    }

    [Fact]
    public void A_Tally_error_keeps_Tallys_message()
    {
        var document = TallyVoucherXmlReader.Read(Encoding.UTF8.GetBytes("<ENVELOPE><BODY><DATA><LINEERROR>Could not find Company</LINEERROR></DATA></BODY></ENVELOPE>"));
        Assert.Equal("Could not find Company", document.TallyMessage);
    }
}

public sealed class TallyRecoveryPlanBuilderTests
{
    private static ReconciliationDifference Difference(int sequence, string type, string severity = "FAIL") =>
        new(sequence, null, "COVERAGE", type, severity, null, null, null, null, "RULE", "rationale", TallyReconciliationEngine.RequiredActions[type]);

    [Fact]
    public void Each_unreconciled_voucher_gets_one_proposal_and_the_batch_is_never_resent()
    {
        var result = new TallyReconciliationResult("7a.1", "FAILED_RECONCILIATION", "FAILED_RECONCILIATION",
        [
            new(1, "RECONCILED", 0, true),
            new(2, "DIFFERENCE", null, true),
            new(3, "DIFFERENCE", 4, true),
            new(4, "DIFFERENCE", 5, true),
            new(5, "RECONCILED_WITH_WARNINGS", 6, true),
            new(6, "EXPORTED", null, false),
            new(7, "ACTUAL_LOCATED", 7, true)
        ],
        [
            Difference(2, "MISSING"), Difference(3, "DUPLICATE"), Difference(4, "SOURCE_CHANGED"), Difference(4, "AMOUNT_MISMATCH"),
            Difference(5, "TAX_MISMATCH", "WARN"), Difference(7, "NOT_VERIFIABLE")
        ]);

        var plan = TallyRecoveryPlanBuilder.Build(result, runId: 9);

        Assert.Equal(9, plan.RunId);
        Assert.False(plan.ResendsWholeBatch);
        Assert.Equal(new[] { (2, "READ_BACK_AGAIN"), (3, "MANUAL_CORRECTION_IN_TALLY"), (4, "REVERSE_AND_REISSUE"), (5, "ACCEPT_WITH_REASON"), (6, "READ_BACK_AGAIN"), (7, "READ_BACK_AGAIN") },
            plan.Steps.Select(step => (step.VoucherSequence, step.Action)));
        Assert.True(plan.Steps.Single(step => step.VoucherSequence == 4).NeedsSeparateApproval);
        Assert.Equal(new[] { "AMOUNT_MISMATCH", "SOURCE_CHANGED" }, plan.Steps.Single(step => step.VoucherSequence == 4).DifferenceTypes);
        Assert.DoesNotContain(plan.Steps, step => step.Action == "RESEND_VOUCHER");
    }

    [Fact]
    public void Recovery_plan_files_have_a_fixed_numbered_name() =>
        Assert.Equal(@"GOLDEN\WLMHW\2026-08\batch-12\recovery-plan-3.json", TallyEvidencePaths.Validate(@"GOLDEN\WLMHW\2026-08\batch-12\recovery-plan-3.json"));
}
