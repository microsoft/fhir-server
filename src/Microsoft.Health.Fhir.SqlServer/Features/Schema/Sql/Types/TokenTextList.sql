--DROP TYPE dbo.TokenTextList
GO
CREATE TYPE dbo.TokenTextList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,Text                     nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL

   --In C# we use LowerInvariant() to mimic case insensitivity, but it does not deal with asterisk insensitivity. Cannot create constraint.
   --PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
)
GO
