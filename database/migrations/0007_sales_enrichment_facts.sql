SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE dbo.sales_line_enrichments
(
    sales_line_enrichment_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_sales_line_enrichments PRIMARY KEY,
    enrichment_type varchar(10) NOT NULL,
    store_code varchar(30) NOT NULL,
    transaction_date date NOT NULL,
    document_number nvarchar(80) NOT NULL,
    product_code nvarchar(80) NOT NULL,
    source_transaction_type nvarchar(80) NOT NULL,
    source_quantity decimal(19,4) NOT NULL,
    source_net_value decimal(19,4) NOT NULL,
    source_cro_number nvarchar(80) NULL,
    scheme_discount decimal(19,4) NULL,
    user_discount decimal(19,4) NULL,
    pre_discount decimal(19,4) NULL,
    other_charges decimal(19,4) NULL,
    activation_details nvarchar(500) NULL,
    user_discount_details nvarchar(500) NULL,
    matched_sales_line_id bigint NULL,
    match_status varchar(20) NOT NULL,
    source_lineage_id bigint NOT NULL,
    CONSTRAINT CK_sales_line_enrichments_type CHECK(enrichment_type IN ('R003','R013')),
    CONSTRAINT CK_sales_line_enrichments_match CHECK(match_status IN ('Matched','Missing','Ambiguous')),
    CONSTRAINT FK_sales_line_enrichments_sales_line FOREIGN KEY(matched_sales_line_id) REFERENCES dbo.sales_lines(sales_line_id),
    CONSTRAINT FK_sales_line_enrichments_lineage FOREIGN KEY(source_lineage_id) REFERENCES dbo.source_lineage(source_lineage_id),
    CONSTRAINT UQ_sales_line_enrichments_lineage UNIQUE(source_lineage_id)
);
CREATE INDEX IX_sales_line_enrichments_scope ON dbo.sales_line_enrichments(transaction_date,store_code,enrichment_type,source_cro_number)
 INCLUDE(source_quantity,source_net_value,scheme_discount,user_discount,pre_discount,match_status);

COMMIT TRANSACTION;
