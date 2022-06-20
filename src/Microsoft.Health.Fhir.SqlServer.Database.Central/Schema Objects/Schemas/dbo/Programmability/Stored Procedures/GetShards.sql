-- DROP PROCEDURE dbo.GetShards
GO
CREATE PROCEDURE dbo.GetShards @Version int = -1 -- -1 gets the active version shardls
AS
set nocount on
SELECT SqlServer
      ,SqlDatabase
      ,Version
      ,ShardId
      ,ChangeDate
  FROM dbo.Shards
  WHERE (@Version = -1 AND IsActive = 1)
     OR (Version = @Version)
  ORDER BY 
       ShardId
GO
/*
exec dbo.GetShards;
exec dbo.GetShards -1;
exec dbo.GetShards 0;
exec dbo.GetShards 999;
*/
