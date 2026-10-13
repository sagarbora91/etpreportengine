CREATE OR ALTER PROCEDURE diag.load_role_permissions
WITH EXECUTE AS N'diag_catalog_reader'
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @is_owner bit=0,@owners_without int=-1;
 DECLARE @app_users TABLE(windows_identity nvarchar(200) NOT NULL,role_code varchar(30) NOT NULL,is_active bit NOT NULL,
   application_user_id int NOT NULL,login_sid varbinary(85) NULL);
 DECLARE @owners TABLE(member_name sysname NOT NULL);
 INSERT @app_users(windows_identity,role_code,is_active,application_user_id)
 SELECT windows_identity,role_code,is_active,application_user_id FROM dbo.application_users;

 -- The caller's own context: its role, and the server-scope check it is allowed to see.
 EXECUTE AS CALLER;
 SET @is_owner=CASE WHEN COALESCE(IS_ROLEMEMBER('etp_owner'),0)=1 OR COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)=1 THEN 1 ELSE 0 END;
 UPDATE @app_users SET login_sid=SUSER_SID(windows_identity);
 IF COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)=1
    OR COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,'CONTROL SERVER'),0)=1
    OR COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,'VIEW ANY DEFINITION'),0)=1
    OR COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,'ALTER ANY LOGIN'),0)=1
 BEGIN
  -- New-EtpOwnersWithoutGrantOptionSql (scripts\etp-operations-common.ps1), with the names kept.
  INSERT @owners(member_name)
  SELECT sp.name FROM sys.server_principals sp
  WHERE sp.type='U'
    AND sp.sid NOT IN(SID_BINARY(N'S-1-5-18'),SID_BINARY(N'S-1-5-19'),SID_BINARY(N'S-1-5-20'))
    AND SUBSTRING(sp.sid,3,10)<>SUBSTRING(SID_BINARY(N'S-1-5-80-0'),3,10)
    AND sp.principal_id IN(SELECT SUSER_ID(u.windows_identity) FROM @app_users u WHERE u.role_code='OWNER' AND u.is_active=1)
    AND NOT EXISTS(SELECT 1 FROM sys.server_permissions p WHERE p.class=100 AND p.grantee_principal_id=sp.principal_id
                   AND p.permission_name=N'ALTER ANY LOGIN' AND p.state='W');
  SET @owners_without=(SELECT COUNT(*) FROM @owners);
 END;
 REVERT;

 -- 1. Permissions of the four etp roles (grants to public and to single users are not listed).
 SELECT r.name role_name,p.class_desc,
   CASE p.class WHEN 1 THEN OBJECT_SCHEMA_NAME(p.major_id) WHEN 3 THEN SCHEMA_NAME(p.major_id)
     WHEN 6 THEN (SELECT SCHEMA_NAME(t.schema_id) FROM sys.types t WHERE t.user_type_id=p.major_id) END schema_name,
   CASE p.class
     WHEN 1 THEN OBJECT_NAME(p.major_id)+CASE WHEN p.minor_id<>0 THEN N'('+COL_NAME(p.major_id,p.minor_id)+N')' ELSE N'' END
     WHEN 6 THEN TYPE_NAME(p.major_id)
     WHEN 24 THEN (SELECT k.name FROM sys.symmetric_keys k WHERE k.symmetric_key_id=p.major_id)
     WHEN 25 THEN (SELECT c.name FROM sys.certificates c WHERE c.certificate_id=p.major_id)
     WHEN 26 THEN (SELECT k.name FROM sys.asymmetric_keys k WHERE k.asymmetric_key_id=p.major_id) END object_name,
   p.permission_name,p.state_desc
 FROM sys.database_permissions p
 JOIN sys.database_principals r ON r.principal_id=p.grantee_principal_id
 WHERE r.type='R' AND r.name IN(N'etp_owner',N'etp_store_manager',N'etp_viewer',N'etp_automation')
 ORDER BY role_name,p.class_desc,schema_name,object_name,p.permission_name,p.state_desc;

 -- 2. Members of the four etp roles, with the application user each one maps to (by name, else by SID).
 DECLARE @members TABLE(role_name sysname NOT NULL,member_name sysname NOT NULL,is_active_application_user bit NOT NULL,
   application_role_code varchar(30) NULL);
 INSERT @members(role_name,member_name,is_active_application_user,application_role_code)
 SELECT r.name,m.name,CONVERT(bit,COALESCE(u.is_active,0)),u.role_code
 FROM sys.database_role_members rm
 JOIN sys.database_principals r ON r.principal_id=rm.role_principal_id
 JOIN sys.database_principals m ON m.principal_id=rm.member_principal_id
 OUTER APPLY(SELECT TOP(1) a.is_active,a.role_code FROM @app_users a
   WHERE a.windows_identity=m.name OR (m.sid IS NOT NULL AND a.login_sid=m.sid)
   ORDER BY CASE WHEN a.windows_identity=m.name THEN 0 ELSE 1 END,a.is_active DESC,a.application_user_id) u
 WHERE r.type='R' AND r.name IN(N'etp_owner',N'etp_store_manager',N'etp_viewer',N'etp_automation');
 IF @is_owner=1
  SELECT role_name,member_name,is_active_application_user,application_role_code,1 member_count
  FROM @members ORDER BY role_name,member_name;
 ELSE
  SELECT role_name,CONVERT(sysname,NULL) member_name,is_active_application_user,application_role_code,COUNT(*) member_count
  FROM @members GROUP BY role_name,is_active_application_user,application_role_code
  ORDER BY role_name,is_active_application_user,application_role_code;

 -- 3. Active Owners without the grant option.
 IF @is_owner=1 AND @owners_without>0
  SELECT member_name,@owners_without owners_without_grant_option FROM @owners ORDER BY member_name;
 ELSE
  SELECT CONVERT(sysname,NULL) member_name,@owners_without owners_without_grant_option;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.load_role_permissions TO etp_automation,etp_owner;
