CREATE TABLE dbo.EventAgentCheckpoint
    (
        CheckpointId varchar(64) NOT NULL,
        LastProcessedDateTime datetimeoffset(7),
        LastProcessedIdentifier varchar(64),
        UpdatedOn datetime2(7) NOT NULL DEFAULT sysutcdatetime(),
        CONSTRAINT PK_EventAgentCheckpoint PRIMARY KEY CLUSTERED (CheckpointId)
    )
    ON [PRIMARY]