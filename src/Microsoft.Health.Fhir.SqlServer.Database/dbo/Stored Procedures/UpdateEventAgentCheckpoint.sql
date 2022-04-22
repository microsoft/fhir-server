CREATE   PROCEDURE dbo.UpdateEventAgentCheckpoint
@CheckpointId VARCHAR (64), @LastProcessedDateTime DATETIMEOFFSET (7)=NULL, @LastProcessedIdentifier VARCHAR (64)=NULL
AS
BEGIN
    IF EXISTS (SELECT *
               FROM   dbo.EventAgentCheckpoint
               WHERE  CheckpointId = @CheckpointId)
        UPDATE dbo.EventAgentCheckpoint
        SET    CheckpointId            = @CheckpointId,
               LastProcessedDateTime   = @LastProcessedDateTime,
               LastProcessedIdentifier = @LastProcessedIdentifier,
               UpdatedOn               = sysutcdatetime()
        WHERE  CheckpointId = @CheckpointId;
    ELSE
        INSERT  INTO dbo.EventAgentCheckpoint (CheckpointId, LastProcessedDateTime, LastProcessedIdentifier, UpdatedOn)
        VALUES                               (@CheckpointId, @LastProcessedDateTime, @LastProcessedIdentifier, sysutcdatetime());
END

