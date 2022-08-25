--DROP PROCEDURE dbo.BeginEnvironmentUpdate
GO
CREATE PROCEDURE dbo.BeginEnvironmentUpdate 
  @FromVersion int -- this is -1 when a new deployment
 ,@Shards      ShardList READONLY
 ,@ToVersion   int OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = convert(varchar(100), 'V='+isnull(convert(varchar,@FromVersion),'NULL')+' SH='+convert(varchar,(SELECT count(*) FROM @Shards)))
       ,@NumberOfShards int = (SELECT count(*) FROM @Shards)
       ,@NumberOfShardlets smallint = 256

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Begin'

BEGIN TRY
  IF 0 = @NumberOfShards
    RAISERROR('Invalid @Shards param', 18, 127)
  IF @FromVersion != isnull((SELECT max(Version) FROM dbo.Shards WHERE IsActive = 1), -1)
    RAISERROR('Obsolete @FromVersion or concurrent environment update transaction', 18, 127)

  SET @ToVersion = (SELECT isnull(1+max(Version), 0) FROM dbo.Shards WHERE IsActive = 1)

  DECLARE @ShardCount int = 0
  WHILE @ShardCount < @NumberOfShards
  BEGIN
    IF (SELECT count(*) FROM @Shards WHERE ShardId = @ShardCount) != 1
      RAISERROR('ShardIds need to be zero-based and sequential', 18, 127)
    SET @ShardCount += 1;
  END;

  SELECT TOP (@NumberOfShardlets) 
         ShardletId = row_number() OVER (ORDER BY A1.column_id) - 1 
    INTO #Shardlets
    FROM sys.columns A1 CROSS JOIN sys.columns A2

  -- if @ToVersion is already present we are retrying a failed reshard
  IF NOT EXISTS (SELECT * FROM dbo.Shards WHERE Version = @ToVersion)
  BEGIN
    BEGIN TRANSACTION
    
    INSERT INTO dbo.Shards (Version, ShardId, IsActive, SqlServer, SqlDatabase)
      SELECT @ToVersion, ShardId, 0, SqlServer, SqlDatabase
        FROM @Shards
    
    INSERT INTO dbo.Shardlets (Version, ShardletId, ShardId)
      SELECT @ToVersion
            ,ShardletId
            ,ShardId = CASE WHEN @NumberOfShards > 1 THEN ShardletId % @NumberOfShards ELSE 0 END
        FROM #Shardlets

    COMMIT TRANSACTION
  END

  -- return shardlet id's to be relocated - if not a new deployment where @FromVersion = -1
  SELECT Shardid, ShardletId
    FROM dbo.Shardlets
    WHERE @FromVersion >= 0
      AND Version = @ToVersion

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End'
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
