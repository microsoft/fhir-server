--DROP TYPE dbo.NumberSearchParamList
GO
CREATE TYPE dbo.NumberSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,SingleValue              decimal(36,18) NULL
   ,LowValue                 decimal(36,18) NULL
   ,HighValue                decimal(36,18) NULL

   UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
)
GO
