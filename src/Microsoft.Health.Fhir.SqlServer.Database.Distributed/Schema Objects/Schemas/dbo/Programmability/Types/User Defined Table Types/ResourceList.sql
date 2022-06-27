CREATE TYPE dbo.ResourceList AS TABLE
(
    ResourceTypeId     smallint            NOT NULL
   ,ResourceId         varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version            int                 NOT NULL
   ,IsHistory          bit                 NOT NULL
   ,TransactionId      bigint              NOT NULL
   ,ShardletId         tinyint             NOT NULL
   ,Sequence           smallint            NOT NULL
   ,IsDeleted          bit                 NOT NULL
   ,RequestMethod      varchar(10)         NULL
   ,SearchParamHash    varchar(64)         NULL

    PRIMARY KEY (ResourceTypeId, TransactionId, ShardletId, Sequence)
)
GO
