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
