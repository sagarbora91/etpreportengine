SET XACT_ABORT ON;

-- 1.9.4. Report audit of 3 October 2026, HEMW FIX-05 = WLMHW FIX-11 and the text half of FIX-12
-- (R-HEMW-05, R-WLMHW-11, R-WLMHW-12). Wording only: no figure, rule or count changes here.
--
-- Settings > Calculations reads dbo.kpi_catalogue, seeded by 0014 with text that no longer matches the reports:
--   * NET_SALES said SUM(R025.NETVALUE), "sourced from R025 NETVALUE". NETVALUE is the ex-GST amount;
--     every sales report sums R025 NETAMOUNT, the GST-inclusive value (decision D1).
--   * INVOICE_COUNT said "Distinct business documents in the selected scope", which describes counting every
--     document. The Daily Sales Report INVOICE row (and its AUPT, AVPT and conversion) counts only documents
--     with an INV line (rule G5, accepted behaviour K3). The Sales Summary and Management Trend count every
--     document, including sales returns (SR) and bill cancellations (BC); the app now labels those columns
--     "Documents (incl. returns)". Whether every screen should count the same way waits for Sagar (Q6).
--
-- The catalogue is read-only to every application role (0022 onwards revokes INSERT, UPDATE and DELETE), so it
-- holds no Owner edits to keep. Each row changes only while it still differs from the corrected text, and its
-- version goes up by one when it does, so the change shows in Settings > Calculations; effective date,
-- approval status, approver and active flag are kept. Running this again changes nothing.

IF OBJECT_ID(N'dbo.kpi_catalogue',N'U') IS NOT NULL
BEGIN
  DECLARE @net_sales_formula nvarchar(1000)=N'SUM(R025.NETAMOUNT)';
  DECLARE @net_sales_source nvarchar(500)=N'Canonical sales lines from R025 NETAMOUNT (GST-inclusive)';
  DECLARE @invoice_definition nvarchar(1000)=N'Distinct INV documents (store + financial year + document number), as counted by the Daily Sales Report INVOICE row and used for its AUPT, AVPT and conversion. Sales returns (SR) and bill cancellations (BC) keep their value and quantity but are not counted; a cancelled bill (INV + BC) counts as 1. The Sales Summary and Management Trend "Documents (incl. returns)" columns count every document, including SR and BC.';
  DECLARE @invoice_formula nvarchar(1000)=N'COUNT(DISTINCT store + financial year + document) over documents with an INV line';
  DECLARE @invoice_source nvarchar(500)=N'Canonical sales invoices and their R025 lines (transaction type INV)';

  UPDATE dbo.kpi_catalogue WITH(UPDLOCK,HOLDLOCK)
  SET formula=@net_sales_formula, data_source=@net_sales_source, version=version+1
  WHERE kpi_code='NET_SALES'
    AND (formula COLLATE Latin1_General_100_BIN2<>@net_sales_formula OR data_source COLLATE Latin1_General_100_BIN2<>@net_sales_source);

  UPDATE dbo.kpi_catalogue WITH(UPDLOCK,HOLDLOCK)
  SET definition=@invoice_definition, formula=@invoice_formula, data_source=@invoice_source, version=version+1
  WHERE kpi_code='INVOICE_COUNT'
    AND (definition COLLATE Latin1_General_100_BIN2<>@invoice_definition OR formula COLLATE Latin1_General_100_BIN2<>@invoice_formula
      OR data_source COLLATE Latin1_General_100_BIN2<>@invoice_source);
END;
