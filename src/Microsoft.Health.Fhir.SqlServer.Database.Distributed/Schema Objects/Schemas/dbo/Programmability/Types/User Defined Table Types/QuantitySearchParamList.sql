CREATE TYPE dbo.QuantitySearchParamList AS TABLE
(
    ResourceTypeId smallint NOT NULL,
    TransactionId               bigint              NOT NULL,
    ShardletId                  tinyint             NOT NULL,
    Sequence                    smallint            NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    QuantityCodeId int NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) NOT NULL,
    HighValue decimal(18,6) NOT NULL,
    IsHistory bit NOT NULL
)
GO
