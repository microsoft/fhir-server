--DROP TYPE dbo.TokenQuantityCompositeSearchParamList
GO
CREATE TYPE dbo.TokenQuantityCompositeSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,SystemId1                int      NULL
   ,Code1                    varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow1            varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,SystemId2                int      NULL
   ,QuantityCodeId2          int      NULL
   ,SingleValue2             decimal(18,6) NULL
   ,LowValue2                decimal(18,6) NULL
   ,HighValue2               decimal(18,6) NULL
)
GO
