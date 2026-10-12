using Etp.Reporting.Reporting;
using PdfSharp.Pdf.IO;

namespace Etp.Reporting.Reporting.Tests;

/// <summary>
/// 1.12 lane U4 (decision 45 #4, UIUX-OUTPUTS OUT-08/13/15): the pack's cover page, the shared PDF's file name and the
/// WhatsApp / e-mail text. The cover reads the pack's own Control Summary, so its figures are the table's figures.
/// </summary>
public sealed class ReportPackCoverTests
{
    private static ReportPackDocument StorePack(string overall = "Failed") => new(
        "ETP Daily Reporting Pack — TW01",
        new(2026, 10, 11), new(2026, 10, 11), overall, "v-test", "The pack was generated with visible missing sources or reconciliation exceptions.",
        new DateTimeOffset(2026, 10, 12, 3, 44, 0, TimeSpan.Zero),
        [
            new("Control Summary", overall, "Synthetic",
                new([new("Report"), new("Status"), new("Control Total", "#,##0.00"), new("Variance", "#,##0.00"), new("Message")],
                [
                    ["Customer-wise Invoice Sales", "Passed", 145_970m, null, "8 invoices; zero sales remains distinct from a missing source. More text."],
                    ["Daily Sales Report", "Passed", 145_970m, 0m, "FTD DSR uses R025."],
                    ["Tender Reconciliation", "Passed", 145_970m, 0m, "Every document matches."],
                    ["Daily Cash Reconciliation", "Failed", 42_310m, -500m, "Counted cash is below calculated cash."],
                    ["System Closing Stock", "Passed", 1_812m, 0m, "Expected equals reported."],
                    ["Physical Closing Stock", "NotRun", null, null, "3 brand(s) do not yet have a counted physical quantity."],
                    ["Business Date Finalisation", "NotRun", null, null, "Not finalised yet."]
                ],
                ["Overall", overall, null, null, "Synthetic"])),
            new("Cash Book", "Failed", "Synthetic", new([new("Line"), new("Amount", "#,##0.00")], [["Opening", 1000m]]))
        ]);

    [Fact]
    public void Cover_shows_the_control_summary_figures_unchanged_with_status_words()
    {
        var cover = ReportPackCoverBuilder.From(StorePack());

        Assert.Equal("TW01 · Sun 11 Oct 2026", cover.Subject);
        Assert.Equal("Not reconciled · 1 to look at", cover.Verdict); // the cash Problem; "Not checked yet" is not a problem
        Assert.Equal(OutputTone.Critical, cover.VerdictTone);
        Assert.StartsWith("Generated 12 Oct 2026", cover.Generated, StringComparison.Ordinal);
        Assert.Equal(
            [("Sales today", "₹1,45,970.00"), ("Tender difference", "₹0.00"), ("Cash difference", "−₹500.00"), ("Stock difference", "0")],
            cover.Figures.Select(x => (x.Label, x.Value)));
        Assert.Equal([OutputTone.Neutral, OutputTone.Success, OutputTone.Critical, OutputTone.Success], cover.Figures.Select(x => x.Tone));
        Assert.Equal(7, cover.Checks.Count);
        Assert.Equal(["OK", "OK", "OK", "Problem", "OK", "Not checked yet", "Not checked yet"], cover.Checks.Select(x => x.StatusWord));
        Assert.Equal("8 invoices; zero sales remains distinct from a missing source.", cover.Checks[0].Message);
        Assert.DoesNotContain(cover.Checks, x => x.StatusWord is "Passed" or "NotRun" or "Failed");
    }

    [Fact]
    public void Combined_pack_cover_keeps_each_store_figure_and_never_adds_them_up()
    {
        var pack = StorePack() with
        {
            Title = "ETP Complete Daily Management Pack — Titan World + Helios",
            Tables =
            [
                new("Combined Control Summary", "Failed", "Synthetic",
                    new([new("Store"), new("Report"), new("Status"), new("Control Total", "#,##0.00"), new("Variance", "#,##0.00"), new("Message")],
                    [
                        ["WLMHW", "Daily Sales Report", "Passed", 69_880m, 0m, "x"],
                        ["HEMW", "Daily Sales Report", "Blocked", null, null, "R022 source is missing."]
                    ]))
            ]
        };
        var cover = ReportPackCoverBuilder.From(pack);
        Assert.StartsWith("Titan World + Helios · ", cover.Subject, StringComparison.Ordinal);
        Assert.Contains(cover.Figures, x => x.Label == "WLMHW · Sales today" && x.Value == "₹69,880.00");
        Assert.Contains(cover.Figures, x => x.Label == "HEMW · Sales today" && x.Value == "—");
        Assert.Contains(cover.Checks, x => x.Name == "HEMW · Daily Sales Report" && x.StatusWord == "Cannot run" && x.Tone == OutputTone.Critical);
    }

    [Fact]
    public void A_pack_without_a_control_summary_lists_its_tables()
    {
        var pack = StorePack() with { Tables = [new("Brand Sales", "Passed", "All good.", new([new("Brand")], [["Titan"]]))] };
        var cover = ReportPackCoverBuilder.From(pack);
        Assert.Empty(cover.Figures);
        Assert.Equal(("Brand Sales", "OK"), (cover.Checks.Single().Name, cover.Checks.Single().StatusWord));
    }

    [Fact]
    public void Pack_pdf_starts_with_the_portrait_cover()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etp-pack-cover-{Guid.NewGuid():N}.pdf");
        try
        {
            new SimplePdfReportPackExporter().Export(path, StorePack());
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Assert.Equal(DailySalesReportPdfExporter.PhonePageWidth, pdf.Pages[0].Width.Point, 1);
            Assert.True(pdf.Pages[0].Height.Point > pdf.Pages[0].Width.Point);
            Assert.True(pdf.PageCount >= 3);
            Assert.All(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>().Skip(1), page => Assert.True(page.Width.Point > page.Height.Point));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Shared_file_name_has_shop_date_and_generation_and_no_guid()
    {
        Assert.Equal("ETP-Pack-TW01-2026-10-11-g42.pdf", ReportPackShareText.SharePdfFileName(StorePack(), 42));
        Assert.Equal("ETP-Pack-TitanWorld-Helios-2026-10-11-g7.pdf",
            ReportPackShareText.SharePdfFileName(StorePack() with { Title = "ETP Complete Daily Management Pack — Titan World + Helios" }, 7));

        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"etp-share-{Guid.NewGuid():N}")).FullName;
        try
        {
            Assert.Equal(Path.Combine(folder, "a.pdf"), ReportPackShareText.UniquePath(folder, "a.pdf"));
            File.WriteAllText(Path.Combine(folder, "a.pdf"), "x");
            Assert.Equal(Path.Combine(folder, "a (2).pdf"), ReportPackShareText.UniquePath(folder, "a.pdf"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void WhatsApp_and_email_text_are_written_for_the_reader()
    {
        var message = ReportPackShareText.WhatsAppMessage(StorePack(), 3);
        Assert.Equal("ETP daily pack · TW01 · Sun 11 Oct 2026\nNot reconciled · 1 to look at\nSales today ₹1,45,970.00\nGeneration 3 · PDF attached", message);
        Assert.DoesNotContain("attach the prepared", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("11-Oct-2026", message, StringComparison.Ordinal);

        Assert.Equal("ETP daily pack · TW01 · 11 Oct 2026 · Not reconciled", ReportPackShareText.EmailSubject(StorePack()));
        Assert.Equal("ETP daily pack · TW01 · 11 Oct 2026 · Reconciled", ReportPackShareText.EmailSubject(StorePack("Passed")));
        var body = ReportPackShareText.EmailBody(StorePack(), 42);
        Assert.Contains("Cash difference: −₹500.00", body, StringComparison.Ordinal);
        Assert.Contains("- Problem: Daily Cash Reconciliation.", body, StringComparison.Ordinal);
        Assert.Contains("generation 42", body, StringComparison.Ordinal);
        Assert.Equal("ETP daily pack · TW01 · Sun 11 Oct 2026\nGeneration 2 · PDF attached", ReportPackShareText.WhatsAppMessage("TW01", new(2026, 10, 11), 2));
    }
}
