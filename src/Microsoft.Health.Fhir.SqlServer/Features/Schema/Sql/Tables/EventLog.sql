
CREATE TABLE dbo.EventLog
  (
     PartitionId   AS isnull(convert(tinyint, EventId % 8),0) PERSISTED
    ,EventId       bigint IDENTITY(1,1) NOT NULL
    ,EventDate     datetime2(7)         NOT NULL
    ,Process       varchar(100)         NOT NULL
    ,Status        varchar(10)          NOT NULL
    ,Mode          varchar(100)         NULL
    ,Action        varchar(20)          NULL
    ,Target        varchar(100)         NULL
    ,Rows          bigint               NULL
    ,Milliseconds  int                  NULL
    ,EventText     nvarchar(3500)       NULL
    ,ParentEventId bigint               NULL
    ,SPID          smallint             NOT NULL
    ,HostName      varchar(64)          NOT NULL
    ,TraceId       uniqueidentifier     NULL

     CONSTRAINT PKC_EventLog_EventDate_EventId_PartitionId PRIMARY KEY CLUSTERED (EventDate, EventId, PartitionId) ON EventLogPartitionScheme(PartitionId)
  ) 
