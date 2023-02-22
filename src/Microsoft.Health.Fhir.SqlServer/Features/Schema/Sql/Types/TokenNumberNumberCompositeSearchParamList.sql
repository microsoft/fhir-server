--DROP TYPE dbo.TokenNumberNumberCompositeSearchParamList
GO
CREATE TYPE dbo.TokenNumberNumberCompositeSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,SystemId1 int NULL
   ,Code1 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow1 varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,SingleValue2 decimal(18,6) NULL
   ,LowValue2 decimal(18,6) NULL
   ,HighValue2 decimal(18,6) NULL
   ,SingleValue3 decimal(18,6) NULL
   ,LowValue3 decimal(18,6) NULL
   ,HighValue3 decimal(18,6) NULL
   ,HasRange bit NOT NULL
)
GO
