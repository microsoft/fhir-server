--DROP TYPE dbo.ReferenceSearchParamList
GO
CREATE TYPE dbo.ReferenceSearchParamList AS TABLE
(
    ResourceTypeId           tinyint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,BaseUri                  varchar(128) COLLATE Latin1_General_100_CS_AS NULL
   ,ReferenceResourceTypeId  tinyint NULL
   ,ReferenceResourceId      varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,ReferenceResourceVersion int      NULL

   UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId) 
)
GO
