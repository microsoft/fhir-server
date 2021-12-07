CREATE TABLE dbo.QuantityCode
(
    QuantityCodeId          int IDENTITY(1,1)           NOT NULL,
    CONSTRAINT UQ_QuantityCode_QuantityCodeId UNIQUE (QuantityCodeId),
    Value                   nvarchar(256)               COLLATE Latin1_General_100_CS_AS    NOT NULL,
    CONSTRAINT PK_QuantityCode PRIMARY KEY CLUSTERED (Value)
)
