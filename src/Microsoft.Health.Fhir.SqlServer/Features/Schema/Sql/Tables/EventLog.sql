CREATE PARTITION FUNCTION EventLogPartitionFunction (tinyint) AS RANGE RIGHT FOR VALUES (0,1,2,3,4,5,6,7)
GO
CREATE PARTITION SCHEME EventLogPartitionScheme AS PARTITION EventLogPartitionFunction ALL TO ([PRIMARY])
GO
--DROP TABLE EventLog
GO
CREATE TABLE dbo.EventLog
  (
     PartitionId   AS isnull(convert(tinyint, EventId % 8),0) PERSISTED
    ,EventId       bigint IDENTITY(1,1) NOT NULL
    ,EventDate     datetime             NOT NULL
    ,Process       varchar(100)         NOT NULL
    ,Status        varchar(10)          NOT NULL
    ,Mode          varchar(100)         NULL
    ,Action        varchar(20)          NULL
    ,Target        varchar(100)         NULL
    ,Rows          bigint               NULL
    ,Milliseconds  int                  NULL
    ,EventText     nvarchar(3500)       NULL
    ,SPID          smallint             NOT NULL
    ,HostName      varchar(64)          NOT NULL

     CONSTRAINT PKC_EventLog_EventDate_EventId_PartitionId PRIMARY KEY CLUSTERED (EventDate, EventId, PartitionId) ON EventLogPartitionScheme(PartitionId)
  ) 
GO
