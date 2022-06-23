CREATE TYPE dbo.ReferenceSearchParamList AS TABLE
(
    ResourceTypeId                      smallint                NOT NULL,
    TransactionId               bigint              NOT NULL,
    ShardletId                  tinyint             NOT NULL,
    Sequence                    smallint            NOT NULL,
    SearchParamId                       smallint                NOT NULL,
    BaseUri                             varchar(128)            COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId             smallint                NULL,
    ReferenceResourceId                 varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion            int                     NULL,
    IsHistory                           bit                     NOT NULL
)
GO
