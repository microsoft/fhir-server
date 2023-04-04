--DROP TYPE dbo.TokenSearchParamList
GO
CREATE TYPE dbo.TokenSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,SystemId                 int      NULL
   ,Code                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
)
GO
