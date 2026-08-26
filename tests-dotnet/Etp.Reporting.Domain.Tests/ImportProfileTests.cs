using Etp.Reporting.Domain.Imports;

namespace Etp.Reporting.Domain.Tests;

public sealed class ImportProfileTests
{
    [Fact]
    public void Normalizes_Source_Headers_Deterministically()
    {
        Assert.Equal("NET AMOUNT", ImportProfile.NormalizeHeader("  Net   Amount  "));
    }

    [Fact]
    public void Rejects_Duplicate_Canonical_Fields()
    {
        Assert.Throws<ArgumentException>(() => new ImportProfile(
            "R025", "1", "1", new string('a', 64),
            [
                new("Article", "product_code", CanonicalDataType.Identifier, true),
                new("SKU", "product_code", CanonicalDataType.Identifier, true)
            ]));
    }
}
