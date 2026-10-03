using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Audit;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Stock;

namespace Etp.Reporting.Infrastructure.SqlServer.Audit;

/// <summary>
/// What 1.9.3 (planner 1) will do with each file (design 5.3), from the folder import's own steps without a write:
/// <list type="number">
/// <item>the outcome before the planner (layout, empty, not needed), as <see cref="SourceInspector"/> found it;</item>
/// <item>the exact duplicate (<c>ExistsInScopeAsync</c>, then <c>PlanImportAsync</c>'s SELECT) gives <c>Duplicate</c>;</item>
/// <item>the persistence package each route's orchestrator builds, captured by a store that never reaches SQL;</item>
/// <item>the file plan: current files and content keys read without lock hints, <c>SharingSnapshotDates</c> and
/// <see cref="PlannerOnePlanRules"/>: a refusal code, <c>Duplicate content</c>, or promotion over the current files;</item>
/// <item>a finalised day in the file's period, or in a file it takes over: <c>Failed SQL_51021</c>;</item>
/// <item>each typed row looked up by the identity its <c>persist_*</c> procedure uses: New, Present or Conflict, in the
/// order the import persists them, so a row sees the rows before it; any conflict fails the file
/// (<c>IMPORT_CONFLICT</c>), as the whole file rolls back.</item>
/// </list>
/// Files are predicted in the app's order, and each predicted import is applied to an in-memory overlay that the
/// later files of the run see (design 6.3).
/// </summary>
public sealed class PlannerOnePredictor(IPlannerOneState state, string connectionString)
{
    private const int SampleLimit = 20;
    private readonly RunOverlay overlay = new();
    private StringComparer Identity => state.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public async Task<AuditPlannerOneReport> PredictAsync(InspectedFile file, bool withSamples, CancellationToken cancellationToken)
    {
        switch (file.Outcome)
        {
            case InspectionOutcome.NotNeeded: return new("Not needed");
            case InspectionOutcome.UnknownLayout: return new("Unknown layout") { Code = file.FailureCode, Message = Message(file.FailureCode, null) };
            case InspectionOutcome.Failed: return new("Failed") { Code = file.FailureCode, Message = Message(file.FailureCode, null) };
        }
        var accepted = file.Accepted!;
        var store = file.Store!;
        var end = file.BusinessDate!.Value;
        var reportCode = accepted.ProfileIdentity.ReportCode;
        var staged = accepted.Staging.Rows.Count;

        if (await ExactAsync(accepted.Workbook.Sha256, reportCode, store, file.PeriodStart, file.PeriodEnd, cancellationToken).ConfigureAwait(false))
            return new("Duplicate") { Rows = new(0, staged, 0) };

        ImportPersistencePackage package;
        try { package = await CaptureAsync(accepted, store, end, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failure = new SafeImportFailureClassifier().DescribeDetailed(exception, FailureStage.Apply);
            return new("Failed") { Code = failure.Code, Message = Message(failure.Code, exception is ImportSourceException ? failure.SafeMessage : null) };
        }

        var registered = package.File;
        var start = registered.PeriodStart ?? registered.BusinessDate;
        var finish = registered.PeriodEnd ?? registered.BusinessDate;
        if (await ExactAsync(registered.SourceSha256, PersistenceValidation.ResolveReportCode(registered), registered.StoreCode, start, finish, cancellationToken).ConfigureAwait(false))
            return new("Duplicate") { Rows = new(0, staged, 0) };

        var keys = SqlServerTransactionalImportStore.ContentKeys(accepted, registered.BusinessDate);
        var previous = await CurrentFilesAsync(registered.StoreCode!, registered.Profile.ReportCode, start!.Value, finish!.Value, cancellationToken).ConfigureAwait(false);
        previous = SqlServerTransactionalImportStore.SharingSnapshotDates(previous, keys.Values, null);
        bool duplicateContent;
        try
        {
            duplicateContent = PlannerOnePlanRules.Decide(start, finish, registered.SourceSha256, keys.Values, previous, null);
        }
        catch (ImportSourceException refusal)
        {
            return new("Failed") { Code = refusal.Code, Message = Message(refusal.Code, refusal.Message) };
        }

        // The new file's row, and each file it takes over, pass the finalised-day guard on dbo.import_files.
        var periods = new List<(DateOnly Start, DateOnly End)> { (start.Value, finish.Value) };
        if (!duplicateContent)
            periods.AddRange(previous.Where(old => old.Start is not null && old.End is not null).Select(old => (old.Start!.Value, old.End!.Value)));
        foreach (var (from, to) in periods)
            if ((await state.LockedDaysAsync(registered.StoreCode!, from, to, cancellationToken).ConfigureAwait(false)).Count > 0)
                return new("Failed") { Code = ImportCodes.Sql(51021), Message = LockedMessage };

        var promotes = duplicateContent ? [] : previous.Select(old => old.Id).Where(id => id > 0).ToArray();
        var snapshotRows = package.StockSnapshots.Count == 0 ? null
            : package.StockSnapshots.GroupBy(row => row.SnapshotDate).OrderBy(group => group.Key)
                .ToDictionary(group => group.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), group => group.Count());
        if (duplicateContent)
        {
            // The duplicate is recorded against the file that holds its rows; it is not a current file of its own.
            overlay.AddFile(registered, keys.Values, [], current: false);
            return new("Duplicate content") { Rows = new(0, keys.Count, 0), SnapshotRows = snapshotRows };
        }

        var outcomes = await ClassifyRowsAsync(package, cancellationToken).ConfigureAwait(false);
        var counts = Count(outcomes.Rows, staged, package);
        if (outcomes.ConflictRecords > 0)
            return new("Failed")
            {
                Code = ImportCodes.ImportConflict, Message = ImportDiagnosticCatalogue.ConflictCountMessage(outcomes.ConflictRecords),
                Rows = counts with { Conflict = outcomes.ConflictRecords }, PromotesOver = promotes, SnapshotRows = snapshotRows,
                ConflictSamples = withSamples ? outcomes.Samples : null
            };
        overlay.AddFile(registered, keys.Values, previous.Select(old => old.Id));
        outcomes.Commit(overlay, registered.StoreCode!);
        var result = staged == 0 ? "empty export" : counts.New == 0 && counts.Present > 0 ? "Duplicate content" : "Imported";
        return new(result) { Rows = counts, PromotesOver = promotes, SnapshotRows = snapshotRows };
    }

    // Stored rows first by id, then this run's predicted rows in the order they were made (their ids count down from -1).
    private static long Order(long id) => id < 0 ? long.MaxValue / 2 - id : id;

    public const string LockedMessage = "A day inside this export is finalised. Reopen it before changing its sources.";

    /// <summary>The run's predictions assume this order (design 6.3).</summary>
    public const string OrderNote = "Predictions assume the files are imported in this order, as the app orders a folder.";

    private static string Message(string? code, string? message) => code is null ? ImportDiagnosticCatalogue.GenericMessage
        : ImportDiagnosticCatalogue.SafeFailureMessage(code, message, null);

    private async Task<bool> ExactAsync(string sha, string report, string? store, DateOnly? start, DateOnly? end, CancellationToken cancellationToken) =>
        overlay.HasExact(sha, report, store, start, end, Identity) ||
        await state.ExactDuplicateAsync(sha, report, store, start, end, cancellationToken).ConfigureAwait(false) is not null;

    private async Task<List<SqlServerTransactionalImportStore.PreviousFile>> CurrentFilesAsync(string store, string report, DateOnly start, DateOnly end,
        CancellationToken cancellationToken)
    {
        var stored = await state.CurrentFilesAsync(store, report, start, end, cancellationToken).ConfigureAwait(false);
        return stored.Where(file => !overlay.Superseded.Contains(file.Id))
            .Concat(overlay.FilesFor(store, report, start, end, Identity))
            .OrderBy(file => Order(file.Id))
            .Select(file => new SqlServerTransactionalImportStore.PreviousFile(file.Id, file.Sha256, file.Start, file.End, file.Imported,
                file.Keys.ToHashSet(StringComparer.Ordinal), file.Version))
            .ToList();
    }

    /// <summary>The package the route's orchestrator would persist; the capturing store stops it before any SQL.</summary>
    private async Task<ImportPersistencePackage> CaptureAsync(MatchedImportEnvelope accepted, string store, DateOnly end, CancellationToken cancellationToken)
    {
        var capture = new CapturingStore();
        try
        {
            switch (SqlServerImportPersistenceUseCase.SelectRoute(accepted.ProfileIdentity.ReportCode))
            {
                case ImportPersistenceRoute.Revenue:
                    await new R022SqlImportOrchestrator(capture).PersistAsync(accepted, cancellationToken: cancellationToken, expectedBusinessDate: end, expectedStoreCode: store).ConfigureAwait(false);
                    break;
                case ImportPersistenceRoute.Sales:
                    await new R025SqlImportOrchestrator(capture).PersistAsync(accepted, cancellationToken: cancellationToken, expectedBusinessDate: end, expectedStoreCode: store).ConfigureAwait(false);
                    break;
                case ImportPersistenceRoute.Stock:
                    await new StockSqlImportOrchestrator(capture).PersistAsync(accepted, cancellationToken: cancellationToken, expectedBusinessDate: end, expectedStoreCode: store).ConfigureAwait(false);
                    break;
                case ImportPersistenceRoute.Enrichment:
                    await new RetailEnrichmentSqlImportOrchestrator(connectionString, capture).PersistAsync(accepted, end, store, null, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await new EtpFamilySqlImportOrchestrator(capture).PersistAsync(accepted, end, store, null, null, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (PackageCaptured captured) { return captured.Package; }
        throw new InvalidOperationException("The import route built no persistence package.");
    }

    // FolderImportService's counts: per source row, a conflict wins, then new, then present (LoadOutcomeAsync). A
    // landing-only family records every row NEW; a typed family records only its typed rows.
    private static AuditRowCounts Count(IReadOnlyDictionary<(string Sheet, int Row), RowOutcome> rows, int staged, ImportPersistencePackage package)
    {
        var typed = package.SalesLines.Count + package.InvoiceControls.Count + package.Tenders.Count + package.StockMovements.Count
            + package.StockSnapshots.Count + package.Enrichments.Count;
        if (typed == 0) return new(staged, 0, 0);
        return new(rows.Values.Count(value => value == RowOutcome.New), rows.Values.Count(value => value == RowOutcome.Present),
            rows.Values.Count(value => value == RowOutcome.Conflict));
    }

    private enum RowOutcome { Present, New, Conflict }

    private sealed class Outcomes(StringComparer identity)
    {
        public Dictionary<(string Sheet, int Row), RowOutcome> Rows { get; } = new();
        public int ConflictRecords { get; private set; }
        public List<string> Samples { get; } = [];
        public Dictionary<string, StoredInvoice> NewInvoices { get; } = new(identity);
        public List<StoredSalesLine> NewLines { get; } = [];
        public List<StoredControl> NewControls { get; } = [];
        public List<StoredTender> NewTenders { get; } = [];
        public List<StoredMovement> NewMovements { get; } = [];
        public List<StoredSnapshot> NewSnapshots { get; } = [];
        public List<string> NewEnrichments { get; } = [];

        public void Record(SourceRowRegistration lineage, RowOutcome outcome, string identityText)
        {
            var key = (lineage.SheetName, lineage.SourceRowNumber);
            Rows[key] = Rows.TryGetValue(key, out var current) ? (RowOutcome)Math.Max((int)current, (int)outcome) : outcome;
            if (outcome != RowOutcome.Conflict) return;
            ConflictRecords++;
            if (Samples.Count < SampleLimit) Samples.Add(identityText);
        }

        public void Commit(RunOverlay overlay, string store)
        {
            foreach (var invoice in NewInvoices.Values) overlay.Invoices.Add((store, invoice));
            overlay.SalesLines.AddRange(NewLines);
            overlay.Controls.AddRange(NewControls);
            overlay.Tenders.AddRange(NewTenders);
            overlay.Movements.AddRange(NewMovements.Select(row => (store, row)));
            overlay.Snapshots.AddRange(NewSnapshots.Select(row => (store, row)));
            overlay.Enrichments.AddRange(NewEnrichments);
        }
    }

    private async Task<Outcomes> ClassifyRowsAsync(ImportPersistencePackage package, CancellationToken cancellationToken)
    {
        var outcomes = new Outcomes(Identity);
        var store = package.File.StoreCode!;
        var invoices = await InvoiceTableAsync(store, package, cancellationToken).ConfigureAwait(false);
        var ids = invoices.Values.Select(invoice => invoice.Id).ToArray();
        // The order of SqlServerTransactionalImportStore.PersistAsync: controls, sales lines, tenders, movements, snapshots, enrichments.
        if (package.InvoiceControls.Count > 0)
        {
            var controls = (await state.ControlsAsync(ids, cancellationToken).ConfigureAwait(false)).Concat(overlay.Controls)
                .GroupBy(row => row.InvoiceId).ToDictionary(group => group.Key, group => group.OrderBy(row => Order(row.Id)).First());
            foreach (var row in package.InvoiceControls)
            {
                var text = $"{row.StoreCode}/{row.InvoiceYear}/{row.DocumentNumber}/CONTROL";
                if (Invoice(invoices, outcomes, row.StoreCode, row.InvoiceYear, row.DocumentNumber, row.TransactionDate, row.Lineage, text) is not { } invoice) continue;
                var incoming = ProtectedValues.Control(row.SourceTransactionType, row.SourceInvoiceQuantity, row.SourceNetValue, row.CurrencyCode);
                if (controls.TryGetValue(invoice, out var existing)) outcomes.Record(row.Lineage, existing.Protected == incoming ? RowOutcome.Present : RowOutcome.Conflict, text);
                else
                {
                    var added = new StoredControl(invoice, incoming, overlay.NextId());
                    controls[invoice] = added;
                    outcomes.NewControls.Add(added);
                    outcomes.Record(row.Lineage, RowOutcome.New, text);
                }
            }
        }
        if (package.SalesLines.Count > 0)
        {
            var lines = new Dictionary<string, StoredSalesLine>(Identity);
            foreach (var line in (await state.SalesLinesAsync(ids, cancellationToken).ConfigureAwait(false)).Concat(overlay.SalesLines))
                lines.TryAdd($"{line.InvoiceId}|{Text(line.LineIdentifier)}", line);
            foreach (var row in package.SalesLines)
            {
                var text = $"{row.StoreCode}/{row.InvoiceYear}/{row.DocumentNumber}/{row.LineIdentifier}";
                if (Invoice(invoices, outcomes, row.StoreCode, row.InvoiceYear, row.DocumentNumber, row.TransactionDate, row.Lineage, text) is not { } invoice) continue;
                var incoming = ProtectedValues.SalesLine(row.ProductCode, row.SourceTransactionType, row.SourceQuantity, row.SourceGrossAmount, row.SourceNetAmount,
                    row.SourceBrandCode, row.SourceBrandName, row.BrandSegment, row.CurrencyCode);
                var key = $"{invoice}|{Text(row.LineIdentifier)}";
                if (lines.TryGetValue(key, out var existing)) outcomes.Record(row.Lineage, existing.Protected == incoming ? RowOutcome.Present : RowOutcome.Conflict, text);
                else
                {
                    var added = new StoredSalesLine(invoice, row.LineIdentifier, incoming, overlay.NextId());
                    lines[key] = added;
                    outcomes.NewLines.Add(added);
                    outcomes.Record(row.Lineage, RowOutcome.New, text);
                }
            }
        }
        if (package.Tenders.Count > 0)
        {
            var tenders = new Dictionary<string, StoredTender>(Identity);
            foreach (var tender in (await state.TendersAsync(ids, cancellationToken).ConfigureAwait(false)).Concat(overlay.Tenders)
                         .OrderBy(row => Order(row.Id)))
                tenders.TryAdd($"{tender.InvoiceId}|{tender.TenderType.ToUpperInvariant()}", tender);
            foreach (var row in package.Tenders)
            {
                var text = $"{row.StoreCode}/{row.InvoiceYear}/{row.DocumentNumber}/TENDER/{row.TenderType.ToUpperInvariant()}";
                if (Invoice(invoices, outcomes, row.StoreCode, row.InvoiceYear, row.DocumentNumber, row.TransactionDate, row.Lineage, text) is not { } invoice) continue;
                var incoming = ProtectedValues.Tender(row.TenderType, row.SourceAmount, row.CurrencyCode, row.IsReportingEligible, row.ExclusionReason);
                var key = $"{invoice}|{row.TenderType.ToUpperInvariant()}";
                if (tenders.TryGetValue(key, out var existing)) outcomes.Record(row.Lineage, existing.Protected == incoming ? RowOutcome.Present : RowOutcome.Conflict, text);
                else
                {
                    var added = new StoredTender(invoice, row.TenderType, incoming, overlay.NextId());
                    tenders[key] = added;
                    outcomes.NewTenders.Add(added);
                    outcomes.Record(row.Lineage, RowOutcome.New, text);
                }
            }
        }
        if (package.StockMovements.Count > 0) await ClassifyMovementsAsync(package, store, outcomes, cancellationToken).ConfigureAwait(false);
        if (package.StockSnapshots.Count > 0) await ClassifySnapshotsAsync(package, store, outcomes, cancellationToken).ConfigureAwait(false);
        if (package.Enrichments.Count > 0)
        {
            var held = new HashSet<string>(Identity);
            foreach (var type in package.Enrichments.Select(row => row.ReportCode).Distinct(StringComparer.Ordinal))
            {
                var rows = package.Enrichments.Where(row => row.ReportCode == type).ToArray();
                foreach (var key in await state.EnrichmentKeysAsync(type, store, rows.Select(row => row.ContentKey).ToArray(), cancellationToken).ConfigureAwait(false))
                    held.Add($"{type}|{Text(store)}|{key}");
            }
            foreach (var key in overlay.Enrichments) held.Add(key);
            foreach (var row in package.Enrichments)
            {
                var key = $"{row.ReportCode}|{Text(row.StoreCode)}|{row.ContentKey}";
                if (held.Add(key))
                {
                    outcomes.NewEnrichments.Add(key);
                    outcomes.Record(row.Lineage, RowOutcome.New, key);
                }
                else outcomes.Record(row.Lineage, RowOutcome.Present, key);
            }
        }
        return outcomes;
    }

    // The invoice every sales row resolves first: a missing one is created by the first row (and dated by it); a row
    // dated otherwise than its stored invoice is a CONFLICT and goes no further.
    private long? Invoice(Dictionary<string, StoredInvoice> invoices, Outcomes outcomes, string store, int year, string document, DateOnly date,
        SourceRowRegistration lineage, string text)
    {
        var key = InvoiceKey(store, year, document);
        if (!invoices.TryGetValue(key, out var invoice))
        {
            invoice = new StoredInvoice(overlay.NextId(), year, document, date);
            invoices[key] = invoice;
            outcomes.NewInvoices[key] = invoice;
            return invoice.Id;
        }
        if (invoice.Date != date)
        {
            outcomes.Record(lineage, RowOutcome.Conflict, text);
            return null;
        }
        return invoice.Id;
    }

    private string InvoiceKey(string store, int year, string document) => $"{Text(store)}|{year}|{Text(document)}";

    private async Task<Dictionary<string, StoredInvoice>> InvoiceTableAsync(string store, ImportPersistencePackage package, CancellationToken cancellationToken)
    {
        var wanted = package.SalesLines.Select(row => (row.InvoiceYear, row.DocumentNumber))
            .Concat(package.InvoiceControls.Select(row => (row.InvoiceYear, row.DocumentNumber)))
            .Concat(package.Tenders.Select(row => (row.InvoiceYear, row.DocumentNumber))).Distinct().ToArray();
        var table = new Dictionary<string, StoredInvoice>(Identity);
        if (wanted.Length == 0) return table;
        foreach (var invoice in await state.InvoicesAsync(store, wanted, cancellationToken).ConfigureAwait(false))
            table.TryAdd(InvoiceKey(store, invoice.Year, invoice.Document), invoice);
        foreach (var (owner, invoice) in overlay.Invoices.Where(entry => Identity.Equals(entry.Store, store)))
            table.TryAdd(InvoiceKey(owner, invoice.Year, invoice.Document), invoice);
        return table;
    }

    private async Task ClassifyMovementsAsync(ImportPersistencePackage package, string store, Outcomes outcomes, CancellationToken cancellationToken)
    {
        var stored = (await state.MovementsAsync(store, package.StockMovements.Select(row => (row.InvoiceYear, row.DocumentNumber)).Distinct().ToArray(),
                cancellationToken).ConfigureAwait(false))
            .Concat(overlay.Movements.Where(entry => Identity.Equals(entry.Store, store)).Select(entry => entry.Row))
            .ToList();
        string GroupKey(int year, string document, DateOnly date, string product, string type, string? from, string? to) =>
            string.Join('\u001f', year, Text(document), date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Text(product), Text(type), Text(from), Text(to));
        var byGroup = stored.GroupBy(row => GroupKey(row.Year, row.Document, row.Date, row.Product, row.Type, row.From, row.To), Identity)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => Order(row.Id)).ToList(), Identity);
        // SqlServerTransactionalImportStore.AlignMovementLinesAsync: a per-unit group of the file is matched with the lines
        // its identity already holds before the lines are compared.
        var movements = package.StockMovements.ToArray();
        foreach (var group in Enumerable.Range(0, movements.Length)
                     .GroupBy(index => GroupKey(movements[index].InvoiceYear, movements[index].DocumentNumber, movements[index].DocumentDate,
                         movements[index].ProductCode, movements[index].SourceTransactionType, movements[index].FromLocation, movements[index].ToLocation), Identity)
                     .Where(group => group.Count() > 1))
        {
            if (!byGroup.TryGetValue(group.Key, out var lines) || lines.Count == 0) continue;
            var indexes = group.ToArray();
            var aligned = StockLineAlignment.Align(indexes.Select(index => new StockLine(movements[index].LineSeq, movements[index].OpeningQuantity,
                movements[index].TransactionQuantity, movements[index].ClosingQuantity)).ToArray(),
                lines.Select(row => new StockLine(row.LineSeq, row.Opening, row.Transaction, row.Closing)).ToArray());
            for (var i = 0; i < indexes.Length; i++) movements[indexes[i]] = movements[indexes[i]] with { LineSeq = aligned[i] };
        }
        foreach (var row in movements)
        {
            var key = GroupKey(row.InvoiceYear, row.DocumentNumber, row.DocumentDate, row.ProductCode, row.SourceTransactionType, row.FromLocation, row.ToLocation);
            var text = $"{row.StoreCode}/{row.InvoiceYear}/{row.DocumentNumber}/{row.DocumentDate:yyyy-MM-dd}/{row.ProductCode}/{row.SourceTransactionType.ToUpperInvariant()}/{row.FromLocation}/{row.ToLocation}/#{row.LineSeq}";
            var incoming = ProtectedValues.Movement(row.OpeningQuantity, row.TransactionQuantity, row.ClosingQuantity);
            var existing = byGroup.TryGetValue(key, out var lines) ? lines.FirstOrDefault(line => line.LineSeq == row.LineSeq) : null;
            if (existing is not null)
                outcomes.Record(row.Lineage, ProtectedValues.Movement(existing.Opening, existing.Transaction, existing.Closing) == incoming ? RowOutcome.Present : RowOutcome.Conflict, text);
            else
            {
                var added = new StoredMovement(row.InvoiceYear, row.DocumentNumber, row.DocumentDate, row.ProductCode, row.SourceTransactionType,
                    row.FromLocation ?? "", row.ToLocation ?? "", row.LineSeq, row.OpeningQuantity, row.TransactionQuantity, row.ClosingQuantity, overlay.NextId());
                if (lines is null) byGroup[key] = lines = [];
                lines.Add(added);
                outcomes.NewMovements.Add(added);
                outcomes.Record(row.Lineage, RowOutcome.New, text);
            }
        }
    }

    private async Task ClassifySnapshotsAsync(ImportPersistencePackage package, string store, Outcomes outcomes, CancellationToken cancellationToken)
    {
        var stored = (await state.SnapshotsAsync(store, package.StockSnapshots.Select(row => row.SnapshotDate).Distinct().ToArray(), cancellationToken).ConfigureAwait(false))
            .Concat(overlay.Snapshots.Where(entry => Identity.Equals(entry.Store, store)).Select(entry => entry.Row))
            .OrderBy(row => Order(row.Id));
        string Key(DateOnly date, string source, string product, string item, int line) =>
            string.Join('\u001f', date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Text(source), Text(product), Text(item), line);
        var table = new Dictionary<string, StoredSnapshot>(Identity);
        foreach (var row in stored) table.TryAdd(Key(row.Date, row.Source, row.Product, row.Item, row.LineSeq), row);
        foreach (var row in package.StockSnapshots)
        {
            // persist_stock_snapshot derives the source from the lineage record type and refuses a different one.
            var source = row.Lineage.SourceRecordType == "R010_SNAPSHOT" ? "R010" : "CLOSING_STOCK";
            var item = row.SourceUid ?? row.BatchNumber ?? row.Ean ?? "";
            var key = Key(row.SnapshotDate, source, row.ProductCode, item, row.LineSeq);
            var text = $"{row.StoreCode}/{row.SnapshotDate:yyyy-MM-dd}/{source}/{row.ProductCode}/{item}/#{row.LineSeq}";
            var incoming = ProtectedValues.Snapshot(row.Ean, row.BrandCode, row.BrandName, row.Cluster, row.Gender, row.BatchNumber, row.SourceUid,
                row.Quantity, row.UnitCost, row.TotalCost);
            if (table.TryGetValue(key, out var existing)) outcomes.Record(row.Lineage, existing.Protected == incoming ? RowOutcome.Present : RowOutcome.Conflict, text);
            else
            {
                var added = new StoredSnapshot(row.SnapshotDate, source, row.ProductCode, item, row.LineSeq, incoming, overlay.NextId());
                table[key] = added;
                outcomes.NewSnapshots.Add(added);
                outcomes.Record(row.Lineage, RowOutcome.New, text);
            }
        }
    }

    // SQL Server compares these identifiers under the database collation, ignoring trailing spaces.
    private string Text(string? value) => state.CaseSensitive ? (value ?? "").TrimEnd() : (value ?? "").TrimEnd().ToUpperInvariant();

    /// <summary>A transactional store that keeps the package it is handed and stops the orchestrator there.</summary>
    private sealed class CapturingStore : ITransactionalImportStore
    {
        public Task<long> PersistAsync(ImportPersistencePackage package, CancellationToken cancellationToken = default) => throw new PackageCaptured(package);
    }

    private sealed class PackageCaptured(ImportPersistencePackage package) : Exception("The persistence package was captured.")
    {
        public ImportPersistencePackage Package { get; } = package;
    }

    /// <summary>The predicted imports of this run, which the later files see (design 6.3). Ids are negative.</summary>
    private sealed class RunOverlay
    {
        private long next;
        private readonly List<(string Store, string Report, string Sha, DateOnly? Start, DateOnly? End, StoredFile File)> files = [];

        public HashSet<long> Superseded { get; } = [];
        public List<(string Store, StoredInvoice Invoice)> Invoices { get; } = [];
        public List<StoredSalesLine> SalesLines { get; } = [];
        public List<StoredControl> Controls { get; } = [];
        public List<StoredTender> Tenders { get; } = [];
        public List<(string Store, StoredMovement Row)> Movements { get; } = [];
        public List<(string Store, StoredSnapshot Row)> Snapshots { get; } = [];
        public List<string> Enrichments { get; } = [];

        public long NextId() => --next;

        public void AddFile(ImportFileRegistration file, IEnumerable<string> keys, IEnumerable<long> promotedOver, bool current = true)
        {
            var start = file.PeriodStart ?? file.BusinessDate;
            var end = file.PeriodEnd ?? file.BusinessDate;
            files.Add((file.StoreCode ?? "", PersistenceValidation.ResolveReportCode(file), file.SourceSha256.Trim().ToLowerInvariant(), start, end,
                new StoredFile(NextId(), file.SourceSha256, start, end, DateTime.UtcNow, keys.ToArray(), 1)));
            foreach (var id in promotedOver) Superseded.Add(id);
            if (!current) Superseded.Add(files[^1].File.Id);
        }

        public bool HasExact(string sha, string report, string? store, DateOnly? start, DateOnly? end, StringComparer identity) =>
            files.Any(file => file.Sha == sha.Trim().ToLowerInvariant() && file.Report == report && identity.Equals(file.Store, store ?? "")
                && file.Start == start && file.End == end);

        public IEnumerable<StoredFile> FilesFor(string store, string report, DateOnly start, DateOnly end, StringComparer identity) =>
            files.Where(file => identity.Equals(file.Store, store) && file.Report == report && !Superseded.Contains(file.File.Id)
                && file.Start <= end && file.End >= start).Select(file => file.File);
    }
}
