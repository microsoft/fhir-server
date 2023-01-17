--DROP TYPE dbo.DateTimeSearchParamList
GO
CREATE TYPE dbo.DateTimeSearchParamList AS TABLE
(
    ResourceTypeId           smallint NOT NULL
   ,ResourceSurrogateId      bigint   NOT NULL
   ,SearchParamId            smallint NOT NULL
   ,StartDateTime            datetimeoffset(7) NOT NULL
   ,EndDateTime              datetimeoffset(7) NOT NULL
   ,IsLongerThanADay         bit      NOT NULL
   ,IsMin                    bit      NOT NULL
   ,IsMax                    bit      NOT NULL

   --UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax)
)
GO
