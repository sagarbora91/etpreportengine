namespace Etp.Reporting.Infrastructure.SqlServer.Audit;

/// <summary>
/// Every SQL text ImportAudit runs (design 6.4): constants only, each a single SELECT with no lock hint, no temporary
/// table and no procedure call. <see cref="ReadOnlySqlGuard"/> checks each before it runs, and
/// <c>ReadOnlySqlGuardTests</c> checks every constant here. Where a column arrives with a later migration, there are two
/// constant variants (before and after 0041) rather than text built at run time. Identities are batched as JSON and
/// joined through <c>OPENJSON</c>, as <c>check-import-upgrade.sql</c> does.
/// </summary>
public static class AuditQueries
{
    /// <summary>The schema facts the reader needs, the database's latest migration, and whether this login could write.</summary>
    public const string Schema = """
        SELECT CASE WHEN OBJECT_ID(N'dbo.schema_migrations',N'U') IS NULL THEN NULL ELSE (SELECT MAX(migration_id) FROM dbo.schema_migrations) END AS latest_migration,
               CASE WHEN OBJECT_ID(N'dbo.etp_import_content',N'U') IS NULL OR COL_LENGTH(N'dbo.import_files',N'data_truth_version') IS NULL THEN 0 ELSE 1 END AS supported,
               CASE WHEN COL_LENGTH(N'dbo.stock_movements',N'line_seq') IS NULL OR COL_LENGTH(N'dbo.stock_movements',N'from_key') IS NULL THEN 0 ELSE 1 END AS movement_line,
               CASE WHEN COL_LENGTH(N'dbo.stock_snapshots',N'source_report_code') IS NULL OR COL_LENGTH(N'dbo.stock_snapshots',N'line_seq') IS NULL
                      OR COL_LENGTH(N'dbo.stock_snapshots',N'item_discriminator') IS NULL THEN 0 ELSE 1 END AS snapshot_line,
               CASE WHEN CONVERT(sysname,DATABASEPROPERTYEX(DB_NAME(),'Collation')) LIKE N'%[_]CS[_]%'
                      OR CONVERT(sysname,DATABASEPROPERTYEX(DB_NAME(),'Collation')) LIKE N'%[_]CS'
                      OR CONVERT(sysname,DATABASEPROPERTYEX(DB_NAME(),'Collation')) LIKE N'%[_]BIN%' THEN 1 ELSE 0 END AS case_sensitive,
               COALESCE(HAS_PERMS_BY_NAME(N'dbo.import_files',N'OBJECT',N'INSERT'),0) AS login_could_write;
        """;

    /// <summary>
    /// The read-consistency mark (design 6.4.3): read before and after; any change means an import ran meanwhile.
    /// </summary>
    public const string StateMark = """
        SELECT (SELECT COUNT_BIG(*) FROM dbo.import_batches) AS batches,
               (SELECT COUNT_BIG(*) FROM dbo.import_batches WHERE status<>'Completed') AS open_batches,
               (SELECT MAX(import_file_id) FROM dbo.import_files) AS file_high_water;
        """;

    /// <summary>The active store codes, as the app's store catalogue gives them to scope detection.</summary>
    public const string ActiveStores = "SELECT store_code FROM dbo.stores WHERE is_active=1 ORDER BY store_id;";

    /// <summary>
    /// <c>PlanImportAsync</c>'s exact-duplicate SELECT, without its application lock: the same file already imported for
    /// this store, report and period.
    /// </summary>
    public const string ExactDuplicate = """
        SELECT TOP(1) import_file_id FROM dbo.import_files WHERE source_sha256=@hash AND data_truth_version=1
          AND report_code=@report AND (store_code=@store OR (store_code IS NULL AND @store IS NULL))
          AND (period_start=@start OR (period_start IS NULL AND @start IS NULL))
          AND (period_end=@end OR (period_end IS NULL AND @end IS NULL))
        ORDER BY import_file_id;
        """;

    /// <summary><c>PlanImportAsync</c>'s current-file SELECT, without its <c>UPDLOCK,HOLDLOCK</c> hints.</summary>
    public const string CurrentFiles = """
        SELECT f.import_file_id,f.source_sha256,f.period_start,f.period_end,b.started_utc,k.content_key,f.data_truth_version
        FROM dbo.import_files f
        JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
        LEFT JOIN dbo.etp_import_content k ON k.import_file_id=f.import_file_id
        WHERE f.store_code=@store AND f.report_code=@report AND f.is_superseded=0
          AND COALESCE(f.period_start,f.business_date)<=@end AND COALESCE(f.period_end,f.business_date)>=@start
        ORDER BY f.import_file_id;
        """;

    /// <summary>The finalised days a file's period touches: the import_files guard refuses the file (51021).</summary>
    public const string LockedDays = """
        SELECT business_date FROM dbo.daily_reporting_days
        WHERE store_code=@store AND status='LOCKED' AND business_date BETWEEN @start AND @end
        ORDER BY business_date;
        """;

    /// <summary>Invoices by financial year and number (<c>persist_sales_*</c> find their invoice the same way).</summary>
    public const string Invoices = """
        SELECT i.sales_invoice_id,i.invoice_year,i.document_number,i.transaction_date
        FROM OPENJSON(@keys) WITH (y int '$.y', d nvarchar(80) '$.d') k
        JOIN dbo.sales_invoices i ON i.store_code=@store AND i.invoice_year=k.y AND i.document_number=k.d;
        """;

    /// <summary>The sales lines of those invoices, with the values <c>persist_sales_line</c> protects.</summary>
    public const string SalesLines = """
        SELECT l.sales_invoice_id,l.line_identifier,l.product_code,l.source_transaction_type,l.source_quantity,l.source_gross_amount,
               l.source_net_amount,l.source_brand_code,l.source_brand_name,l.brand_segment,l.currency_code,l.sales_line_id
        FROM OPENJSON(@ids) WITH (id bigint '$') k
        JOIN dbo.sales_lines l ON l.sales_invoice_id=k.id;
        """;

    /// <summary>The invoice controls of those invoices (<c>persist_sales_invoice_control</c>).</summary>
    public const string InvoiceControls = """
        SELECT c.sales_invoice_id,c.source_transaction_type,c.source_invoice_quantity,c.source_net_value,c.currency_code,c.sales_invoice_control_id
        FROM OPENJSON(@ids) WITH (id bigint '$') k
        JOIN dbo.sales_invoice_controls c ON c.sales_invoice_id=k.id;
        """;

    /// <summary>The tenders of those invoices (<c>persist_sales_tender</c>).</summary>
    public const string Tenders = """
        SELECT t.sales_invoice_id,t.tender_type,t.source_amount,t.currency_code,t.is_reporting_eligible,t.exclusion_reason,t.sales_tender_id
        FROM OPENJSON(@ids) WITH (id bigint '$') k
        JOIN dbo.sales_tenders t ON t.sales_invoice_id=k.id;
        """;

    /// <summary>Stock movements of the given documents, after 0041 (<c>line_seq</c>, <c>from_key</c>, <c>to_key</c> stored).</summary>
    public const string Movements = """
        SELECT m.invoice_year,m.document_number,m.document_date,m.product_code,m.source_transaction_type,m.from_key,m.to_key,
               m.line_seq,m.opening_quantity,m.transaction_quantity,m.closing_quantity,m.stock_movement_id
        FROM OPENJSON(@keys) WITH (y int '$.y', d nvarchar(80) '$.d') k
        JOIN dbo.stock_movements m ON m.store_code=@store AND m.invoice_year=k.y AND m.document_number=k.d;
        """;

    /// <summary>
    /// Stock movements before 0041: locations read as 0041's <c>from_key</c>/<c>to_key</c>, and <c>line_seq</c> numbered as
    /// 0041's backfill numbers it (running-balance order, the id standing in for file order).
    /// </summary>
    public const string MovementsBefore0041 = """
        SELECT x.invoice_year,x.document_number,x.document_date,x.product_code,x.source_transaction_type,x.from_key,x.to_key,
               x.line_seq,x.opening_quantity,x.transaction_quantity,x.closing_quantity,x.stock_movement_id
        FROM (
          SELECT m.invoice_year,m.document_number,m.document_date,m.product_code,m.source_transaction_type,
                 ISNULL(m.from_location,N'') AS from_key,ISNULL(m.to_location,N'') AS to_key,
                 CONVERT(int,ROW_NUMBER() OVER(PARTITION BY m.store_code,m.invoice_year,m.document_number,m.document_date,m.product_code,
                     m.source_transaction_type,ISNULL(m.from_location,N''),ISNULL(m.to_location,N'')
                   ORDER BY CASE WHEN m.transaction_quantity<0 THEN -m.opening_quantity ELSE m.opening_quantity END,
                            CASE WHEN m.transaction_quantity<0 THEN -m.closing_quantity ELSE m.closing_quantity END,
                            m.opening_quantity,m.transaction_quantity,m.closing_quantity,m.stock_movement_id)) AS line_seq,
                 m.opening_quantity,m.transaction_quantity,m.closing_quantity,m.stock_movement_id
          FROM OPENJSON(@keys) WITH (y int '$.y', d nvarchar(80) '$.d') k
          JOIN dbo.stock_movements m ON m.store_code=@store AND m.invoice_year=k.y AND m.document_number=k.d
        ) x;
        """;

    /// <summary>Stock snapshots of the given dates, after 0041 (source, line and item discriminator stored).</summary>
    public const string Snapshots = """
        SELECT s.snapshot_date,s.source_report_code,s.product_code,s.item_discriminator,s.line_seq,s.ean,s.brand_code,s.brand_name,
               s.cluster,s.gender,s.batch_number,s.source_uid,s.quantity,s.unit_cost,s.total_cost,s.stock_snapshot_id
        FROM OPENJSON(@dates) WITH (t date '$') k
        JOIN dbo.stock_snapshots s ON s.store_code=@store AND s.snapshot_date=k.t;
        """;

    /// <summary>
    /// Stock snapshots before 0041: the source derived from the lineage record type and <c>line_seq</c> numbered as 0041's
    /// backfill does. The R011 rows 0041 rebuilds from logged outcomes are not emulated (the report says so).
    /// </summary>
    public const string SnapshotsBefore0041 = """
        SELECT x.snapshot_date,x.source_report_code,x.product_code,x.item_discriminator,x.line_seq,x.ean,x.brand_code,x.brand_name,
               x.cluster,x.gender,x.batch_number,x.source_uid,x.quantity,x.unit_cost,x.total_cost,x.stock_snapshot_id
        FROM (
          SELECT y.*,CONVERT(int,ROW_NUMBER() OVER(PARTITION BY y.store_code,y.snapshot_date,y.source_report_code,y.product_code,y.item_discriminator
                   ORDER BY y.quantity,y.unit_cost,y.total_cost,y.stock_snapshot_id)) AS line_seq
          FROM (
            SELECT s.store_code,s.snapshot_date,
                   CONVERT(varchar(30),CASE WHEN l.source_record_type='R010_SNAPSHOT' THEN 'R010' ELSE 'CLOSING_STOCK' END) AS source_report_code,
                   s.product_code,COALESCE(s.source_uid,s.batch_number,s.ean,N'') AS item_discriminator,s.ean,s.brand_code,s.brand_name,
                   s.cluster,s.gender,s.batch_number,s.source_uid,s.quantity,s.unit_cost,s.total_cost,s.stock_snapshot_id
            FROM OPENJSON(@dates) WITH (t date '$') k
            JOIN dbo.stock_snapshots s ON s.store_code=@store AND s.snapshot_date=k.t
            JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id
          ) y
        ) x;
        """;

    /// <summary>Enrichment content keys already held (<c>persist_phase_one_enrichment</c> reports those ALREADY_PRESENT).</summary>
    public const string EnrichmentKeys = """
        SELECT e.content_key
        FROM OPENJSON(@keys) WITH (k varchar(80) '$') x
        JOIN dbo.sales_line_enrichments e ON e.enrichment_type=@type AND e.store_code=@store AND e.content_key=x.k;
        """;

    /// <summary>Every constant above, for the guard test.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Schema, StateMark, ActiveStores, ExactDuplicate, CurrentFiles, LockedDays, Invoices, SalesLines, InvoiceControls, Tenders,
        Movements, MovementsBefore0041, Snapshots, SnapshotsBefore0041, EnrichmentKeys
    ];
}
