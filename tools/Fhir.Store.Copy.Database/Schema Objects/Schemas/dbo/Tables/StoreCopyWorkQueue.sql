--DROP TABLE dbo.StoreCopyWorkQueue
GO
CREATE TABLE dbo.StoreCopyWorkQueue
(
     UnitId          int           NOT NULL
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

     CONSTRAINT PKC_StoreCopyWorkQueue_UnitId PRIMARY KEY CLUSTERED (UnitId)
)
GO
--CREATE INDEX IX_Thread_Status ON StoreCopyWorkQueue (Thread, Status)
GO
CREATE INDEX IX_Status ON StoreCopyWorkQueue (Status)
GO
