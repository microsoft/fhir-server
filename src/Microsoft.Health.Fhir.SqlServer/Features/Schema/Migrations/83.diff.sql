ALTER PROCEDURE dbo.GetPartitionedTables @IncludeNotDisabled bit = 1, @IncludeNotSupported bit = 1
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'GetPartitionedTables'
       ,@Mode varchar(200) = 'PS=PartitionScheme_ResourceTypeId D='+isnull(convert(varchar,@IncludeNotDisabled),'NULL')+' S='+isnull(convert(varchar,@IncludeNotSupported),'NULL')
       ,@st datetime = getUTCdate()

DECLARE @NotSupportedTables TABLE (id int PRIMARY KEY)

BEGIN TRY
  INSERT INTO @NotSupportedTables
    SELECT DISTINCT O.object_id
      FROM sys.indexes I
           JOIN sys.objects O ON O.object_id = I.object_id
      WHERE O.type = 'u'
        AND EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
        -- table is supported if all indexes contain ResourceTypeId as key column and all indexes are partitioned on the same scheme
        AND (NOT EXISTS 
               (SELECT * 
                  FROM sys.index_columns IC JOIN sys.columns C ON C.object_id = IC.object_id AND C.column_id = IC.column_id 
                  WHERE IC.object_id = I.object_id
                    AND IC.index_id = I.index_id
                    AND IC.key_ordinal > 0
                    AND IC.is_included_column = 0 
                    AND C.name = 'ResourceTypeId'
               )
             OR 
             EXISTS 
               (SELECT * 
                  FROM sys.indexes NSI 
                  WHERE NSI.object_id = O.object_id 
                    AND NOT EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = NSI.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
               )
            )

  SELECT convert(varchar(100),O.name), convert(bit,CASE WHEN EXISTS (SELECT * FROM @NotSupportedTables NSI WHERE NSI.id = O.object_id) THEN 0 ELSE 1 END)
    FROM sys.indexes I
         JOIN sys.objects O ON O.object_id = I.object_id
    WHERE O.type = 'u'
      AND I.index_id IN (0,1)
      AND EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
      AND EXISTS (SELECT * FROM sys.index_columns IC JOIN sys.columns C ON C.object_id = I.object_id AND C.column_id = IC.column_id AND IC.is_included_column = 0 AND C.name = 'ResourceTypeId')
      AND (@IncludeNotSupported = 1 
           OR NOT EXISTS (SELECT * FROM @NotSupportedTables NSI WHERE NSI.id = O.object_id)
          )
      AND (@IncludeNotDisabled = 1 OR EXISTS (SELECT * FROM sys.indexes D WHERE D.object_id = O.object_id AND D.is_disabled = 1))
    ORDER BY 1

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.EnqueueJobs @QueueType tinyint, @Definitions StringList READONLY, @GroupId bigint = NULL, @ForceOneActiveJobGroup bit = 1, @IsCompleted bit = NULL, @Status tinyint = NULL, @Result varchar(max) = NULL, @StartDate datetime = NULL, @ReturnJobs bit = 1
-- TODO: Remove after deployment @IsCompleted
AS
set nocount on
DECLARE @SP varchar(100) = 'EnqueueJobs'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' D='+convert(varchar,(SELECT count(*) FROM @Definitions))
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
                           +' F='+isnull(convert(varchar,@ForceOneActiveJobGroup),'NULL')
                           +' S='+isnull(convert(varchar,@Status),'NULL')
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
            ,Result
            ,StartDate
            ,EndDate
        )
      OUTPUT inserted.JobId INTO @JobIds
      SELECT @QueueType
            ,GroupId = isnull(@GroupId,@MaxJobId+1)
            ,JobId
            ,Definition
            ,DefinitionHash
            ,Status = isnull(@Status,0)
            ,Result = CASE WHEN @Status = 2 THEN @Result ELSE NULL END
            ,StartDate = CASE WHEN @Status = 1 THEN getUTCdate() ELSE @StartDate END
            ,EndDate = CASE WHEN @Status = 2 THEN getUTCdate() ELSE NULL END
        FROM (SELECT JobId = @MaxJobId + row_number() OVER (ORDER BY Dummy), * FROM (SELECT *, Dummy = 0 FROM @Input) A) A -- preserve input order
        WHERE NOT EXISTS (SELECT * FROM dbo.JobQueue B WITH (INDEX = IX_QueueType_DefinitionHash) WHERE B.QueueType = @QueueType AND B.DefinitionHash = A.DefinitionHash AND B.Status <> 5)
    SET @Rows = @@rowcount

    COMMIT TRANSACTION
  END

  IF @ReturnJobs = 1
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
IF object_id('InitDefrag') IS NOT NULL DROP PROCEDURE dbo.InitDefrag
GO
CREATE OR ALTER PROCEDURE dbo.DefragGetFragmentation @TableName varchar(200), @IndexName varchar(200) = NULL, @PartitionNumber int = NULL
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@st datetime = getUTCdate()
       ,@msg varchar(1000)
       ,@Rows int
       ,@MinFragPct int = isnull((SELECT Number FROM dbo.Parameters WHERE Id = 'Defrag.MinFragPct'),10)
       ,@MinSizeGB float = isnull((SELECT Number FROM dbo.Parameters WHERE Id = 'Defrag.MinSizeGB'),0.1)
       ,@PreviousGroupId bigint
       ,@IndexId int

DECLARE @Mode varchar(200) = 'T='+@TableName+' I='+isnull(@IndexName,'NULL')+' P='+isnull(convert(varchar,@PartitionNumber),'NULL')+' MF='+convert(varchar,@MinFragPct)+' MS='+convert(varchar,@MinSizeGB)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  IF object_id(@TableName) IS NULL
    RAISERROR('Table does not exist',18,127)
  
  SET @IndexId = (SELECT index_id FROM sys.indexes WHERE object_id = object_id(@TableName) AND name = @IndexName)
  IF @IndexName IS NOT NULL AND @IndexId IS NULL
    RAISERROR('Index does not exist',18,127)

  -- find closest archived group id for this table
  SET @PreviousGroupId = (SELECT TOP 1 GroupId FROM dbo.JobQueue WHERE QueueType = 3 AND Status = 5 AND Definition = @TableName ORDER BY GroupId DESC)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='@PreviousGroupId',@Text=@PreviousGroupId

  SELECT TableName
        ,IndexName
        ,partition_number
        ,frag_in_percent
    FROM (SELECT TableName = @TableName
                ,IndexName = I.name
                ,partition_number
                ,frag_in_percent = avg_fragmentation_in_percent
                ,prev_frag_in_percent = isnull(convert(float, Result),0)
            FROM (SELECT object_id, index_id, partition_number, avg_fragmentation_in_percent
                    FROM sys.dm_db_index_physical_stats(db_id(), object_id(@TableName), @IndexId, @PartitionNumber, 'LIMITED') A
                    WHERE index_id > 0
                      AND (@PartitionNumber IS NOT NULL OR avg_fragmentation_in_percent >= @MinFragPct AND A.page_count > @MinSizeGB*1024*1024/8)
                 ) A
                 JOIN sys.indexes I ON I.object_id = A.object_id AND I.index_id = A.index_id
                 LEFT OUTER JOIN dbo.JobQueue ON QueueType = 3 AND Status = 5 AND GroupId = @PreviousGroupId AND Definition = I.name+';'+convert(varchar,partition_number)
         ) A
    WHERE @PartitionNumber IS NOT NULL OR frag_in_percent >= prev_frag_in_percent + @MinFragPct

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.Defrag @TableName varchar(100), @IndexName varchar(200), @PartitionNumber int, @IsPartitioned bit
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = @TableName+'.'+@IndexName+'.'+convert(varchar,@PartitionNumber)+'.'+convert(varchar,@IsPartitioned)
       ,@st datetime = getUTCdate()
       ,@SQL varchar(3500) 
       ,@msg varchar(1000)
       ,@SizeBefore float
       ,@SizeAfter float
       ,@IndexId int
       ,@Operation varchar(50) = CASE WHEN EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'Defrag.IndexRebuild.IsEnabled' AND Number = 1) THEN 'REBUILD' ELSE 'REORGANIZE' END    

SET @Mode = @Mode + ' ' + @Operation

BEGIN TRY
  SET @IndexId = (SELECT index_id FROM sys.indexes WHERE object_id = object_id(@TableName) AND name = @IndexName)
  SET @Sql = 'ALTER INDEX '+quotename(@IndexName)+' ON dbo.'+quotename(@TableName)+' '+@Operation
           + CASE WHEN @IsPartitioned = 1 THEN ' PARTITION = '+convert(varchar,@PartitionNumber) ELSE '' END
           + CASE 
               WHEN @Operation = 'REBUILD'
                 THEN ' WITH (ONLINE = ON'
                      + CASE WHEN EXISTS (SELECT * FROM sys.partitions WHERE object_id = object_id(@TableName) AND index_id = @IndexId AND data_compression_desc = 'PAGE') THEN ', DATA_COMPRESSION = PAGE' ELSE '' END
                      + ')'
               ELSE ''
             END
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Text=@Sql

  SET @SizeBefore = (SELECT sum(reserved_page_count) FROM sys.dm_db_partition_stats WHERE object_id = object_id(@TableName) AND index_id = @IndexId AND partition_number = @PartitionNumber) * 8.0 / 1024 / 1024
  SET @msg = 'Size[GB] before='+convert(varchar,@SizeBefore)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Text=@msg

  BEGIN TRY
    EXECUTE(@Sql)
    SET @SizeAfter = (SELECT sum(reserved_page_count) FROM sys.dm_db_partition_stats WHERE object_id = object_id(@TableName) AND index_id = @IndexId AND partition_number = @PartitionNumber) * 8.0 / 1024 / 1024
    SET @msg = 'Size[GB] before='+convert(varchar,@SizeBefore)+', after='+convert(varchar,@SizeAfter)+', reduced by='+convert(varchar,@SizeBefore-@SizeAfter)
    EXECUTE dbo.LogEvent @Process=@SP,@Status='End',@Mode=@Mode,@Action=@Operation,@Start=@st,@Text=@msg
  END TRY
  BEGIN CATCH
    EXECUTE dbo.LogEvent @Process=@SP,@Status='Error',@Mode=@Mode,@Action=@Operation,@Start=@st;
    THROW
  END CATCH
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
