CREATE TABLE dbo.QuantityCode
(
    QuantityCodeId          int IDENTITY(1,1)           NOT NULL,
    CONSTRAINT UQ_QuantityCode_QuantityCodeId UNIQUE (QuantityCodeId),
    Value                   nvarchar(256)               COLLATE Latin1_General_100_CS_AS    NOT NULL,
    CONSTRAINT PKC_QuantityCode PRIMARY KEY CLUSTERED (Value) WITH (DATA_COMPRESSION = PAGE)
)
SET IDENTITY_INSERT dbo.QuantityCode ON;

Insert INTO dbo.QuantityCode (QuantityCodeId, Value)
Values (0, '')

SET IDENTITY_INSERT dbo.QuantityCode OFF;
GO
