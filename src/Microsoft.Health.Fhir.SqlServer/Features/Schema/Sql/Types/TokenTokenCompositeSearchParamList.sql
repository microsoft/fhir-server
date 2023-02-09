--DROP TYPE dbo.TokenTokenCompositeSearchParamList
GO
CREATE TYPE dbo.TokenTokenCompositeSearchParamList AS TABLE
(
    ResourceTypeId            smallint NOT NULL
   ,ResourceSurrogateId       bigint   NOT NULL
   ,SearchParamId             smallint NOT NULL
   ,SystemId1                 int      NULL
   ,Code1                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow1             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,SystemId2                 int      NULL
   ,Code2                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow2             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
)
GO
