using System.Globalization;

namespace Etp.Reporting.Reporting;

/// <summary>
/// What the reader of a shared pack sees (decision 45 #4, UIUX-OUTPUTS OUT-13/15, MOCK-OUT-08-after): the WhatsApp
/// message, the e-mail subject and body, and the shared PDF's file name. Written for the person receiving it: shop, day,
/// verdict and the day's sales, never an instruction to the sender ("attach the prepared PDF" stays in ETP's status
/// line) and never a GUID. Figures come from the pack's cover (<see cref="ReportPackCoverBuilder"/>), so the text always
/// matches the PDF it goes with.
/// </summary>
public static class ReportPackShareText
{
    /// <summary>"ETP-Pack-TW01-2026-10-11-g12.pdf": the generation id makes it unique without a GUID.</summary>
    public static string SharePdfFileName(ReportPackDocument pack, long generationId)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return $"ETP-Pack-{OutputText.FileNamePart(ReportPackCoverBuilder.SubjectOf(pack))}-{pack.DateTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-g{generationId.ToString(CultureInfo.InvariantCulture)}.pdf";
    }

    /// <summary>The same name with " (2)", " (3)"... when a file of that name is already in the folder (it may be open).</summary>
    public static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName); var extension = Path.GetExtension(fileName);
        for (var copy = 2; File.Exists(path); copy++) path = Path.Combine(folder, $"{stem} ({copy}){extension}");
        return path;
    }

    /// <summary>
    /// The WhatsApp message: "ETP daily pack · TW01 · Sat 11 Oct 2026", the verdict, the day's sales, the generation.
    /// </summary>
    public static string WhatsAppMessage(ReportPackDocument pack, int generationNumber)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var cover = ReportPackCoverBuilder.From(pack);
        var lines = new List<string> { $"ETP daily pack · {cover.Subject}", cover.Verdict };
        lines.AddRange(cover.Figures.Where(x => x.Label.EndsWith("Sales today", StringComparison.Ordinal) && x.Value != DsrDisplay.Missing).Select(x => $"{x.Label} {x.Value}"));
        lines.Add($"Generation {generationNumber.ToString(CultureInfo.InvariantCulture)} · PDF attached");
        return string.Join("\n", lines);
    }

    /// <summary>The message when the pack document is not at hand: shop, day and generation only.</summary>
    public static string WhatsAppMessage(string storeLabel, DateOnly businessDate, int generationNumber) =>
        $"ETP daily pack · {storeLabel} · {OutputText.ShortWeekdayDate(businessDate)}\nGeneration {generationNumber.ToString(CultureInfo.InvariantCulture)} · PDF attached";

    /// <summary>"ETP daily pack · TW01 · 11 Oct 2026 · Reconciled": searchable by shop and date in an inbox.</summary>
    public static string EmailSubject(ReportPackDocument pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var verdict = ReportPackCoverBuilder.Verdict(pack.OverallStatus);
        var day = pack.DateFrom == pack.DateTo ? OutputText.Date(pack.DateTo) : $"{OutputText.Date(pack.DateFrom)} – {OutputText.Date(pack.DateTo)}";
        return $"ETP daily pack · {ReportPackCoverBuilder.SubjectOf(pack)} · {day} · {verdict}";
    }

    /// <summary>The cover page in words: verdict, the four figures, every check that needs a look, the stamp.</summary>
    public static string EmailBody(ReportPackDocument pack, long generationId)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var cover = ReportPackCoverBuilder.From(pack);
        var lines = new List<string> { $"ETP daily reporting pack · {cover.Subject}", cover.Verdict, string.Empty };
        lines.AddRange(cover.Figures.Select(x => $"{x.Label}: {x.Value} ({x.Meaning})"));
        var attention = cover.Checks.Where(x => x.Tone is OutputTone.Critical or OutputTone.Warning).ToArray();
        if (attention.Length > 0)
        {
            lines.Add(string.Empty); lines.Add("To look at:");
            lines.AddRange(attention.Select(x => $"- {x.StatusWord}: {x.Name}. {x.Message}".TrimEnd()));
        }
        lines.Add(string.Empty);
        lines.Add($"{cover.Generated} · generation {generationId.ToString(CultureInfo.InvariantCulture)}. The full pack is the attached PDF.");
        lines.Add($"Values incl. GST, in ₹. {OutputText.MissingLegend}");
        return string.Join("\n", lines);
    }
}
