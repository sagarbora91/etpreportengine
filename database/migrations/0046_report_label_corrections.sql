SET XACT_ABORT ON;

-- 1.9.4. Report audit of 3 October 2026, HEMW FIX-05 = WLMHW FIX-11 and FIX-12 (R-HEMW-05, R-HEMW-06,
-- R-WLMHW-11, R-WLMHW-12), with the owner's answers of 3 October (decision 13: Q2 gift cards, Q6 invoice counts),
-- amended in place before any 1.9.4 install (decision 17, D-L9-3). Wording only: the figures are changed by the
-- report code, not here.
--
-- Settings > Calculations reads dbo.kpi_catalogue, seeded by 0014 with text that no longer matches the reports:
--   * NET_SALES said SUM(R025.NETVALUE), "sourced from R025 NETVALUE". NETVALUE is the ex-GST amount;
--     every sales report sums R025 NETAMOUNT, the GST-inclusive value (decision D1). The Daily Sales Report VALUE
--     leaves gift-card lines out and shows them on their own GIFT CARD line (Q2); the definition says so.
--   * INVOICE_COUNT said "Distinct business documents in the selected scope", which describes counting every
--     document. Every screen now counts only documents with an INV line as "Invoices" (rule G5, Q6) and shows
--     sales returns (SR) and bill cancellations (BC) as a separate "Returns" count.
--
-- The catalogue is read-only to every application role (0022 onwards revokes INSERT, UPDATE and DELETE), so it
-- holds no Owner edits to keep. Each row changes only while it still differs from the corrected text, and its
-- version goes up by one when it does, so the change shows in Settings > Calculations; effective date,
-- approval status, approver and active flag are kept. Running this again changes nothing.

IF OBJECT_ID(N'dbo.kpi_catalogue',N'U') IS NOT NULL
BEGIN
  DECLARE @net_sales_definition nvarchar(1000)=N'Primary sales value including GST, with sales returns retaining their negative signs. The Daily Sales Report VALUE (and its VOL and INVOICE counts) leaves gift-card lines (item GIFT CARD or BRAND GC) out and shows them on their own GIFT CARD line; the other sales reports include them.';
  DECLARE @net_sales_formula nvarchar(1000)=N'SUM(R025.NETAMOUNT)';
  DECLARE @net_sales_source nvarchar(500)=N'Canonical sales lines from R025 NETAMOUNT (GST-inclusive)';
  DECLARE @invoice_definition nvarchar(1000)=N'Distinct INV documents (store + financial year + document number), shown as "Invoices" on every screen: the Daily Sales Report INVOICE row (and its AUPT, AVPT and conversion), Sales Summary, Management Trend, the Operations sales and control trend and Customer-wise. Sales returns (SR) and bill cancellations (BC) keep their value and quantity but are not counted as invoices; those screens show them as a separate "Returns" count of distinct SR or BC documents, so a cancelled bill (INV + BC) is 1 invoice and 1 return. On the Daily Sales Report a bill whose only INV lines are gift cards is not counted.';
  DECLARE @invoice_formula nvarchar(1000)=N'COUNT(DISTINCT store + financial year + document) over documents with an INV line';
  DECLARE @invoice_source nvarchar(500)=N'Canonical sales invoices and their R025 lines (transaction type INV)';

  UPDATE dbo.kpi_catalogue WITH(UPDLOCK,HOLDLOCK)
  SET definition=@net_sales_definition, formula=@net_sales_formula, data_source=@net_sales_source, version=version+1
  WHERE kpi_code='NET_SALES'
    AND (definition COLLATE Latin1_General_100_BIN2<>@net_sales_definition OR formula COLLATE Latin1_General_100_BIN2<>@net_sales_formula
      OR data_source COLLATE Latin1_General_100_BIN2<>@net_sales_source);

  UPDATE dbo.kpi_catalogue WITH(UPDLOCK,HOLDLOCK)
  SET definition=@invoice_definition, formula=@invoice_formula, data_source=@invoice_source, version=version+1
  WHERE kpi_code='INVOICE_COUNT'
    AND (definition COLLATE Latin1_General_100_BIN2<>@invoice_definition OR formula COLLATE Latin1_General_100_BIN2<>@invoice_formula
      OR data_source COLLATE Latin1_General_100_BIN2<>@invoice_source);
END;
