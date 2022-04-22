CREATE   PROCEDURE dbo.FetchEventAgentCheckpoint
@CheckpointId VARCHAR (64)
AS
BEGIN
    SELECT TOP (1) CheckpointId,
                   LastProcessedDateTime,
                   LastProcessedIdentifier
    FROM   dbo.EventAgentCheckpoint
    WHERE  CheckpointId = @CheckpointId;
END

