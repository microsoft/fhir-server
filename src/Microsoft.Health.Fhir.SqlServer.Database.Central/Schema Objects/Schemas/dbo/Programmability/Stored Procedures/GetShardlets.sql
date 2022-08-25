--DROP PROCEDURE dbo.GetShardlets
GO
CREATE PROCEDURE dbo.GetShardlets @Version int = -1 -- -1 gets the active version shardlets
AS
--set nocount on -- cannot be used for CLR with DataAccess = DataAccessKind.Read
DECLARE @SP varchar(100) = 'GetShardlets'
       ,@Mode varchar(100) = 'V='+convert(varchar(10),@Version)
       ,@Rows int = 0
       ,@st datetime = getUTCdate()

IF @Version = -1
  SET @Version = (SELECT max(Version) FROM dbo.Shards WHERE IsActive = 1)

SELECT A.ShardId
      ,ConnectionString = 'server=' + SqlServer + ';database=' + SqlDatabase
      ,ShardletId
      ,A.Version
  FROM (SELECT * 
          FROM dbo.Shards 
          WHERE Version = @Version
       ) A
       JOIN dbo.Shardlets B ON B.Version = A.Version AND B.ShardId = A.ShardId
  ORDER BY ShardletId

SET @Rows = @Rows + @@rowcount
EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Start=@st,@Rows=@Rows
GO
-- Quick test code.
-- EXECUTE GetShardlets
