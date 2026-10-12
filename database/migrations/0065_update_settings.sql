-- 1.12 lane B1 (in-app "Update available", option C; plan section B, decisions 39 and 44 #1). Settings only.
--
-- product_settings.update_feed_path: the folder ETP reads releases from. NULL = the signed-in user's OneDrive\ETP-Updates
--    (resolved on the PC at run time, so it is never a stored per-user path). A value must be a non-blank full path; the
--    application refuses a relative one. Only Sagar publishes there; the signature on release.json is the protection, not
--    the folder (AUDIT-2 S-03).
-- product_settings.update_check_enabled: whether ETP checks the feed and offers "Install now" to the Owner. Default 0 (off).
--    It stays off until a real unattended Workpc upgrade has worked (design rule 9, decision 44 #1); the Owner turns it on
--    in Settings > Software update (wave 2).
--
-- product_settings is already Owner-only for writes (no role holds INSERT or UPDATE on it; 0022 and later revoke them),
-- so these columns need no grant and ExpectedRolePermissions.json does not change.
SET XACT_ABORT ON;

ALTER TABLE dbo.product_settings ADD update_feed_path nvarchar(400) NULL;

ALTER TABLE dbo.product_settings ADD update_check_enabled bit NOT NULL
    CONSTRAINT DF_product_settings_update_check_enabled DEFAULT(0);

-- A later statement references the new column, so it compiles in its own batch.
EXEC(N'ALTER TABLE dbo.product_settings ADD CONSTRAINT CK_product_settings_update_feed_path
    CHECK(update_feed_path IS NULL OR LEN(LTRIM(RTRIM(update_feed_path)))>0)');
