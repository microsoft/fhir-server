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
