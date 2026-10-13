CREATE OR ALTER VIEW dbo.v_stock_snapshots_effective AS
SELECT s.stock_snapshot_id,s.store_code,s.snapshot_date,s.product_code,s.ean,s.brand_code,s.brand_name,s.cluster,s.gender,
       s.batch_number,s.source_uid,s.quantity,s.unit_cost,s.total_cost,s.source_lineage_id,s.source_report_code,s.line_seq,
       s.item_discriminator
FROM dbo.stock_snapshots s
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
