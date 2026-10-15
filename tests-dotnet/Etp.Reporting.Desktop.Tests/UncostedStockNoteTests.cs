using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

// 1.12.1 lane F2 (decision 69): stock rows with no cost price are named, never silently left out of the stock value.
public sealed class UncostedStockNoteTests
{
    private static readonly UncostedStockRecord[] Rows =
    [
        new("WLMHW", "CASIO", 2), new("WLMHW", "SEIKO", 1), new("HEMW", "FOSSIL", 1),
    ];

    [Fact]
    public void Title_uses_the_owner_wording_and_the_plural()
    {
        Assert.Equal("4 items have no cost price; stock value leaves them out", UncostedStockNote.Title(UncostedStockNote.Total(Rows)));
        Assert.Equal("1 item has no cost price; stock value leaves it out", UncostedStockNote.Title(1));
    }

    [Fact]
    public void Nothing_uncosted_says_nothing()
    {
        Assert.Equal("", UncostedStockNote.Title(0));
        Assert.Equal("", UncostedStockNote.Sentence([]));
        Assert.Equal("", UncostedStockNote.PackSentence([]));
        Assert.Equal("", ReportsWorkspaceView.UncostedExportText(""));
    }

    [Fact]
    public void Sentence_names_the_count_per_store_and_brand_in_code_order()
    {
        Assert.Equal("4 items have no cost price; stock value leaves them out (HEMW: FOSSIL 1; WLMHW: CASIO 2, SEIKO 1).",
            UncostedStockNote.Sentence(Rows));
    }

    [Fact]
    public void Pack_sentence_counts_per_store_only()
    {
        Assert.Equal("4 items have no cost price; stock value leaves them out (HEMW 1, WLMHW 3).", UncostedStockNote.PackSentence(Rows));
    }

    [Fact]
    public void Brands_of_one_store_that_differ_only_in_case_are_one_row()
    {
        Assert.Equal("WLMHW: casio 3", UncostedStockNote.PerStoreBrand([new("WLMHW", "casio", 1), new("WLMHW", "CASIO", 2)]));
    }

    [Fact]
    public void Export_text_is_added_after_a_space_so_the_note_reads_as_its_own_sentence()
    {
        Assert.Equal(" 1 item has no cost price; stock value leaves it out (WLMHW: CASIO 1).",
            ReportsWorkspaceView.UncostedExportText(UncostedStockNote.Sentence([new("WLMHW", "CASIO", 1)])));
    }
}
