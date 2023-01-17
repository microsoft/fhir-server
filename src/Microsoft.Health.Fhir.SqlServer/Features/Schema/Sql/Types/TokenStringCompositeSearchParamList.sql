--DROP TYPE dbo.TokenStringCompositeSearchParamList
GO
CREATE TYPE dbo.TokenStringCompositeSearchParamList AS TABLE
(
    ResourceTypeId            smallint NOT NULL
   ,ResourceSurrogateId       bigint   NOT NULL
   ,SearchParamId             smallint NOT NULL
   ,SystemId1                 int      NULL
   ,Code1                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow1             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,Text2                     nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL
   ,TextOverflow2             nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL
)
GO
