CREATE TYPE ShardList AS TABLE 
(
  ShardId     tinyint      NOT NULL PRIMARY KEY
 ,SqlServer   varchar(128) NOT NULL
 ,SqlDatabase varchar(128) NOT NULL
  
  UNIQUE (SqlServer, SqlDatabase)
)
GO

