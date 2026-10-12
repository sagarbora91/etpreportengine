using System.IO;
using Etp.Reporting.Import.DailyInputs;

namespace Etp.Reporting.Desktop.Modules.Imports;

/// <summary>One sheet the "Get blank daily sheet" button can write: a store and a kind (decision 31: Service money sits under Titan World).</summary>
public sealed record BlankDailySheetOption(string Label, string StoreCode, string StoreLabel, DailyInputsKind Kind);

/// <summary>What the person chose: the sheet and the business date used in the file name.</summary>
public sealed record BlankDailySheetRequest(BlankDailySheetOption Option, DateOnly BusinessDate);

/// <summary>
/// 1.11.1: gives staff the blank daily-inputs workbook (family D001) from the app. It only calls the existing
/// <see cref="DailyInputsTemplateWriter"/>; nothing is read from or written to the database.
/// </summary>
public static class BlankDailySheet
{
    public static IReadOnlyList<BlankDailySheetOption> Options { get; } =
    [
        new("Titan World (WLMHW) - retail sheet", "WLMHW", "Titan World", DailyInputsKind.Retail),
        new("Helios (HEMW) - retail sheet", "HEMW", "Helios", DailyInputsKind.Retail),
        new("Service Centre - service money, counted under Titan World (WLMHW)", "WLMHW", "Titan World (Service money)", DailyInputsKind.Service)
    ];

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
            DailyInputsTemplateWriter.Write(file, request.Option.Kind, request.Option.StoreCode, request.Option.StoreLabel);
        }
        catch
        {
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
