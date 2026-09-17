SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Owner-approved D4 mapping, 17 September 2026. Replace the provisional 0024
-- rows for these two stores only; leave any other store's configuration intact.
DELETE FROM dbo.brand_row_codes WHERE store_code IN ('WLMHW', 'HEMW');
DELETE FROM dbo.brand_rows WHERE store_code IN ('WLMHW', 'HEMW');

INSERT dbo.brand_rows(store_code, row_label, sort_order) VALUES
 ('WLMHW', N'TITAN', 10),
 ('WLMHW', N'Raga', 20),
 ('WLMHW', N'EDGE', 30),
 ('WLMHW', N'SONATA', 40),
 ('WLMHW', N'Fastrack', 50),
 ('WLMHW', N'XYLYS', 60),
 ('HEMW', N'SEIKO', 10),
 ('HEMW', N'FOSSIL', 20),
 ('HEMW', N'TOMMY HILFIGER', 30),
 ('HEMW', N'CERUTI', 40),
 ('HEMW', N'KENNETH COLE', 50),
 ('HEMW', N'CITIZEN', 60),
 ('HEMW', N'POLICE', 70),
 ('HEMW', N'ANNE KLEIN', 80);

-- The reporting query matches code, then name, then cluster. Do not map the
-- HELIOS house-brand name: it would consume Seiko/Fossil/Citizen before their
-- cluster mappings can match. SPORT is likewise not a brand row.
INSERT dbo.brand_row_codes(store_code, source_brand, brand_row_id)
SELECT approved.store_code, approved.source_brand, rows.brand_row_id
FROM (VALUES
 ('WLMHW', N'TITAN', N'TITAN'),
 ('WLMHW', N'Raga', N'Raga'),
 ('WLMHW', N'EDGE', N'EDGE'),
 ('WLMHW', N'SONATA', N'SONATA'),
 ('WLMHW', N'Fastrack', N'FASTRACK WATCH'),
 ('WLMHW', N'Fastrack', N'FASTRACK WEARABLES'),
 ('WLMHW', N'XYLYS', N'XYLYS'),
 ('HEMW', N'SEIKO', N'SEKOG'),
 ('HEMW', N'FOSSIL', N'FOSLG'),
 ('HEMW', N'FOSSIL', N'FOSLL'),
 ('HEMW', N'TOMMY HILFIGER', N'TOMMY HILFIGER'),
 ('HEMW', N'CERUTI', N'CERUTI'),
 ('HEMW', N'KENNETH COLE', N'KENNETH COLE'),
 ('HEMW', N'CITIZEN', N'CTZNG'),
 ('HEMW', N'POLICE', N'POLICE'),
 ('HEMW', N'ANNE KLEIN', N'ANNE KLEIN')
) approved(store_code, row_label, source_brand)
JOIN dbo.brand_rows rows
  ON rows.store_code = approved.store_code AND rows.row_label = approved.row_label;

COMMIT TRANSACTION;
