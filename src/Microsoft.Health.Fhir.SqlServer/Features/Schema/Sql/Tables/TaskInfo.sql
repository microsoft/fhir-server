/*************************************************************
    Task Table
**************************************************************/
CREATE TABLE [dbo].[TaskInfo] (
    [TaskId]            VARCHAR (64)  NOT NULL,
    [QueueId]           VARCHAR (64)  NOT NULL,
    [Status]            SMALLINT      NOT NULL,
    [TaskTypeId]        SMALLINT      NOT NULL,
    [RunId]             VARCHAR (50)  NULL,
    [IsCanceled]        BIT           NOT NULL,
    [RetryCount]        SMALLINT      NOT NULL,
    [MaxRetryCount]     SMALLINT      NOT NULL,
    [HeartbeatDateTime] DATETIME2 (7) NULL,
    [InputData]         VARCHAR (MAX) NOT NULL,
    [TaskContext]       VARCHAR (MAX) NULL,
    [Result]            VARCHAR (MAX) NULL,
    [CreateDateTime]    DATETIME2 (7) NOT NULL CONSTRAINT DF_TaskInfo_CreateDate DEFAULT SYSUTCDATETIME(),
    [StartDateTime]     DATETIME2 (7) NULL,
    [EndDateTime]       DATETIME2 (7) NULL,
    [Worker]            VARCHAR (100) NULL,
    [RestartInfo]       VARCHAR (MAX) NULL,
    [ParentTaskId]      VARCHAR (64)  NULL,
    CONSTRAINT PKC_TaskInfo PRIMARY KEY CLUSTERED (TaskId) WITH (DATA_COMPRESSION = PAGE)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

GO
CREATE NONCLUSTERED INDEX IX_QueueId_Status ON dbo.TaskInfo
(
    QueueId,
    Status
)

GO
CREATE NONCLUSTERED INDEX IX_QueueId_ParentTaskId ON dbo.TaskInfo
(
    QueueId,
    ParentTaskId
)
