SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.schema_migrations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.schema_migrations
    (
        migration_id varchar(100) NOT NULL CONSTRAINT PK_schema_migrations PRIMARY KEY,
        checksum char(64) NOT NULL,
        applied_utc datetime2(3) NOT NULL CONSTRAINT DF_schema_migrations_applied_utc DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.business_units', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.business_units
    (
        business_unit_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_business_units PRIMARY KEY,
        business_unit_code varchar(30) NOT NULL,
        business_unit_name nvarchar(200) NOT NULL,
        is_active bit NOT NULL CONSTRAINT DF_business_units_is_active DEFAULT (1),
        CONSTRAINT UQ_business_units_code UNIQUE (business_unit_code)
    );
END;

IF OBJECT_ID(N'dbo.stores', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.stores
    (
        store_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_stores PRIMARY KEY,
        store_code varchar(30) NOT NULL,
        store_name nvarchar(200) NOT NULL,
        business_unit_id int NULL,
        is_active bit NOT NULL CONSTRAINT DF_stores_is_active DEFAULT (1),
        CONSTRAINT UQ_stores_code UNIQUE (store_code),
        CONSTRAINT FK_stores_business_units FOREIGN KEY (business_unit_id) REFERENCES dbo.business_units(business_unit_id)
    );
END;

IF OBJECT_ID(N'dbo.import_profiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.import_profiles
    (
        import_profile_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_import_profiles PRIMARY KEY,
        report_code varchar(30) NOT NULL,
        layout_version varchar(50) NOT NULL,
        profile_version varchar(50) NOT NULL,
        header_signature_sha256 char(64) NOT NULL,
        is_active bit NOT NULL CONSTRAINT DF_import_profiles_is_active DEFAULT (1),
        CONSTRAINT UQ_import_profiles_version UNIQUE (report_code, layout_version, profile_version)
    );
END;

IF OBJECT_ID(N'dbo.import_batches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.import_batches
    (
        import_batch_id uniqueidentifier NOT NULL CONSTRAINT PK_import_batches PRIMARY KEY,
        status varchar(20) NOT NULL,
        store_id int NULL,
        period_start date NULL,
        period_end date NULL,
        started_utc datetime2(3) NOT NULL,
        completed_utc datetime2(3) NULL,
        CONSTRAINT CK_import_batches_status CHECK (status IN ('Pending','Processing','Completed','Failed')),
        CONSTRAINT FK_import_batches_stores FOREIGN KEY (store_id) REFERENCES dbo.stores(store_id)
    );
END;

IF OBJECT_ID(N'dbo.import_files', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.import_files
    (
        import_file_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_import_files PRIMARY KEY,
        import_batch_id uniqueidentifier NOT NULL,
        import_profile_id int NULL,
        original_file_name nvarchar(260) NOT NULL,
        source_sha256 char(64) NOT NULL,
        size_bytes bigint NOT NULL,
        source_row_count int NULL,
        CONSTRAINT CK_import_files_size CHECK (size_bytes >= 0),
        CONSTRAINT FK_import_files_batches FOREIGN KEY (import_batch_id) REFERENCES dbo.import_batches(import_batch_id),
        CONSTRAINT FK_import_files_profiles FOREIGN KEY (import_profile_id) REFERENCES dbo.import_profiles(import_profile_id)
    );
END;

COMMIT TRANSACTION;
