CREATE TYPE dbo.TokenTextList AS TABLE
(
    ResourceTypeId              smallint            NOT NULL,
    TransactionId               bigint              NOT NULL,
    ShardletId                  tinyint             NOT NULL,
    Sequence                    smallint            NOT NULL,
    SearchParamId               smallint            NOT NULL,
    Text                        nvarchar(400)       COLLATE Latin1_General_CI_AI NOT NULL,
    IsHistory                   bit                 NOT NULL
)
GO
