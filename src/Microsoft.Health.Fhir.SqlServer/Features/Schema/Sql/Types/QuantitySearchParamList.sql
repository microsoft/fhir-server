--DROP TYPE dbo.QuantitySearchParamList
GO
CREATE TYPE dbo.QuantitySearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,SystemId                 int      NULL
   ,QuantityCodeId           int      NULL
   ,SingleValue              decimal(18,6) NULL
   ,LowValue                 decimal(18,6) NULL
   ,HighValue                decimal(18,6) NULL

   UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue)
)
GO
