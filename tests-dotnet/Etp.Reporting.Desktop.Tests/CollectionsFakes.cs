using Etp.Reporting.Application.Collections;
using Etp.Reporting.Desktop.Modules.Collections;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>Fake Collections services for the 1.12 wave-2 lane U4 screens. Synthetic values only: store COLA, accounts and
/// layouts named SYN_*, no bank, person, phone or account number.</summary>
internal sealed class FakeImportService : ICollectionsImportService
{
    public Dictionary<string, CollectionsFilePreview> Previews { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Queue<CollectionsImportResult> Results { get; } = new();
    public Exception? ImportFailure { get; set; }
    public List<string> Previewed { get; } = [];
    public List<CollectionsImportRequest> Imported { get; } = [];

    public Task<CollectionsFilePreview> PreviewAsync(string filePath, CancellationToken cancellationToken = default)
    {
        Previewed.Add(filePath);
        return Previews.TryGetValue(filePath, out var preview) ? Task.FromResult(preview) : Task.FromException<CollectionsFilePreview>(new FileNotFoundException("The selected file no longer exists.", filePath));
    }

    public Task<CollectionsImportResult> ImportAsync(CollectionsImportRequest request, CancellationToken cancellationToken = default)
    {
        Imported.Add(request);
        if (ImportFailure is not null) return Task.FromException<CollectionsImportResult>(ImportFailure);
        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Imported5());
    }

    public static CollectionsImportResult Imported5() => new(CollectionsImportOutcome.Imported, 1, 5, 0, 0, 0, 0, "Imported 5 lines.");

    public static MatchedLayout Layout(int id, string account) => new(id, "SYN_" + id, "Synthetic layout " + id, "BANK", "SYN_ACC" + id, account);

    public static CollectionsFilePreview Preview(string name, params MatchedLayout[] layouts) =>
        new(name, new string('a', 64), layouts, layouts.Length == 0 ? "This file does not match any file layout. Ask the owner to add one in Settings > Collections setup." : null);
}

internal sealed class FakeDepositService : ICashDepositService
{
    public IReadOnlyList<DepositInTransit> Transit { get; set; } = [];
    public IReadOnlyList<DepositProposal> Proposals { get; set; } = [];
    public int ProposalCalls { get; private set; }
    public int TransitCalls { get; private set; }
    public List<DepositProposalAcceptance> Accepted { get; } = [];
    public Exception? AcceptFailure { get; set; }
    public List<IReadOnlyList<string>?> StoreScopes { get; } = [];

    public Task<IReadOnlyList<DepositInTransit>> LoadInTransitAsync(IReadOnlyList<string>? storeCodes, CancellationToken cancellationToken = default)
    {
        TransitCalls++; StoreScopes.Add(storeCodes);
        return Task.FromResult(Transit);
    }

    public Task<IReadOnlyList<DepositProposal>> LoadProposalsAsync(IReadOnlyList<string>? storeCodes, CancellationToken cancellationToken = default)
    {
        ProposalCalls++;
        return Task.FromResult(Proposals);
    }

    public Task<long> AcceptProposalAsync(DepositProposalAcceptance acceptance, CancellationToken cancellationToken = default)
    {
        Accepted.Add(acceptance);
        if (AcceptFailure is not null) return Task.FromException<long>(AcceptFailure);
        Transit = Transit.Where(deposit => !(deposit.StoreCode == acceptance.StoreCode && deposit.DepositDate == acceptance.DepositDate)).ToList();
        Proposals = Proposals.Where(proposal => proposal.Deposit.StoreCode != acceptance.StoreCode || proposal.Deposit.DepositDate != acceptance.DepositDate).ToList();
        return Task.FromResult(41L);
    }

    public Task<long> ConfirmManuallyAsync(ManualDepositConfirmation confirmation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task WithdrawAsync(long confirmationId, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public static DepositInTransit Deposit(string store, int day, decimal amount, int age) => new(store, new DateOnly(2026, 8, day), amount, age);

    public static DepositProposal Proposal(DepositInTransit deposit, long lineId, bool ambiguous = false, string? narration = "SYNTHETIC NARRATION") =>
        new(deposit, new DepositCandidateLine(lineId, 7, "SYN_COLA", "Synthetic COLA account", "COLA", deposit.DepositDate.AddDays(1), deposit.Amount, "DEP000" + lineId, narration), ambiguous);
}

internal sealed class FakeStatementQuery : ICollectionsStatementQuery
{
    public CollectionsStatement Statement { get; set; } = new([], false);
    public List<CollectionsStatementRequest> Requests { get; } = [];
    public Exception? Failure { get; set; }

    public Task<CollectionsStatement> LoadStatementAsync(CollectionsStatementRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Failure is null ? Task.FromResult(Statement) : Task.FromException<CollectionsStatement>(Failure);
    }

    public Task<IReadOnlyList<DepositInTransit>> LoadDepositsInTransitAsync(IReadOnlyList<string>? storeCodes, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DepositInTransit>>([]);
}

internal sealed class FakeSetupService : ICollectionsSetupService
{
    public CollectionsSetup Setup { get; set; } = new([], [], [], [], [], [], @"C:\SyntheticEvidence");
    public int Loads { get; private set; }
    public List<(BankAccountDraft Draft, string Reason)> BankAccounts { get; } = [];
    public List<(StatementLayoutDraft Draft, string Reason)> Layouts { get; } = [];
    public List<(string Path, int HeaderRow, string? Sheet)> HeaderReads { get; } = [];
    public List<StatementLayoutDraft> Tests { get; } = [];
    public LayoutTestResult TestResult { get; set; } = new(true, [], 2, 1, [new LayoutTestLine(3, new DateOnly(2026, 8, 1), "REF1", 1250m, null, null, null, null, null, null)]);

    public Task<CollectionsSetup> LoadAsync(CancellationToken cancellationToken = default) { Loads++; return Task.FromResult(Setup); }

    public Task<int> SaveBankAccountAsync(BankAccountDraft draft, string reason, CancellationToken cancellationToken = default)
    {
        BankAccounts.Add((draft, reason));
        return Task.FromResult(7);
    }

    public Task SaveProviderAsync(ProviderDraft draft, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<int> SaveMerchantIdAsync(MerchantIdDraft draft, string reason, CancellationToken cancellationToken = default) => Task.FromResult(1);
    public Task<int> SavePolicyAsync(CollectionPolicyDraft draft, string reason, CancellationToken cancellationToken = default) => Task.FromResult(1);

    public Task<int> SaveLayoutAsync(StatementLayoutDraft draft, string reason, CancellationToken cancellationToken = default)
    {
        Layouts.Add((draft, reason));
        return Task.FromResult(3);
    }

    public Task SaveTenderProviderAsync(TenderProviderDraft draft, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveEvidenceRootAsync(string path, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<SampleFileHeader> ReadSampleHeaderAsync(string samplePath, string? sheetName, int headerRow, CancellationToken cancellationToken = default)
    {
        HeaderReads.Add((samplePath, headerRow, sheetName));
        return Task.FromResult(new SampleFileHeader(["Sheet1"], sheetName ?? "Sheet1", headerRow, ["Date", "Narration", "Credit"], new string('b', 64)));
    }

    public Task<LayoutTestResult> TestLayoutAsync(string samplePath, StatementLayoutDraft draft, CancellationToken cancellationToken = default)
    {
        Tests.Add(draft);
        return Task.FromResult(TestResult);
    }
}

internal sealed class FakeDialogs : ICollectionsDialogs
{
    public IReadOnlyList<string> Files { get; set; } = [];
    public string? Sample { get; set; }
    public string? ReasonAnswer { get; set; }
    public bool ConfirmAnswer { get; set; } = true;
    public List<string> Asked { get; } = [];
    public List<string?> Suggestions { get; } = [];

    public IReadOnlyList<string> ChooseStatementFiles() => Files;
    public string? ChooseSampleFile() => Sample;
    public string? AskReason(string title, string prompt, string? suggestion = null) { Asked.Add(title); Suggestions.Add(suggestion); return ReasonAnswer; }
    public bool Confirm(string action, string detail) => ConfirmAnswer;
}
