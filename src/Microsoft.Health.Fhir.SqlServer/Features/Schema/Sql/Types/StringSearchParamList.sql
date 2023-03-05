--DROP TYPE dbo.TokenTextList
GO
CREATE TYPE dbo.StringSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,Text                     nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL
   ,TextOverflow             nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL
   ,IsMin                    bit      NOT NULL
   ,IsMax                    bit      NOT NULL
)
GO
