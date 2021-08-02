/*************************************************************
    Event Agent checkpoint feature
**************************************************************/

/*************************************************************
    Event Agent checkpoint table
**************************************************************/

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EventAgentCheckpoint')
BEGIN
    CREATE TABLE dbo.EventAgentCheckpoint
    (
        CheckpointId varchar(64) NOT NULL,
        LastProcessedDateTime datetimeoffset(7),
        LastProcessedIdentifier varchar(64),
        UpdatedOn datetime2(7) NOT NULL DEFAULT sysutcdatetime(),
        CONSTRAINT PK_EventAgentCheckpoint PRIMARY KEY CLUSTERED (CheckpointId)
    )
    ON [PRIMARY]
END
GO

/*************************************************************
    Stored procedures for getting and setting checkpoints
**************************************************************/
--
-- STORED PROCEDURE
--     UpdateEventAgentCheckpoint
--
-- DESCRIPTION
--     Sets a checkpoint for an Event Agent
--
-- PARAMETERS
--     @CheckpointId
--         * The identifier of the checkpoint.
--     @LastProcessedDateTime
--         * The datetime of last item that was processed.
--     @LastProcessedIdentifier
--         *The identifier of the last item that was processed.
--
-- RETURN VALUE
--     It does not return a value.
--
CREATE OR ALTER PROCEDURE dbo.UpdateEventAgentCheckpoint
    @CheckpointId varchar(64),
    @LastProcessedDateTime datetimeoffset(7) = NULL,
    @LastProcessedIdentifier varchar(64) = NULL
AS
BEGIN
    IF EXISTS (SELECT * FROM dbo.EventAgentCheckpoint WHERE CheckpointId = @CheckpointId)
    UPDATE dbo.EventAgentCheckpoint SET CheckpointId = @CheckpointId, LastProcessedDateTime = @LastProcessedDateTime, LastProcessedIdentifier = @LastProcessedIdentifier, UpdatedOn = sysutcdatetime()
    WHERE CheckpointId = @CheckpointId
    ELSE
    INSERT INTO dbo.EventAgentCheckpoint
        (CheckpointId, LastProcessedDateTime, LastProcessedIdentifier, UpdatedOn)
    VALUES
        (@CheckpointId, @LastProcessedDateTime, @LastProcessedIdentifier, sysutcdatetime())
END
GO

--
-- STORED PROCEDURE
--     GetEventAgentCheckpoint
--
-- DESCRIPTION
--     Gets a checkpoint for an Event Agent
--
-- PARAMETERS
--     @Id
--         * The identifier of the checkpoint.
--
-- RETURN VALUE
--     A checkpoint for the given checkpoint id, if one exists.
--
CREATE OR ALTER PROCEDURE dbo.FetchEventAgentCheckpoint
    @CheckpointId varchar(64)
AS
BEGIN
    SELECT TOP(1) CheckpointId, LastProcessedDateTime, LastProcessedIdentifier
    FROM dbo.EventAgentCheckpoint
    WHERE CheckpointId = @CheckpointId
END
GO

