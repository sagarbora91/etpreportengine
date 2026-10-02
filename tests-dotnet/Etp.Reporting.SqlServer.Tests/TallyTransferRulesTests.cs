using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer.Tally;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class AccountingBatchStatusRulesTests
{
    // Each voucher is written STATUS[+flag...]: +covered, +notverifiable, +rejected (only MISSING with a
    // per-object Tally rejection), +accepted (warnings accepted). Cases follow the plan's seven steps in order.
    [Theory]
    // (1) any OUTCOME_UNKNOWN wins, even over differences and uncovered exports
    [InlineData("OUTCOME_UNKNOWN", false, "OUTCOME_UNKNOWN")]
    [InlineData("OUTCOME_UNKNOWN;DIFFERENCE;EXPORTED", true, "OUTCOME_UNKNOWN")]
    // (2) an export or submission no read-back covers, or a NOT_VERIFIABLE check on a located voucher
    [InlineData("EXPORTED", false, "RECONCILIATION_INCOMPLETE")]
    [InlineData("SUBMITTED;DIFFERENCE", true, "RECONCILIATION_INCOMPLETE")]
    [InlineData("RECONCILED;RECONCILED+notverifiable", false, "RECONCILIATION_INCOMPLETE")]
    [InlineData("DIFFERENCE+notverifiable", false, "RECONCILIATION_INCOMPLETE")]
    // (3) HTTP only: something landed and the rest were rejected by Tally object by object
    [InlineData("RECONCILED;DIFFERENCE+rejected", true, "PARTIALLY_APPLIED")]
    [InlineData("ACTUAL_LOCATED;DIFFERENCE+rejected", true, "PARTIALLY_APPLIED")]
    [InlineData("RECONCILED;DIFFERENCE+rejected", false, "FAILED_RECONCILIATION")]
    [InlineData("DIFFERENCE+rejected", true, "FAILED_RECONCILIATION")]
    // (4) any other difference
    [InlineData("RECONCILED;DIFFERENCE", true, "FAILED_RECONCILIATION")]
    [InlineData("DIFFERENCE", false, "FAILED_RECONCILIATION")]
    [InlineData("EXPORTED+covered;DIFFERENCE", false, "FAILED_RECONCILIATION")]
    // (5) everything reconciled; excluded and cancelled vouchers are ignored
    [InlineData("RECONCILED", false, "RECONCILED")]
    [InlineData("RECONCILED;RECONCILED;EXCLUDED;CANCELLED", true, "RECONCILED")]
    // (6) reconciled with warnings only when every warning is accepted
    [InlineData("RECONCILED;RECONCILED_WITH_WARNINGS+accepted", false, "RECONCILED_WITH_ACCEPTED_WARNINGS")]
    [InlineData("RECONCILED_WITH_WARNINGS+accepted", true, "RECONCILED_WITH_ACCEPTED_WARNINGS")]
    [InlineData("RECONCILED_WITH_WARNINGS", false, "RECONCILIATION_INCOMPLETE")]
    // (7) nothing left
    [InlineData("", false, "CANCELLED")]
    [InlineData("EXCLUDED;CANCELLED", true, "CANCELLED")]
    // otherwise: not proven
    [InlineData("ACTUAL_LOCATED", false, "RECONCILIATION_INCOMPLETE")]
    [InlineData("EXPORTED+covered", false, "RECONCILIATION_INCOMPLETE")]
    [InlineData("RECONCILED;ACTUAL_LOCATED", true, "RECONCILIATION_INCOMPLETE")]
    public void Derive_applies_the_seven_steps_in_order(string vouchers, bool hasHttpAttempt, string expected)
    {
        Assert.Equal(expected, AccountingBatchStatusRules.Derive(Parse(vouchers), hasHttpAttempt));
    }

    [Theory]
    [InlineData("PLANNED")]
    [InlineData("BLOCKED")]
    [InlineData("POSTED")]
    public void Derive_refuses_statuses_it_must_never_see(string status)
    {
        Assert.Throws<ArgumentException>(() => AccountingBatchStatusRules.Derive([new VoucherState(status)], false));
    }

    [Fact]
    public void Derive_always_returns_a_batch_status_and_outcome_unknown_always_wins()
    {
        string[] derivable = ["EXCLUDED", "EXPORTED", "SUBMITTED", "OUTCOME_UNKNOWN", "ACTUAL_LOCATED", "RECONCILED", "RECONCILED_WITH_WARNINGS", "DIFFERENCE", "CANCELLED"];
        var states = derivable.SelectMany(status => Enumerable.Range(0, 16).Select(flags => new VoucherState(status,
            CoveredByReadback: (flags & 1) != 0, HasNotVerifiableDifferenceOnLocatedVoucher: (flags & 2) != 0,
            OnlyMissingWithTallyRejection: (flags & 4) != 0, WarningsAccepted: (flags & 8) != 0))).ToArray();

        foreach (var first in states)
        foreach (var second in states)
        foreach (var http in new[] { false, true })
        {
            var result = AccountingBatchStatusRules.Derive([first, second], http);
            Assert.Contains(result, TallyBatchStatus.All);
            if (first.Status == "OUTCOME_UNKNOWN" || second.Status == "OUTCOME_UNKNOWN") Assert.Equal("OUTCOME_UNKNOWN", result);
            if (result == "RECONCILED") Assert.All(new[] { first, second }, v => Assert.Contains(v.Status, new[] { "RECONCILED", "EXCLUDED", "CANCELLED" }));
            if (result == "PARTIALLY_APPLIED") Assert.True(http);
        }
    }

    private static VoucherState[] Parse(string vouchers) =>
        vouchers.Length == 0 ? Array.Empty<VoucherState>() : vouchers.Split(';').Select(item =>
        {
            var parts = item.Split('+');
            return new VoucherState(parts[0],
                CoveredByReadback: parts.Contains("covered"),
                HasNotVerifiableDifferenceOnLocatedVoucher: parts.Contains("notverifiable"),
                OnlyMissingWithTallyRejection: parts.Contains("rejected"),
                WarningsAccepted: parts.Contains("accepted"));
        }).ToArray();
}

public sealed class AccountingBatchTransitionsTests
{
    [Theory]
    [InlineData("DRAFT", "APPROVED_READY")]
    [InlineData("BLOCKED", "DRAFT")]
    [InlineData("APPROVED_READY", "EXPORTED_AWAITING_IMPORT")]
    [InlineData("APPROVED_READY", "DRAFT")]
    [InlineData("EXPORTED_AWAITING_IMPORT", "IMPORT_REPORTED_AWAITING_RECONCILIATION")]
    [InlineData("SUBMITTED_AWAITING_RESULT", "APPROVED_READY")]
    [InlineData("OUTCOME_UNKNOWN", "RECONCILIATION_INCOMPLETE")]
    [InlineData("IMPORT_REPORTED_AWAITING_RECONCILIATION", "RECONCILED")]
    [InlineData("RECONCILED", "RE_AUDIT_REQUIRED")]
    [InlineData("RE_AUDIT_REQUIRED", "FAILED_RECONCILIATION")]
    [InlineData("RECONCILED_WITH_ACCEPTED_WARNINGS", "RECONCILED")]
    public void Plan_transitions_are_allowed(string from, string to) =>
        Assert.True(AccountingBatchTransitions.IsAllowed(from, to));

    [Theory]
    // DRAFT is reachable only from BLOCKED and APPROVED_READY
    [InlineData("EXPORTED_AWAITING_IMPORT", "DRAFT")]
    [InlineData("REJECTED", "DRAFT")]
    // a written file is never "reconciled" without a claimed import and a run
    [InlineData("EXPORTED_AWAITING_IMPORT", "RECONCILED")]
    [InlineData("APPROVED_READY", "RECONCILED")]
    // an import claim cannot be withdrawn by cancelling
    [InlineData("IMPORT_REPORTED_AWAITING_RECONCILIATION", "CANCELLED")]
    // a reconciled batch only moves through a re-audit
    [InlineData("RECONCILED", "FAILED_RECONCILIATION")]
    [InlineData("DRAFT", "EXPORTED_AWAITING_IMPORT")]
    public void Other_transitions_are_refused(string from, string to) =>
        Assert.False(AccountingBatchTransitions.IsAllowed(from, to));

    [Fact]
    public void Rejected_and_cancelled_are_final()
    {
        Assert.Empty(AccountingBatchTransitions.TargetsFrom("REJECTED"));
        Assert.Empty(AccountingBatchTransitions.TargetsFrom("CANCELLED"));
    }

    [Fact]
    public void Unknown_status_names_are_refused()
    {
        Assert.Throws<ArgumentException>(() => AccountingBatchTransitions.IsAllowed("REVIEW", "DRAFT"));
        Assert.Throws<ArgumentException>(() => AccountingBatchTransitions.IsAllowed("DRAFT", "POSTED"));
    }

    [Fact]
    public void Every_target_is_a_known_status()
    {
        foreach (var from in TallyBatchStatus.All)
            Assert.All(AccountingBatchTransitions.TargetsFrom(from), to => Assert.Contains(to, TallyBatchStatus.All));
    }
}

public sealed class TallyCorrespondenceKeyTests
{
    [Fact]
    public void Key_has_the_plan_shape_and_is_found_again_in_a_narration()
    {
        var key = TallyCorrespondenceKey.Build("WLMHW", 2027, "INV/1234", "SALES", 1);
        Assert.Equal("ETP:WLMHW:2027:INV/1234:SALES:1", key);
        Assert.Equal(new[] { key }, TallyCorrespondenceKey.FindAll($"{key} | ETP WLMHW invoice INV/1234 dated 25-Aug-2026"));
    }

    [Fact]
    public void An_80_character_and_a_return_series_document_number_round_trip_unchanged()
    {
        var longNumber = new string('7', 40) + new string('A', 40);
        foreach (var number in new[] { longNumber, "312345678" })
        {
            var key = TallyCorrespondenceKey.Build("HEMW", 2027, number, "CREDIT_NOTE", 2);
            Assert.Equal(new[] { key }, TallyCorrespondenceKey.FindAll($"Narration {key}."));
        }
    }

    [Theory]
    [InlineData("A:1")]
    [InlineData("A|1")]
    [InlineData("A 1")]
    [InlineData("")]
    public void Unsafe_document_numbers_are_refused(string number)
    {
        Assert.False(TallyCorrespondenceKey.IsDocumentNumberSafe(number));
        var error = Assert.Throws<ArgumentException>(() => TallyCorrespondenceKey.Build("WLMHW", 2027, number, "SALES", 1));
        Assert.Contains("KEY_UNSAFE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_number_over_80_characters_is_unsafe() =>
        Assert.False(TallyCorrespondenceKey.IsDocumentNumberSafe(new string('1', 81)));

    [Fact]
    public void Two_different_keys_in_one_text_are_both_reported()
    {
        var text = "ETP:WLMHW:2027:A1:SALES:1 and ETP:WLMHW:2027:A2:SALES:1 and again ETP:WLMHW:2027:A1:SALES:1";
        Assert.Equal(new[] { "ETP:WLMHW:2027:A1:SALES:1", "ETP:WLMHW:2027:A2:SALES:1" }, TallyCorrespondenceKey.FindAll(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Cash sale, no key")]
    [InlineData("ETP:wlmhw:2027:A1:SALES:1")]
    [InlineData("ETP:WLMHW:27:A1:SALES:1")]
    [InlineData("ETP:WLMHW:2027:A1:POSTED:1")]
    public void Text_without_a_well_formed_key_yields_nothing(string? text) =>
        Assert.Empty(TallyCorrespondenceKey.FindAll(text));

    [Theory]
    [InlineData("wlmhw", 2027, "SALES", 1)]
    [InlineData("WLMHW", 27, "SALES", 1)]
    [InlineData("WLMHW", 2027, "JOURNAL", 1)]
    [InlineData("WLMHW", 2027, "SALES", 0)]
    public void Malformed_key_parts_are_refused(string store, int year, string role, int revision) =>
        Assert.ThrowsAny<ArgumentException>(() => TallyCorrespondenceKey.Build(store, year, "A1", role, revision));
}

public sealed class TallyEvidencePathsTests
{
    [Theory]
    [InlineData(@"GOLDEN\WLMHW\2026-08\batch-12\payload.xml")]
    [InlineData(@"GOLDEN\WLMHW\2026-08\batch-12\manifest.json")]
    [InlineData(@"GOLDEN\WLMHW\2026-08\batch-12\attempts\attempt-3-request.xml")]
    [InlineData(@"GOLDEN\WLMHW\2026-08\batch-12\actuals\readback-4.xml")]
    [InlineData("GOLDEN/WLMHW/2026-08/batch-12/reconciliation/run-5.json")]
    public void Fixed_evidence_paths_are_accepted(string path) =>
        Assert.Equal(path.Replace('/', '\\'), TallyEvidencePaths.Validate(path));

    [Theory]
    [InlineData(@"GOLDEN\Ramesh Kumar\2026-08\batch-12\payload.xml")]
    [InlineData(@"GOLDEN\9876543210\2026-08\batch-12\payload.xml")]
    [InlineData(@"GOLDEN\WLMHW\..\batch-12\payload.xml")]
    [InlineData(@"..\WLMHW\2026-08\batch-12\payload.xml")]
    [InlineData(@"GOLDEN\WLMHW\2026-13\batch-12\payload.xml")]
    [InlineData(@"GOLDEN\WLMHW\2026-08\batch-0\payload.xml")]
    [InlineData(@"GOLDEN\WLMHW\2026-08\batch-12\Ramesh Kumar.xml")]
    [InlineData(@"GOLDEN\WLMHW\2026-08\batch-12\attempts\readback-4.xml")]
    [InlineData(@"GOLDEN\WLMHW\2026-08\batch-12\other\run-5.json")]
    [InlineData(@"C:\GOLDEN\WLMHW\2026-08\batch-12\payload.xml")]
    [InlineData(@"GOLDEN\WLMHW\2026-08\batch-12")]
    [InlineData("")]
    public void Anything_else_is_refused(string path) =>
        Assert.Throws<ArgumentException>(() => TallyEvidencePaths.Validate(path));

    [Fact]
    public void Batch_folder_is_built_from_codes_and_checked()
    {
        Assert.Equal(@"GOLDEN\WLMHW\2026-08\batch-12", TallyEvidencePaths.BatchFolder("GOLDEN", "WLMHW", new DateOnly(2026, 8, 25), 12));
        Assert.Throws<ArgumentException>(() => TallyEvidencePaths.BatchFolder("GOLDEN", "Ramesh Kumar", new DateOnly(2026, 8, 25), 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => TallyEvidencePaths.BatchFolder("GOLDEN", "WLMHW", new DateOnly(2026, 8, 25), 0));
    }
}

public sealed class TallyQuantityParserTests
{
    [Theory]
    [InlineData("1 Nos", "1.000", "Nos")]
    [InlineData(" -2.5 Kg ", "-2.500", "Kg")]
    [InlineData("12", "12.000", null)]
    [InlineData("0.125 Pcs", "0.125", "Pcs")]
    public void Number_then_unit_is_parsed(string text, string value, string? unit)
    {
        Assert.True(TallyQuantityParser.TryParse(text, out var quantity));
        Assert.Equal(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), quantity!.Value);
        Assert.Equal(unit, quantity.Unit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Nos")]
    [InlineData("1,000 Nos")]
    [InlineData("1.2345 Nos")]
    [InlineData("one Nos")]
    public void Anything_else_is_not_parsed_and_never_becomes_zero(string? text)
    {
        Assert.False(TallyQuantityParser.TryParse(text, out var quantity));
        Assert.Null(quantity);
    }
}

public sealed class TallyProfileRulesTests
{
    private static TallyProfile Valid() => TallyProfile.NewTest(" golden ", "  TEST - ETP Golden ", new[] { "wlmhw", " HEMW", "WLMHW", "" });

    [Fact]
    public void A_new_test_profile_is_normalised()
    {
        var value = TallyProfileRules.Normalise(Valid(), "First test company");
        Assert.Equal("GOLDEN", value.ProfileCode);
        Assert.Equal("TEST - ETP Golden", value.CompanyName);
        Assert.Equal(new[] { "HEMW", "WLMHW" }, value.StoreCodes);
        Assert.Equal("TEST", value.Environment);
        Assert.Equal("Cash Sales", value.SinglePartyLedger);
    }

    [Theory]
    [InlineData("http://127.0.0.1:9000/")]
    [InlineData("http://localhost:65535/")]
    public void This_PCs_Tally_is_accepted(string endpoint) =>
        Assert.Equal(endpoint, TallyProfileRules.Normalise(Valid() with { EndpointUrl = endpoint }, "Probe").EndpointUrl);

    [Theory]
    [InlineData("http://192.168.1.20:9000/")]
    [InlineData("https://127.0.0.1:9000/")]
    [InlineData("http://127.0.0.1:9000")]
    [InlineData("http://127.0.0.1:65536/")]
    [InlineData("http://127.0.0.1:0/")]
    [InlineData("http://localhost.example.com:9000/")]
    public void Any_other_Tally_address_is_refused(string endpoint)
    {
        var error = Assert.Throws<ArgumentException>(() => TallyProfileRules.Normalise(Valid() with { EndpointUrl = endpoint }, "Probe"));
        Assert.Equal(TallyProfileRules.OnlyThisPc, error.Message);
    }

    [Theory]
    [InlineData("party", "Named customer ledgers need a clearing ledger per tender mode.")]
    [InlineData("ledger", "Enter the one Tally ledger")]
    [InlineData("code", "short code")]
    [InlineData("company", "Tally company name")]
    [InlineData("http", "address where Tally answers")]
    [InlineData("json", "JSON files stay unavailable")]
    [InlineData("window", "first allowed voucher date")]
    [InlineData("store", "is not a store code")]
    [InlineData("environment", "test books or live books")]
    public void Invalid_profiles_are_refused_in_plain_words(string fault, string message)
    {
        var profile = fault switch
        {
            "party" => Valid() with { PartyPolicy = "NAMED_LEDGERS" },
            "ledger" => Valid() with { SinglePartyLedger = " " },
            "code" => Valid() with { ProfileCode = "Ramesh Kumar" },
            "company" => Valid() with { CompanyName = "  " },
            "http" => Valid() with { DefaultDeliveryMode = "HTTP" },
            "json" => Valid() with { PayloadFormat = "JSON" },
            "window" => Valid() with { PostingFromDate = new DateOnly(2026, 9, 1), PostingToDate = new DateOnly(2026, 8, 1) },
            "store" => Valid() with { StoreCodes = new[] { "WLM HW!" } },
            _ => Valid() with { Environment = "LIVE" }
        };
        var error = Assert.Throws<ArgumentException>(() => TallyProfileRules.Normalise(profile, "Synthetic change"));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_reason_is_required(string reason) =>
        Assert.Throws<ArgumentException>(() => TallyProfileRules.Normalise(Valid(), reason));
}
