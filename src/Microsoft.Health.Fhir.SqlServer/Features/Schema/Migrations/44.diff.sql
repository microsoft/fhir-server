--DROP PROCEDURE dbo.GetResourceSurrogateIdRanges
GO
CREATE OR ALTER PROCEDURE dbo.GetResourceSurrogateIdRanges @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @RangeSize int, @NumberOfRanges int = 100, @Up bit = 1
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourceSurrogateIdRanges'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
                           +' R='+isnull(convert(varchar,@RangeSize),'NULL')
                           +' UP='+isnull(convert(varchar,@Up),'NULL')
       ,@st datetime = getUTCdate()

BEGIN TRY
  IF @Up = 1
    SELECT RangeId
          ,min(ResourceSurrogateId)
          ,max(ResourceSurrogateId)
          ,count(*)
      FROM (SELECT RangeId = isnull(convert(int, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @RangeSize), 0)
                  ,ResourceSurrogateId
              FROM (SELECT TOP (@RangeSize * @NumberOfRanges)
                           ResourceSurrogateId
                      FROM dbo.Resource
                      WHERE ResourceTypeId = @ResourceTypeId
                        AND ResourceSurrogateId >= @StartId
                        AND ResourceSurrogateId <= @EndId
                      ORDER BY
                           ResourceSurrogateId
                   ) A
           ) A
      GROUP BY
           RangeId
      OPTION (MAXDOP 1)
  ELSE
    SELECT RangeId
          ,min(ResourceSurrogateId)
          ,max(ResourceSurrogateId)
          ,count(*)
      FROM (SELECT RangeId = isnull(convert(int, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @RangeSize), 0)
                  ,ResourceSurrogateId
              FROM (SELECT TOP (@RangeSize * @NumberOfRanges)
                           ResourceSurrogateId
                      FROM dbo.Resource
                      WHERE ResourceTypeId = @ResourceTypeId
                        AND ResourceSurrogateId >= @StartId
                        AND ResourceSurrogateId <= @EndId
                      ORDER BY
                           ResourceSurrogateId DESC
                   ) A
           ) A
      GROUP BY
           RangeId
      OPTION (MAXDOP 1)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 
THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--set nocount on
--DECLARE @Ranges TABLE (UnitId int, MinId bigint, MaxId bigint, Cnt int)
--DECLARE @MaxId bigint = 0
--       ,@msg varchar(1000)
--       ,@Loop int = 0

--WHILE @MaxId IS NOT NULL AND @Loop < 1000
--BEGIN
--  DELETE FROM @Ranges
--  INSERT INTO @Ranges
--    EXECUTE GetResourceSurrogateIdRanges 96, @MaxId, 9e18, 100000
--  SET @MaxId = (SELECT TOP 1 MaxId FROM @Ranges ORDER BY MaxId DESC) + 1
--  SET @msg = 'Loop='+convert(varchar,@Loop)+' MaxId='+convert(varchar,@MaxId)
--  RAISERROR(@msg,0,1) WITH NOWAIT
--  SET @Loop += 1
--END
--DROP PROCEDURE dbo.PutJobCancelation
GO
CREATE OR ALTER PROCEDURE dbo.PutJobCancelation @QueueType tinyint, @GroupId bigint = NULL, @JobId bigint = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'PutJobCancelation'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
                           +' J='+isnull(convert(varchar,@JobId),'NULL')
       ,@st datetime = getUTCdate()
       ,@Rows int
       ,@PartitionId tinyint = @JobId % 16

BEGIN TRY
  IF @JobId IS NULL AND @GroupId IS NULL
    RAISERROR('@JobId = NULL and @GroupId = NULL',18,127)

  IF @JobId IS NOT NULL
  BEGIN
    UPDATE dbo.JobQueue
      SET Status = 4 -- cancelled
         ,EndDate = getUTCdate()
         ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
      WHERE QueueType = @QueueType
        AND PartitionId = @PartitionId
        AND JobId = @JobId
        AND Status = 0
    SET @Rows = @@rowcount

    IF @Rows = 0
    BEGIN
      UPDATE dbo.JobQueue
        SET CancelRequested = 1 -- It is upto job logic to determine what to do 
        WHERE QueueType = @QueueType
          AND PartitionId = @PartitionId
          AND JobId = @JobId
          AND Status = 1
      SET @Rows = @@rowcount
    END
  END
  ELSE 
  BEGIN
    UPDATE dbo.JobQueue
      SET Status = 4 -- cancelled 
         ,EndDate = getUTCdate()
         ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
      WHERE QueueType = @QueueType
        AND GroupId = @GroupId
        AND Status = 0
    SET @Rows = @@rowcount

    UPDATE dbo.JobQueue
      SET CancelRequested = 1 -- It is upto job logic to determine what to do
      WHERE QueueType = @QueueType
        AND GroupId = @GroupId
        AND Status = 1
    SET @Rows += @@rowcount
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DROP PROCEDURE dbo.GetUsedResourceTypes
GO
CREATE OR ALTER PROCEDURE dbo.GetUsedResourceTypes
AS
set nocount on
DECLARE @SP varchar(100) = 'GetUsedResourceTypes'
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()

BEGIN TRY
  SELECT ResourceTypeId, Name
    FROM dbo.ResourceType A
    WHERE EXISTS (SELECT * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DROP PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange
GO
CREATE OR ALTER PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @GlobalStartId bigint = NULL, @GlobalEndId bigint = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourcesByTypeAndSurrogateIdRange'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
                           +' GS='+isnull(convert(varchar,@GlobalStartId),'NULL') -- Is global start id needed? I'm not seeing a usecase for setting it.
                           +' GE='+isnull(convert(varchar,@GlobalEndId),'NULL') -- Could this just be a boolean for if historical records should be returned? GlobalEndId should equal EndId in all cases I can think of.
       ,@st datetime = getUTCdate()

BEGIN TRY
  DECLARE @ResourceIds TABLE (ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS, ResourceSurrogateId bigint, RowId int, PRIMARY KEY (ResourceId, RowId))

  IF @GlobalStartId IS NULL -- export from time zero (no lower boundary)
    SET @GlobalStartId = 0

  IF @GlobalEndId IS NOT NULL -- snapshot view
    INSERT INTO @ResourceIds
      SELECT ResourceId, ResourceSurrogateId, RowId = row_number() OVER (PARTITION BY ResourceId ORDER BY ResourceSurrogateId)
        FROM dbo.Resource 
        WHERE ResourceTypeId = @ResourceTypeId
          AND ResourceId IN (SELECT DISTINCT ResourceId
                               FROM dbo.Resource 
                               WHERE ResourceTypeId = @ResourceTypeId 
                                 AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
                                 AND IsHistory = 1
                            )
          AND ResourceSurrogateId BETWEEN @GlobalStartId AND @GlobalEndId
   
  IF EXISTS (SELECT * FROM @ResourceIds)
  BEGIN
    DECLARE @SurrogateIdMap TABLE (MinSurrogateId bigint, MaxSurrogateId bigint)
    INSERT INTO @SurrogateIdMap
      SELECT MinSurrogateId = A.ResourceSurrogateId
            ,MaxSurrogateId = C.ResourceSurrogateId
        FROM (SELECT * FROM @ResourceIds WHERE RowId = 1 AND ResourceSurrogateId BETWEEN @StartId AND @EndId) A
             CROSS APPLY (SELECT ResourceSurrogateId FROM @ResourceIds B WHERE B.ResourceId = A.ResourceId) C

    SELECT @ResourceTypeId
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.ResourceId ELSE A.ResourceId END
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.Version ELSE A.Version END
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.IsDeleted ELSE A.IsDeleted END
          ,isnull(C.ResourceSurrogateId, A.ResourceSurrogateId)
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.RequestMethod ELSE A.RequestMethod END
          ,IsMatch = convert(bit,1)
          ,IsPartial = convert(bit,0)
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.IsRawResourceMetaSet ELSE A.IsRawResourceMetaSet END
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.SearchParamHash ELSE A.SearchParamHash END
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.RawResource ELSE A.RawResource END
      FROM dbo.Resource A
           LEFT OUTER JOIN @SurrogateIdMap B ON B.MinSurrogateId = A.ResourceSurrogateId
           LEFT OUTER JOIN dbo.Resource C ON C.ResourceTypeId = @ResourceTypeId AND C.ResourceSurrogateId = MaxSurrogateId
      WHERE A.ResourceTypeId = @ResourceTypeId 
        AND A.ResourceSurrogateId BETWEEN @StartId AND @EndId 
        AND (A.IsHistory = 0 OR MaxSurrogateId IS NOT NULL)
  END
  ELSE
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource 
      FROM dbo.Resource 
      WHERE ResourceTypeId = @ResourceTypeId 
        AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
        AND IsHistory = 0 
        AND IsDeleted = 0

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--set nocount on
--DECLARE @Ranges TABLE (UnitId int PRIMARY KEY, MinId bigint, MaxId bigint, Cnt int)
--INSERT INTO @Ranges
--  EXECUTE dbo.GetResourceSurrogateIdRanges 96, 0, 9e18, 90000, 10
--SELECT count(*) FROM @Ranges
--DECLARE @UnitId int
--       ,@MinId bigint
--       ,@MaxId bigint
--DECLARE @Resources TABLE (RawResource varbinary(max))
--WHILE EXISTS (SELECT * FROM @Ranges)
--BEGIN
--  SELECT TOP 1 @UnitId = UnitId, @MinId = MinId, @MaxId = MaxId FROM @Ranges ORDER BY UnitId
--  INSERT INTO @Resources
--    EXECUTE dbo.GetResourcesByTypeAndSurrogateIdRange 96, @MinId, @MaxId, NULL, @MaxId -- last is to invoke snapshot logic 
--  DELETE FROM @Resources
--  DELETE FROM @Ranges WHERE UnitId = @UnitId
--END
