namespace Etp.Reporting.Application.Imports;

/// <summary>What a Fix button on Imports > Problems or History does (1.12 U2, wave plan C-c).</summary>
public enum ImportFixKind
{
    /// <summary>The Owner reopens the finalised day with a reason, then ETP imports the routed file again.</summary>
    ReopenDayAndReapply,
    /// <summary>ETP imports the routed file again, as it is (a timeout, a database error, a permission or store since fixed).</summary>
    Retry,
    /// <summary>Copies the shop's sentence (what happened, what to do) for the person who talks to the shop.</summary>
    TellShop,
    /// <summary>Explorer opens the folder ETP moved the file to, with the file selected (only under the configured roots).</summary>
    OpenFolder,
    /// <summary>Copies the 12-character diagnostics Ref.</summary>
    CopyRef,
    /// <summary>The Owner opens the Error register at that Ref.</summary>
    OpenErrorRegister
}

/// <summary>One Fix button: its kind, its label (design system B3 verbs) and the sentence its tooltip and automation help say.</summary>
public sealed record ImportFixAction(ImportFixKind Kind, string Label, string Explanation);

/// <summary>
/// What the advisor needs to know about one problem row and the person looking at it.
/// </summary>
/// <param name="CanImport">Owner or Store Manager (a Viewer gets no action that changes or reveals a path).</param>
/// <param name="IsOwner">The Owner: reopening a day and the Error register are Owner-only.</param>
/// <param name="HasRoutedFile">ETP knows where the file went (0069 <c>import_source_locations</c>) and the file is still there.</param>
/// <param name="RoutedFileUnderConfiguredRoot">That place is inside the watch folders or the hand-import folder ETP was given.</param>
/// <param name="Ref">The diagnostics Ref of the failure, when it wrote one.</param>
/// <param name="FromShop">The file came from a shop (a shop zip or a daily-inputs sheet).</param>
public sealed record ImportFixContext(bool CanImport, bool IsOwner, bool HasRoutedFile, bool RoutedFileUnderConfiguredRoot,
    string? Ref = null, bool FromShop = false);

/// <summary>
/// Pure rules: which Fix buttons a problem row shows. L7 left only a hint ("Reopen the day, then press Retry failed"),
/// because the row did not know where the file went; 0069 records that, so the hint becomes buttons. The order is the
/// order on screen: the one that fixes the row first.
/// </summary>
public static class ImportFixAdvisor
{
    public const string ReopenLabel = "Reopen day and re-apply";
    public const string RetryLabel = "Retry";
    public const string TellShopLabel = "Tell the shop";
    public const string OpenFolderLabel = "Open folder";
    public const string CopyRefLabel = "Copy Ref";
    public const string ErrorRegisterLabel = "Open in Error register";

    /// <summary>The SQL numbers of the day-lock triggers that refuse a change to a finalised day (0021 and later).</summary>
    private static readonly HashSet<int> DayLockNumbers = [51020, 51021, 51030, 51031, 51032, 51033, 51034, 51035, 51036, 51038, 51042, 51043];

    /// <summary>Codes whose cause can be gone by the next try: nothing in the file has to change.</summary>
    private static readonly HashSet<string> RetryableCodes = new(StringComparer.Ordinal)
    {
        ImportCodes.ImportTimeout, ImportCodes.CommitOutcomeUnknown, ImportCodes.ImportUpgradePending,
        "IMPORT_NOT_COMPLETED", "DAILY_INPUTS_NO_PERMISSION", "DAILY_INPUTS_UNKNOWN_STORE", "DAILY_INPUTS_FIELD_INACTIVE",
        "DAILY_INPUTS_NOT_AVAILABLE"
    };

    /// <summary>A refusal because the business day is finalised (the daily-inputs code or a day-lock trigger).</summary>
    public static bool IsDayFinalised(string? code, int? sqlNumber) =>
        code == "DAILY_INPUTS_DAY_FINALISED" || sqlNumber is { } number && DayLockNumbers.Contains(number);

    /// <summary>
    /// A refusal that the same bytes can pass next time: a timeout, an unknown commit, a database error that is not one of
    /// ETP's own refusals, a permission or a store fixed since, or a failure with no code at all.
    /// </summary>
    public static bool IsRetryable(string? code, int? sqlNumber)
    {
        if (IsDayFinalised(code, sqlNumber)) return false;
        if (string.IsNullOrWhiteSpace(code)) return true;
        if (RetryableCodes.Contains(code)) return true;
        return sqlNumber is { } number && number is not (>= 50000 and <= 59999);
    }

    /// <summary>The Fix buttons for a refused row (<paramref name="code"/> is its failure code, <paramref name="sqlNumber"/> its SQL number).</summary>
    public static IReadOnlyList<ImportFixAction> For(string? code, ImportFixContext context, int? sqlNumber = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var actions = new List<ImportFixAction>();
        if (context.CanImport && context.HasRoutedFile)
        {
            if (IsDayFinalised(code, sqlNumber))
            {
                if (context.IsOwner)
                    actions.Add(new(ImportFixKind.ReopenDayAndReapply, ReopenLabel,
                        "Reopen the finalised day with a reason, then import this file again from where ETP keeps it."));
            }
            else if (IsRetryable(code, sqlNumber))
                actions.Add(new(ImportFixKind.Retry, RetryLabel, "Import this file again from where ETP keeps it. Nothing in the file changes."));
        }
        if (context.CanImport && context.FromShop)
            actions.Add(new(ImportFixKind.TellShop, TellShopLabel, "Copy the sentence the shop sees for this file: " + ImportDiagnosticCatalogue.ShopSentence(code)));
        if (context.CanImport && context.HasRoutedFile && context.RoutedFileUnderConfiguredRoot)
            actions.Add(new(ImportFixKind.OpenFolder, OpenFolderLabel, "Open the folder ETP moved this file to."));
        if (!string.IsNullOrWhiteSpace(context.Ref))
        {
            actions.Add(new(ImportFixKind.CopyRef, CopyRefLabel, "Copy the reference of this failure."));
            if (context.IsOwner)
                actions.Add(new(ImportFixKind.OpenErrorRegister, ErrorRegisterLabel, "Open the Error register at this reference."));
        }
        return actions;
    }

    /// <summary>
    /// The one line a row shows when no button can fix it, so the row still says what to do (L7's hint, kept for the
    /// cases the buttons do not cover). Null when a button covers it.
    /// </summary>
    public static string? HintWithoutButton(string? code, ImportFixContext context, int? sqlNumber = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsDayFinalised(code, sqlNumber))
        {
            if (!context.IsOwner) return "The day is finalised. Ask the Owner to reopen it and re-apply this file.";
            if (!context.HasRoutedFile) return "Reopen the day (Today > Close day), then import this file again.";
            return null;
        }
        if (IsRetryable(code, sqlNumber) && context.CanImport && !context.HasRoutedFile)
            return "Import this file again. ETP does not know where this copy went.";
        return null;
    }
}
