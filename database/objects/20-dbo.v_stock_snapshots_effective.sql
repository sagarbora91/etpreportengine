CREATE OR ALTER VIEW dbo.v_stock_snapshots_effective AS
-- One snapshot source per store-day: Closing Stock (R011) where the day has it, else BinWise (R010).
-- IF-032 (decision 66): Closing Stock counts the retail bin only, so a BinWise reading standing in for it counts its
-- RETAILBIN, read from the file's own landed row (dbo.etp_r010, same file, sheet and row as the fact's lineage), and
-- its cost is that quantity at the row's unit cost. A BinWise fact with no landed row keeps its stored (all-bins) figures.
SELECT s.stock_snapshot_id,s.store_code,s.snapshot_date,s.product_code,s.ean,s.brand_code,s.brand_name,s.cluster,s.gender,
       s.batch_number,s.source_uid,
       CONVERT(decimal(19,4),CASE WHEN r.landed=1 THEN COALESCE(r.retailbin,0) ELSE s.quantity END) quantity,
       s.unit_cost,
       CONVERT(decimal(19,4),CASE WHEN r.landed=1 THEN COALESCE(r.retailbin,0)*s.unit_cost ELSE s.total_cost END) total_cost,
       s.source_lineage_id,s.source_report_code,s.line_seq,
       s.item_discriminator
FROM dbo.stock_snapshots s
OUTER APPLY (SELECT TOP(1) CONVERT(bit,1) landed,e.retailbin
             FROM dbo.source_lineage ls
             JOIN dbo.source_lineage ll ON ll.import_file_id=ls.import_file_id AND ll.sheet_name=ls.sheet_name
                                       AND ll.source_row_number=ls.source_row_number
             JOIN dbo.etp_r010 e ON e.import_file_id=ll.import_file_id AND e.source_lineage_id=ll.source_lineage_id
             WHERE s.source_report_code='R010' AND ls.source_lineage_id=s.source_lineage_id
             ORDER BY e.etp_row_id) r
WHERE s.source_report_code='CLOSING_STOCK'
   OR (NOT EXISTS(SELECT 1 FROM dbo.stock_snapshots c WHERE c.store_code=s.store_code AND c.snapshot_date=s.snapshot_date
                  AND c.source_report_code='CLOSING_STOCK')
       AND (s.source_report_code='R010'
            OR NOT EXISTS(SELECT 1 FROM dbo.stock_snapshots b WHERE b.store_code=s.store_code AND b.snapshot_date=s.snapshot_date
                          AND b.source_report_code='R010')))
   OR (s.source_report_code='R010'
       AND EXISTS(SELECT 1 FROM dbo.stock_snapshot_r011_fallbacks k JOIN dbo.import_files f ON f.import_file_id=k.import_file_id
                  WHERE k.stock_snapshot_id=s.stock_snapshot_id AND f.is_superseded=0)
       AND NOT EXISTS(SELECT 1 FROM dbo.stock_snapshots c WHERE c.store_code=s.store_code AND c.snapshot_date=s.snapshot_date
                      AND c.source_report_code='CLOSING_STOCK' AND c.product_code=s.product_code
                      AND c.item_discriminator=s.item_discriminator))
-- >>> GRANTS
DENY DELETE ON dbo.v_stock_snapshots_effective TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_stock_snapshots_effective TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_stock_snapshots_effective TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_stock_snapshots_effective TO etp_owner,etp_store_manager,etp_viewer;
