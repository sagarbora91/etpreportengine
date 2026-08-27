using System.Text.Json;
using Etp.Reporting.Domain.Periods;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record InvoiceSalesSummaryRow(
    DateOnly BusinessDate,
    string StoreCode,
    string DocumentNumber,
    string TransactionTypes,
    decimal Quantity,
    decimal NetValue,
    int SourceRows);

public sealed record InvoiceSalesLineageRow(
    DateOnly BusinessDate,
    string StoreCode,
    string DocumentNumber,
    string LineIdentifier,
    string ProductCode,
    string? Brand,
    string? BrandSegment,
    string? TransactionType,
    decimal Quantity,
    decimal? NetValue,
    string? CroNumber,
    string SourceWorkbook,
    string SourceSheet,
    int SourceRow);

public sealed record DsrManagementRow(
    string Period,
    string Store,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal? TySales,
    decimal? LySales,
    decimal? GrowthPercent,
    string GrowthStatus,
    decimal? TyUnits,
    decimal? LyUnits,
    int? TyInvoices,
    int? LyInvoices,
    decimal? Upt,
    decimal? Atv,
    decimal? WalkIns,
    decimal? ConversionPercent,
    string MetricPolicy);

public sealed record StaffPerformanceRow(
    string StoreCode,
    string CroNumber,
    decimal NetSales,
    decimal? LastYearSales,
    decimal? GrowthPercent,
    string GrowthStatus,
    decimal NetQuantity,
    decimal Discount,
    int Transactions,
    decimal? Upt,
    decimal? Atv,
    decimal ContributionPercent,
    decimal? TargetSales,
    decimal? TargetAchievementPercent,
    int Rank);

public sealed record StaffPerformanceResult(
    IReadOnlyList<StaffPerformanceRow> Rows,
    decimal CanonicalSales,
    decimal AttributedSales,
    decimal Variance,
    ReconciliationStatus Status,
    string Message,
    string MetricPolicy);

public sealed record PhysicalStockReportRow(
    string StoreCode,
    DateOnly BusinessDate,
    string InventoryGroupCode,
    decimal? DisplayQuantity,
    decimal? BackstockQuantity,
    decimal? DefectiveQuantity,
    decimal? YLocationQuantity,
    decimal? ComponentTotal,
    decimal? CountedPhysicalQuantity,
    decimal? CompositionVariance,
    decimal SystemQuantity,
    decimal? SystemVariance,
    string? Remarks,
    string Status);

public sealed record StockInventoryReportRow(
    DateOnly SnapshotDate,
    string StoreCode,
    string ProductCode,
    string? Brand,
    string? InventoryGroup,
    decimal Quantity,
    decimal? UnitCost,
    decimal? TotalCost,
    DateOnly? LastSaleDate,
    int? DaysSinceLastSale,
    string MovementStatus);

public sealed record DailyExceptionRow(
    string Severity,
    string Area,
    string Code,
    string StoreCode,
    DateOnly BusinessDate,
    string? DocumentNumber,
    string? ItemCode,
    decimal? Variance,
    string? SourceWorkbook,
    string? SourceSheet,
    int? SourceRow,
    string Message,
    string RecommendedAction);

public sealed record ServiceSalesRow(
    string Period,
    string StoreCode,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal? Cash,
    decimal? Card,
    decimal? Upi,
    decimal? Total,
    decimal? LastYearTotal,
    decimal? GrowthPercent,
    string Availability);

public sealed record CashReconciliationResult(
    string StoreCode,
    DateOnly BusinessDate,
    decimal? OpeningCash,
    decimal RetailCash,
    decimal? ServiceCash,
    decimal? Expenses,
    decimal? CashDeposit,
    decimal? Adjustment,
    decimal? CalculatedClosing,
    decimal? CountedClosing,
    decimal? Variance,
    ReconciliationStatus Status,
    string Message);

public sealed class OperationalReportRepository(string connectionString)
{
    public const string DsrMetricPolicy = "DSR_INVOICE_DENOMINATOR_SOURCE_EVIDENCE_V1";
    public const string StaffMetricPolicy = "R013_ATTRIBUTED_TRANSACTION_DENOMINATOR_V1";
    public const string StockInventorySql = """
        SELECT s.snapshot_date,s.store_code,s.product_code,
               COALESCE(NULLIF(LTRIM(RTRIM(s.brand_name)),''),NULLIF(LTRIM(RTRIM(s.brand_code)),'')),
               NULLIF(LTRIM(RTRIM(s.cluster)),''),SUM(s.quantity),
               CASE WHEN COUNT(s.unit_cost)=0 THEN NULL ELSE MAX(s.unit_cost) END,
               CASE WHEN COUNT(s.total_cost)=0 THEN NULL ELSE SUM(s.total_cost) END,
               sale.last_sale_date,
               CASE WHEN sale.last_sale_date IS NULL THEN NULL ELSE DATEDIFF(day,sale.last_sale_date,s.snapshot_date) END
        FROM dbo.stock_snapshots s
        OUTER APPLY
        (
          SELECT MAX(i.transaction_date) last_sale_date
          FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
          WHERE i.store_code=s.store_code AND l.product_code=s.product_code AND i.transaction_date<=s.snapshot_date
                AND COALESCE(l.source_quantity,0)>0
        ) sale
        WHERE s.snapshot_date=@date
          AND (@stores IS NULL OR s.store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)))
          AND (@segments IS NULL OR s.cluster IN(SELECT CONVERT(nvarchar(100),[value]) FROM OPENJSON(@segments)))
          AND (@items IS NULL OR s.product_code IN(SELECT CONVERT(nvarchar(80),[value]) FROM OPENJSON(@items)))
        GROUP BY s.snapshot_date,s.store_code,s.product_code,
                 COALESCE(NULLIF(LTRIM(RTRIM(s.brand_name)),''),NULLIF(LTRIM(RTRIM(s.brand_code)),'')),
                 NULLIF(LTRIM(RTRIM(s.cluster)),''),sale.last_sale_date
        ORDER BY 2,5,4,3;
        """;

    public async Task<IReadOnlyList<InvoiceSalesSummaryRow>> LoadInvoiceSummaryAsync(
        ReportingQueryScope scope,
        CancellationToken cancellationToken = default)
    {
        scope.Validate();
        const string sql = """
            SELECT i.transaction_date,i.store_code,i.document_number,
                   CASE WHEN COUNT(DISTINCT COALESCE(l.source_transaction_type,'UNMAPPED'))=1
                        THEN MIN(COALESCE(l.source_transaction_type,'UNMAPPED')) ELSE 'MIXED' END,
                   SUM(l.source_quantity),SUM(l.source_net_amount),COUNT_BIG(*)
            FROM dbo.sales_invoices i JOIN dbo.sales_lines l ON l.sales_invoice_id=i.sales_invoice_id
            WHERE i.transaction_date BETWEEN @from AND @to
              AND (@stores IS NULL OR i.store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)))
            GROUP BY i.transaction_date,i.store_code,i.document_number
            ORDER BY i.transaction_date,i.store_code,i.document_number;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@from", scope.DateFrom);
        command.Parameters.AddWithValue("@to", scope.DateTo);
        command.Parameters.AddWithValue("@stores", Json(scope.StoreCodes));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<InvoiceSalesSummaryRow>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetFieldValue<DateOnly>(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetDecimal(4), reader.GetDecimal(5), checked((int)reader.GetInt64(6))));
        return rows;
    }

    public async Task<IReadOnlyList<InvoiceSalesLineageRow>> LoadInvoiceLineageAsync(
        ReportingQueryScope scope,
        CancellationToken cancellationToken = default)
    {
        scope.Validate();
        const string sql = """
            SELECT i.transaction_date,i.store_code,i.document_number,l.line_identifier,l.product_code,
                   COALESCE(l.source_brand_name,l.source_brand_code),l.brand_segment,l.source_transaction_type,
                   l.source_quantity,l.source_net_amount,cro.source_cro_number,f.original_file_name,s.sheet_name,s.source_row_number
            FROM dbo.sales_lines l
            JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
            JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id
            JOIN dbo.import_files f ON f.import_file_id=s.import_file_id
            OUTER APPLY
            (
              SELECT TOP(1) e.source_cro_number
              FROM dbo.sales_line_enrichments e
              WHERE e.matched_sales_line_id=l.sales_line_id AND e.enrichment_type='R013' AND e.match_status='Matched'
              ORDER BY e.sales_line_enrichment_id
            ) cro
            WHERE i.transaction_date BETWEEN @from AND @to
              AND (@stores IS NULL OR i.store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)))
              AND (@segments IS NULL OR l.brand_segment IN(SELECT CONVERT(nvarchar(100),[value]) FROM OPENJSON(@segments)))
              AND (@types IS NULL OR l.source_transaction_type IN(SELECT CONVERT(nvarchar(80),[value]) FROM OPENJSON(@types)))
              AND (@items IS NULL OR l.product_code IN(SELECT CONVERT(nvarchar(80),[value]) FROM OPENJSON(@items)))
            ORDER BY i.transaction_date,i.store_code,i.document_number,l.line_identifier;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = ScopeCommand(connection, sql, scope);
        command.Parameters.AddWithValue("@segments", Json(scope.BrandSegments));
        command.Parameters.AddWithValue("@types", Json(scope.TransactionTypes));
        command.Parameters.AddWithValue("@items", Json(scope.ItemCodes));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<InvoiceSalesLineageRow>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetFieldValue<DateOnly>(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetDecimal(8), NullableDecimal(reader, 9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetInt32(13)));
        return rows;
    }

    public async Task<IReadOnlyList<DsrManagementRow>> LoadDsrAsync(
        DateOnly businessDate,
        IReadOnlyList<string>? storeCodes = null,
        CancellationToken cancellationToken = default)
    {
        var stores = storeCodes is { Count: > 0 }
            ? storeCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : ["WLMHW", "HEMW"];
        if (stores.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Store codes cannot be blank.", nameof(storeCodes));
        var periodPolicy = new IndianFinancialYearPeriodPolicy();
        var metricEngine = new ManagementMetricEngine();
        var rows = new List<DsrManagementRow>();
        await using var connection = await OpenAsync(cancellationToken);
        foreach (var kind in Enum.GetValues<ReportingPeriodKind>())
        {
            var period = periodPolicy.Resolve(businessDate, kind);
            var facts = await LoadDsrFactsAsync(connection, period, stores, cancellationToken);
            var walkIns = await LoadWalkInsAsync(connection, period.Current, stores, cancellationToken);
            foreach (var store in stores)
                rows.Add(BuildDsrRow(kind.ToString().ToUpperInvariant(), store, period, facts.GetValueOrDefault(store) ?? new(),
                    walkIns.GetValueOrDefault(store) ?? new(), metricEngine));

            var combinedFacts = Combine(facts.Values);
            decimal? combinedWalkIns = walkIns.Count == stores.Length && walkIns.Values.All(x => x.IsComplete)
                ? walkIns.Values.Sum(x => x.Value) : null;
            rows.Add(BuildDsrRow(kind.ToString().ToUpperInvariant(), "COMBINED", period, combinedFacts,
                new(combinedWalkIns, combinedWalkIns is not null), metricEngine));
        }
        return rows;
    }

    public async Task<DailySalesReportDocument> LoadDailySalesReportDocumentAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        var dsr = await LoadDsrAsync(businessDate, ["WLMHW", "HEMW"], cancellationToken);
        return await ComposeDailySalesReportDocumentAsync(businessDate, dsr, cancellationToken);
    }

    public async Task<DailySalesReportDocument> ComposeDailySalesReportDocumentAsync(
        DateOnly businessDate,
        IReadOnlyList<DsrManagementRow> dsr,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dsr);
        var service = await LoadServiceSalesAsync(businessDate, ["WLMHW", "HEMW"], cancellationToken);
        var supplementary = await LoadDsrSupplementaryAsync(businessDate, cancellationToken);
        return DailySalesReportBuilder.Build(businessDate,
            dsr.Select(x => new DsrPeriodFact(x.Period, x.Store, x.TySales, x.LySales, x.TyUnits, x.LyUnits,
                x.TyInvoices, x.LyInvoices, x.Upt, x.Atv, x.WalkIns, x.ConversionPercent)).ToArray(),
            service.Select(x => new DsrServiceFact(x.Period, x.StoreCode, x.Cash, x.Card, x.Upi, x.Total, x.LastYearTotal)).ToArray(),
            supplementary.Targets, supplementary.ServiceWdc, DsrMetricPolicy);
    }

    public async Task<StaffPerformanceResult> LoadStaffPerformanceAsync(
        ReportingQueryScope scope,
        CancellationToken cancellationToken = default)
    {
        scope.Validate();
        const string staffSql = """
            SELECT e.store_code,e.source_cro_number,SUM(e.source_net_value),SUM(e.source_quantity),
                   SUM(COALESCE(e.scheme_discount,0)+COALESCE(e.user_discount,0)+COALESCE(e.pre_discount,0)),
                   COUNT(DISTINCT e.document_number)
            FROM dbo.sales_line_enrichments e
            WHERE e.enrichment_type='R013' AND e.match_status='Matched' AND e.transaction_date BETWEEN @from AND @to
              AND (@stores IS NULL OR e.store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)))
              AND e.source_cro_number IS NOT NULL
            GROUP BY e.store_code,e.source_cro_number ORDER BY e.store_code,SUM(e.source_net_value) DESC;
            """;
        const string lastYearSql = """
            SELECT e.store_code,e.source_cro_number,SUM(e.source_net_value)
            FROM dbo.sales_line_enrichments e
            WHERE e.enrichment_type='R013' AND e.match_status='Matched' AND e.transaction_date BETWEEN @from AND @to
              AND (@stores IS NULL OR e.store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)))
              AND e.source_cro_number IS NOT NULL
            GROUP BY e.store_code,e.source_cro_number;
            """;
        const string totalSql = """
            SELECT i.store_code,COALESCE(SUM(l.source_net_amount),0)
            FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
            WHERE i.transaction_date BETWEEN @from AND @to
              AND (@stores IS NULL OR i.store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)))
            GROUP BY i.store_code;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        var raw = new List<(string Store, string Cro, decimal Sales, decimal Quantity, decimal Discount, int Transactions)>();
        await using (var command = ScopeCommand(connection, staffSql, scope))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                raw.Add((reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetInt32(5)));
        var lastYearScope = scope with { DateFrom = scope.DateFrom.AddYears(-1), DateTo = scope.DateTo.AddYears(-1) };
        var lastYear = new Dictionary<(string Store, string Cro), decimal>();
        await using (var command = ScopeCommand(connection, lastYearSql, lastYearScope))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                lastYear[(reader.GetString(0).ToUpperInvariant(), reader.GetString(1).ToUpperInvariant())] = reader.GetDecimal(2);
        var canonicalByStore = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        await using (var command = ScopeCommand(connection, totalSql, scope))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) canonicalByStore[reader.GetString(0)] = reader.GetDecimal(1);
        var targets = await new OperationalCompletionRepository(connectionString).LoadStaffTargetsAsync(scope, cancellationToken);
        var targetByStaff = targets.ToDictionary(x => (x.StoreCode.ToUpperInvariant(), x.CroNumber.ToUpperInvariant()), x => x.TargetSales);
        foreach (var target in targets.Where(target => raw.All(x =>
                     !string.Equals(x.Store, target.StoreCode, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(x.Cro, target.CroNumber, StringComparison.OrdinalIgnoreCase))))
            raw.Add((target.StoreCode, target.CroNumber, 0m, 0m, 0m, 0));

        var metricEngine = new ManagementMetricEngine();
        var rows = new List<StaffPerformanceRow>();
        foreach (var storeGroup in raw.GroupBy(x => x.Store, StringComparer.OrdinalIgnoreCase))
        {
            var rank = 0;
            decimal? priorSales = null;
            foreach (var x in storeGroup.OrderByDescending(x => x.Sales).ThenBy(x => x.Cro, StringComparer.OrdinalIgnoreCase))
            {
                if (priorSales != x.Sales) { rank++; priorSales = x.Sales; }
                var key = (x.Store.ToUpperInvariant(), x.Cro.ToUpperInvariant());
                decimal? ly = lastYear.TryGetValue(key, out var lastYearSales) ? lastYearSales : null;
                var growth = metricEngine.Growth(x.Sales, ly);
                decimal? target = targetByStaff.TryGetValue(key, out var staffTarget) ? staffTarget : null;
                var achievement = target is null or 0m ? null : x.Sales / target * 100m;
                var storeCanonical = canonicalByStore.GetValueOrDefault(x.Store);
                rows.Add(new(x.Store, x.Cro, x.Sales, ly, growth.Value, growth.Availability.ToString(), x.Quantity, x.Discount, x.Transactions,
                    x.Transactions == 0 ? null : x.Quantity / x.Transactions,
                    x.Transactions == 0 ? null : x.Sales / x.Transactions,
                    storeCanonical == 0 ? 0 : x.Sales / storeCanonical * 100m,
                    target, achievement, rank));
            }
        }
        var canonical = canonicalByStore.Values.Sum();
        var attributed = raw.Sum(x => x.Sales);
        var variance = canonical - attributed;
        var status = variance == 0 ? ReconciliationStatus.Passed : ReconciliationStatus.Failed;
        return new(rows.OrderBy(x => x.StoreCode).ThenBy(x => x.Rank).ThenBy(x => x.CroNumber).ToArray(), canonical, attributed, variance, status,
            status == ReconciliationStatus.Passed
                ? "R013 attributed sales reconcile to canonical R025 sales."
                : "Attributed and canonical sales differ; review unmatched/unassigned source rows without rounding away the variance. LY, target achievement and rank remain independently visible.",
            StaffMetricPolicy);
    }

    public async Task<IReadOnlyList<ServiceSalesRow>> LoadServiceSalesAsync(
        DateOnly businessDate,
        IReadOnlyList<string>? storeCodes = null,
        CancellationToken cancellationToken = default)
    {
        var stores = storeCodes is { Count: > 0 }
            ? storeCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : ["WLMHW", "HEMW"];
        var policy = new IndianFinancialYearPeriodPolicy();
        var metricEngine = new ManagementMetricEngine();
        var rows = new List<ServiceSalesRow>();
        await using var connection = await OpenAsync(cancellationToken);
        foreach (var kind in Enum.GetValues<ReportingPeriodKind>())
        {
            var period = policy.Resolve(businessDate, kind);
            var facts = await LoadServiceFactsAsync(connection, period, stores, cancellationToken);
            foreach (var store in stores)
            {
                var fact = facts.GetValueOrDefault(store) ?? new();
                var currentComplete = fact.CurrentCount == period.Current.InclusiveDayCount * 3;
                var lastComplete = fact.LastYearCount == period.LastYear.InclusiveDayCount * 3;
                decimal? cash = currentComplete ? fact.Cash : null;
                decimal? card = currentComplete ? fact.Card : null;
                decimal? upi = currentComplete ? fact.Upi : null;
                decimal? total = currentComplete ? cash + card + upi : null;
                decimal? ly = lastComplete ? fact.LastYearTotal : null;
                var growth = metricEngine.Growth(total, ly);
                rows.Add(new(kind.ToString().ToUpperInvariant(), store, period.Current.Start, period.Current.End,
                    cash, card, upi, total, ly, growth.Value,
                    currentComplete ? growth.Availability.ToString() : MetricAvailability.MissingInput.ToString()));
            }
        }
        return rows;
    }

    public async Task<CashReconciliationResult> LoadCashReconciliationAsync(
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        var workflow = await new DailyReportingWorkflowRepository(connectionString).LoadAsync(storeCode, businessDate, cancellationToken);
        var values = workflow.ManualInputs.ToDictionary(x => x.FieldCode, x => x.NumericValue, StringComparer.OrdinalIgnoreCase);
        var tenders = await new SqlServerReportingQueryRepository(connectionString).LoadTendersAsync(
            new(businessDate, businessDate, [storeCode]), cancellationToken);
        var retailCash = tenders.Where(x => string.Equals(x.TenderType, "CASH", StringComparison.OrdinalIgnoreCase)).Sum(x => x.SourceAmount);
        var evaluation = new CashBalanceReconciliationService().Reconcile(new(
            Value(values, "OPENING_CASH"), retailCash, Value(values, "SERVICE_CASH"), Value(values, "EXPENSES"),
            Value(values, "CASH_DEPOSIT"), Value(values, "CASH_ADJUSTMENT"), Value(values, "CLOSING_CASH_COUNTED")));
        var missing = evaluation.MissingInputs;
        if (!workflow.ImportedReports.Contains("R022", StringComparer.OrdinalIgnoreCase)) missing = missing.Append("R022").ToArray();
        if (missing.Count > 0)
            return new(storeCode, businessDate, Value(values, "OPENING_CASH"), retailCash, Value(values, "SERVICE_CASH"),
                Value(values, "EXPENSES"), Value(values, "CASH_DEPOSIT"), Value(values, "CASH_ADJUSTMENT"), null,
                Value(values, "CLOSING_CASH_COUNTED"), null, ReconciliationStatus.Blocked,
                $"Missing required cash evidence: {string.Join(", ", missing)}.");

        return new(storeCode, businessDate, values["OPENING_CASH"], retailCash, values["SERVICE_CASH"], values["EXPENSES"],
            values["CASH_DEPOSIT"], values["CASH_ADJUSTMENT"], evaluation.CalculatedClosingCash, values["CLOSING_CASH_COUNTED"],
            evaluation.Variance, evaluation.Status, $"{evaluation.Formula}; counted closing remains independent evidence.");
    }

    public async Task<IReadOnlyList<PhysicalStockReportRow>> LoadPhysicalStockAsync(
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        storeCode = string.IsNullOrWhiteSpace(storeCode) ? throw new ArgumentException("A store code is required.", nameof(storeCode)) : storeCode.Trim();
        const string sql = """
            WITH system_stock AS
            (
              SELECT store_code,snapshot_date,
                     COALESCE(NULLIF(LTRIM(RTRIM(cluster)),''),NULLIF(LTRIM(RTRIM(brand_name)),''),NULLIF(LTRIM(RTRIM(brand_code)),''),product_code) inventory_group_code,
                     SUM(quantity) system_quantity
              FROM dbo.stock_snapshots
              WHERE store_code=@store AND snapshot_date=@date
              GROUP BY store_code,snapshot_date,COALESCE(NULLIF(LTRIM(RTRIM(cluster)),''),NULLIF(LTRIM(RTRIM(brand_name)),''),NULLIF(LTRIM(RTRIM(brand_code)),''),product_code)
            )
            SELECT COALESCE(m.store_code,s.store_code),COALESCE(m.business_date,s.snapshot_date),COALESCE(m.inventory_group_code,s.inventory_group_code),
                   m.display_quantity,m.backstock_quantity,m.defective_quantity,m.y_location_quantity,
                   CASE WHEN m.manual_stock_count_id IS NULL OR (m.display_quantity IS NULL AND m.backstock_quantity IS NULL AND m.defective_quantity IS NULL AND m.y_location_quantity IS NULL)
                        THEN NULL ELSE COALESCE(m.display_quantity,0)+COALESCE(m.backstock_quantity,0)+COALESCE(m.defective_quantity,0)+COALESCE(m.y_location_quantity,0) END,
                   m.counted_physical_quantity,
                   CASE WHEN m.counted_physical_quantity IS NULL OR (m.display_quantity IS NULL AND m.backstock_quantity IS NULL AND m.defective_quantity IS NULL AND m.y_location_quantity IS NULL)
                        THEN NULL ELSE m.counted_physical_quantity-(COALESCE(m.display_quantity,0)+COALESCE(m.backstock_quantity,0)+COALESCE(m.defective_quantity,0)+COALESCE(m.y_location_quantity,0)) END,
                   COALESCE(s.system_quantity,0),
                   CASE WHEN m.counted_physical_quantity IS NULL THEN NULL ELSE m.counted_physical_quantity-COALESCE(s.system_quantity,0) END,
                   m.remarks
            FROM dbo.manual_stock_counts m FULL OUTER JOIN system_stock s
              ON s.store_code=m.store_code AND s.snapshot_date=m.business_date AND s.inventory_group_code=m.inventory_group_code
            WHERE COALESCE(m.store_code,s.store_code)=@store AND COALESCE(m.business_date,s.snapshot_date)=@date
            ORDER BY COALESCE(m.inventory_group_code,s.inventory_group_code);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", storeCode); command.Parameters.AddWithValue("@date", businessDate);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<PhysicalStockReportRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var compositionVariance = NullableDecimal(reader, 9);
            var systemVariance = NullableDecimal(reader, 11);
            var status = systemVariance is null ? "MANUAL INPUT MISSING" : systemVariance != 0 ? "FAIL" : compositionVariance is not null && compositionVariance != 0 ? "WARNING" : "PASS";
            rows.Add(new(reader.GetString(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2), NullableDecimal(reader, 3), NullableDecimal(reader, 4),
                NullableDecimal(reader, 5), NullableDecimal(reader, 6), NullableDecimal(reader, 7), NullableDecimal(reader, 8), compositionVariance,
                reader.GetDecimal(10), systemVariance, reader.IsDBNull(12) ? null : reader.GetString(12), status));
        }
        return rows;
    }

    public async Task<IReadOnlyList<StockInventoryReportRow>> LoadStockInventoryAsync(
        ReportingQueryScope scope,
        CancellationToken cancellationToken = default)
    {
        scope.Validate();
        await using var connection=await OpenAsync(cancellationToken);await using var command=new SqlCommand(StockInventorySql,connection);
        command.Parameters.AddWithValue("@date",scope.DateTo);command.Parameters.AddWithValue("@stores",Json(scope.StoreCodes));command.Parameters.AddWithValue("@segments",Json(scope.BrandSegments));command.Parameters.AddWithValue("@items",Json(scope.ItemCodes));
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);var rows=new List<StockInventoryReportRow>();
        while(await reader.ReadAsync(cancellationToken))
        {
            var quantity=reader.GetDecimal(5);DateOnly? last=reader.IsDBNull(8)?null:reader.GetFieldValue<DateOnly>(8);int? days=reader.IsDBNull(9)?null:reader.GetInt32(9);
            var status=quantity==0?"ZERO STOCK":last is null?"NEVER SOLD":days>=90?"SLOW - 90+ DAYS":days>=60?"WATCH - 60+ DAYS":"ACTIVE";
            rows.Add(new(reader.GetFieldValue<DateOnly>(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),quantity,NullableDecimal(reader,6),NullableDecimal(reader,7),last,days,status));
        }
        return rows;
    }

    public async Task<IReadOnlyList<DailyExceptionRow>> LoadDailyExceptionsAsync(
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<DailyExceptionRow>();
        var workflow = await new DailyReportingWorkflowRepository(connectionString).LoadAsync(storeCode, businessDate, cancellationToken);
        rows.AddRange(workflow.MissingReports.Select(code => new DailyExceptionRow("BLOCKER", "Source", "SOURCE_MISSING", storeCode, businessDate,
            null, null, null, null, null, null, $"Required ETP report {code} has not been imported.", "Import the approved report for this store and business date.")));
        rows.AddRange(workflow.MissingRequiredInputs.Select(code => new DailyExceptionRow("BLOCKER", "Manual input", "MANUAL_INPUT_MISSING", storeCode, businessDate,
            null, null, null, null, null, null, $"Required operational input {code} is missing.", "Enter a value; enter zero explicitly when zero is the true value.")));

        var scope = new ReportingQueryScope(businessDate, businessDate, [storeCode]);
        var executor = new SqlBackedReportingExecutor(new SqlServerReportingQueryRepository(connectionString),
            RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);
        var tender = await executor.ExecuteTenderReconciliationAsync(scope, cancellationToken);
        var pointers = await LoadInvoicePointersAsync(scope, cancellationToken);
        foreach (var document in tender.Documents.Where(x => x.Status != ReconciliationStatus.Passed))
        {
            pointers.TryGetValue((document.StoreCode.ToUpperInvariant(), document.DocumentNumber.ToUpperInvariant()), out var pointer);
            rows.Add(new("FAIL", "Tender", "TENDER_VARIANCE", document.StoreCode, businessDate, document.DocumentNumber, null, document.Variance,
                pointer?.FileName, pointer?.SheetName, pointer?.SourceRow,
                $"Revenue control and reporting-eligible tenders differ by {document.Variance:N2}.", "Review missing, excess or quarantined tender rows; do not change the control total."));
        }

        const string enrichmentSql = """
            SELECT e.store_code,e.transaction_date,e.document_number,e.product_code,e.match_status,f.original_file_name,s.sheet_name,s.source_row_number
            FROM dbo.sales_line_enrichments e JOIN dbo.source_lineage s ON s.source_lineage_id=e.source_lineage_id
            JOIN dbo.import_files f ON f.import_file_id=s.import_file_id
            WHERE e.store_code=@store AND e.transaction_date=@date AND e.match_status<>'Matched'
            ORDER BY e.document_number,e.product_code,s.source_row_number;
            """;
        await using (var connection = await OpenAsync(cancellationToken))
        await using (var command = new SqlCommand(enrichmentSql, connection))
        {
            command.Parameters.AddWithValue("@store", storeCode); command.Parameters.AddWithValue("@date", businessDate);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new(reader.GetString(4) == "Ambiguous" ? "FAIL" : "WARNING", "Staff enrichment", $"R013_{reader.GetString(4).ToUpperInvariant()}",
                    reader.GetString(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2), reader.GetString(3), null,
                    reader.GetString(5), reader.GetString(6), reader.GetInt32(7),
                    "The staff/CRO enrichment could not be linked to exactly one canonical R025 sales line.", "Review the invoice, item and source row; preserve the exact variance."));
        }

        var physical = await LoadPhysicalStockAsync(storeCode, businessDate, cancellationToken);
        foreach (var item in physical.Where(x => x.Status is "FAIL" or "WARNING" or "MANUAL INPUT MISSING"))
            rows.Add(new(item.Status == "MANUAL INPUT MISSING" ? "WARNING" : item.Status, "Physical stock", item.Status switch
                {
                    "FAIL" => "PHYSICAL_SYSTEM_VARIANCE",
                    "WARNING" => "PHYSICAL_COMPOSITION_VARIANCE",
                    _ => "PHYSICAL_COUNT_MISSING"
                },
                storeCode, businessDate, null, item.InventoryGroupCode, item.Status == "FAIL" ? item.SystemVariance : item.CompositionVariance,
                null, null, null, item.Status == "MANUAL INPUT MISSING" ? "No counted physical quantity has been entered for this system-stock group." : "Physical stock evidence does not match its comparison control.",
                item.Status == "MANUAL INPUT MISSING" ? "Enter the physical count when the operational count is performed; enter zero explicitly when correct." : "Recount or record an approved correction reason; system stock is never overwritten."));

        var cash = await LoadCashReconciliationAsync(storeCode, businessDate, cancellationToken);
        if (cash.Status is ReconciliationStatus.Blocked or ReconciliationStatus.Failed)
            rows.Add(new(cash.Status == ReconciliationStatus.Blocked ? "BLOCKER" : "FAIL", "Cash", "CASH_RECONCILIATION", storeCode, businessDate,
                null, null, cash.Variance, null, null, null, cash.Message, "Complete the missing cash evidence or investigate the exact variance."));

        var staff = await LoadStaffPerformanceAsync(scope, cancellationToken);
        if (staff.Status == ReconciliationStatus.Failed)
            rows.Add(new("FAIL", "Staff", "STAFF_CANONICAL_VARIANCE", storeCode, businessDate, null, null, staff.Variance,
                null, null, null, staff.Message, "Review unmatched and unassigned R013 rows; do not round the variance away."));
        return rows.OrderBy(x => x.Severity).ThenBy(x => x.Area).ThenBy(x => x.DocumentNumber).ThenBy(x => x.SourceRow).ToArray();
    }

    private async Task<Dictionary<(string Store, string Document), SourcePointer>> LoadInvoicePointersAsync(
        ReportingQueryScope scope,
        CancellationToken token)
    {
        const string sql = """
            SELECT i.store_code,i.document_number,MIN(f.original_file_name),MIN(s.sheet_name),MIN(s.source_row_number)
            FROM dbo.sales_invoice_controls c JOIN dbo.sales_invoices i ON i.sales_invoice_id=c.sales_invoice_id
            JOIN dbo.source_lineage s ON s.source_lineage_id=c.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=s.import_file_id
            WHERE i.transaction_date BETWEEN @from AND @to
              AND (@stores IS NULL OR i.store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)))
            GROUP BY i.store_code,i.document_number;
            """;
        await using var connection = await OpenAsync(token);
        await using var command = ScopeCommand(connection, sql, scope);
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new Dictionary<(string Store, string Document), SourcePointer>();
        while (await reader.ReadAsync(token))
            result[(reader.GetString(0).ToUpperInvariant(), reader.GetString(1).ToUpperInvariant())] =
                new(reader.GetString(2), reader.GetString(3), reader.GetInt32(4));
        return result;
    }

    private static async Task<Dictionary<string, DsrFacts>> LoadDsrFactsAsync(
        SqlConnection connection,
        BusinessReportingPeriod period,
        IReadOnlyList<string> stores,
        CancellationToken token)
    {
        const string sql = """
            SELECT i.store_code,
              SUM(CASE WHEN i.transaction_date BETWEEN @currentFrom AND @currentTo THEN l.source_net_amount END),
              SUM(CASE WHEN i.transaction_date BETWEEN @lastFrom AND @lastTo THEN l.source_net_amount END),
              SUM(CASE WHEN i.transaction_date BETWEEN @currentFrom AND @currentTo THEN l.source_quantity END),
              SUM(CASE WHEN i.transaction_date BETWEEN @lastFrom AND @lastTo THEN l.source_quantity END),
              COUNT(DISTINCT CASE WHEN i.transaction_date BETWEEN @currentFrom AND @currentTo THEN i.document_number END),
              COUNT(DISTINCT CASE WHEN i.transaction_date BETWEEN @lastFrom AND @lastTo THEN i.document_number END)
            FROM dbo.sales_invoices i JOIN dbo.sales_lines l ON l.sales_invoice_id=i.sales_invoice_id
            WHERE (i.transaction_date BETWEEN @currentFrom AND @currentTo OR i.transaction_date BETWEEN @lastFrom AND @lastTo)
              AND i.store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores))
              AND UPPER(COALESCE(l.source_transaction_type,'')) IN('INV','SR')
            GROUP BY i.store_code;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@currentFrom", period.Current.Start);
        command.Parameters.AddWithValue("@currentTo", period.Current.End);
        command.Parameters.AddWithValue("@lastFrom", period.LastYear.Start);
        command.Parameters.AddWithValue("@lastTo", period.LastYear.End);
        command.Parameters.AddWithValue("@stores", JsonSerializer.Serialize(stores));
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new Dictionary<string, DsrFacts>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(token))
            result[reader.GetString(0)] = new(
                NullableDecimal(reader, 1), NullableDecimal(reader, 2), NullableDecimal(reader, 3), NullableDecimal(reader, 4),
                reader.GetInt32(5), reader.GetInt32(6));
        return result;
    }

    private static async Task<Dictionary<string, WalkInFacts>> LoadWalkInsAsync(
        SqlConnection connection,
        DateRange period,
        IReadOnlyList<string> stores,
        CancellationToken token)
    {
        const string sql = """
            SELECT store_code,SUM(numeric_value),COUNT(DISTINCT business_date)
            FROM dbo.manual_operational_inputs
            WHERE field_code='WALK_INS' AND business_date BETWEEN @from AND @to
              AND store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores))
            GROUP BY store_code;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@from", period.Start);
        command.Parameters.AddWithValue("@to", period.End);
        command.Parameters.AddWithValue("@stores", JsonSerializer.Serialize(stores));
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new Dictionary<string, WalkInFacts>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(token))
        {
            var complete = reader.GetInt32(2) == period.InclusiveDayCount;
            result[reader.GetString(0)] = new(complete ? reader.GetDecimal(1) : null, complete);
        }
        return result;
    }

    private async Task<DsrSupplementaryFacts> LoadDsrSupplementaryAsync(DateOnly businessDate, CancellationToken token)
    {
        const string sql = """
            SELECT store_code,field_code,numeric_value
            FROM dbo.manual_operational_inputs
            WHERE business_date=@date AND field_code IN('SALES_TARGET','SERVICE_WDC')
              AND store_code IN('WLMHW','HEMW');
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@date", businessDate);
        await using var reader = await command.ExecuteReaderAsync(token);
        var targets = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        var wdc = new List<decimal>();
        while (await reader.ReadAsync(token))
        {
            if (reader.IsDBNull(2)) continue;
            if (reader.GetString(1).Equals("SALES_TARGET", StringComparison.OrdinalIgnoreCase)) targets[reader.GetString(0)] = reader.GetDecimal(2);
            else wdc.Add(reader.GetDecimal(2));
        }
        return new(targets, wdc.Count == 0 ? null : wdc.Sum());
    }

    private static async Task<Dictionary<string, ServiceFacts>> LoadServiceFactsAsync(
        SqlConnection connection,
        BusinessReportingPeriod period,
        IReadOnlyList<string> stores,
        CancellationToken token)
    {
        const string sql = """
            SELECT store_code,
              SUM(CASE WHEN business_date BETWEEN @currentFrom AND @currentTo AND field_code='SERVICE_CASH' THEN numeric_value ELSE 0 END),
              SUM(CASE WHEN business_date BETWEEN @currentFrom AND @currentTo AND field_code='SERVICE_CARD' THEN numeric_value ELSE 0 END),
              SUM(CASE WHEN business_date BETWEEN @currentFrom AND @currentTo AND field_code='SERVICE_UPI' THEN numeric_value ELSE 0 END),
              SUM(CASE WHEN business_date BETWEEN @lastFrom AND @lastTo THEN numeric_value ELSE 0 END),
              COUNT(DISTINCT CASE WHEN business_date BETWEEN @currentFrom AND @currentTo THEN CONCAT(CONVERT(char(10),business_date,23),'|',field_code) END),
              COUNT(DISTINCT CASE WHEN business_date BETWEEN @lastFrom AND @lastTo THEN CONCAT(CONVERT(char(10),business_date,23),'|',field_code) END)
            FROM dbo.manual_operational_inputs
            WHERE field_code IN('SERVICE_CASH','SERVICE_CARD','SERVICE_UPI')
              AND (business_date BETWEEN @currentFrom AND @currentTo OR business_date BETWEEN @lastFrom AND @lastTo)
              AND store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores))
            GROUP BY store_code;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@currentFrom", period.Current.Start);
        command.Parameters.AddWithValue("@currentTo", period.Current.End);
        command.Parameters.AddWithValue("@lastFrom", period.LastYear.Start);
        command.Parameters.AddWithValue("@lastTo", period.LastYear.End);
        command.Parameters.AddWithValue("@stores", JsonSerializer.Serialize(stores));
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new Dictionary<string, ServiceFacts>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(token))
            result[reader.GetString(0)] = new(reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3),
                reader.GetDecimal(4), reader.GetInt32(5), reader.GetInt32(6));
        return result;
    }

    private static DsrManagementRow BuildDsrRow(
        string periodName,
        string store,
        BusinessReportingPeriod period,
        DsrFacts facts,
        WalkInFacts walkIns,
        ManagementMetricEngine engine)
    {
        var growth = engine.Growth(facts.TySales, facts.LySales);
        var denominator = new ProductivityRule(DsrMetricPolicy, TransactionDenominator.InvoiceCount);
        var upt = engine.UnitsPerTransaction(facts.TyUnits, facts.TyInvoices, denominator);
        var atv = engine.AverageTransactionValue(facts.TySales, facts.TyInvoices, denominator);
        var conversion = engine.Conversion(facts.TyInvoices, walkIns.Value);
        return new(periodName, store, period.Current.Start, period.Current.End, facts.TySales, facts.LySales,
            growth.Value, growth.Availability.ToString(), facts.TyUnits, facts.LyUnits, facts.TyInvoices, facts.LyInvoices,
            upt.Value, atv.Value, walkIns.Value, conversion.Value, DsrMetricPolicy);
    }

    private static DsrFacts Combine(IEnumerable<DsrFacts> facts)
    {
        var values = facts.ToArray();
        return new(SumNullable(values.Select(x => x.TySales)), SumNullable(values.Select(x => x.LySales)),
            SumNullable(values.Select(x => x.TyUnits)), SumNullable(values.Select(x => x.LyUnits)),
            values.Sum(x => x.TyInvoices ?? 0), values.Sum(x => x.LyInvoices ?? 0));
    }

    private static decimal? SumNullable(IEnumerable<decimal?> values)
    {
        var array = values.ToArray();
        return array.Any(x => x is not null) ? array.Sum(x => x ?? 0) : null;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqlConnection(connectionString);
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static SqlCommand ScopeCommand(SqlConnection connection, string sql, ReportingQueryScope scope)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@from", scope.DateFrom);
        command.Parameters.AddWithValue("@to", scope.DateTo);
        command.Parameters.AddWithValue("@stores", Json(scope.StoreCodes));
        return command;
    }

    private static object Json(IReadOnlyList<string>? values) => values is { Count: > 0 }
        ? JsonSerializer.Serialize(values.Distinct(StringComparer.OrdinalIgnoreCase)) : DBNull.Value;

    private static decimal? NullableDecimal(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    private static decimal? Value(IReadOnlyDictionary<string, decimal?> values, string key) => values.GetValueOrDefault(key);
    private sealed record DsrFacts(decimal? TySales = null, decimal? LySales = null, decimal? TyUnits = null, decimal? LyUnits = null, int? TyInvoices = null, int? LyInvoices = null);
    private sealed record WalkInFacts(decimal? Value = null, bool IsComplete = false);
    private sealed record DsrSupplementaryFacts(IReadOnlyDictionary<string, decimal?> Targets, decimal? ServiceWdc);
    private sealed record ServiceFacts(decimal Cash = 0, decimal Card = 0, decimal Upi = 0, decimal LastYearTotal = 0, int CurrentCount = 0, int LastYearCount = 0);
    private sealed record SourcePointer(string FileName, string SheetName, int SourceRow);
}
