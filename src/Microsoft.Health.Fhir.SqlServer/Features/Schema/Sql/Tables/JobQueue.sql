
CREATE TABLE dbo.JobQueue
(
     QueueType           tinyint       NOT NULL -- 1=export, 2=import, 3=whatever next
    ,GroupId             bigint        NOT NULL -- export id, import id
    ,JobId               bigint        NOT NULL
    ,PartitionId         AS convert(tinyint, JobId % 16) PERSISTED -- physical separation for performance
    ,Definition          varchar(max)  NOT NULL -- unique info identifying a job
    ,DefinitionHash      varbinary(20) NOT NULL -- to ensure idempotence
    ,Version             bigint        NOT NULL CONSTRAINT DF_JobQueue_Version DEFAULT datediff_big(millisecond,'0001-01-01',getUTCdate()) -- to prevent racing
    ,Status              tinyint       NOT NULL CONSTRAINT DF_JobQueue_Status DEFAULT 0 -- 0:created  1=running, 2=completed, 3=failed, 4=cancelled, 5=archived
    ,Priority            tinyint       NOT NULL CONSTRAINT DF_JobQueue_Priority DEFAULT 100 
    ,Data                bigint        NULL
    ,Result              varchar(max)  NULL
    ,CreateDate          datetime2(7)  NOT NULL CONSTRAINT DF_JobQueue_CreateDate DEFAULT getUTCdate() 
    ,StartDate           datetime2(7)  NULL
    ,EndDate             datetime2(7)  NULL 
    ,HeartbeatDate       datetime2(7)  NOT NULL CONSTRAINT DF_JobQueue_HeartbeatDate DEFAULT getUTCdate() 
    ,Worker              varchar(100)  NULL 
    ,Info                varchar(1000) NULL
    ,CancelRequested     bit           NOT NULL CONSTRAINT DF_JobQueue_CancelRequested DEFAULT 0

     CONSTRAINT PKC_JobQueue_QueueType_PartitionId_JobId PRIMARY KEY CLUSTERED (QueueType, PartitionId, JobId) ON TinyintPartitionScheme(QueueType)
)

CREATE INDEX IX_QueueType_PartitionId_Status_Priority ON dbo.JobQueue (PartitionId, Status, Priority) ON TinyintPartitionScheme(QueueType) -- dequeue

CREATE INDEX IX_QueueType_GroupId ON dbo.JobQueue (QueueType, GroupId) ON TinyintPartitionScheme(QueueType) -- wait for completion, delete

CREATE INDEX IX_QueueType_DefinitionHash ON dbo.JobQueue (QueueType, DefinitionHash) ON TinyintPartitionScheme(QueueType) -- cannot express as unique constraint as I want to exclude archived


