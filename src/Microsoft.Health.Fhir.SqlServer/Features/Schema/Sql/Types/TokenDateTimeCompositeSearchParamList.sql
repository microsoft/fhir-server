--DROP TYPE dbo.TokenDateTimeCompositeSearchParamList
GO
CREATE TYPE dbo.TokenDateTimeCompositeSearchParamList AS TABLE
(
    ResourceTypeId            smallint NOT NULL
   ,ResourceSurrogateId       bigint   NOT NULL
   ,SearchParamId             smallint NOT NULL
   ,SystemId1                 int      NULL
   ,Code1                     varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow1             varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,StartDateTime2            datetimeoffset(7) NOT NULL
   ,EndDateTime2              datetimeoffset(7) NOT NULL
   ,IsLongerThanADay2         bit      NOT NULL
)
GO
