--DROP TYPE dbo.TokenTextList
GO
CREATE TYPE dbo.TokenTextList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,Text                     nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL

   --TODO: Code generates dups. Remove.
   --PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
)
GO
