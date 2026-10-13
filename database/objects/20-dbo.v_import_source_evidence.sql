CREATE OR ALTER VIEW dbo.v_import_source_evidence AS
SELECT source_sha256,size_bytes,first_import_file_id,retained_utc FROM dbo.import_source_content
-- >>> GRANTS
GRANT SELECT ON dbo.v_import_source_evidence TO etp_owner,etp_store_manager,etp_viewer;
