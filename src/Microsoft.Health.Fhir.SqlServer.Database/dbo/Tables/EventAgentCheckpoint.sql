CREATE TABLE [dbo].[EventAgentCheckpoint] (
    [CheckpointId]            VARCHAR (64)       NOT NULL,
    [LastProcessedDateTime]   DATETIMEOFFSET (7) NULL,
    [LastProcessedIdentifier] VARCHAR (64)       NULL,
    [UpdatedOn]               DATETIME2 (7)      DEFAULT (sysutcdatetime()) NOT NULL,
    CONSTRAINT [PK_EventAgentCheckpoint] PRIMARY KEY CLUSTERED ([CheckpointId] ASC)
);

