--DROP TABLE dbo.Shardlets
GO
CREATE TABLE dbo.Shardlets
(
     Version          int       NOT NULL
    ,ShardletId       smallint  NOT NULL
    ,ShardId          tinyint   NOT NULL
    ,ChangeDate       datetime  NOT NULL CONSTRAINT DF_Shardlets_ChangeDate DEFAULT getUTCdate() 

     CONSTRAINT PK_Shardlets_Version_ShardletId PRIMARY KEY CLUSTERED (Version, ShardletId)
    ,CONSTRAINT FK_Shardlets_Version_ShardId_Shards_Version_ShardId FOREIGN KEY (Version, ShardId) REFERENCES Shards (Version, ShardId)
)
GO
CREATE TRIGGER dbo.ShardletsIns ON dbo.Shardlets FOR INSERT
AS
BEGIN
  IF (SELECT count(*) FROM dbo.Shardlets WHERE Version = (SELECT max(Version) FROM Shards)) <> 2048
  BEGIN
    RAISERROR('All shardlets must be assigned', 18, 127)
    ROLLBACK TRANSACTION
  END
END
GO
CREATE TRIGGER dbo.ShardletsUpdDel ON dbo.Shardlets FOR DELETE, UPDATE
AS
BEGIN
  RAISERROR('This table cannot be updated', 18, 127)
  ROLLBACK TRANSACTION
END
GO
