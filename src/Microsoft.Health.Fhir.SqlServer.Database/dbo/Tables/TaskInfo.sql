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
    [CreateDateTime]    DATETIME2 (7) CONSTRAINT [DF_TaskInfo_CreateDate] DEFAULT (sysutcdatetime()) NOT NULL,
    [StartDateTime]     DATETIME2 (7) NULL,
    [EndDateTime]       DATETIME2 (7) NULL,
    [Worker]            VARCHAR (100) NULL,
    [RestartInfo]       VARCHAR (MAX) NULL,
    CONSTRAINT [PKC_TaskInfo] PRIMARY KEY CLUSTERED ([TaskId] ASC) WITH (DATA_COMPRESSION = PAGE)
);


GO
CREATE NONCLUSTERED INDEX [IX_QueueId_Status]
    ON [dbo].[TaskInfo]([QueueId] ASC, [Status] ASC);

