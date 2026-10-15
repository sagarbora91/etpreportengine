using System.Globalization;
using System.Text.Json;

namespace Etp.Reporting.Application.DailyReadiness;

/// <summary>
/// One shop PC (TITAN, HELIOS or SERVICE) as the shop-live file lists it: its role, the store codes it sends for, and the
/// first business day it sends (null = not live yet).
/// </summary>
public sealed record ShopLiveEntry(string Role, IReadOnlyList<string> StoreCodes, DateOnly? SendingFrom);

/// <summary>
/// 1.12.1 lane P7 (decision 51): which shops are LIVE. While only the Titan PC sends, Helios and the Service Centre are not
/// expected, so Today, DATA-01 and Morning (all through <see cref="DailyReadinessEvaluator"/>) and the Workpc receive script
/// (same file, <c>scripts/onedrive/OneDriveCommon.ps1</c>) never call them missing.
/// <para>The file is <c>shop-live.json</c>: <c>{ "shops": [ { "role": "TITAN", "storeCodes": ["STORE1"], "sendingFrom": "2026-10-20" } ] }</c>.
/// A shop without <c>sendingFrom</c> is not live yet. A store the file does not list is expected, and so is every store when
/// there is no file or it cannot be read: the rule only ever waives what it names, so a broken file asks for more, never less.</para>
/// </summary>
public sealed class ShopLiveSchedule
{
    public static ShopLiveSchedule Empty { get; } = new([]);

    public ShopLiveSchedule(IReadOnlyList<ShopLiveEntry> entries) =>
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));

    public IReadOnlyList<ShopLiveEntry> Entries { get; }

    /// <summary>True when files for the store are expected on the business date (always true for a store the file does not list).</summary>
    public bool IsSending(string storeCode, DateOnly businessDate)
    {
        if (string.IsNullOrWhiteSpace(storeCode)) return true;
        var code = storeCode.Trim();
        var entry = Entries.FirstOrDefault(e => e.StoreCodes.Contains(code, StringComparer.OrdinalIgnoreCase));
        return entry is null || (entry.SendingFrom is { } start && businessDate >= start);
    }

    /// <summary>The first day the store is expected, or null when it is not live yet or not listed.</summary>
    public DateOnly? SendingFrom(string storeCode)
    {
        if (string.IsNullOrWhiteSpace(storeCode)) return null;
        var code = storeCode.Trim();
        return Entries.FirstOrDefault(e => e.StoreCodes.Contains(code, StringComparer.OrdinalIgnoreCase))?.SendingFrom;
    }

    /// <summary>Parses the file text. False (and <paramref name="schedule"/> = <see cref="Empty"/>) when it is not valid.</summary>
    public static bool TryParse(string? json, out ShopLiveSchedule schedule, out string? error)
    {
        schedule = Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "The shop-live file is empty."; return false; }
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object) { error = "The shop-live file must be a JSON object."; return false; }
            var entries = new List<ShopLiveEntry>();
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (document.RootElement.TryGetProperty("shops", out var shops))
            {
                if (shops.ValueKind != JsonValueKind.Array) { error = "'shops' must be a list."; return false; }
                foreach (var shop in shops.EnumerateArray())
                {
                    var role = shop.ValueKind == JsonValueKind.Object && shop.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String
                        ? r.GetString()!.Trim().ToUpperInvariant() : "";
                    if (role is not ("TITAN" or "HELIOS" or "SERVICE")) { error = $"Unknown role '{role}' (use TITAN, HELIOS or SERVICE)."; return false; }
                    if (!roles.Add(role)) { error = $"Role {role} is listed twice."; return false; }

                    var codes = new List<string>();
                    if (shop.TryGetProperty("storeCodes", out var list) && list.ValueKind == JsonValueKind.Array)
                        foreach (var item in list.EnumerateArray())
                        {
                            var code = item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null;
                            if (string.IsNullOrEmpty(code)) { error = $"A store code of {role} is empty."; return false; }
                            if (!stores.Add(code)) { error = $"Store {code} is listed twice."; return false; }
                            codes.Add(code);
                        }

                    DateOnly? firstDay = null;
                    if (shop.TryGetProperty("sendingFrom", out var day) && day.ValueKind != JsonValueKind.Null)
                    {
                        if (day.ValueKind != JsonValueKind.String)
                        { error = $"sendingFrom for {role} must be a date text (yyyy-MM-dd) or null."; return false; }
                        var text = day.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                            { error = $"sendingFrom '{text}' for {role} is not a date (use yyyy-MM-dd)."; return false; }
                            firstDay = parsed;
                        }
                    }
                    entries.Add(new ShopLiveEntry(role, codes, firstDay));
                }
            }
            schedule = new ShopLiveSchedule(entries);
            return true;
        }
        catch (JsonException exception)
        {
            error = "The shop-live file is not valid JSON: " + exception.Message;
            return false;
        }
    }
}
