CREATE PARTITION FUNCTION TinyintPartitionFunction (tinyint) AS RANGE RIGHT FOR VALUES (0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114,115,116,117,118,119,120,121,122,123,124,125,126,127,128,129,130,131,132,133,134,135,136,137,138,139,140,141,142,143,144,145,146,147,148,149,150,151,152,153,154,155,156,157,158,159,160,161,162,163,164,165,166,167,168,169,170,171,172,173,174,175,176,177,178,179,180,181,182,183,184,185,186,187,188,189,190,191,192,193,194,195,196,197,198,199,200,201,202,203,204,205,206,207,208,209,210,211,212,213,214,215,216,217,218,219,220,221,222,223,224,225,226,227,228,229,230,231,232,233,234,235,236,237,238,239,240,241,242,243,244,245,246,247,248,249,250,251,252,253,254,255)
GO
CREATE PARTITION SCHEME TinyintPartitionScheme AS PARTITION TinyintPartitionFunction ALL TO ([PRIMARY])
--DROP TABLE dbo.WorkQueue
GO
CREATE TABLE dbo.JobQueue
(
     JobTypeId           tinyint       NOT NULL -- 1 - Export, 2 - Whatever next to use
    ,ItemId              bigint        NOT NULL
    ,ItemTypeId          tinyint       NOT NULL
    ,ParentItemId        bigint        NULL
    ,Definition          varchar(max)  NOT NULL
    ,DefinitionHash      AS hashbytes('SHA1',Definition) PERSISTED
    ,Result              varchar(max)  NULL
    ,Info                varchar(1000) NULL
    ,Status              tinyint       NOT NULL CONSTRAINT DF_JobQueue_Status DEFAULT 0 -- 0:created  1:running  2:completed with success  3:completed with failure 4:cancelled
    ,Priority            tinyint       NOT NULL CONSTRAINT DF_JobQueue_Priority DEFAULT 100 
    ,CreateDate          datetime      NOT NULL CONSTRAINT DF_JobQueue_CreateDate DEFAULT getUTCdate() 
    ,HeartbeatDate       datetime      NOT NULL CONSTRAINT DF_JobQueue_HeartbeatDate DEFAULT getUTCdate() 
    ,StartDate           datetime      NULL
    ,EndDate             datetime      NULL 
    ,Worker              varchar(100)  NULL 
    ,CancelRequested     bit           NOT NULL CONSTRAINT DF_JobQueue_CancelRequested DEFAULT 0
    ,Version             rowversion    NOT NULL

     CONSTRAINT PK_JobQueue_JobTypeId_ItemId PRIMARY KEY CLUSTERED (JobTypeId, ItemId) ON TinyintPartitionScheme(JobTypeId)
)
GO
CREATE INDEX IX_Status_Priority_JobTypeId ON dbo.JobQueue (Status, Priority) ON TinyintPartitionScheme(JobTypeId) -- dequeue
GO
CREATE INDEX IX_JobTypeId_ItemTypeId_ParentItemId ON dbo.JobQueue (GroupId, ItemTypeId, ShardId) ON TinyintPartitionScheme(JobTypeId) -- wait for completion, delete
GO
CREATE INDEX IX_JobTypeId_DefinitionHash ON dbo.JobQueue (JobTypeId, DefinitionHash) ON TinyintPartitionScheme(JobTypeId) -- idempotence
GO
