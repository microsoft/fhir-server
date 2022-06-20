--DROP PROCEDURE dbo.BeginEnvironmentUpdate
GO
CREATE PROCEDURE dbo.BeginEnvironmentUpdate 
  @InstanceId  tinyint
 ,@FromVersion int -- this is -1 when a new deployment
 ,@Shards      ShardList READONLY
 ,@ToVersion   int OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = convert(varchar(100), 'I='+isnull(convert(varchar(10),@InstanceId),'NULL')+' V=['+isnull(convert(varchar(10),@FromVersion),'NULL')+']')
       ,@NumberOfShards int = (SELECT count(*) FROM @Shards)
       ,@NumberOfShardlets smallint = 2048
       ,@MinShardletId smallint
       ,@MaxShardletId smallint

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Begin'

BEGIN TRY
  IF 0 = @NumberOfShards
    RAISERROR('Invalid @Shards param', 18, 127)
  IF @FromVersion != isnull((SELECT max(Version) FROM dbo.Shards WHERE IsActive = 1), -1)
    RAISERROR('Obsolete @FromVersion or concurrent environment update transaction', 18, 127)

  SET @ToVersion = (SELECT isnull(1+max(Version), 0) FROM dbo.Shards WHERE IsActive = 1)

  DECLARE @CommaIdx   int
         ,@ShardCount int = 0
  WHILE @ShardCount < @NumberOfShards
  BEGIN
    SELECT @CommaIdx = charindex(',', SqlServer) FROM @Shards WHERE ShardId = @ShardCount
    IF @@rowcount != 1
      RAISERROR('ShardIds need to be zero-based and sequential', 18, 127)
    IF isnull(@CommaIdx, 0) = 0
      RAISERROR('@ServerName param not in canonical format', 18, 127)
    SET @ShardCount += 1;
  END;

  IF @InstanceId = 0 -- OW: 1024 shardlets
  BEGIN
    SET @MinShardletId = 1024
    SET @MaxShardletId = @NumberOfShardlets - 1
  END
  ELSE IF @InstanceId = 1 -- Next: lower than 1024 128 shardlets
  BEGIN
    SET @MinShardletId = 1024 - 128
    SET @MaxShardletId = @NumberOfShardlets - 1024 - 1
  END

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
            ,CASE WHEN ShardletId BETWEEN @MinShardletId AND @MaxShardletId THEN 1 ELSE 0 END
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
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
END CATCH
GO

/*
select * from dbo.Shards;
select count(*) from dbo.Shardlets;

IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'ShardsUpdDel' AND parent_id = object_id('Shards'))
  DISABLE TRIGGER ShardsUpdDel ON dbo.Shards;
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'ShardletsUpdDel' AND parent_id = object_id('Shardlets'))
  DISABLE TRIGGER ShardletsUpdDel ON dbo.Shardlets;
delete dbo.Shardlets;
delete dbo.Shards;
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'ShardsUpdDel' AND parent_id = object_id('Shards'))
  ENABLE TRIGGER ShardsUpdDel ON dbo.Shards;
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'ShardletsUpdDel' AND parent_id = object_id('Shardlets'))
  ENABLE TRIGGER ShardletsUpdDel ON dbo.Shardlets;

declare @ToVersion int, @shards ShardList;
insert @shards values (0, 'sqlserver,1433', 'sqldb_000_v0'), (1, 'sqlserver,1433', 'sqldb_001_v0');
exec dbo.BeginEnvironmentUpdate 0, -1, @shards, @ToVersion OUT;
select @ToVersion;
*/
