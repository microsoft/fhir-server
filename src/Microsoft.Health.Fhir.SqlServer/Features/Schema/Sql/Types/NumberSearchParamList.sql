--DROP TYPE dbo.NumberSearchParamList
GO
CREATE TYPE dbo.NumberSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,SingleValue              decimal(18,6) NULL
   ,LowValue                 decimal(18,6) NULL
   ,HighValue                decimal(18,6) NULL

   UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue)
)
GO
