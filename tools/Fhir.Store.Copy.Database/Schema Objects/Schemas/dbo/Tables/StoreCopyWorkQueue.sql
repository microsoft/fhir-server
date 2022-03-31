CREATE PARTITION FUNCTION StoreCopyWorkQueuePartitionFunction (tinyint) AS RANGE RIGHT FOR VALUES (0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15)
GO
CREATE PARTITION SCHEME StoreCopyWorkQueuePartitionScheme AS PARTITION StoreCopyWorkQueuePartitionFunction ALL TO ([PRIMARY])
GO
--DROP TABLE dbo.StoreCopyWorkQueue
GO
CREATE TABLE dbo.StoreCopyWorkQueue
(
     PartitionId     AS convert(tinyint,UnitId % 16) PERSISTED
    ,UnitId          int           NOT NULL
    ,ResourceTypeId  smallint      NOT NULL
    ,Thread          tinyint       NOT NULL CONSTRAINT DF_StoreCopyWorkQueue_Thread DEFAULT 0
    ,MinId           varchar(64)   NOT NULL
    ,MaxId           varchar(64)   NOT NULL
    ,ResourceCount   int           NOT NULL
    ,Status          tinyint       NOT NULL CONSTRAINT DF_StoreCopyWorkQueue_Status DEFAULT 0 -- 0:created  1:running  2:completed with success  3:completed with failure  
    ,CreateDate      datetime      NOT NULL CONSTRAINT DF_StoreCopyWorkQueue_CreateDate DEFAULT getUTCdate() 
    ,HeartBeatDate   datetime      NOT NULL CONSTRAINT DF_StoreCopyWorkQueue_HeartBeatDate DEFAULT getUTCdate()  
    ,StartDate       datetime      NULL
    ,EndDate         datetime      NULL 
    ,Worker          varchar(100)  NULL 
    ,Result          xml           NULL
    ,Info            varchar(1000) NULL

     CONSTRAINT PKC_StoreCopyWorkQueue_PartitionId_UnitId PRIMARY KEY CLUSTERED (PartitionId, UnitId) ON StoreCopyWorkQueuePartitionScheme(PartitionId)
)
GO
ALTER TABLE dbo.StoreCopyWorkQueue ADD CONSTRAINT U_StoreCopyWorkQueue_UnitId UNIQUE (UnitId) ON [PRIMARY]
GO
--CREATE INDEX IX_Thread_Status ON StoreCopyWorkQueue (Thread, Status)
GO
CREATE INDEX IX_Status_PartitionId ON StoreCopyWorkQueue (Status, PartitionId) ON StoreCopyWorkQueuePartitionScheme(PartitionId)
GO
