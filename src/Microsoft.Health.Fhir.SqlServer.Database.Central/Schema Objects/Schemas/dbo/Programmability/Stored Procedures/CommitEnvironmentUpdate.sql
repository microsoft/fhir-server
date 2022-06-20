--DROP PROCEDURE dbo.CommitEnvironmentUpdate
GO
CREATE PROCEDURE dbo.CommitEnvironmentUpdate 
  @FromVersion int
 ,@ToVersion   int
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = convert(varchar(100), 'F='+isnull(convert(varchar(10),@FromVersion),'NULL')+' T=['+isnull(convert(varchar(10),@ToVersion),'NULL')+']')

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Begin'

BEGIN TRY
  IF NOT EXISTS (SELECT * FROM dbo.Shards WHERE (IsActive = 1 AND Version = @FromVersion) OR @FromVersion = -1)
     OR NOT EXISTS (SELECT * FROM dbo.Shards WHERE IsActive = 0 AND @ToVersion = (SELECT max(Version) FROM dbo.Shards))
    RAISERROR('No open transaction to commit or concurrent transaction', 18, 127)   

  IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'ShardsUpdDel' AND parent_id = object_id('Shards'))
    DISABLE TRIGGER ShardsUpdDel ON dbo.Shards

  UPDATE dbo.Shards 
    SET IsActive = CASE WHEN Version = @ToVersion THEN 1 ELSE 0 END
    WHERE Version IN (@FromVersion, @ToVersion)

  IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'ShardsUpdDel' AND parent_id = object_id('Shards'))
    ENABLE TRIGGER ShardsUpdDel ON dbo.Shards

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End'
END TRY
BEGIN CATCH
  IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'ShardsUpdDel' AND parent_id = object_id('Shards'))
    ENABLE TRIGGER ShardsUpdDel ON dbo.Shards
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
END CATCH
GO

