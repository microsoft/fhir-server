--DROP TYPE dbo.UriSearchParamList
GO
CREATE TYPE dbo.UriSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,Uri                      varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL

   PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
)
GO
