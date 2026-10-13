CREATE OR ALTER PROCEDURE dbo.save_evening_brand
 @requested int,@store varchar(30),@label nvarchar(100),@order int,@codes nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)<>1 AND COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51420,'Owner or Store Manager permission is required.',1;
 IF NULLIF(LTRIM(RTRIM(@store)),'') IS NULL OR NULLIF(LTRIM(RTRIM(@label)),'') IS NULL
   OR @label IN(N'Other / unmapped',N'VOL',N'VALUE',N'AUPT',N'AVPT',N'RETAIL WALKIN',N'INVOICE',N'CONVERSION %',N'WCC WALKIN',N'WCC SALES',N'WDC BILLS')
  THROW 51426,'Enter a store and a non-reserved brand label.',1;
 IF ISJSON(@codes)<>1 OR LEFT(LTRIM(@codes),1)<>N'[' OR EXISTS(SELECT 1 FROM OPENJSON(@codes) WHERE type<>1 OR LEN(value)>100 OR LEN(LTRIM(RTRIM(value)))=0)
  THROW 51426,'Enter valid source brand codes.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @id int=@requested;
  IF @id=0 BEGIN
   INSERT dbo.brand_rows(store_code,row_label,sort_order,modified_by) VALUES(@store,@label,@order,SUSER_SNAME());
   SET @id=SCOPE_IDENTITY();
  END ELSE BEGIN
   UPDATE dbo.brand_rows SET row_label=@label,sort_order=@order,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME()
   WHERE brand_row_id=@id AND store_code=@store;
   IF @@ROWCOUNT<>1 THROW 51401,'The brand row no longer exists in this store.',1;
  END;
  IF EXISTS(SELECT 1 FROM OPENJSON(@codes) j JOIN dbo.brand_row_codes b WITH(UPDLOCK,HOLDLOCK)
    ON b.store_code=@store AND b.source_brand=j.value WHERE b.brand_row_id<>@id)
   THROW 51402,'This source brand is already assigned. Remove it from the other row first.',1;
  DELETE dbo.brand_row_codes WHERE brand_row_id=@id;
  INSERT dbo.brand_row_codes(store_code,source_brand,brand_row_id) SELECT DISTINCT @store,CONVERT(nvarchar(100),value),@id FROM OPENJSON(@codes);
  EXEC dbo.record_operational_audit 'MasterDataChange','Succeeded',N'Evening brand mapping changed',N'database';
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.save_evening_brand TO etp_owner,etp_store_manager;
