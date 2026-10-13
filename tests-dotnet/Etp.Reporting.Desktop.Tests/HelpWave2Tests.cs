using System.Text.RegularExpressions;
using Etp.Reporting.Desktop;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 wave 2 (lane DOC): every new screen has a Help topic, F1 on it opens that topic, and Help sentences stay free of
/// report and store codes. The task ids are the ones the plan (section 4) froze; each has a TaskNavigation row.
/// </summary>
public sealed class HelpWave2Tests
{
    private static readonly string[] NewTopicIds =
        ["morning", "no-sale-day", "import-fixes", "month-start", "tally-screens", "collections", "software-update"];

    [Theory]
    [InlineData("morning", "morning")]
    [InlineData("collections-setup", "collections")]
    [InlineData("bank-files", "collections")]
    [InlineData("collections-statement", "collections")]
    [InlineData("deposits-in-transit", "collections")]
    [InlineData("update-settings", "software-update")]
    public void Every_wave_two_task_id_has_a_help_topic_for_F1(string taskId, string expectedTopic)
    {
        var topicId = ContextHelpRouter.ResolveTopicId("Settings", null, taskId);
        Assert.Equal(expectedTopic, topicId);
        Assert.NotNull(HelpCentreRegistry.Find(topicId));
    }

    [Fact]
    public void Every_new_screen_topic_exists_with_a_category_and_keywords()
    {
        foreach (var id in NewTopicIds)
        {
            var topic = HelpCentreRegistry.Find(id);
            Assert.NotNull(topic);
            Assert.NotEmpty(topic!.Keywords);
            Assert.False(string.IsNullOrWhiteSpace(HelpCentreRegistry.Category(id)));
        }
    }

    [Fact]
    public void Help_titles_and_overviews_carry_no_report_or_store_codes()
    {
        // Report codes (R025, S004, A001-style) and the Service Centre store code are for the office tools, not for people reading Help.
        // The Service topic keeps "aw330" only as a search keyword so that someone who knows the code can still find it.
        var code = new Regex(@"\b(?:R|S|AW)\d{3}\b", RegexOptions.CultureInvariant);
        foreach (var topic in HelpCentreRegistry.Topics)
        {
            Assert.False(code.IsMatch(topic.Title), $"Help title '{topic.Title}' contains a code.");
            Assert.False(code.IsMatch(topic.Overview), $"Help topic '{topic.Id}' contains a code in its text.");
        }
    }

    [Fact]
    public void Service_help_names_the_tender_collection_in_words()
    {
        var text = HelpCentreRegistry.Find("service-centre")!.Overview;
        Assert.DoesNotContain("S004", text, StringComparison.Ordinal);
        Assert.Contains("tender collection", text, StringComparison.Ordinal);
    }

    [Fact]
    public void New_topics_are_found_by_the_words_people_use()
    {
        Assert.Contains(HelpCentreRegistry.Search("no sale"), topic => topic.Id == "no-sale-day");
        Assert.Contains(HelpCentreRegistry.Search("finalise all"), topic => topic.Id == "morning");
        Assert.Contains(HelpCentreRegistry.Search("copy all stores"), topic => topic.Id == "month-start");
        Assert.Contains(HelpCentreRegistry.Search("deposits in transit"), topic => topic.Id == "collections");
        Assert.Contains(HelpCentreRegistry.Search("install now"), topic => topic.Id == "software-update");
        Assert.Contains(HelpCentreRegistry.Search("test pc"), topic => topic.Id == "tally-screens");
        Assert.Contains(HelpCentreRegistry.Search("reapply"), topic => topic.Id == "import-fixes");
    }

    [Fact]
    public void Topics_that_open_a_screen_name_the_task_the_help_route_resolves()
    {
        // Unregistered ids (until U1 adds the rows) resolve to null: that only hides the Open button, it never throws.
        foreach (var id in NewTopicIds)
            _ = HelpTaskRoutes.Find(id);
        Assert.Equal("conflicts", HelpTaskRoutes.Find("import-fixes")?.Id);
        Assert.Equal("masters", HelpTaskRoutes.Find("month-start")?.Id);
    }
}
