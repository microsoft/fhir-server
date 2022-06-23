CREATE TYPE dbo.DateTimeSearchParamList AS TABLE
(
    ResourceTypeId smallint NOT NULL,
    TransactionId               bigint              NOT NULL,
    ShardletId                  tinyint             NOT NULL,
    Sequence                    smallint            NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime datetime2(7) NOT NULL,
    EndDateTime datetime2(7) NOT NULL,
    IsLongerThanADay bit NOT NULL,
    IsHistory bit NOT NULL,
    IsMin bit NOT NULL,
    IsMax bit NOT NULL
)
GO
