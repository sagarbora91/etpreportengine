using System.IO;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Modules.Imports;

/// <summary>One sheet the "Get blank daily sheet" button can write: a store and a kind (decision 31: Service money sits under the service-money shop).</summary>
public sealed record BlankDailySheetOption(string Label, string StoreCode, string StoreLabel, DailyInputsKind Kind);

/// <summary>What the person chose: the sheet and the business date (file name and the sheet's Business date cell C5).</summary>
public sealed record BlankDailySheetRequest(BlankDailySheetOption Option, DateOnly BusinessDate);

/// <summary>
/// 1.11.1: gives staff the blank daily-inputs workbook (family D001) from the app. It only calls the existing
/// <see cref="DailyInputsTemplateWriter"/>; nothing is written to the database. The sheets come from the store
/// list (dbo.stores, as every store picker reads it), so no store code is fixed in code.
/// </summary>
public static class BlankDailySheet
{
    /// <summary>
    /// One retail sheet per active shop store (never a Service Centre store), in the catalogue's order, then the
    /// Service money sheet under the store flagged <c>is_service_money_shop</c> (0052, decision 31). No flagged
    /// active store: no Service sheet.
    /// </summary>
    public static IReadOnlyList<BlankDailySheetOption> OptionsFor(IEnumerable<StoreCatalogEntry> stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        var shops = stores.Where(store => store.IsActive && !store.IsServiceCentre).ToArray();
        var options = shops
            .Select(store => new BlankDailySheetOption($"{StoreScopeCatalog.Label(store)} - retail sheet", store.Code, store.Name, DailyInputsKind.Retail))
            .ToList();
        if (shops.FirstOrDefault(store => store.IsServiceMoneyShop) is { } money)
            options.Add(new($"Service Centre - service money, counted under {StoreScopeCatalog.Label(money)}", money.Code,
                $"{money.Name} (Service money)", DailyInputsKind.Service));
        return options;
    }

    /// <summary>The same window the sheet's own date rule accepts: today and up to 31 days back.</summary>
    public static bool IsDateAllowed(DateOnly date, DateOnly today) =>
        date <= today && date >= today.AddDays(-DailyInputsTemplate.LookbackDays);

    /// <summary><c>ETP-Daily-Inputs-&lt;STORE&gt;-&lt;KIND&gt;-&lt;yyyy-MM-dd&gt;.xlsx</c>, the name the importer expects.</summary>
    public static string SuggestedFileName(BlankDailySheetRequest request) =>
        DailyInputsTemplate.FileName(request.Option.StoreCode, request.Option.Kind, request.BusinessDate);

    /// <summary>Writes the blank workbook to <paramref name="path"/>; a half-written file is removed when the write fails.</summary>
    public static void Save(string path, BlankDailySheetRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var file = File.Create(path);
            DailyInputsTemplateWriter.Write(file, request.Option.Kind, request.Option.StoreCode, request.Option.StoreLabel, request.BusinessDate);
        }
        catch
        {
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
