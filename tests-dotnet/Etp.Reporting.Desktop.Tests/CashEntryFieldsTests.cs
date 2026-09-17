using Etp.Reporting.Desktop.Modules.DailyWorkflow;

namespace Etp.Reporting.Desktop.Tests;

public sealed class CashEntryFieldsTests
{
    private sealed record Field(string FieldCode);

    private static IReadOnlyList<string> Pick(params string[] available) =>
        CashEntryFields.Prominent(available.Select(x => new Field(x)).ToArray(), x => x.FieldCode)
            .Select(x => x.FieldCode).ToArray();

    [Fact]
    public void Expenses_is_a_first_class_cash_field()
    {
        // The whole point of the change: Expenses was reachable only by opening a
        // generic dropdown, so the Cash Book asked for a figure that looked absent.
        Assert.Contains("EXPENSES", CashEntryFields.Ordered);
        Assert.Contains("EXPENSES", Pick("WALK_INS", "EXPENSES", "OPENING_CASH"));
    }

    [Fact]
    public void Fields_follow_the_cash_book_formula_order_not_the_database_order()
    {
        var picked = Pick("CLOSING_CASH_COUNTED", "EXPENSES", "OPENING_CASH", "CASH_DEPOSIT", "SERVICE_CASH");
        Assert.Equal(["OPENING_CASH", "SERVICE_CASH", "EXPENSES", "CASH_DEPOSIT", "CLOSING_CASH_COUNTED"], picked);
    }

    [Fact]
    public void Walk_ins_and_stock_fields_stay_off_the_cash_row()
    {
        var picked = Pick("WALK_INS", "PHYSICAL_STOCK", "BACKSTOCK", "OPERATIONAL_REMARK", "EXPENSES");
        Assert.Equal(["EXPENSES"], picked);
    }

    [Fact]
    public void A_field_the_database_does_not_offer_is_never_invented()
    {
        // SALES_TARGET is inactive in the shop database; deactivating a cash field must
        // remove it from the row rather than produce an input the engine would reject.
        Assert.Equal(["OPENING_CASH"], Pick("OPENING_CASH"));
        Assert.Empty(Pick());
        Assert.Empty(CashEntryFields.Prominent<Field>(null, x => x.FieldCode));
    }

    [Fact]
    public void Duplicate_definitions_are_shown_once()
    {
        Assert.Equal(["EXPENSES"], Pick("EXPENSES", "EXPENSES"));
    }

    [Fact]
    public void Every_field_the_cash_book_formula_consumes_is_present()
    {
        // CashBookRepository reads exactly these; if one drops out of this list the
        // Cash Book can never reach "Complete".
        foreach (var required in new[]
                 { "OPENING_CASH", "SERVICE_CASH", "SERVICE_CARD", "SERVICE_UPI",
                   "EXPENSES", "CASH_DEPOSIT", "CASH_ADJUSTMENT", "CLOSING_CASH_COUNTED" })
            Assert.Contains(required, CashEntryFields.Ordered);
    }
}
