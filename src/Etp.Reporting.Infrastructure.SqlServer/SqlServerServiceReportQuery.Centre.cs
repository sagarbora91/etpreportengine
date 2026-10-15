using Etp.Reporting.Application.Service;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// 1.12.1 P8: the per-centre statements for the parts invoices, the closing stock and the "Not matched to any centre" jobs.
/// P1's scoped query holds <c>serviceStoreCode</c>; <see cref="ScopedSql"/> swaps an all-centres statement for its centre twin
/// (same columns, same order) and <see cref="BindScope"/> adds <c>@centre</c> when the twin names it. Money (S004 against the
/// manual entries), goods in transit (two stock locations, not stores) and the file dates carry no Service Centre and are
/// never swapped. The twins read dbo.v_service_parts_centre and dbo.v_service_stock_summary_centre.
/// </summary>
public sealed partial class SqlServerServiceReportQuery
{
    /// <summary>True when this query shows the jobs, invoices and stock that match no Service Centre.</summary>
    internal bool ScopeIsNotMatched => ServiceCentreScope.IsNotMatched(serviceStoreCode);

    // The jobs of no centre: v_service_job.service_store_code is NULL when no prefix starts the number and there are several centres.
    internal const string CentreJobsNotMatchedSql = "SELECT " + JobColumns + " FROM dbo.v_service_job WHERE service_store_code IS NULL ORDER BY job_order_number;";

    internal const string PartsCentreSql = """
        SELECT p.invoice_number,p.invoice_date,p.item_id,p.shipped_quantity,p.received_quantity,p.net_amount,p.grn_number,p.grn_date,p.received_date,p.status,p.from_location,p.days_open,p.snapshot_date
        FROM dbo.v_service_parts p LEFT JOIN dbo.v_service_parts_centre c ON c.invoice_number=p.invoice_number AND c.item_id=p.item_id
        WHERE p.invoice_number IS NOT NULL AND c.service_store_code=@centre;
        """;

    internal const string PartsNotMatchedSql = """
        SELECT p.invoice_number,p.invoice_date,p.item_id,p.shipped_quantity,p.received_quantity,p.net_amount,p.grn_number,p.grn_date,p.received_date,p.status,p.from_location,p.days_open,p.snapshot_date
        FROM dbo.v_service_parts p LEFT JOIN dbo.v_service_parts_centre c ON c.invoice_number=p.invoice_number AND c.item_id=p.item_id
        WHERE p.invoice_number IS NOT NULL AND c.service_store_code IS NULL;
        """;

    internal const string StockCentreSql =
        "SELECT snapshot_date,items,quantity,value FROM dbo.v_service_stock_summary_centre WHERE service_store_code=@centre;";

    internal const string StockNotMatchedSql =
        "SELECT snapshot_date,items,quantity,value FROM dbo.v_service_stock_summary_centre WHERE service_store_code IS NULL;";

    /// <summary>The centre twins, for the text tests (views only, no privacy column).</summary>
    internal static IReadOnlyList<string> CentreStatements { get; } =
        [CentreJobsNotMatchedSql, PartsCentreSql, PartsNotMatchedSql, StockCentreSql, StockNotMatchedSql];

    /// <summary>
    /// The statement to run: <paramref name="sql"/> itself for an all-centres query or a statement that carries no centre,
    /// else its twin for the chosen centre or for "Not matched". Only the statements of the Jobs, Parts invoices and Stock
    /// reads are swapped (P1 already swaps the centre's job rows).
    /// </summary>
    internal string ScopedSql(string sql)
    {
        if (serviceStoreCode is null) return sql;
        var none = ScopeIsNotMatched;
        if (ReferenceEquals(sql, PartsSql) || sql == PartsSql) return none ? PartsNotMatchedSql : PartsCentreSql;
        if (ReferenceEquals(sql, StockSql) || sql == StockSql) return none ? StockNotMatchedSql : StockCentreSql;
        if (sql == CentreJobsSql && none) return CentreJobsNotMatchedSql;
        return sql;
    }

    /// <summary>Adds <c>@centre</c> to a command whose text names it and that has not bound it yet.</summary>
    internal void BindScope(SqlCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (serviceStoreCode is null || ScopeIsNotMatched) return;
        if (!command.CommandText.Contains("@centre", StringComparison.Ordinal) || command.Parameters.Contains("@centre")) return;
        command.Parameters.Add("@centre", System.Data.SqlDbType.VarChar, MaxStoreCodeLength).Value = serviceStoreCode;
    }
}
