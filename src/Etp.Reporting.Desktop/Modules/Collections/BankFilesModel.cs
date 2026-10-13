extern alias EtpApplication;
using System.IO;

using EtpApplication::Etp.Reporting.Application.Collections;

namespace Etp.Reporting.Desktop.Modules.Collections;

public enum BankFileState
{
    /// <summary>The file is being matched to the Owner's layouts.</summary>
    Checking,
    /// <summary>One layout matches (or the user chose the account): Import is allowed.</summary>
    Ready,
    /// <summary>Several layouts match: "Which account is this statement for?" - Import stays off until one is chosen.</summary>
    ChooseAccount,
    /// <summary>No layout matches this file's header, or the file could not be read.</summary>
    NoLayout,
    Importing,
    Imported,
    AlreadyImported,
    DuplicateContent,
    /// <summary>Lines look like ones already imported: import them as new, or let this file replace the earlier one, with a reason.</summary>
    PossibleDuplicates,
    Failed
}

/// <summary>One file in the Bank and settlement files list: what it matched, what the user chose and the sentence the last step gave.</summary>
public sealed class BankFileItem(string filePath)
{
    public string FilePath { get; } = filePath;

    /// <summary>The file name with any run of five or more digits cut to its last four (an account number must never show in full).</summary>
    public string DisplayName { get; } = CollectionsScreens.MaskDigits(Path.GetFileName(filePath));

    public CollectionsFilePreview? Preview { get; internal set; }
    public IReadOnlyList<MatchedLayout> Layouts => Preview?.Layouts ?? [];
    public int? SelectedLayoutId { get; internal set; }
    public BankFileState State { get; internal set; } = BankFileState.Checking;
    public string Message { get; internal set; } = "Checking the file...";
    public CollectionsImportResult? Result { get; internal set; }

    /// <summary>More than one layout matches the file's header: the account choice is mandatory.</summary>
    public bool NeedsAccountChoice => Layouts.Count > 1;

    public MatchedLayout? SelectedLayout => Layouts.FirstOrDefault(layout => layout.LayoutId == SelectedLayoutId);

    public StatusTone Tone => State switch
    {
        BankFileState.Imported => StatusTone.Success,
        BankFileState.Ready => StatusTone.Information,
        BankFileState.ChooseAccount or BankFileState.PossibleDuplicates or BankFileState.AlreadyImported or BankFileState.DuplicateContent => StatusTone.Warning,
        BankFileState.NoLayout or BankFileState.Failed => StatusTone.Critical,
        _ => StatusTone.Neutral
    };

    /// <summary>The status chip's word (ST1: every state has a word).</summary>
    public string StatusWord => State switch
    {
        BankFileState.Checking => "Checking",
        BankFileState.Ready => "Ready to import",
        BankFileState.ChooseAccount => "Choose the account",
        BankFileState.NoLayout => "No layout",
        BankFileState.Importing => "Importing",
        BankFileState.Imported => "Imported",
        BankFileState.AlreadyImported => "Already imported",
        BankFileState.DuplicateContent => "Nothing new",
        BankFileState.PossibleDuplicates => "Possible duplicates",
        _ => "Not imported"
    };

    /// <summary>What a matched layout is called in the account choice: "Main account (HDFC statement)".</summary>
    public static string LayoutLabel(MatchedLayout layout) =>
        $"{CollectionsScreens.MaskDigits(layout.AccountOrProviderName)} ({CollectionsScreens.MaskDigits(layout.DisplayName)})";
}

/// <summary>
/// Import > Bank and settlement files (Import template; plan U4). Pure presentation logic over
/// <see cref="ICollectionsImportService"/>: choose files, see which of the Owner's layouts each one matches, choose the
/// account when several match, import, and settle "possible duplicate lines" with a reason. The sentence of each file is the
/// service's <see cref="CollectionsImportResult.Message"/>, shown as it is. Owner and Store Manager only; the service
/// checks the role again.
/// </summary>
public sealed class BankFilesModel
{
    public const string ChooseAccountPrompt = "Which account is this statement for?";
    public const string NothingReadyText = "No file is ready to import.";
    private readonly Func<ICollectionsImportService> service;
    private readonly List<BankFileItem> items = [];

    public BankFilesModel(Func<ICollectionsImportService> service, ShellAccess access)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        Access = access ?? throw new ArgumentNullException(nameof(access));
    }

    public ShellAccess Access { get; }
    public bool IsAllowed => CollectionsScreens.CanImportFiles(Access);
    public string? RefusalText => IsAllowed ? null : Access.HasAssignedRole ? CollectionsScreens.BankFilesRefusedText : CollectionsScreens.NotSignedInText;
    public IReadOnlyList<BankFileItem> Items => items;
    public event Action? Changed;

    /// <summary>The one line under the toolbar: "2 files ready, 1 needs an account choice."</summary>
    public string Summary
    {
        get
        {
            if (items.Count == 0) return "Choose the bank statement or settlement files to import.";
            var parts = new List<string>();
            var ready = items.Count(item => item.State == BankFileState.Ready);
            var choose = items.Count(item => item.State == BankFileState.ChooseAccount);
            var done = items.Count(item => item.State == BankFileState.Imported);
            var stuck = items.Count(item => item.State is BankFileState.NoLayout or BankFileState.Failed or BankFileState.PossibleDuplicates);
            if (ready > 0) parts.Add($"{Files(ready)} ready");
            if (choose > 0) parts.Add(choose == 1 ? "1 file needs an account choice" : $"{choose} files need an account choice");
            if (done > 0) parts.Add($"{Files(done)} imported");
            if (stuck > 0) parts.Add(stuck == 1 ? "1 file needs a look" : $"{stuck} files need a look");
            return parts.Count == 0 ? $"{Files(items.Count)} checked." : string.Join(", ", parts) + ".";
        }
    }

    /// <summary>The primary button's state: at least one file is ready and nothing is importing.</summary>
    public bool CanImportReady => IsAllowed && items.Any(item => item.State == BankFileState.Ready) && items.All(item => item.State != BankFileState.Importing);

    /// <summary>Why Import is off, for the disabled button's tooltip (B6).</summary>
    public string ImportDisabledReason =>
        !IsAllowed ? RefusalText! :
        items.Any(item => item.State == BankFileState.ChooseAccount) ? ChooseAccountPrompt :
        items.Count == 0 ? "Choose a file first." : NothingReadyText;

    public async Task AddFilesAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!IsAllowed) return;
        var fresh = new List<BankFileItem>();
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (items.Any(item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase))) continue;
            var item = new BankFileItem(path);
            items.Add(item); fresh.Add(item);
        }
        Changed?.Invoke();
        foreach (var item in fresh)
        {
            try
            {
                var preview = await service().PreviewAsync(item.FilePath, cancellationToken).ConfigureAwait(true);
                item.Preview = preview;
                if (preview.Layouts.Count == 0)
                {
                    item.State = BankFileState.NoLayout;
                    item.Message = preview.Problem ?? "This file does not match any file layout. Ask the Owner to add one in Settings > Collections setup.";
                }
                else if (preview.Layouts.Count == 1)
                {
                    item.SelectedLayoutId = preview.Layouts[0].LayoutId;
                    item.State = BankFileState.Ready;
                    item.Message = $"Matches {BankFileItem.LayoutLabel(preview.Layouts[0])}.";
                }
                else
                {
                    item.State = BankFileState.ChooseAccount;
                    item.Message = ChooseAccountPrompt;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                item.State = BankFileState.Failed;
                item.Message = CollectionsScreens.Sentence(exception);
            }
            Changed?.Invoke();
        }
    }

    /// <summary>The user chose which account a file with several matching layouts is for.</summary>
    public bool Choose(BankFileItem item, int layoutId)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.State is not (BankFileState.ChooseAccount or BankFileState.Ready) || item.Layouts.All(layout => layout.LayoutId != layoutId)) return false;
        item.SelectedLayoutId = layoutId;
        item.State = BankFileState.Ready;
        item.Message = $"Will import into {BankFileItem.LayoutLabel(item.SelectedLayout!)}.";
        Changed?.Invoke();
        return true;
    }

    public bool CanImport(BankFileItem item) =>
        IsAllowed && item.State == BankFileState.Ready && item.SelectedLayout is not null;

    /// <summary>Imports every ready file, one at a time, and returns how many were imported.</summary>
    public async Task<int> ImportReadyAsync(CancellationToken cancellationToken = default)
    {
        var imported = 0;
        foreach (var item in items.Where(CanImport).ToArray())
            if (await ImportCoreAsync(item, CollectionsOverlapChoice.Stop, null, cancellationToken).ConfigureAwait(true)) imported++;
        return imported;
    }

    public Task<bool> ImportAsync(BankFileItem item, CancellationToken cancellationToken = default) =>
        ImportCoreAsync(item, CollectionsOverlapChoice.Stop, null, cancellationToken);

    /// <summary>"Import as new lines" or "Replace the earlier file", each with a reason.</summary>
    public bool CanResolve(BankFileItem item, CollectionsOverlapChoice choice, string? reason) =>
        IsAllowed && item.State == BankFileState.PossibleDuplicates && choice != CollectionsOverlapChoice.Stop
        && !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500;

    public Task<bool> ResolveAsync(BankFileItem item, CollectionsOverlapChoice choice, string? reason, CancellationToken cancellationToken = default) =>
        CanResolve(item, choice, reason) ? ImportCoreAsync(item, choice, reason!.Trim(), cancellationToken) : Task.FromResult(false);

    public void Remove(BankFileItem item)
    {
        if (item.State == BankFileState.Importing) return;
        if (items.Remove(item)) Changed?.Invoke();
    }

    public void Clear()
    {
        items.RemoveAll(item => item.State != BankFileState.Importing);
        Changed?.Invoke();
    }

    private async Task<bool> ImportCoreAsync(BankFileItem item, CollectionsOverlapChoice choice, string? reason, CancellationToken cancellationToken)
    {
        var allowed = choice == CollectionsOverlapChoice.Stop ? CanImport(item) : IsAllowed && item.State == BankFileState.PossibleDuplicates;
        if (!allowed || item.SelectedLayoutId is not { } layoutId) return false;
        item.State = BankFileState.Importing;
        item.Message = "Importing...";
        Changed?.Invoke();
        try
        {
            var result = await service().ImportAsync(new CollectionsImportRequest(item.FilePath, layoutId, choice, reason), cancellationToken).ConfigureAwait(true);
            item.Result = result;
            item.Message = result.Message;
            item.State = result.Outcome switch
            {
                CollectionsImportOutcome.Imported => BankFileState.Imported,
                CollectionsImportOutcome.AlreadyImported => BankFileState.AlreadyImported,
                CollectionsImportOutcome.DuplicateContent => BankFileState.DuplicateContent,
                _ => BankFileState.PossibleDuplicates
            };
            Changed?.Invoke();
            return result.Outcome == CollectionsImportOutcome.Imported;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            // The file stays in the list: the user fixes the cause (a closed Excel file, a missing layout) and presses Import again.
            item.State = choice == CollectionsOverlapChoice.Stop ? BankFileState.Ready : BankFileState.PossibleDuplicates;
            item.Message = CollectionsScreens.Sentence(exception);
            Changed?.Invoke();
            return false;
        }
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";
}
