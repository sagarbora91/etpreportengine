using Etp.Reporting.Application.Service;
using Etp.Reporting.Application.Stores;
using Etp.Reporting.Desktop.Modules.Service;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>1.12.1 lane P8: the picker's "Not matched to any centre" choice and how a selected code is read. Synthetic only.</summary>
public sealed class ServiceCentreChoicesTests
{
    private static readonly ServiceCentre First = new("SCSYN1", "Synthetic Service Centre One", "JOSCSYN1");
    private static readonly ServiceCentre Second = new("SC999", "Synthetic Service Centre", "JOSC999");

    [Fact]
    public void Two_centres_get_the_not_matched_choice_last()
    {
        var choices = ServiceCentreChoices.WithNotMatched(ServiceCentreServices.Choices([First, Second]), 2);
        Assert.Equal([ServiceCentreServices.AllCentresLabel, First.Name, Second.Name, "Not matched to any centre"], choices.Select(choice => choice.Label));
        Assert.Equal(ServiceCentreScope.NotMatchedCode, choices[^1].Code);
    }

    [Fact]
    public void One_centre_gets_no_not_matched_choice()
    {
        var plain = ServiceCentreServices.Choices([First]);
        Assert.Same(plain, ServiceCentreChoices.WithNotMatched(plain, 1));
        Assert.Empty(ServiceCentreChoices.WithNotMatched([], 3));
    }

    [Fact]
    public void A_selected_code_reads_as_all_a_centre_or_not_matched()
    {
        var all = ServiceCentreChoices.Resolve([First, Second], null);
        Assert.False(all.IsFiltered);
        Assert.Null(all.StoreCode);

        var centre = ServiceCentreChoices.Resolve([First, Second], " sc999 ");
        Assert.True(centre.IsFiltered);
        Assert.False(centre.NotMatched);
        Assert.Equal(Second, centre.Centre);
        Assert.Equal("SC999", centre.StoreCode);
        Assert.Equal(Second.Name, centre.Name);

        var none = ServiceCentreChoices.Resolve([First, Second], ServiceCentreScope.NotMatchedCode);
        Assert.True(none.IsFiltered);
        Assert.True(none.NotMatched);
        Assert.Null(none.Centre);
        Assert.Equal(ServiceCentreScope.NotMatchedCode, none.StoreCode);
        Assert.Equal("Not matched to any centre", none.Name);

        Assert.False(ServiceCentreChoices.Resolve([First, Second], "SYNGONE").IsFiltered);
        Assert.False(ServiceCentreChoices.Resolve([First], ServiceCentreScope.NotMatchedCode).IsFiltered);
    }
}
