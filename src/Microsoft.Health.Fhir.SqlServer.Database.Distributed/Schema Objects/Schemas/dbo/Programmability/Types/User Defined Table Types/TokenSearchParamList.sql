CREATE TYPE dbo.TokenSearchParamList AS TABLE
(
    ResourceTypeId              smallint                NOT NULL,
    TransactionId               bigint              NOT NULL,
    ShardletId                  tinyint             NOT NULL,
    Sequence                    smallint            NOT NULL,
    SearchParamId               smallint                NOT NULL,
    SystemId                    int                     NULL,
    Code                        varchar(128)            COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory                   bit                     NOT NULL
)
GO
