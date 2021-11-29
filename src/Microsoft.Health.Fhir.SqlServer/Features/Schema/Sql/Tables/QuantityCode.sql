CREATE TABLE dbo.QuantityCode
(
    QuantityCodeId          int IDENTITY(1,1)           NOT NULL,
    Value                   nvarchar(256)               COLLATE Latin1_General_100_CS_AS    NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_QuantityCode on dbo.QuantityCode
(
    Value
)
