--DROP TYPE dbo.ReferenceTokenCompositeSearchParamList
GO
CREATE TYPE dbo.ReferenceTokenCompositeSearchParamList AS TABLE
(
    ResourceTypeId            smallint NOT NULL
   ,ResourceSurrogateId       bigint   NOT NULL
   ,SearchParamId             smallint NOT NULL
   ,BaseUri1                  varchar(128) COLLATE Latin1_General_100_CS_AS NULL
   ,ReferenceResourceTypeId1  smallint NULL
   ,ReferenceResourceId1      varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,ReferenceResourceVersion1 int      NULL
   ,SystemId2                 int      NULL
   ,Code2                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow2             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
)
GO
