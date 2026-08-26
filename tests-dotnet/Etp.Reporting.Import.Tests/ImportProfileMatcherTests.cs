using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Tests;

public sealed class ImportProfileMatcherTests
{
    [Fact]
    public void Matches_Known_Headers_Without_Depending_On_Display_Casing()
    {
        var signature = ImportProfileMatcher.CreateHeaderSignature(["Bill Date", "Article", "Net Amount"]);
        var profile = new ImportProfile("R025", "1", "1", signature,
        [
            new("Bill Date", "transaction_date", CanonicalDataType.Date, true),
            new("Article", "product_code", CanonicalDataType.Identifier, true),
            new("Net Amount", "net_sales_amount", CanonicalDataType.Decimal, true)
        ]);

        var match = new ImportProfileMatcher().Match([" bill date ", "ARTICLE", "Net  Amount"], [profile]);

        Assert.Same(profile, match);
    }

    [Fact]
    public void Unknown_Layout_Does_Not_Guess()
    {
        var signature = ImportProfileMatcher.CreateHeaderSignature(["Bill Date"]);
        var profile = new ImportProfile("R025", "1", "1", signature,
            [new("Bill Date", "transaction_date", CanonicalDataType.Date, true)]);

        Assert.Null(new ImportProfileMatcher().Match(["Different Header"], [profile]));
    }
}
