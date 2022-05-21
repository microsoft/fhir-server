IF NOT EXISTS (SELECT * FROM sys.table_types WHERE name = 'StringList') 
CREATE TYPE dbo.StringList AS TABLE
(
    String varchar(max)
)
GO
IF NOT EXISTS (SELECT * FROM sys.table_types WHERE name = 'BigintList') 
CREATE TYPE dbo.BigintList AS TABLE
(
    Id bigint NOT NULL PRIMARY KEY
)
GO
IF object_id('Parameters') IS NULL
CREATE TABLE dbo.Parameters 
  (
     Id          varchar(100)     NOT NULL
    ,Date        datetime         NULL
    ,Number      float            NULL
    ,Bigint      bigint           NULL
    ,Char        varchar(4000)    NULL
    ,Binary      varbinary(max)   NULL
    
    ,UpdatedDate datetime         NULL
    ,UpdatedBy   nvarchar(255)    NULL
    
     CONSTRAINT PKC_Parameters_Id PRIMARY KEY CLUSTERED (Id)
  )
GO
IF object_id('ParametersHistory') IS NULL
CREATE TABLE dbo.ParametersHistory 
  (
     ChangeId    int              NOT NULL IDENTITY(1,1)
    ,Id          varchar(100)     NOT NULL
    ,Date        datetime         NULL
    ,Number      float            NULL
    ,Bigint      bigint           NULL
    ,Char        varchar(4000)    NULL
    ,Binary      varbinary(max)   NULL
    ,UpdatedDate datetime         NULL
    ,UpdatedBy   nvarchar(255)    NULL
  )
GO
IF NOT EXISTS (SELECT * FROM sys.partition_functions WHERE name = 'TinyintPartitionFunction')
CREATE PARTITION FUNCTION TinyintPartitionFunction (tinyint) AS RANGE RIGHT FOR VALUES (0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114,115,116,117,118,119,120,121,122,123,124,125,126,127,128,129,130,131,132,133,134,135,136,137,138,139,140,141,142,143,144,145,146,147,148,149,150,151,152,153,154,155,156,157,158,159,160,161,162,163,164,165,166,167,168,169,170,171,172,173,174,175,176,177,178,179,180,181,182,183,184,185,186,187,188,189,190,191,192,193,194,195,196,197,198,199,200,201,202,203,204,205,206,207,208,209,210,211,212,213,214,215,216,217,218,219,220,221,222,223,224,225,226,227,228,229,230,231,232,233,234,235,236,237,238,239,240,241,242,243,244,245,246,247,248,249,250,251,252,253,254,255)
GO
IF NOT EXISTS (SELECT * FROM sys.partition_schemes WHERE name = 'TinyintPartitionScheme')
CREATE PARTITION SCHEME TinyintPartitionScheme AS PARTITION TinyintPartitionFunction ALL TO ([PRIMARY])
GO
IF object_id('JobQueue') IS NULL
CREATE TABLE dbo.JobQueue
(
     QueueType           tinyint       NOT NULL -- 1=export, 2=import, 3=whatever next
    ,GroupId             bigint        NOT NULL -- export id, import id
    ,JobId               bigint        NOT NULL
    ,PartitionId         AS convert(tinyint, JobId % 16) PERSISTED -- physical separation for performance
    ,Definition          varchar(max)  NOT NULL -- unique info identifying a job
    ,DefinitionHash      varbinary(20) NOT NULL -- to ensure idempotence
    ,Version             bigint        NOT NULL CONSTRAINT DF_JobQueue_Version DEFAULT datediff_big(millisecond,'0001-01-01',getUTCdate()) -- to prevent racing
    ,Status              tinyint       NOT NULL CONSTRAINT DF_JobQueue_Status DEFAULT 0 -- 0:created  1=running, 2=completed, 3=failed, 4=cancelled, 5=archived
    ,Priority            tinyint       NOT NULL CONSTRAINT DF_JobQueue_Priority DEFAULT 100 
    ,Data                bigint        NULL
    ,Result              varchar(max)  NULL
    ,CreateDate          datetime      NOT NULL CONSTRAINT DF_JobQueue_CreateDate DEFAULT getUTCdate() 
    ,StartDate           datetime      NULL
    ,EndDate             datetime      NULL 
    ,HeartbeatDate       datetime      NOT NULL CONSTRAINT DF_JobQueue_HeartbeatDate DEFAULT getUTCdate() 
    ,Worker              varchar(100)  NULL 
    ,Info                varchar(1000) NULL
    ,CancelRequested     bit           NOT NULL CONSTRAINT DF_JobQueue_CancelRequested DEFAULT 0

     CONSTRAINT PKC_JobQueue_QueueType_PartitionId_JobId PRIMARY KEY CLUSTERED (QueueType, PartitionId, JobId) ON TinyintPartitionScheme(QueueType)
)
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_QueueType_PartitionId_Status_Priority' AND object_id = object_id('JobQueue'))
CREATE INDEX IX_QueueType_PartitionId_Status_Priority ON dbo.JobQueue (PartitionId, Status, Priority) ON TinyintPartitionScheme(QueueType) -- dequeue
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_QueueType_GroupId' AND object_id = object_id('JobQueue'))
CREATE INDEX IX_QueueType_GroupId ON dbo.JobQueue (QueueType, GroupId) ON TinyintPartitionScheme(QueueType) -- wait for completion, delete
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_QueueType_DefinitionHash' AND object_id = object_id('JobQueue'))
CREATE INDEX IX_QueueType_DefinitionHash ON dbo.JobQueue (QueueType, DefinitionHash) ON TinyintPartitionScheme(QueueType) -- cannot express as unique constraint as I want to exclude archived
GO
IF NOT EXISTS (SELECT * FROM sys.partition_functions WHERE name = 'EventLogPartitionFunction')
CREATE PARTITION FUNCTION EventLogPartitionFunction (tinyint) AS RANGE RIGHT FOR VALUES (0,1,2,3,4,5,6,7)
GO
IF NOT EXISTS (SELECT * FROM sys.partition_schemes WHERE name = 'EventLogPartitionScheme')
CREATE PARTITION SCHEME EventLogPartitionScheme AS PARTITION EventLogPartitionFunction ALL TO ([PRIMARY])
GO
IF object_id('EventLog') IS NULL
CREATE TABLE dbo.EventLog
  (
     PartitionId   AS isnull(convert(tinyint, EventId % 8),0) PERSISTED
    ,EventId       bigint IDENTITY(1,1) NOT NULL
    ,EventDate     datetime             NOT NULL
    ,Process       varchar(100)         NOT NULL
    ,Status        varchar(10)          NOT NULL
    ,Mode          varchar(100)         NULL
    ,Action        varchar(20)          NULL
    ,Target        varchar(100)         NULL
    ,Rows          bigint               NULL
    ,Milliseconds  int                  NULL
    ,EventText     nvarchar(3500)       NULL
    ,ParentEventId bigint               NULL
    ,SPID          smallint             NOT NULL
    ,HostName      varchar(64)          NOT NULL
    ,TraceId       uniqueidentifier     NULL

     CONSTRAINT PKC_EventLog_EventDate_EventId_PartitionId PRIMARY KEY CLUSTERED (EventDate, EventId, PartitionId) ON EventLogPartitionScheme(PartitionId)
  ) 
GO
CREATE OR ALTER PROCEDURE dbo.LogEvent    
   @Process         varchar(100)
  ,@Status          varchar(10)
  ,@Mode            varchar(200)   = NULL    
  ,@Action          varchar(20)    = NULL    
  ,@Target          varchar(100)   = NULL    
  ,@Rows            bigint         = NULL    
  ,@Start           datetime       = NULL
  ,@Text            nvarchar(3500) = NULL
  ,@EventId         bigint         = NULL    OUTPUT
  ,@Retry           int            = NULL
AS
set nocount on
DECLARE @ErrorNumber  int           = error_number()
       ,@ErrorMessage varchar(1000) = ''
       ,@TranCount    int           = @@trancount
       ,@DoWork       bit           = 0
       ,@NumberAdded  bit

IF @ErrorNumber IS NOT NULL OR @Status IN ('Warn','Error')
  SET @DoWork = 1

IF @DoWork = 0
  SET @DoWork = CASE WHEN EXISTS (SELECT * FROM dbo.Parameters WHERE Id = isnull(@Process,'') AND Char = 'LogEvent') THEN 1 ELSE 0 END

IF @DoWork = 0
  RETURN

IF @ErrorNumber IS NOT NULL 
  SET @ErrorMessage = CASE WHEN @Retry IS NOT NULL THEN 'Retry '+convert(varchar,@Retry)+', ' ELSE '' END
                    + 'Error '+convert(varchar,error_number())+': '
                    + convert(varchar(1000), error_message())
                    + ', Level '+convert(varchar,error_severity())    
                    + ', State '+convert(varchar,error_state())    
                    + CASE WHEN error_procedure() IS NOT NULL THEN ', Procedure '+error_procedure() ELSE '' END   
                    + ', Line '+convert(varchar,error_line()) 

IF @TranCount > 0 AND @ErrorNumber IS NOT NULL ROLLBACK TRANSACTION

IF databasepropertyex(db_name(), 'UpdateAbility') = 'READ_WRITE'
BEGIN
  INSERT INTO dbo.EventLog    
      (    
           Process
          ,Status
          ,Mode
          ,Action
          ,Target
          ,Rows
          ,Milliseconds
          ,EventDate
          ,EventText
          ,SPID
          ,HostName
      )    
    SELECT @Process
          ,@Status
          ,@Mode
          ,@Action
          ,@Target
          ,@Rows
          ,datediff(millisecond,@Start,getUTCdate())
          ,EventDate = getUTCdate()
          ,Text = CASE 
                    WHEN @ErrorNumber IS NULL THEN @Text
                    ELSE @ErrorMessage + CASE WHEN isnull(@Text,'')<>'' THEN '. '+@Text ELSE '' END
                  END
          ,@@SPID
          ,HostName = host_name()
    
  SET @EventId = scope_identity()
END
    
-- Restore @@trancount
IF @TranCount > 0 AND @ErrorNumber IS NOT NULL BEGIN TRANSACTION
GO
CREATE OR ALTER PROCEDURE dbo.DequeueJob @QueueType tinyint, @StartPartitionId tinyint = NULL, @Worker varchar(100), @HeartbeatTimeoutSec int
AS
set nocount on
DECLARE @SP varchar(100) = 'DequeueJob'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' P='+isnull(convert(varchar,@StartPartitionId),'NULL')
                           +' H='+isnull(convert(varchar,@HeartbeatTimeoutSec),'NULL')
                           +' W='+isnull(@Worker,'NULL')
       ,@Rows int
       ,@st datetime = getUTCdate()
       ,@JobId bigint
       ,@msg varchar(100)
       ,@Lock varchar(100)
       ,@PartitionId tinyint = @StartPartitionId
       ,@MaxPartitions tinyint = 16 -- !!! hardcoded
       ,@LookedAtPartitions tinyint = 0

BEGIN TRY
  IF @PartitionId IS NULL
    SET @PartitionId = @MaxPartitions * rand()

  SET TRANSACTION ISOLATION LEVEL READ COMMITTED 

  WHILE @JobId IS NULL AND @LookedAtPartitions <= @MaxPartitions
  BEGIN
    SET @Lock = 'DequeueJob_'+convert(varchar,@QueueType)+'_'+convert(varchar,@PartitionId)

    BEGIN TRANSACTION  

    EXECUTE sp_getapplock @Lock, 'Exclusive'

    UPDATE T
      SET StartDate = getUTCdate()
         ,HeartbeatDate = getUTCdate()
         ,Worker = @Worker 
         ,Status = 1 -- running
         ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
         ,@JobId = T.JobId
      FROM dbo.JobQueue T WITH (PAGLOCK)
           JOIN (SELECT TOP 1 
                        JobId
                   FROM dbo.JobQueue WITH (INDEX = IX_QueueType_PartitionId_Status_Priority)
                   WHERE QueueType = @QueueType
                     AND PartitionId = @PartitionId
                     AND Status = 0
                   ORDER BY 
                        Priority
                       ,JobId
                ) S
             ON QueueType = @QueueType AND PartitionId = @PartitionId AND T.JobId = S.JobId
    SET @Rows = @@rowcount

    COMMIT TRANSACTION

    IF @JobId IS NULL
    BEGIN
      SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
      SET @LookedAtPartitions = @LookedAtPartitions + 1 
    END
  END

  -- Do timed out items. 
  SET @LookedAtPartitions = 0
  WHILE @JobId IS NULL AND @LookedAtPartitions <= @MaxPartitions
  BEGIN
    SET @Lock = 'DequeueStoreCopyWorkUnit_'+convert(varchar, @PartitionId)

    BEGIN TRANSACTION  

    EXECUTE sp_getapplock @Lock, 'Exclusive'

    UPDATE T
      SET StartDate = getUTCdate()
         ,HeartbeatDate = getUTCdate()
         ,Worker = @Worker 
         ,Status = CASE WHEN CancelRequested = 0 THEN 1 ELSE 4 END 
         ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
         ,@JobId = CASE WHEN CancelRequested = 0 THEN T.JobId END
         ,Info = isnull(Info,'')+' Prev: Worker='+Worker+' Start='+convert(varchar,StartDate,121)
      FROM dbo.JobQueue T WITH (PAGLOCK)
           JOIN (SELECT TOP 1 
                        JobId
                   FROM dbo.JobQueue WITH (INDEX = IX_QueueType_PartitionId_Status_Priority)
                   WHERE QueueType = @QueueType
                     AND PartitionId = @PartitionId
                     AND Status = 1
                     AND datediff(second,HeartbeatDate,getUTCdate()) > @HeartbeatTimeoutSec
                   ORDER BY 
                        Priority
                       ,JobId
                ) S
             ON QueueType = @QueueType AND PartitionId = @PartitionId AND T.JobId = S.JobId
    SET @Rows = @@rowcount

    COMMIT TRANSACTION

    IF @JobId IS NULL
    BEGIN
      SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
      SET @LookedAtPartitions = @LookedAtPartitions + 1 
    END
  END

  IF @JobId IS NOT NULL
    EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobId = @JobId
  
  SET @msg = 'J='+isnull(convert(varchar,@JobId),'NULL')+' P='+convert(varchar,@PartitionId)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
CREATE OR ALTER PROCEDURE dbo.EnqueueJobs @QueueType tinyint, @Definitions StringList READONLY, @GroupId bigint = NULL, @ForceOneActiveJobGroup bit, @IsCompleted bit = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'EnqueueJobs'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' D='+convert(varchar,(SELECT count(*) FROM @Definitions))
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
                           +' F='+isnull(convert(varchar,@ForceOneActiveJobGroup),'NULL')
                           +' C='+isnull(convert(varchar,@IsCompleted),'NULL')
       ,@st datetime = getUTCdate()
       ,@Lock varchar(100) = 'EnqueueJobs_'+convert(varchar,@QueueType)
       ,@MaxJobId bigint
       ,@Rows int
       ,@msg varchar(1000)
       ,@JobIds BigintList
       ,@InputRows int

BEGIN TRY
  DECLARE @Input TABLE (DefinitionHash varbinary(20) PRIMARY KEY, Definition varchar(max))
  INSERT INTO @Input SELECT DefinitionHash = hashbytes('SHA1',String), Definition = String FROM @Definitions
  SET @InputRows = @@rowcount

  INSERT INTO @JobIds
    SELECT JobId
      FROM @Input A
           JOIN dbo.JobQueue B ON B.QueueType = @QueueType AND B.DefinitionHash = A.DefinitionHash AND B.Status <> 5
  
  IF @@rowcount < @InputRows
  BEGIN
    BEGIN TRANSACTION  

    EXECUTE sp_getapplock @Lock, 'Exclusive'

    IF @ForceOneActiveJobGroup = 1 AND EXISTS (SELECT * FROM dbo.JobQueue WHERE QueueType = @QueueType AND Status IN (0,1) AND (@GroupId IS NULL OR GroupId <> @GroupId))
      RAISERROR('There are other active job groups',18,127)

    SET @MaxJobId = isnull((SELECT TOP 1 JobId FROM dbo.JobQueue WHERE QueueType = @QueueType ORDER BY JobId DESC),0)
  
    INSERT INTO dbo.JobQueue
        (
             QueueType
            ,GroupId
            ,JobId
            ,Definition
            ,DefinitionHash
            ,Status
        )
      OUTPUT inserted.JobId INTO @JobIds
      SELECT @QueueType
            ,GroupId = isnull(@GroupId,@MaxJobId+1)
            ,JobId
            ,Definition
            ,DefinitionHash
            ,Status = CASE WHEN @IsCompleted = 1 THEN 2 ELSE 0 END
        FROM (SELECT JobId = @MaxJobId + row_number() OVER (ORDER BY substring(Definition,1,1)), * FROM @Input) A
        WHERE NOT EXISTS (SELECT * FROM dbo.JobQueue B WHERE B.QueueType = @QueueType AND B.DefinitionHash = A.DefinitionHash AND B.Status <> 5)
    SET @Rows = @@rowcount

    COMMIT TRANSACTION
  END

  EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobIds = @JobIds

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
CREATE OR ALTER PROCEDURE dbo.GetJobs
   @QueueType        tinyint
  ,@JobId            bigint  = NULL
  ,@JobIds           BigintList READONLY
  ,@GroupId          bigint  = NULL
  ,@ReturnDefinition bit     = 1
AS
set nocount on
DECLARE @SP varchar(100) = 'GetJobs'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' J='+isnull(convert(varchar,@JobId),'NULL')
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
       ,@st datetime = getUTCdate()
       ,@PartitionId tinyint = @JobId % 16

BEGIN TRY
  IF @JobId IS NULL AND @GroupId IS NULL AND NOT EXISTS (SELECT * FROM @JobIds)
    RAISERROR('@JobId = NULL and @GroupId = NULL and @JobIds is empty',18,127)

  IF @JobId IS NOT NULL
    SELECT GroupId
          ,JobId
          ,Definition = CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END
          ,Version
          ,Status
          ,Priority
          ,Data
          ,Result
          ,CreateDate
          ,StartDate
          ,EndDate
          ,HeartbeatDate
          ,CancelRequested
      FROM dbo.JobQueue -- This can return only one item
      WHERE QueueType = @QueueType
        AND PartitionId = @PartitionId
        AND JobId = isnull(@JobId,-1)    
        AND Status <> 5 -- not archived
  ELSE 
    IF @GroupId IS NOT NULL 
      SELECT GroupId
            ,JobId
            ,Definition = CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END
            ,Version
            ,Status
            ,Priority
            ,Data
            ,Result
            ,CreateDate
            ,StartDate
            ,EndDate
            ,HeartbeatDate
            ,CancelRequested
        FROM dbo.JobQueue WITH (INDEX = IX_QueueType_GroupId) -- Force access by group id -- This can return more than one item
        WHERE QueueType = @QueueType
          AND GroupId = isnull(@GroupId,-1) 
          AND Status <> 5 -- not archived
    ELSE
      SELECT GroupId
            ,JobId
            ,Definition = CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END
            ,Version
            ,Status
            ,Priority
            ,Data
            ,Result
            ,CreateDate
            ,StartDate
            ,EndDate
            ,HeartbeatDate
            ,CancelRequested
      FROM dbo.JobQueue -- This can return only one item
        WHERE QueueType = @QueueType
          AND JobId IN (SELECT Id FROM @JobIds)
          AND PartitionId = JobId % 16
          AND Status <> 5 -- not archived

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
CREATE OR ALTER PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @GlobalStartId bigint = NULL, @GlobalEndId bigint = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourcesByTypeAndSurrogateIdRange'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
                           +' GS='+isnull(convert(varchar,@GlobalStartId),'NULL')
                           +' GE='+isnull(convert(varchar,@GlobalEndId),'NULL')
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

    SELECT isnull(C.RawResource, A.RawResource) 
      FROM dbo.Resource A
           LEFT OUTER JOIN @SurrogateIdMap B ON B.MinSurrogateId = A.ResourceSurrogateId
           LEFT OUTER JOIN dbo.Resource C ON C.ResourceTypeId = @ResourceTypeId AND C.ResourceSurrogateId = MaxSurrogateId
      WHERE A.ResourceTypeId = @ResourceTypeId 
        AND A.ResourceSurrogateId BETWEEN @StartId AND @EndId 
        AND (A.IsHistory = 0 OR MaxSurrogateId IS NOT NULL)
  END
  ELSE
    SELECT RawResource 
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
CREATE OR ALTER PROCEDURE dbo.GetResourceSurrogateIdRanges @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @UnitSize int, @NumberOfRanges int = 100
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourceSurrogateIdRanges'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
                           +' U='+isnull(convert(varchar,@UnitSize),'NULL')
       ,@st datetime = getUTCdate()
       ,@IntStartId bigint
       ,@IntEndId bigint

BEGIN TRY
  SELECT UnitId
        ,min(ResourceSurrogateId)
        ,max(ResourceSurrogateId)
        ,count(*)
    FROM (SELECT UnitId = isnull(convert(int, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @UnitSize), 0)
                ,ResourceSurrogateId
            FROM (SELECT TOP (@UnitSize * @NumberOfRanges)
                         ResourceSurrogateId
                    FROM dbo.Resource
                    WHERE ResourceTypeId = @ResourceTypeId
                      AND ResourceSurrogateId >= @StartId
                      AND ResourceSurrogateId < @EndId
                    ORDER BY
                         ResourceSurrogateId
                 ) A
         ) A
    GROUP BY
         UnitId
    ORDER BY
         UnitId
    OPTION (MAXDOP 1)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
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
CREATE OR ALTER PROCEDURE dbo.PutJobHeartbeat @QueueType tinyint, @JobId bigint, @Version bigint, @Data bigint = NULL, @CurrentResult varchar(max) = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'PutJobHeartbeat'
       ,@Mode varchar(100)
       ,@st datetime = getUTCdate()
       ,@Rows int = 0
       ,@PartitionId tinyint = @JobId % 16

SET @Mode = 'Q='+convert(varchar,@QueueType)+' J='+convert(varchar,@JobId)+' P='+convert(varchar,@PartitionId)+' V='+convert(varchar,@Version)+' D='+isnull(convert(varchar,@Data),'NULL')

BEGIN TRY
  IF @CurrentResult IS NULL
    UPDATE dbo.JobQueue
      SET HeartbeatDate = getUTCdate()
         ,Data = isnull(@Data,Data)
      WHERE QueueType = @QueueType
        AND PartitionId = @PartitionId
        AND JobId = @JobId
        AND Status = 1
        AND Version = @Version
  ELSE
    UPDATE dbo.JobQueue
      SET HeartbeatDate = getUTCdate()
         ,Data = isnull(@Data,Data)
         ,Result = @CurrentResult
      WHERE QueueType = @QueueType
        AND PartitionId = @PartitionId
        AND JobId = @JobId
        AND Status = 1
        AND Version = @Version
  
  SET @Rows = @@rowcount
  
  IF @Rows = 0
  BEGIN
    IF EXISTS (SELECT * FROM dbo.JobQueue WHERE QueueType = @QueueType AND PartitionId = @PartitionId AND JobId = @JobId)
      THROW 50412, 'Precondition failed', 1
    ELSE
      THROW 50404, 'Job record not found', 1
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
CREATE OR ALTER PROCEDURE dbo.PutJobStatus @QueueType tinyint, @JobId bigint, @Version bigint, @Failed bit, @Data bigint, @FinalResult varchar(max), @RequestCancellationOnFailure bit
AS
set nocount on
DECLARE @SP varchar(100) = 'PutJobStatus'
       ,@Mode varchar(100)
       ,@st datetime = getUTCdate()
       ,@Rows int = 0
       ,@PartitionId tinyint = @JobId % 16
       ,@GroupId bigint

SET @Mode = 'Q='+convert(varchar,@QueueType)+' J='+convert(varchar,@JobId)+' P='+convert(varchar,@PartitionId)+' V='+convert(varchar,@Version)+' F='+convert(varchar,@Failed)+' R='+isnull(@FinalResult,'NULL')

BEGIN TRY
  UPDATE dbo.JobQueue
    SET EndDate = getUTCdate()
       ,Status = CASE WHEN @Failed = 1 THEN 3 WHEN CancelRequested = 1 THEN 4 ELSE 2 END -- 2=completed 3=failed 4=cancelled
       ,Data = @Data
       ,Result = @FinalResult
       ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
       ,@GroupId = GroupId
    WHERE QueueType = @QueueType
      AND PartitionId = @PartitionId
      AND JobId = @JobId
      AND Status = 1
      AND Version = @Version
  SET @Rows = @@rowcount
  
  IF @Rows = 0
  BEGIN
    IF EXISTS (SELECT * FROM dbo.JobQueue WHERE QueueType = @QueueType AND PartitionId = @PartitionId AND JobId = @JobId)
      THROW 50412, 'Precondition failed', 1
    ELSE
      THROW 50404, 'Job record not found', 1
  END

  IF @Failed = 1 AND @RequestCancellationOnFailure = 1
    EXECUTE dbo.PutJobCancelation @QueueType = @QueueType, @GroupId = @GroupId

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
IF object_id('ExportJob') IS NOT NULL
  UPDATE dbo.ExportJob
    SET Status = 'Canceled'
    WHERE Status IN ('Queued','Running')
GO
