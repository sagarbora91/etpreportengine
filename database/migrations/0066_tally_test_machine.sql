-- 1.12 wave-1 integration (review M-3, decision 53): sending straight to Tally is tied to the acceptance VM.
--
-- tally_profiles.test_machine_name: the Windows computer name of the one PC where ETP may send vouchers to this TEST
--    company's Tally over HTTP (the acceptance VM, set there by Sagar). ETP compares it with the name of the PC it runs on
--    before every send, and also requires port 9000 and Tally reporting Educational mode (decision 49). NULL (the default)
--    means sending is refused everywhere; reading back and the file steps are unaffected. Only a TEST company may name a PC.
--
-- tally_profiles is already Owner-only (0038 denies every other role), so this column needs no grant and
-- ExpectedRolePermissions.json does not change. THROW numbers: none.
SET XACT_ABORT ON;

ALTER TABLE dbo.tally_profiles ADD test_machine_name nvarchar(63) NULL;

-- Later statements reference the new column, so they compile in their own batches.
EXEC(N'ALTER TABLE dbo.tally_profiles ADD CONSTRAINT CK_tally_profiles_test_machine_name
    CHECK(test_machine_name IS NULL OR (LEN(test_machine_name) BETWEEN 1 AND 63 AND test_machine_name NOT LIKE N''%[^-A-Za-z0-9_.]%''))');

EXEC(N'ALTER TABLE dbo.tally_profiles ADD CONSTRAINT CK_tally_profiles_test_machine_environment
    CHECK(test_machine_name IS NULL OR environment=''TEST'')');
