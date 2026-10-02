using Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class AccountingValidationRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 1);
    private static TallyProfile Profile => TallyProfile.NewTest("GOLDEN", "TEST - ETP Golden", new[] { "WLMHW" }) with { Id = 1 };
    private static ValidationContext Context => new(Profile, Today, HasFinalGeneration: true);

    // A cash sale of 1,180.00: 1,000.00 taxable, CGST 9% 90.00, SGST 9% 90.00.
    private static ValidationVoucher Sale(int sequence = 1) => new(
        sequence, "WLMHW", "INV-1", "INV", new DateOnly(2026, 8, 25), 1180m, 1000m, 180m,
        new[] { new ValidationLine("L1", 1180m, 1000m, 180m, new[] { new ValidationTaxComponent("CGST", 9m, 90m), new ValidationTaxComponent("SGST", 9m, 90m) }) },
        new[] { new ValidationTender("CASH", "Cash", false, 1180m) },
        new[]
        {
            new ValidationEntry("TENDER_CASH", "Cash", 1180m, 0m),
            new ValidationEntry("SALES_REVENUE", "Sales", 0m, 1000m),
            new ValidationEntry("OUTPUT_CGST_9", "Output CGST 9%", 0m, 90m),
            new ValidationEntry("OUTPUT_SGST_9", "Output SGST 9%", 0m, 90m)
        });

    [Fact]
    public void A_complete_cash_sale_has_no_findings_and_can_be_approved()
    {
        var findings = AccountingValidationRules.Evaluate(Context, new[] { Sale() });
        Assert.Empty(findings);
        Assert.True(AccountingValidationRules.CanApprove(findings, _ => false));
        Assert.Null(AccountingValidationRules.BlockedReason(findings, 1));
    }

    [Theory]
    [InlineData("return", "RULE-SRC-002", "FAIL")]
    [InlineData("split", "RULE-SRC-003", "FAIL")]
    [InlineData("multirate", "RULE-SRC-003", "FAIL")]
    [InlineData("changed", "RULE-SRC-004", "FAIL")]
    [InlineData("unmapped", "RULE-MAP-001", "FAIL")]
    [InlineData("unmappedtender", "RULE-MAP-002", "FAIL")]
    [InlineData("unknowntender", "RULE-MAP-002", "FAIL")]
    [InlineData("unbalanced", "RULE-AMT-001", "FAIL")]
    [InlineData("gross", "RULE-AMT-002", "FAIL")]
    [InlineData("tendershort", "RULE-AMT-003", "FAIL")]
    [InlineData("large", "RULE-AMT-004", "WARN")]
    [InlineData("taxmissing", "RULE-TAX-001", "FAIL")]
    [InlineData("taxsplit", "RULE-TAX-001", "FAIL")]
    [InlineData("unsafe", "RULE-KEY-001", "FAIL")]
    [InlineData("reserved", "RULE-KEY-001", "FAIL")]
    [InlineData("sent", "RULE-KEY-001", "FAIL")]
    [InlineData("future", "RULE-DAT-001", "FAIL")]
    [InlineData("old", "RULE-DAT-002", "WARN")]
    public void Each_fault_is_reported_by_its_rule(string fault, string rule, string severity)
    {
        var sale = Sale();
        var voucher = fault switch
        {
            "return" => sale with { TransactionType = "SR" },
            "split" => sale with { Tenders = new[] { new ValidationTender("CASH", "Cash", false, 1000m), new ValidationTender("UPI", "UPI", false, 180m) } },
            "multirate" => sale with { Lines = new[] { sale.Lines[0], sale.Lines[0] with { LineIdentifier = "L2", TaxComponents = new[] { new ValidationTaxComponent("CGST", 2.5m, 90m), new ValidationTaxComponent("SGST", 2.5m, 90m) } } } },
            "changed" => sale with { SourceUnchanged = false },
            "unmapped" => sale with { Entries = sale.Entries.Select(e => e.BusinessEvent == "SALES_REVENUE" ? e with { LedgerName = null } : e).ToArray() },
            "unmappedtender" => sale with { Entries = sale.Entries.Select(e => e.BusinessEvent == "TENDER_CASH" ? e with { LedgerName = " " } : e).ToArray() },
            "unknowntender" => sale with { Tenders = new[] { new ValidationTender("PAYMENTTYPE99", null, false, 1180m) } },
            "unbalanced" => sale with { Entries = sale.Entries.Select(e => e.BusinessEvent == "SALES_REVENUE" ? e with { Credit = 999.99m } : e).ToArray() },
            "gross" => sale with { Gross = 1180.01m, Tenders = new[] { new ValidationTender("CASH", "Cash", false, 1180.01m) } },
            "tendershort" => sale with { Tenders = new[] { new ValidationTender("CASH", "Cash", false, 1179.60m) } },
            "large" => Large(sale),
            "taxmissing" => sale with { Lines = new[] { sale.Lines[0] with { TaxComponents = null } } },
            "taxsplit" => sale with { Lines = new[] { sale.Lines[0] with { TaxComponents = new[] { new ValidationTaxComponent("CGST", 9m, 90m), new ValidationTaxComponent("SGST", 9m, 89.99m) } } } },
            "unsafe" => sale with { DocumentNumber = "INV:1" },
            "reserved" => sale with { KeyAlreadyReserved = true },
            "sent" => sale with { AlreadySent = true },
            "future" => sale with { VoucherDate = Today.AddDays(1) },
            _ => sale with { VoucherDate = Today.AddDays(-31) }
        };
        var findings = AccountingValidationRules.Evaluate(Context, new[] { voucher });
        var finding = Assert.Single(findings);
        Assert.Equal(rule, finding.RuleId);
        Assert.Equal(severity, finding.Severity);
        Assert.Equal(1, finding.VoucherSequence);
        Assert.False(string.IsNullOrWhiteSpace(finding.Explanation));
    }

    [Fact]
    public void Round_off_completes_the_tender_and_is_never_a_payment_mode()
    {
        var sale = Sale() with { Gross = 1180.40m, Net = 1000.40m, Lines = new[] { Sale().Lines[0] with { Gross = 1180.40m, Net = 1000.40m } },
            Tenders = new[] { new ValidationTender("CASH", "Cash", false, 1180m), new ValidationTender("ROUND_OFF", null, true, 0.40m) },
            Entries = new[]
            {
                new ValidationEntry("TENDER_CASH", "Cash", 1180m, 0m), new ValidationEntry("ROUND_OFF", "Round Off", 0.40m, 0m),
                new ValidationEntry("SALES_REVENUE", "Sales", 0m, 1000.40m), new ValidationEntry("OUTPUT_CGST_9", "Output CGST 9%", 0m, 90m),
                new ValidationEntry("OUTPUT_SGST_9", "Output SGST 9%", 0m, 90m)
            } };
        Assert.Empty(AccountingValidationRules.Evaluate(Context, new[] { sale }));
    }

    [Fact]
    public void Batch_level_failures_block_every_voucher()
    {
        var context = Context with { HasFinalGeneration = false, Profile = Profile with { Environment = "PRODUCTION", IsEnabled = false } };
        var findings = AccountingValidationRules.Evaluate(context, new[] { Sale(1), Sale(2) with { DocumentNumber = "INV-2" } });
        Assert.Equal(new[] { "RULE-SRC-001", "RULE-ENV-001", "RULE-ENV-001" }, findings.Select(f => f.RuleId));
        Assert.All(findings, f => Assert.Null(f.VoucherSequence));
        Assert.Equal("RULE-SRC-001, RULE-ENV-001", AccountingValidationRules.BlockedReason(findings, 2));
    }

    [Fact]
    public void Production_profile_without_enable_fails()
    {
        var context = Context with { Profile = Profile with { Environment = "PRODUCTION" } };
        var finding = Assert.Single(AccountingValidationRules.Evaluate(context, new[] { Sale() }));
        Assert.Equal("RULE-ENV-001", finding.RuleId);
        Assert.Empty(AccountingValidationRules.Evaluate(context with { Profile = context.Profile with { ProductionEnabledUtc = DateTime.UtcNow } }, new[] { Sale() }));
    }

    [Fact]
    public void A_warning_blocks_approval_until_it_is_accepted()
    {
        var findings = AccountingValidationRules.Evaluate(Context, new[] { Large(Sale()) });
        Assert.False(AccountingValidationRules.CanApprove(findings, _ => false));
        Assert.True(AccountingValidationRules.CanApprove(findings, finding => finding.RuleId == "RULE-AMT-004"));
        Assert.Null(AccountingValidationRules.BlockedReason(findings, 1));
    }

    [Fact]
    public void Posting_window_and_tender_rules_follow_the_profile()
    {
        var windowed = Context with { Profile = Profile with { PostingFromDate = new DateOnly(2026, 8, 26) } };
        Assert.Equal("RULE-DAT-001", Assert.Single(AccountingValidationRules.Evaluate(windowed, new[] { Sale() })).RuleId);

        var clearing = Context with { Profile = Profile with { TenderModel = "CLEARING_LEDGER", PartyPolicy = "NAMED_LEDGERS" } };
        var shortTender = Sale() with { Tenders = new[] { new ValidationTender("CASH", "Cash", false, 1179.60m) } };
        Assert.Equal("WARN", Assert.Single(AccountingValidationRules.Evaluate(clearing, new[] { shortTender })).Severity);
    }

    // Twice the money everywhere keeps the sale balanced and pushes it over the 5,00,000 warning line.
    private static ValidationVoucher Large(ValidationVoucher sale)
    {
        const decimal f = 500m;
        return sale with
        {
            Gross = sale.Gross * f, Net = sale.Net * f, Tax = sale.Tax * f,
            Lines = sale.Lines.Select(l => l with { Gross = l.Gross * f, Net = l.Net * f, Tax = l.Tax * f, TaxComponents = l.TaxComponents!.Select(c => c with { Amount = c.Amount * f }).ToArray() }).ToArray(),
            Tenders = sale.Tenders.Select(t => t with { Amount = t.Amount * f }).ToArray(),
            Entries = sale.Entries.Select(e => e with { Debit = e.Debit * f, Credit = e.Credit * f }).ToArray()
        };
    }
}
