using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Domain.Periods;
using Etp.Reporting.Domain.Primitives;
using Etp.Reporting.Domain.Sales;

namespace Etp.Reporting.Domain.Tests;

public sealed class CanonicalContractsTests
{
    [Fact]
    public void Money_Normalizes_Currency_And_Rejects_Cross_Currency_Arithmetic()
    {
        var inr = new Money(10.25m, " inr ");
        Assert.Equal("INR", inr.CurrencyCode);
        Assert.Equal(15.25m, inr.Add(new Money(5m, "INR")).Amount);
        Assert.Throws<InvalidOperationException>(() => inr.Add(new Money(1m, "USD")));
    }

    [Fact]
    public void Quantity_Rejects_Cross_Unit_Arithmetic()
    {
        var pieces = new Quantity(2m, "pcs");
        Assert.Equal(5m, pieces.Add(new Quantity(3m, "PCS")).Value);
        Assert.Throws<InvalidOperationException>(() => pieces.Add(new Quantity(1m, "KG")));
    }

    [Fact]
    public void Source_Lineage_Requires_Valid_Hash_And_Positive_Row()
    {
        Assert.Throws<ArgumentException>(() => new SourceLineage("bad", "Sales", 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceLineage(new string('a', 64), "Sales", 0));
    }

    [Fact]
    public void Sales_Command_Preserves_Source_Signs_Without_Classifying_Them()
    {
        var command = new SalesImportCommand("S1", "INV1", "1", new DateOnly(2026, 8, 25), "P1",
            new Quantity(-2m, "PCS"), new Money(-100m, "INR"),
            new SourceLineage(new string('a', 64), "Sales", 2), "UNKNOWN");

        Assert.Equal(-2m, command.SourceQuantity.Value);
        Assert.Equal(-100m, command.SourceGrossAmount.Amount);
        Assert.Equal("UNKNOWN", command.SourceTransactionType);
        Assert.Equal(0, (int)SalesTransactionClassification.Unresolved);
    }

    [Fact]
    public void Date_Range_Is_Inclusive_And_Ordered()
    {
        var range = new DateRange(new DateOnly(2024, 2, 28), new DateOnly(2024, 3, 1));
        Assert.Equal(3, range.InclusiveDayCount);
        Assert.Throws<ArgumentException>(() => new DateRange(range.End, range.Start));
    }
}
