using System.Globalization;
using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.Service;

namespace Etp.Reporting.Application.Morning;

/// <summary>
/// 1.12 U1 (wave plan C-a, decision 2): turns <see cref="MorningSources"/> into the Owner's Morning page. Pure: no SQL,
/// no files, no clock of its own, so every state is tested from fixtures. Wording follows ETP-DESIGN-SYSTEM.md: every state
/// is a word with a tone (ST1), no report code in a sentence, no amounts and no names beyond the store's own.
/// </summary>
public static class MorningSummaryBuilder
{
    /// <summary>Same limit as OPS-10 (<c>OffPcCopyProbe.MaximumCopyAgeHours</c>) and OPS-11 (G1): 36 hours.</summary>
    public const double CopyAgeLimitHours = 36;

    /// <summary>A nightly run older than this many days reads as old (same as the System check tile).</summary>
    public const int NightlyStaleAfterDays = 3;

    /// <summary>The month-start card shows on days 1 to 7 of a month.</summary>
    public const int MonthStartLastDay = 7;

    public const string HealthCheckTask = "health-check";
    public const string ImportHistoryTask = "import-history";
    public const string ImportProblemsTask = "conflicts";
    /// <summary>The Fix handler U2 registers (plan section 4); the shell falls back to Problems until it exists.</summary>
    public const string ImportFixTask = "import-problems";
    public const string ServiceTodayTask = "service-today";
    public const string CloseDayTask = "readiness";
    public const string TargetsTask = "masters";

    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    public static MorningSummary Build(MorningSources sources, DateOnly today, DateTimeOffset nowUtc, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        zone ??= TimeZoneInfo.Local;
        var cards = sources.Stores
            .Select(store => Card(store, sources.ShopResults, zone))
            .ToArray();
        var ready = cards.Where(card => card.IsReady).Select(card => card.StoreCode).ToArray();
        var (verdict, detail, tone) = Verdict(sources.BusinessDate, cards);
        var chips = new[]
        {
            SystemCheckChip(sources, today),
            ShopFilesChip(sources, zone),
            ServiceChip(sources.ServiceFreshness)
        };
        return new(sources.BusinessDate, verdict, detail, tone, ready, chips, cards,
            MonthStartCard(sources, today), Footer(sources, nowUtc, zone), nowUtc);
    }

    // ------------------------------------------------------------------ verdict

    public static (string Verdict, string Detail, MorningTone Tone) Verdict(DateOnly day, IReadOnlyList<MorningStoreCard> cards)
    {
        var date = day.ToString("dddd dd MMM", English);
        if (cards.Count == 0) return ($"{date}: no active shop", "Add a shop in Settings > Stores to see it here.", MorningTone.Neutral);
        var open = cards.Where(card => card.Status != MorningStoreStatus.Locked).ToArray();
        if (open.Length == 0)
            return ($"{date}: all {Stores(cards.Count)} finalised", "Nothing to close. The day is locked for every shop.", MorningTone.Success);
        var ready = open.Count(card => card.IsReady);
        var blocked = open.Where(card => !card.IsReady).ToArray();
        var verdict = ready == 0 ? $"{date}: no store is ready to close" : $"{date}: {ready} of {Stores(open.Length)} ready to close";
        if (blocked.Length == 0)
            return (verdict, "Finalise them in one go. Each store is locked on its own; Reopen day undoes one.", MorningTone.Success);
        var first = blocked[0];
        var why = first.Blockers.Count > 0 ? first.Blockers[0] : first.StatusWord.ToLowerInvariant();
        var state = first.Status == MorningStoreStatus.NoSaleDay ? "a no-sale day but not ready" : first.StatusWord.ToLowerInvariant();
        var text = $"{first.StoreName} is {state}: {Lower(why)}.";
        if (blocked.Length > 1) text += blocked.Length == 2 ? " 1 more store is not ready." : $" {blocked.Length - 1} more stores are not ready.";
        return (verdict, text, ready == 0 ? MorningTone.Critical : MorningTone.Warning);
    }

    private static string Stores(int count) => count == 1 ? "1 store" : $"{count} stores";

    // ------------------------------------------------------------------ store card

    public static MorningStoreCard Card(MorningStoreSource store, IReadOnlyList<MorningShopResult> results, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(store);
        var name = string.IsNullOrWhiteSpace(store.StoreName) ? store.StoreCode : store.StoreName.Trim();
        var refused = store.Imports.Where(entry => entry.Result.Failed).OrderByDescending(entry => entry.RecordedUtc).ToArray();
        var lines = new List<MorningCardLine>();
        var actions = new List<MorningAction>();
        var workflow = store.Workflow;

        if (workflow is null)
        {
            var failure = string.IsNullOrWhiteSpace(store.LoadFailure) ? "The day could not be read. Refresh to try again." : store.LoadFailure!.Trim();
            lines.Add(SentBack(store, results, zone));
            return new(store.StoreCode, name, MorningStoreStatus.NotChecked, "Not checked", MorningTone.Neutral, lines, [failure], [], store.NoSale, false);
        }

        lines.Add(FilesLine(workflow, refused, store.Imports, zone));
        lines.Add(EntriesLine(workflow, store.NoSale is not null));
        lines.Add(ChecksLine(workflow));
        lines.Add(SentBack(store, results, zone));

        if (workflow.Status == DailyWorkflowStatus.Locked)
        {
            actions.Add(new("Open Close day", CloseDayTask));
            return new(store.StoreCode, name, MorningStoreStatus.Locked, "Finalised", MorningTone.Success, lines, [], actions, store.NoSale, false);
        }

        IReadOnlyList<string> blockers = workflow.CanFinalise ? [] : Blockers(workflow, refused);
        foreach (var entry in refused.Take(3))
            actions.Add(new("Fix " + FileName(entry.Result.ReportCode).ToLowerInvariant(), ImportFixTask, entry.Key));
        if (!workflow.CanFinalise && store.NoSale is null)
        {
            if (workflow.MissingRequiredInputs.Contains("WALK_INS", StringComparer.OrdinalIgnoreCase)) actions.Add(new("Enter walk-ins", "walk-ins"));
            if (workflow.MissingRequiredInputs.Any(code => !code.Equals("WALK_INS", StringComparison.OrdinalIgnoreCase))) actions.Add(new("Enter cash", "cash-input"));
        }
        actions.Add(new("Open Close day", CloseDayTask));

        // Decision 52: a store-day with no sales file at all can be marked as a no-sale day; N1's procedure has the last word.
        var canMark = store.NoSale is null && !workflow.ImportedReports.Contains("R025", StringComparer.OrdinalIgnoreCase);

        if (store.NoSale is not null)
            return new(store.StoreCode, name, MorningStoreStatus.NoSaleDay, "No-sale day", MorningTone.Information, lines, blockers, actions, store.NoSale, false);
        return workflow.CanFinalise
            ? new(store.StoreCode, name, MorningStoreStatus.Ready, "Ready", MorningTone.Success, lines, [], actions, null, canMark)
            : new(store.StoreCode, name, MorningStoreStatus.Blocked, "Blocked", MorningTone.Critical, lines, blockers, actions, null, canMark);
    }

    public static IReadOnlyList<string> Blockers(DailyWorkflowState workflow, IReadOnlyList<ImportHistoryEntry> refused)
    {
        var result = new List<string>();
        var refusedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in refused)
        {
            var code = entry.Result.ReportCode ?? string.Empty;
            if (!refusedCodes.Add(code)) continue;
            var why = entry.Result.Failure?.SafeMessage ?? entry.Result.Message;
            result.Add(string.IsNullOrWhiteSpace(why) ? $"{FileName(code)} was refused" : $"{FileName(code)} was refused ({TrimSentence(why)})");
        }
        foreach (var code in workflow.MissingReports.Where(code => !refusedCodes.Contains(code)))
            result.Add($"{FileName(code)} not imported yet");
        foreach (var code in workflow.MissingRequiredInputs)
            result.Add($"{InputName(code)} not entered");
        return result;
    }

    private static MorningCardLine FilesLine(DailyWorkflowState workflow, IReadOnlyList<ImportHistoryEntry> refused, IReadOnlyList<ImportHistoryEntry> imports, TimeZoneInfo zone)
    {
        var total = workflow.ImportedReports.Count + workflow.MissingReports.Count;
        var arrived = imports.Where(entry => !entry.Result.Failed).Select(entry => entry.RecordedUtc).DefaultIfEmpty().Max();
        var at = arrived == default ? string.Empty : " · last at " + Local(arrived, zone).ToString("HH:mm", English);
        if (refused.Count > 0)
        {
            var names = string.Join(", ", refused.Select(entry => FileName(entry.Result.ReportCode)).Distinct(StringComparer.Ordinal));
            return new("Files", refused.Count == 1 ? "1 refused" : $"{refused.Count} refused", MorningTone.Critical, $"Refused: {Lower(names)}{at}");
        }
        if (workflow.MissingReports.Count > 0)
            return new("Files", $"{workflow.ImportedReports.Count} of {total} in", workflow.ImportedReports.Count == 0 ? MorningTone.Critical : MorningTone.Warning,
                "Waiting for: " + Lower(string.Join(", ", workflow.MissingReports.Select(FileName).Distinct(StringComparer.Ordinal))) + at);
        return new("Files", $"{total} of {total} in", MorningTone.Success, "Every required file is in" + at);
    }

    private static MorningCardLine EntriesLine(DailyWorkflowState workflow, bool noSale)
    {
        if (workflow.MissingRequiredInputs.Count == 0)
            return new("Entries", "Entered", MorningTone.Success, noSale ? "Not needed on a no-sale day" : "Walk-ins and cash entries are in");
        return new("Entries", "Not entered", MorningTone.Warning,
            Lower(string.Join(", ", workflow.MissingRequiredInputs.Select(InputName))) + " not entered (they may come with the shop's daily sheet)");
    }

    private static MorningCardLine ChecksLine(DailyWorkflowState workflow) => workflow.Status switch
    {
        DailyWorkflowStatus.Locked => new("Checks", "Locked", MorningTone.Success, "The day is finalised"),
        DailyWorkflowStatus.Reconciled => new("Checks", "Passed", MorningTone.Success, "Controls reconcile"),
        DailyWorkflowStatus.ReadyWithWarnings => new("Checks", "Warnings", MorningTone.Warning, "Finalise is still allowed; Close day lists the warnings"),
        _ => new("Checks", "Not run", MorningTone.Neutral, "Run when the files and entries are in")
    };

    public static MorningCardLine SentBack(MorningStoreSource store, IReadOnlyList<MorningShopResult> results, TimeZoneInfo zone)
    {
        var names = new HashSet<string>(store.Imports.Select(entry => entry.Result.FileName).Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        var mine = results.Where(result => result.BusinessDate == store.Workflow?.BusinessDate || store.Workflow is null)
            .Where(result => result.FileNames.Any(names.Contains))
            .OrderByDescending(result => result.Sequence).ThenByDescending(result => result.FileTimeUtc)
            .FirstOrDefault();
        if (mine is null) return new("Sent back", "No result", MorningTone.Neutral, "No shop result file names this store's files");
        var time = Local(mine.FileTimeUtc.UtcDateTime, zone).ToString("HH:mm", English);
        if (!mine.Published) return new("Sent back", "Waiting to send", MorningTone.Warning, $"Result written at {time}; publish it to tell the shop");
        return mine.Overall switch
        {
            "Refused" => new("Sent back", "Refused", MorningTone.Critical, $"The shop was told what to do (result of {time})"),
            "AcceptedWithWarning" => new("Sent back", "Accepted with a warning", MorningTone.Warning, $"The shop was told (result of {time})"),
            "Duplicate" => new("Sent back", "Already had it", MorningTone.Neutral, $"The shop was told (result of {time})"),
            _ => new("Sent back", "Accepted", MorningTone.Success, $"The shop was told (result of {time})")
        };
    }

    // ------------------------------------------------------------------ chips

    public static MorningChip SystemCheckChip(MorningSources sources, DateOnly today)
    {
        const string label = "System check";
        if (sources.NightlyUnreadable) return new(label, "Not read", MorningTone.Neutral, "The nightly check could not be read. Refresh to try again.", HealthCheckTask);
        if (sources.Nightly is not { } nightly) return new(label, "Not run yet", MorningTone.Neutral, "The nightly check has not run yet.", HealthCheckTask);
        var counts = nightly.Counts;
        var total = counts.Pass + counts.Warning + counts.Fail + counts.Known + counts.Skipped;
        if (today.DayNumber - nightly.BusinessDate.DayNumber > NightlyStaleAfterDays)
            return new(label, "Old", MorningTone.Warning, $"Last run checked {nightly.BusinessDate.ToString("dd MMM", English)}; the nightly check has not run since.", HealthCheckTask);
        var finding = string.IsNullOrWhiteSpace(sources.NightlyTopFinding) ? string.Empty : " " + TrimSentence(sources.NightlyTopFinding!) + ".";
        var fails = Math.Max(counts.Fail, nightly.Overall == HealthOutcome.Fail ? 1 : 0);
        if (fails > 0) return new(label, fails == 1 ? "1 problem" : $"{fails} problems", MorningTone.Critical, "Last night's check found a problem." + finding, HealthCheckTask);
        var warnings = Math.Max(counts.Warning, nightly.Overall == HealthOutcome.Warning ? 1 : 0);
        if (warnings > 0) return new(label, warnings == 1 ? "1 warning" : $"{warnings} warnings", MorningTone.Warning, "Last night's check has something to look at." + finding, HealthCheckTask);
        return new(label, "OK", MorningTone.Success, $"Last night's check passed ({total} checks).", HealthCheckTask);
    }

    public static MorningChip ShopFilesChip(MorningSources sources, TimeZoneInfo zone)
    {
        const string label = "Shop files";
        if (sources.ImportsUnreadable) return new(label, "Not read", MorningTone.Neutral, "The import history could not be read. Refresh to try again.", ImportHistoryTask);
        var entries = sources.AllImports.DistinctBy(entry => entry.Key).ToArray();
        var refused = entries.Count(entry => entry.Result.Failed);
        var pending = sources.ShopResults.Count(result => !result.Published);
        var published = sources.ShopResults.Where(result => result.Published).Select(result => result.FileTimeUtc).DefaultIfEmpty().Max();
        var counts = entries.Length == 1 ? "1 file" : $"{entries.Length} files";
        if (refused > 0) counts += refused == 1 ? ", 1 refused" : $", {refused} refused";
        var sent = pending > 0 ? (pending == 1 ? " · 1 result waiting to be sent" : $" · {pending} results waiting to be sent")
            : published != default ? " · results sent back at " + Local(published.UtcDateTime, zone).ToString("HH:mm", English) : string.Empty;
        if (refused > 0) return new(label, refused == 1 ? "1 refused" : $"{refused} refused", MorningTone.Critical, counts + sent, ImportProblemsTask);
        if (entries.Length == 0) return new(label, "None yet", MorningTone.Warning, "No shop file has been imported for this day yet.", ImportHistoryTask);
        if (pending > 0) return new(label, "Results waiting", MorningTone.Warning, counts + sent, ImportHistoryTask);
        return new(label, "All in", MorningTone.Success, counts + sent, ImportHistoryTask);
    }

    public static MorningChip ServiceChip(IReadOnlyList<ServiceFreshnessChip>? freshness)
    {
        const string label = "Service";
        if (freshness is null) return new(label, "Not read", MorningTone.Neutral, "Service freshness could not be read.", ServiceTodayTask);
        if (freshness.Count == 0) return new(label, "No data", MorningTone.Neutral, "No Service export has been imported yet.", ServiceTodayTask);
        // One chip (design system D): the daily jobs export decides; the other groups are counted, not listed.
        var jobs = freshness.FirstOrDefault(chip => chip.Group == "Jobs") ?? freshness.OrderByDescending(chip => chip.Colour).First();
        var older = freshness.Count(chip => !ReferenceEquals(chip, jobs) && chip.Colour > jobs.Colour && chip.Colour >= ServiceFreshnessColour.Amber);
        var detail = $"{jobs.Group}: {jobs.Text}" + (older == 0 ? "." : older == 1 ? "; 1 other group is older." : $"; {older} other groups are older.");
        return jobs.Colour switch
        {
            ServiceFreshnessColour.Fresh => new(label, "Fresh", MorningTone.Success, detail, ServiceTodayTask),
            ServiceFreshnessColour.Amber => new(label, "Getting old", MorningTone.Warning, detail, ServiceTodayTask),
            ServiceFreshnessColour.Red => new(label, "Out of date", MorningTone.Critical, detail, ServiceTodayTask),
            _ => new(label, "No data", MorningTone.Neutral, detail, ServiceTodayTask)
        };
    }

    // ------------------------------------------------------------------ month start and footer

    public static MorningMonthStartCard? MonthStartCard(MorningSources sources, DateOnly today)
    {
        if (today.Day > MonthStartLastDay || sources.MonthStartMissing is not { Count: > 0 } missing) return null;
        var targets = missing.Where(item => item.Kind is DailyReadinessItemKind.MissingMonthlyTarget or DailyReadinessItemKind.MissingStaffTargets).ToArray();
        if (targets.Length == 0) return null;
        var codes = targets.Select(item => item.StoreCode).Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var names = codes.Select(code => sources.Stores.FirstOrDefault(store => store.StoreCode.Equals(code, StringComparison.OrdinalIgnoreCase))?.StoreName is { Length: > 0 } name ? name : code);
        var month = (sources.MonthStart ?? today).ToString("MMMM", English);
        return new($"{month} targets: not set for {string.Join(", ", names)}",
            "Last month's targets can be copied for every store in one go on Brands and targets. Staff targets stay Owner-only.",
            codes, TargetsTask);
    }

    public static IReadOnlyList<MorningCardLine> Footer(MorningSources sources, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        var lines = new List<MorningCardLine>();
        if (sources.OffPcBackupConfigured)
            lines.Add(sources.OffPcBackupAgeHours is { } offPc
                ? Age("Off-PC backup", offPc)
                : new("Off-PC backup", "No copy yet", MorningTone.Warning, "No backup copy is in OneDrive yet"));
        if (sources.DiskCopyAgeHours is { } disk) lines.Add(Age("Backup disk copy", disk));
        lines.Add(new("As at", Local(nowUtc.UtcDateTime, zone).ToString("dd MMM yyyy HH:mm", English), MorningTone.Neutral,
            $"Built from the last business day ({sources.BusinessDate.ToString("dd MMM yyyy", English)})"));
        return lines;
    }

    public static MorningCardLine Age(string label, double hours)
    {
        var text = AgeText(hours);
        return hours > CopyAgeLimitHours
            ? new(label, "Old", MorningTone.Warning, $"{text} old (limit {CopyAgeLimitHours:0} hours)")
            : new(label, "OK", MorningTone.Success, $"{text} old");
    }

    public static string AgeText(double hours) => hours switch
    {
        < 1 => "under an hour",
        < 1.5 => "1 hour",
        < 48 => $"{Math.Round(hours):0} hours",
        _ => $"{Math.Floor(hours / 24):0} days"
    };

    // ------------------------------------------------------------------ words

    /// <summary>The plain name of a required file; never the report code (no code in a sentence).</summary>
    public static string FileName(string? reportCode) => (reportCode ?? string.Empty).ToUpperInvariant() switch
    {
        "R025" => "Sales lines",
        "R022" => "Invoice tenders",
        "R011" or "CLOSING_STOCK" => "Closing stock",
        "R030" or "STOCK_LEDGER" => "Stock ledger",
        "R003" or "R013" => "Sales detail file",
        "D001" => "Daily sheet",
        var code when code.StartsWith('S') => "Service file",
        _ => "Shop file"
    };

    public static string InputName(string code) => code.ToUpperInvariant() switch
    {
        "WALK_INS" => "Walk-ins",
        "OPENING_CASH" => "Opening cash",
        "EXPENSES" => "Expenses",
        "CASH_DEPOSIT" => "Cash deposit",
        _ => Capitalise(code.Replace('_', ' ').ToLowerInvariant())
    };

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    private static string TrimSentence(string text) => text.Trim().TrimEnd('.', ' ');

    private static DateTime Local(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
}
