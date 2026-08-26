SET XACT_ABORT ON;
BEGIN TRANSACTION;

INSERT dbo.manual_input_definitions(field_code,display_name,value_kind,is_required_for_finalisation,applies_to)
VALUES
 ('SERVICE_CASH',N'Service cash','Money',0,'Service'),
 ('SERVICE_CARD',N'Service card','Money',0,'Service'),
 ('SERVICE_UPI',N'Service UPI','Money',0,'Service'),
 ('CASH_ADJUSTMENT',N'Cash adjustment','Money',0,'Cash'),
 ('CLOSING_CASH_COUNTED',N'Counted closing cash','Money',0,'Cash');

COMMIT TRANSACTION;
