--DROP TYPE dbo.ReferenceSearchParamList
GO
CREATE TYPE dbo.ReferenceSearchParamList AS TABLE
(
    ResourceTypeId smallint NOT NULL
   ,ResourceOffset int NOT NULL
   ,SearchParamId smallint NOT NULL
   ,BaseUri varchar(128) COLLATE Latin1_General_100_CS_AS NULL
   ,ReferenceResourceTypeId smallint NULL
   ,ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,ReferenceResourceVersion int NULL

   UNIQUE (ResourceTypeId, ResourceOffset, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId) 
)
GO
