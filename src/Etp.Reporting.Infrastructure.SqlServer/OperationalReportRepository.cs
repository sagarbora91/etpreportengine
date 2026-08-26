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
    decimal NetQuantity,
    decimal Discount,
    int Transactions,
    decimal? Upt,
    decimal? Atv,
    decimal ContributionPercent);

public sealed record StaffPerformanceResult(
    IReadOnlyList<StaffPerformanceRow> Rows,
    decimal CanonicalSales,
    decimal AttributedSales,
    decimal Variance,
    ReconciliationStatus Status,
    string Message,
    string MetricPolicy);

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
        const string totalSql = """
            SELECT COALESCE(SUM(l.source_net_amount),0)
            FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
            WHERE i.transaction_date BETWEEN @from AND @to
              AND (@stores IS NULL OR i.store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)));
            """;
        await using var connection = await OpenAsync(cancellationToken);
        var raw = new List<(string Store, string Cro, decimal Sales, decimal Quantity, decimal Discount, int Transactions)>();
        await using (var command = ScopeCommand(connection, staffSql, scope))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                raw.Add((reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetInt32(5)));
        decimal canonical;
        await using (var command = ScopeCommand(connection, totalSql, scope))
            canonical = Convert.ToDecimal(await command.ExecuteScalarAsync(cancellationToken));
        var attributed = raw.Sum(x => x.Sales);
        var rows = raw.Select(x => new StaffPerformanceRow(x.Store, x.Cro, x.Sales, x.Quantity, x.Discount, x.Transactions,
            x.Transactions == 0 ? null : x.Quantity / x.Transactions,
            x.Transactions == 0 ? null : x.Sales / x.Transactions,
            attributed == 0 ? 0 : x.Sales / attributed * 100m)).ToArray();
        var variance = canonical - attributed;
        var status = variance == 0 ? ReconciliationStatus.Passed : ReconciliationStatus.Failed;
        return new(rows, canonical, attributed, variance, status,
            status == ReconciliationStatus.Passed
                ? "R013 attributed sales reconcile to canonical R025 sales."
                : "Attributed and canonical sales differ; review unmatched/unassigned source rows without rounding away the variance.",
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
    private sealed record ServiceFacts(decimal Cash = 0, decimal Card = 0, decimal Upi = 0, decimal LastYearTotal = 0, int CurrentCount = 0, int LastYearCount = 0);
}
