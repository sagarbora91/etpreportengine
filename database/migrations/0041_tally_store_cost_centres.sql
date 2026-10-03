-- Phase 7 task 6, D12 = one Tally company for the firm with each store as a cost centre.
-- Each store linked to a Tally company may name the cost centre its vouchers carry, exactly as in Tally.
-- Additive: existing rows keep NULL, which means no cost centre.

ALTER TABLE dbo.tally_profile_stores ADD cost_centre nvarchar(100) NULL;

EXEC(N'ALTER TABLE dbo.tally_profile_stores ADD CONSTRAINT CK_tally_profile_stores_cost_centre
 CHECK(cost_centre IS NULL OR LEN(LTRIM(RTRIM(cost_centre)))>0);');
