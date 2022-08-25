--DROP PROCEDURE dbo.InitializeEnvironment
GO
CREATE PROCEDURE dbo.InitializeEnvironment 
  @ServerName varchar(128)
 ,@SkipShardsConfig bit = NULL
 ,@ShardCount tinyint = 2 -- 0 is special it will install one shard in the same db as central
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = convert(varchar(100),'S=['+isnull(@ServerName,'NULL')+']')
       ,@NumberOfShardlets smallint = 256
       ,@i tinyint

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Begin'

CREATE TABLE #SpecialServerNames (Name varchar(128) NOT NULL PRIMARY KEY)
INSERT #SpecialServerNames (Name)
  VALUES ('.'), ('(local)'), ('localhost'), ('127.0.0.1'), (@@servername)

SELECT TOP (@NumberOfShardlets) 
       ShardletId = row_number() OVER (ORDER BY A1.column_id) - 1 
  INTO #Shardlets
  FROM sys.columns A1 CROSS JOIN sys.columns A2

BEGIN TRY
  BEGIN TRANSACTION
  
  -- default empty db configuration
  IF isnull(@SkipShardsConfig, 0) = 0 AND NOT EXISTS (SELECT * FROM dbo.Shards)
  BEGIN
    IF @ShardCount = 0 
      INSERT INTO dbo.Shards (Version, ShardId, IsActive, SqlServer, SqlDatabase)
        SELECT 0, 0, 1, @ServerName, db_name()

    SET @i = 0
    WHILE @i < @ShardCount
    BEGIN
      INSERT INTO dbo.Shards (Version, ShardId, IsActive, SqlServer, SqlDatabase)
        SELECT 0, @i, 1, @ServerName, db_name()+'_'+format(@i,'0#')+'_v0'
      SET @i += 1
    END

    INSERT INTO dbo.Shardlets 
        (
             Version
            ,ShardletId
            ,ShardId
        )
      SELECT 0
            ,ShardletId
            ,ShardId = CASE WHEN @ShardCount > 1 THEN ShardletId % @ShardCount ELSE 0 END
        FROM #Shardlets A
        WHERE NOT EXISTS (SELECT * FROM dbo.Shardlets B WHERE B.ShardletId = A.ShardletId AND B.Version = (SELECT max(Version) FROM dbo.Shards))
  END
  ELSE 
    IF EXISTS (SELECT * FROM dbo.Shards WHERE IsActive = 1 AND SqlServer IN (SELECT Name FROM #SpecialServerNames))
    BEGIN
      IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'ShardsUpdDel' AND parent_id = object_id('Shards') )
        DISABLE TRIGGER ShardsUpdDel ON dbo.Shards
      UPDATE dbo.Shards SET SqlServer = @ServerName, ChangeDate = getutcdate()
        WHERE IsActive = 1
          AND SqlServer IN (SELECT Name FROM #SpecialServerNames)
      IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'ShardsUpdDel' AND parent_id = object_id('Shards') )
        ENABLE TRIGGER ShardsUpdDel ON dbo.Shards
    END

  COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End'
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
-- Quick test code.
--EXECUTE InitializeEnvironment 0
--SELECT * FROM EventLog ORDER BY EventDate DESC
--SELECT * FROM GlobalShardlets
