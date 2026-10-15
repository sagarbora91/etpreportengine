extern alias EtpApplication;

using System.Globalization;
using EtpApplication::Etp.Reporting.Application.Collections;

namespace Etp.Reporting.Desktop.Modules.Collections;

/// <summary>The bank account form. Only the last four digits of an account number exist anywhere in ETP.</summary>
public sealed class BankAccountEdit
{
    public int? Id { get; set; }
    public string AccountKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public bool TakesCashDeposits { get; set; }
    public bool Active { get; set; } = true;
    public string Reason { get; set; } = string.Empty;

    public BankAccountDraft ToDraft() => new(Id, AccountKey, DisplayName, BankName, Last4, StoreCode, TakesCashDeposits, Active);

    public static BankAccountEdit From(BankAccountRow row) => new()
    {
        Id = row.Id, AccountKey = row.AccountKey, DisplayName = row.DisplayName, BankName = row.BankName, Last4 = row.AccountLast4 ?? string.Empty,
        StoreCode = row.StoreCode ?? string.Empty, TakesCashDeposits = row.TakesCashDeposits, Active = row.Active
    };
}

public sealed class ProviderEdit
{
    public bool IsNew { get; set; } = true;
    public string ProviderKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Mode { get; set; } = "UPI";
    public string SettlesVia { get; set; } = "BANK";
    public string StatementKind { get; set; } = "UNKNOWN";
    public string Granularity { get; set; } = "UNKNOWN";
    public int? BankAccountId { get; set; }
    public string RoundingTolerance { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public string Reason { get; set; } = string.Empty;

    public static ProviderEdit From(CollectionProviderRow row) => new()
    {
        IsNew = false, ProviderKey = row.ProviderKey, DisplayName = row.DisplayName, Mode = row.Mode, SettlesVia = row.SettlesVia,
        StatementKind = row.StatementKind, Granularity = row.Granularity, BankAccountId = row.BankAccountId,
        RoundingTolerance = row.RoundingTolerance?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty, Active = row.Active
    };
}

public sealed class MerchantIdEdit
{
    public int? Id { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string TerminalId { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public string Reason { get; set; } = string.Empty;

    public static MerchantIdEdit From(MerchantIdRow row) => new()
    {
        Id = row.Id, ProviderKey = row.ProviderKey, MerchantId = row.MerchantId, TerminalId = row.TerminalId ?? string.Empty, StoreCode = row.StoreCode, Active = row.Active
    };
}

/// <summary>1.12.1 P2: the settlement rule form (D20). Numbers are text so the model can say what is wrong with them; the dates
/// are picked. A rule with no store applies to every store; with no provider, to the whole payment mode.</summary>
public sealed class PolicyEdit
{
    public int? Id { get; set; }
    public string Mode { get; set; } = "Cash";
    public string StoreCode { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string SettlementWindowDays { get; set; } = string.Empty;
    public string OverdueAfterDays { get; set; } = string.Empty;
    public string CashTolerance { get; set; } = string.Empty;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Reason { get; set; } = string.Empty;

    public static PolicyEdit From(CollectionPolicyRow row) => new()
    {
        Id = row.Id, Mode = row.Mode, StoreCode = row.StoreCode ?? string.Empty, ProviderKey = row.ProviderKey ?? string.Empty,
        SettlementWindowDays = row.SettlementWindowDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        OverdueAfterDays = row.OverdueAfterDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        CashTolerance = row.CashTolerance?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
        EffectiveFrom = row.EffectiveFrom, EffectiveTo = row.EffectiveTo
    };

    /// <summary>The draft, or the first thing wrong with the numbers or dates as a plain sentence.</summary>
    public CollectionPolicyDraft ToDraft()
    {
        var window = Days(SettlementWindowDays, "The settlement window");
        var overdue = Days(OverdueAfterDays, "Overdue after");
        decimal? tolerance = null;
        if (!string.IsNullOrWhiteSpace(CashTolerance))
        {
            if (!decimal.TryParse(CashTolerance.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
                throw new ArgumentException("Enter the cash tolerance as rupees, for example 10 or 0.50.");
            tolerance = parsed;
        }
        if (EffectiveFrom is not { } from) throw new ArgumentException("Choose the date the rule starts.");
        return new(Id, string.IsNullOrWhiteSpace(StoreCode) ? null : StoreCode, Mode, string.IsNullOrWhiteSpace(ProviderKey) ? null : ProviderKey,
            window, overdue, tolerance, from, EffectiveTo);
    }

    private static int? Days(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            ? days : throw new ArgumentException($"{label} must be a whole number of days.");
    }
}

/// <summary>The file layout form. The header signature is read from a sample file, never typed (see <see cref="CollectionsSetupModel.ReadSampleAsync"/>).</summary>
public sealed class LayoutEdit
{
    private string sheetName = string.Empty;
    private int headerRow = 1;
    private string signature = string.Empty;
    private string? signedSheet;
    private int signedRow;

    public int? Id { get; set; }
    public string LayoutKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LayoutKind { get; set; } = "BANK";
    public int? BankAccountId { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public int SkipFooterRows { get; set; }
    public string MappingJson { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public string Reason { get; set; } = string.Empty;
    public string SamplePath { get; set; } = string.Empty;
    public IReadOnlyList<string> Headers { get; internal set; } = [];
    public IReadOnlyList<string> SheetNames { get; internal set; } = [];
    public LayoutTestResult? TestResult { get; internal set; }

    public string SheetName { get => sheetName; set => sheetName = value ?? string.Empty; }
    public int HeaderRow { get => headerRow; set => headerRow = value; }
    public string HeaderSignature => signature;

    /// <summary>The signature was read for a different sheet or header row than the form now says: read the sample again.</summary>
    public bool SignatureIsStale => signature.Length > 0 && (!string.Equals(signedSheet ?? string.Empty, sheetName.Trim(), StringComparison.Ordinal) || signedRow != headerRow);

    internal void SetSignature(SampleFileHeader header, string sheetAsAsked)
    {
        signature = header.HeaderSignatureSha256;
        signedSheet = sheetAsAsked.Trim();
        signedRow = header.HeaderRow;
        Headers = header.Headers;
        SheetNames = header.SheetNames;
        headerRow = header.HeaderRow;
    }

    public StatementLayoutDraft ToDraft() => new(Id, LayoutKey, DisplayName, LayoutKind, LayoutKind == "BANK" ? BankAccountId : null,
        LayoutKind == "PROVIDER" ? ProviderKey : null, signature, string.IsNullOrWhiteSpace(sheetName) ? null : sheetName.Trim(), headerRow, SkipFooterRows, MappingJson, Active);

    public static LayoutEdit From(StatementLayoutRow row)
    {
        var edit = new LayoutEdit
        {
            Id = row.Id, LayoutKey = row.LayoutKey, DisplayName = row.DisplayName, LayoutKind = row.LayoutKind, BankAccountId = row.BankAccountId,
            ProviderKey = row.ProviderKey ?? string.Empty, SkipFooterRows = row.SkipFooterRows, MappingJson = row.MappingJson, Active = row.Active,
            sheetName = row.SheetName ?? string.Empty, headerRow = row.HeaderRow, signature = row.HeaderSignatureSha256
        };
        edit.signedSheet = edit.sheetName; edit.signedRow = row.HeaderRow;
        return edit;
    }
}

/// <summary>A payment code (a tender code of the sales files) and the provider that settles it.</summary>
public sealed class TenderEdit
{
    public string SourceTenderCode { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public bool IsTillCash { get; set; }
    public string Reason { get; set; } = string.Empty;

    public static TenderEdit From(TenderProviderRow row) => new() { SourceTenderCode = row.SourceTenderCode, Mode = row.Mode, ProviderKey = row.ProviderKey ?? string.Empty, IsTillCash = row.IsTillCash };
}

/// <summary>
/// Settings > Collections setup (Form template; plan U4). Pure presentation logic over <see cref="ICollectionsSetupService"/>:
/// bank accounts (last four digits only), providers, merchant ids, payment codes, file layouts with "Test on this file", and
/// the statement folder, and (1.12.1 P2) the settlement rules. Owner only: any other role gets one sentence and no data, and every action re-checks the role.
/// Each save needs a reason (the service audits it). Drafts are checked with <see cref="CollectionsSetupRules"/> first, so
/// the screen shows the rule's plain sentence rather than a database error.
/// </summary>
public sealed class CollectionsSetupModel
{
    public const string BankAccountNumberHint = "Enter only the last four digits of the account number. ETP never keeps the full number.";
    public const string SampleFileFilterText = "Statement files (*.csv;*.xlsx)|*.csv;*.xlsx";
    public const string BankMappingExample = "{\n  \"value_date\": \"Value Dt\",\n  \"narration\": \"Narration\",\n  \"reference\": \"Chq./Ref.No.\",\n  \"debit\": \"Withdrawal Amt.\",\n  \"credit\": \"Deposit Amt.\",\n  \"date_format\": \"dd/MM/yyyy\"\n}";
    public const string ProviderMappingExample = "{\n  \"txn_datetime\": \"Txn Date Time\",\n  \"merchant_id\": \"MID\",\n  \"txn_reference\": \"RRN\",\n  \"gross\": \"Gross Amount\",\n  \"net\": \"Net Amount\",\n  \"settlement_date\": \"Settlement Date\",\n  \"datetime_format\": \"dd/MM/yyyy HH:mm:ss\"\n}";
    private readonly Func<ICollectionsSetupService> service;
    private readonly ICollectionsDialogs dialogs;
    private readonly Func<DateOnly> today;

    public CollectionsSetupModel(Func<ICollectionsSetupService> service, ShellAccess access, ICollectionsDialogs dialogs, Func<DateOnly>? today = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        Access = access ?? throw new ArgumentNullException(nameof(access));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        this.today = today ?? (() => DateOnly.FromDateTime(DateTime.Today));
        Policy = new PolicyEdit { EffectiveFrom = this.today() };
    }

    public ShellAccess Access { get; }
    public bool IsAllowed => CollectionsScreens.CanSetUp(Access);
    public string? RefusalText => IsAllowed ? null : Access.HasAssignedRole ? CollectionsScreens.SetupOwnerOnlyText : CollectionsScreens.NotSignedInText;
    public CollectionsSetup? Setup { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public StatusTone MessageTone { get; private set; } = StatusTone.Neutral;
    public event Action? Changed;

    public BankAccountEdit BankAccount { get; private set; } = new();
    public ProviderEdit Provider { get; private set; } = new();
    public MerchantIdEdit Merchant { get; private set; } = new();
    public LayoutEdit Layout { get; private set; } = new();
    public TenderEdit Tender { get; private set; } = new();
    public PolicyEdit Policy { get; private set; } = new();
    public string EvidenceRoot { get; set; } = string.Empty;
    public string EvidenceReason { get; set; } = string.Empty;

    public IReadOnlyList<string> Modes => CollectionsSetupRules.Modes;
    public IReadOnlyList<string> SettlesVia => CollectionsSetupRules.SettlesVia;
    public IReadOnlyList<string> StatementKinds => CollectionsSetupRules.StatementKinds;
    public IReadOnlyList<string> Granularities => CollectionsSetupRules.Granularities;
    public IReadOnlyList<string> LayoutKinds => CollectionsSetupRules.LayoutKinds;

    /// <summary>Plain words for a provider's file kind and a bank account's store, so a grid never shows a code.</summary>
    public static string WordsFor(string code) => code switch
    {
        "GROSS_WITH_DEDUCTIONS" => "Gross, with fees taken out",
        "NET" => "Net of fees",
        "UNKNOWN" => "Not known yet",
        "TRANSACTION" => "Each payment",
        "SETTLEMENT_SUMMARY" => "Settlements only",
        "BANK" => "Pays into a bank account",
        "NONE" => "Does not pay into a bank account",
        _ => code
    };

    /// <summary>The names of the bank accounts for a provider's account choice: the account's name, never its number.</summary>
    public string AccountName(int? bankAccountId) =>
        Setup?.BankAccounts.FirstOrDefault(account => account.Id == bankAccountId) is { } account ? CollectionsScreens.MaskDigits(account.DisplayName) : CollectionsScreens.MissingValue;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAllowed) { Setup = null; Tell(StatusTone.Critical, RefusalText!, false); return; }
        try
        {
            Setup = await service().LoadAsync(cancellationToken).ConfigureAwait(true);
            EvidenceRoot = Setup.EvidenceRoot;
            if (Message.Length == 0 || MessageTone == StatusTone.Critical) Tell(StatusTone.Neutral, string.Empty, false);
            Changed?.Invoke();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { Setup = null; Tell(StatusTone.Critical, CollectionsScreens.Sentence(exception)); }
    }

    // ---- forms: new / edit ----

    public void NewBankAccount() { BankAccount = new BankAccountEdit(); Changed?.Invoke(); }
    public void EditBankAccount(BankAccountRow row) { BankAccount = BankAccountEdit.From(row); Changed?.Invoke(); }
    public void NewProvider() { Provider = new ProviderEdit(); Changed?.Invoke(); }
    public void EditProvider(CollectionProviderRow row) { Provider = ProviderEdit.From(row); Changed?.Invoke(); }
    public void NewMerchant() { Merchant = new MerchantIdEdit(); Changed?.Invoke(); }
    public void EditMerchant(MerchantIdRow row) { Merchant = MerchantIdEdit.From(row); Changed?.Invoke(); }
    public void EditTender(TenderProviderRow row) { Tender = TenderEdit.From(row); Changed?.Invoke(); }
    public void NewPolicy() { Policy = new PolicyEdit { EffectiveFrom = today() }; Changed?.Invoke(); }
    public void EditPolicy(CollectionPolicyRow row) { Policy = PolicyEdit.From(row); Changed?.Invoke(); }

    /// <summary>A rule's state in words for its chip: in force today, ended, or starting later.</summary>
    public string PolicyState(CollectionPolicyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var now = today();
        return CollectionPolicyResolver.InForce(row, now) ? "In force" : row.EffectiveFrom > now ? "Starts later" : "Ended";
    }

    public void NewLayout(string kind = "BANK")
    {
        Layout = new LayoutEdit { LayoutKind = kind, MappingJson = kind == "BANK" ? BankMappingExample : ProviderMappingExample };
        Changed?.Invoke();
    }

    public void EditLayout(StatementLayoutRow row) { Layout = LayoutEdit.From(row); Changed?.Invoke(); }

    // ---- saves (each: role, rule check, service, reload, one sentence) ----

    public Task<bool> SaveBankAccountAsync(CancellationToken cancellationToken = default) => SaveAsync(BankAccount.Reason, async reason =>
    {
        var draft = CollectionsSetupRules.Normalise(BankAccount.ToDraft());
        await service().SaveBankAccountAsync(draft, reason, cancellationToken).ConfigureAwait(true);
        return $"Saved the bank account {CollectionsScreens.MaskDigits(draft.DisplayName)}.";
    }, () => BankAccount = new BankAccountEdit(), cancellationToken);

    public Task<bool> SaveProviderAsync(CancellationToken cancellationToken = default) => SaveAsync(Provider.Reason, async reason =>
    {
        decimal? tolerance = null;
        if (!string.IsNullOrWhiteSpace(Provider.RoundingTolerance))
        {
            if (!decimal.TryParse(Provider.RoundingTolerance.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                throw new ArgumentException("Enter the rounding tolerance as a number of rupees, for example 1 or 0.50.");
            tolerance = parsed;
        }
        var draft = CollectionsSetupRules.Normalise(new ProviderDraft(Provider.ProviderKey, Provider.DisplayName, Provider.Mode, Provider.SettlesVia,
            Provider.StatementKind, Provider.Granularity, Provider.BankAccountId, tolerance, Provider.Active));
        await service().SaveProviderAsync(draft, reason, cancellationToken).ConfigureAwait(true);
        return $"Saved the provider {draft.DisplayName}.";
    }, () => Provider = new ProviderEdit(), cancellationToken);

    public Task<bool> SaveMerchantAsync(CancellationToken cancellationToken = default) => SaveAsync(Merchant.Reason, async reason =>
    {
        var draft = CollectionsSetupRules.Normalise(new MerchantIdDraft(Merchant.Id, Merchant.ProviderKey, Merchant.MerchantId, Merchant.TerminalId, Merchant.StoreCode, Merchant.Active));
        await service().SaveMerchantIdAsync(draft, reason, cancellationToken).ConfigureAwait(true);
        return $"Saved the merchant id for store {draft.StoreCode}.";
    }, () => Merchant = new MerchantIdEdit(), cancellationToken);

    /// <summary>1.12.1 P2: saves a settlement rule. The rules first (CollectionsSetupRules), then an overlap with a rule of the same
    /// scope is refused before SQL is asked (SQL refuses it again, THROW 52000). To change a rule from a date on, give the old
    /// rule an end date and add a new one; a rule is never deleted (its history is in collections_setup_history).</summary>
    public Task<bool> SavePolicyAsync(CancellationToken cancellationToken = default) => SaveAsync(Policy.Reason, async reason =>
    {
        var draft = CollectionsSetupRules.Normalise(Policy.ToDraft());
        if (CollectionPolicyResolver.Overlaps(Setup?.Policies ?? [], draft) is not null) throw new ArgumentException(CollectionPolicyResolver.OverlapText);
        await service().SavePolicyAsync(draft, reason, cancellationToken).ConfigureAwait(true);
        return $"Saved the {draft.Mode} settlement rule for {(draft.StoreCode is null ? "all stores" : "store " + draft.StoreCode)}.";
    }, () => Policy = new PolicyEdit { EffectiveFrom = today() }, cancellationToken);

    public Task<bool> SaveTenderAsync(CancellationToken cancellationToken = default) => SaveAsync(Tender.Reason, async reason =>
    {
        var draft = CollectionsSetupRules.Normalise(new TenderProviderDraft(Tender.SourceTenderCode, Tender.ProviderKey, Tender.IsTillCash));
        await service().SaveTenderProviderAsync(draft, reason, cancellationToken).ConfigureAwait(true);
        return $"Saved payment code {draft.SourceTenderCode}.";
    }, () => Tender = new TenderEdit(), cancellationToken);

    public Task<bool> SaveEvidenceRootAsync(CancellationToken cancellationToken = default) => SaveAsync(EvidenceReason, async reason =>
    {
        var root = CollectionsSetupRules.EvidenceRoot(EvidenceRoot);
        await service().SaveEvidenceRootAsync(root, reason, cancellationToken).ConfigureAwait(true);
        EvidenceReason = string.Empty;
        return "Saved the statement folder.";
    }, null, cancellationToken);

    public Task<bool> SaveLayoutAsync(CancellationToken cancellationToken = default) => SaveAsync(Layout.Reason, async reason =>
    {
        if (Layout.HeaderSignature.Length == 0) throw new ArgumentException("Read the header from a sample file before saving the layout.");
        if (Layout.SignatureIsStale) throw new ArgumentException("The sheet or header row changed. Read the header from the sample file again before saving.");
        var draft = CollectionsSetupRules.Normalise(Layout.ToDraft());
        await service().SaveLayoutAsync(draft, reason, cancellationToken).ConfigureAwait(true);
        return $"Saved the file layout {draft.DisplayName}.";
    }, () => Layout = new LayoutEdit(), cancellationToken);

    // ---- layouts: the sample file ----

    /// <summary>"Read header from a file": the header row of a sample file as the layout would see it. The signature is stored
    /// in the form; it is never typed.</summary>
    public async Task<bool> ReadSampleAsync(string? samplePath = null, CancellationToken cancellationToken = default)
    {
        if (!IsAllowed) { Tell(StatusTone.Critical, RefusalText!); return false; }
        var path = samplePath ?? dialogs.ChooseSampleFile();
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            Layout.SamplePath = path;
            var asked = Layout.SheetName;
            var header = await service().ReadSampleHeaderAsync(path, string.IsNullOrWhiteSpace(asked) ? null : asked.Trim(), Layout.HeaderRow, cancellationToken).ConfigureAwait(true);
            Layout.SetSignature(header, asked);
            Layout.TestResult = null;
            Tell(StatusTone.Success, $"Read {IndianCount(header.Headers.Count, "column")} from row {header.HeaderRow}. Use these names in the column map, then press Test on this file.");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { Tell(StatusTone.Critical, CollectionsScreens.Sentence(exception)); return false; }
    }

    /// <summary>"Test on this file": reads the sample with the layout as it is on screen, without saving anything.</summary>
    public async Task<LayoutTestResult?> TestOnFileAsync(string? samplePath = null, CancellationToken cancellationToken = default)
    {
        if (!IsAllowed) { Tell(StatusTone.Critical, RefusalText!); return null; }
        var path = samplePath ?? (string.IsNullOrWhiteSpace(Layout.SamplePath) ? dialogs.ChooseSampleFile() : Layout.SamplePath);
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            Layout.SamplePath = path;
            if (Layout.HeaderSignature.Length == 0 || Layout.SignatureIsStale)
                if (!await ReadSampleAsync(path, cancellationToken).ConfigureAwait(true)) return null;
            var result = await service().TestLayoutAsync(path, Layout.ToDraft(), cancellationToken).ConfigureAwait(true);
            Layout.TestResult = result;
            Tell(TestTone(result), TestSentence(result));
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { Tell(StatusTone.Critical, CollectionsScreens.Sentence(exception)); return null; }
    }

    public static StatusTone TestTone(LayoutTestResult result) =>
        !result.SignatureMatches || result.Problems.Count > 0 || result.ReadableLines == 0 ? StatusTone.Critical :
        result.UnreadableLines > 0 ? StatusTone.Warning : StatusTone.Success;

    /// <summary>The one sentence "Test on this file" gives: whether the header matches and how many lines read.</summary>
    public static string TestSentence(LayoutTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Problems.Count > 0 && !result.SignatureMatches) return string.Join(" ", result.Problems);
        var read = $"{IndianCount(result.ReadableLines, "line")} could be read";
        var unread = result.UnreadableLines == 0 ? string.Empty : $" and {IndianCount(result.UnreadableLines, "line")} could not";
        var problems = result.Problems.Count == 0 ? string.Empty : " " + string.Join(" ", result.Problems);
        var header = result.SignatureMatches ? "The header matches." : "The header does not match.";
        return $"{header} {read}{unread}.{problems}";
    }

    /// <summary>One line of the first-lines table as text: the date, the reference, the amounts or the reason it could not be read.</summary>
    public static IReadOnlyList<LayoutTestRow> TestRows(LayoutTestResult result) =>
        result.FirstLines.Select(line => new LayoutTestRow(line)).ToList();

    private async Task<bool> SaveAsync(string reasonText, Func<string, Task<string>> save, Action? afterSaved, CancellationToken cancellationToken)
    {
        if (!IsAllowed) { Tell(StatusTone.Critical, RefusalText!); return false; }
        try
        {
            var reason = CollectionsSetupRules.Reason(reasonText);
            var done = await save(reason).ConfigureAwait(true);
            afterSaved?.Invoke();
            Tell(StatusTone.Success, done, false);
            await LoadAsync(cancellationToken).ConfigureAwait(true);
            Changed?.Invoke();
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { Tell(StatusTone.Critical, CollectionsScreens.Sentence(exception)); return false; }
    }

    private void Tell(StatusTone tone, string text, bool raise = true)
    {
        Message = text; MessageTone = tone;
        if (raise) Changed?.Invoke();
    }

    private static string IndianCount(int count, string noun) => Etp.Reporting.Reporting.IndianFormat.Plural(count, noun);
}

/// <summary>A line of the "Test on this file" table.</summary>
public sealed class LayoutTestRow(LayoutTestLine line)
{
    public int Row => line.RowNumber;
    public string Date => Etp.Reporting.Reporting.IndianFormat.Date(line.LineDate);
    public string Reference => string.IsNullOrWhiteSpace(line.Reference) ? CollectionsScreens.MissingValue : line.Reference;
    public string Credit => Etp.Reporting.Reporting.IndianFormat.MoneyCell(line.Credit);
    public string Debit => Etp.Reporting.Reporting.IndianFormat.MoneyCell(line.Debit);
    public string Gross => Etp.Reporting.Reporting.IndianFormat.MoneyCell(line.Gross);
    public string Net => Etp.Reporting.Reporting.IndianFormat.MoneyCell(line.Net);
    public string Kind => line.LineKind ?? CollectionsScreens.MissingValue;
    public string Result => line.ParseError ?? "Read";
    public bool Readable => line.ParseError is null;
}
