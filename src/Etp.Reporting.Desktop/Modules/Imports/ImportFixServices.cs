extern alias EtpApplication;

using System.Diagnostics;
using Etp.Reporting.Infrastructure.SqlServer.ImportSourceLocations;
using Etp.Reporting.Infrastructure.SqlServer.ShopResults;
using FolderImportFileResult = EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult;
using ImportFixAction = EtpApplication::Etp.Reporting.Application.Imports.ImportFixAction;
using ImportFixAdvisor = EtpApplication::Etp.Reporting.Application.Imports.ImportFixAdvisor;
using ImportFixContext = EtpApplication::Etp.Reporting.Application.Imports.ImportFixContext;
using ImportFixKind = EtpApplication::Etp.Reporting.Application.Imports.ImportFixKind;

namespace Etp.Reporting.Desktop.Modules.Imports;

/// <summary>
/// The services the import Fix buttons use (1.12 U2, wave plan C-c). The composition root sets <see cref="Current"/> in
/// <c>DesktopCompositionRoot.ImportFixes.cs</c> (the same pattern as the diagnostics screens), so Problems and History, which the
/// shell builds, reach them without a connection string. Null (tests, a window that was never composed): no Fix buttons.
/// </summary>
public sealed class ImportFixServices
{
    public static ImportFixServices? Current { get; set; }

    public required Func<IImportFixService> Fixes { get; init; }
    public required Func<IImportSourceLocationStore> Locations { get; init; }
    public Func<ShopResultWriter> ShopResults { get; init; } = static () => new ShopResultWriter();

    /// <summary>Opens Explorer at the file (<c>explorer.exe /select,</c>). A test replaces it.</summary>
    public Action<string> OpenFolder { get; init; } = static path =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();

    /// <summary>The signed-in role read from the database (History has no host access of its own). Default: no rights.</summary>
    public Func<CancellationToken, Task<ImportWorkspaceAccess>> LoadAccess { get; init; } =
        static _ => Task.FromResult(new ImportWorkspaceAccess(false, false));

    /// <summary>Opens the Error register at a Ref (Owner); null hides the button (set by the shell when it can navigate with an argument).</summary>
    public Action<string>? OpenErrorRegister { get; init; }

    /// <summary>The most rows a Problems or History load looks up a location for (one query each).</summary>
    public const int MaximumLookups = 60;
}

/// <summary>One Fix button of one row, ready for a grid cell: label, tooltip, automation name and what it does.</summary>
public sealed class ImportFixButtonModel(ImportFixAction action, string fileName, Func<Task> run)
{
    public ImportFixKind Kind { get; } = action.Kind;
    public string Label { get; } = action.Label;
    public string Explanation { get; } = action.Explanation;
    /// <summary>B5: the visible text is ambiguous in a grid of many rows, so the name says which file.</summary>
    public string AutomationName { get; } = $"{action.Label}: {fileName}";
    public Func<Task> Run { get; } = run;
}

/// <summary>Turns one problem row into its Fix buttons: looks the file up (0069), asks the advisor, and binds each button to its work.</summary>
public static class ImportFixRows
{
    /// <summary>Whether a file came from a shop: a daily-inputs sheet, or a file whose routed source is a shop zip.</summary>
    public static bool IsFromShop(FolderImportFileResult result, ImportFixLocation? location) =>
        string.Equals(result.ReportCode, "D001", StringComparison.OrdinalIgnoreCase)
        || (result.Failure?.Code ?? "").StartsWith("DAILY_INPUTS_", StringComparison.Ordinal)
        || location is not null && SqlServerImportFixService.TryParseRoutedShopZip(location.Location.RoutedPath, out _, out _);

    public static ImportFixContext ContextFor(ImportFixLocation? location, ImportWorkspaceAccess access, FolderImportFileResult result) =>
        new(access.CanImport, access.CanAdminister, location is { Exists: true }, location is { Exists: true, UnderConfiguredRoot: true },
            location?.Location.AttemptRef, IsFromShop(result, location));

    public static ImportFixTarget TargetFor(FolderImportFileResult result) =>
        new(System.IO.Path.GetFileName(result.FileName), result.SourceSha256, result.StoreCode, result.PeriodEnd);

    /// <summary>The Fix buttons of a refused or problem row; none for a clean row or without services.</summary>
    public static async Task<IReadOnlyList<ImportFixButtonModel>> ForAsync(FolderImportFileResult result, ImportWorkspaceAccess access,
        ImportFixServices? services, ImportFixHandlers handlers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(handlers);
        if (services is null || !ImportProblems.IsProblem(result)) return [];
        ImportFixLocation? location = null;
        // A Viewer is denied the location table; it gets the buttons that need no location (none today).
        if (access.CanImport)
            location = await services.Fixes().LocateAsync(TargetFor(result), cancellationToken).ConfigureAwait(true);
        var code = result.Failure?.Code;
        var actions = ImportFixAdvisor.For(code, ContextFor(location, access, result), result.Failure?.SqlNumber);
        // A duplicate, a conflict or an unknown layout was not refused for a reason a re-import removes: only its folder.
        var refused = result.Failed || result.Failure is not null;
        return actions
            .Where(action => refused || action.Kind is ImportFixKind.OpenFolder)
            .Where(action => action.Kind != ImportFixKind.OpenErrorRegister || services.OpenErrorRegister is not null)
            .Select(action => new ImportFixButtonModel(action, result.FileName, () => handlers.RunAsync(action.Kind, result, location, services)))
            .ToArray();
    }
}
