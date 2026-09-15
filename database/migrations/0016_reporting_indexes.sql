SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE INDEX IX_sales_invoices_date ON dbo.sales_invoices(transaction_date, store_code)
    INCLUDE(document_number, sales_invoice_id);
CREATE INDEX IX_stock_snapshots_product ON dbo.stock_snapshots(store_code, product_code, snapshot_date DESC);

EXEC(N'CREATE OR ALTER TRIGGER dbo.trg_application_users_history ON dbo.application_users
AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted WHERE role_code=''OWNER'' AND is_active=1)
       AND NOT EXISTS(SELECT 1 FROM dbo.application_users WITH(UPDLOCK,HOLDLOCK)
                      WHERE role_code=''OWNER'' AND is_active=1)
        THROW 51230, ''Keep at least one active Owner. Add another Owner before changing this account.'', 1;
    INSERT dbo.application_user_history(application_user_id,windows_identity,old_values_json,new_values_json,changed_by,change_reason)
    SELECT COALESCE(i.application_user_id,d.application_user_id),COALESCE(i.windows_identity,d.windows_identity),
        CASE WHEN d.application_user_id IS NULL THEN NULL ELSE (SELECT d.display_name displayName,d.role_code roleCode,d.is_active isActive FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) END,
        CASE WHEN i.application_user_id IS NULL THEN NULL ELSE (SELECT i.display_name displayName,i.role_code roleCode,i.is_active isActive FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) END,
        COALESCE(i.modified_by,d.modified_by),COALESCE(i.change_reason,d.change_reason)
    FROM inserted i FULL OUTER JOIN deleted d ON d.application_user_id=i.application_user_id;
END;');

COMMIT TRANSACTION;
