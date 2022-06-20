--DROP TABLE dbo.Shards
GO
CREATE TABLE dbo.Shards
(
     Version          int          NOT NULL
    ,ShardId          tinyint      NOT NULL
    ,SqlServer        varchar(128) NOT NULL 
    ,SqlDatabase      varchar(128) NOT NULL 
    ,IsActive         bit          NOT NULL CONSTRAINT DF_Shards_IsActive DEFAULT 0
    ,ChangeDate       datetime     NOT NULL CONSTRAINT DF_Shards_ChangeDate DEFAULT getUTCdate()

    ,CONSTRAINT PKC_Shards_Version_ShardId PRIMARY KEY CLUSTERED (Version, ShardId)
    ,CONSTRAINT U_Shards_SqlServer_SqlDatabase UNIQUE (SqlServer, SqlDatabase)
)
GO
CREATE TRIGGER dbo.ShardsUpdDel ON dbo.Shards FOR DELETE, UPDATE
AS
BEGIN
  RAISERROR('This table cannot be updated', 18, 127)
  ROLLBACK TRANSACTION
END
GO
