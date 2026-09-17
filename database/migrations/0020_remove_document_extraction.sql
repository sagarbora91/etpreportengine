-- Phase 1 keeps retained originals, hashes and the document viewer only.
DROP TABLE IF EXISTS dbo.document_extractions;
ALTER TABLE dbo.product_settings DROP COLUMN ocr_helper_path, ocr_model_path;
UPDATE dbo.source_documents
SET lifecycle_status='RECEIVED', safe_message=N'Scanned document retained with its original hash.'
WHERE lifecycle_status='REVIEW_REQUIRED';
