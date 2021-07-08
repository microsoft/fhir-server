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
         Partition varchar(64) NOT NULL,
         LastProcessedDateTime datetimeoffset(7),
         LastProcessedIdentifier varchar(64),
         UpdatedOn datetime2(7) NOT NULL DEFAULT sysutcdatetime(),
        CONSTRAINT PK_EventAgentCheckpoint PRIMARY KEY CLUSTERED (CheckpointId, Partition)
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
--     @Partition
--         * The partition that the Event Agent is reading from.
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
    @Partition varchar(64),
    @LastProcessedDateTime datetimeoffset(7) = NULL,
    @LastProcessedIdentifier varchar(64) = NULL
AS

BEGIN
    IF EXISTS (SELECT * FROM dbo.EventAgentCheckpoint WHERE CheckpointId = @CheckpointId AND Partition = @Partition)
    UPDATE dbo.EventAgentCheckpoint SET CheckpointId = @CheckpointId, "Partition" = @Partition, LastProcessedDateTime = @LastProcessedDateTime, LastProcessedIdentifier = @LastProcessedIdentifier, UpdatedOn = sysutcdatetime()
    WHERE CheckpointId = @CheckpointId AND Partition = @Partition
    ELSE
    INSERT INTO dbo.EventAgentCheckpoint
        (CheckpointId, "Partition", LastProcessedDateTime, LastProcessedIdentifier, UpdatedOn)
    VALUES
        (@CheckpointId, @Partition, @LastProcessedDateTime, @LastProcessedIdentifier, sysutcdatetime())
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
--     @Partition
--         * The partition that the Event Agent is reading from.
--
-- RETURN VALUE
--     A checkpoint for the given checkpoint id and partition, if one exists.
--
CREATE OR ALTER PROCEDURE dbo.FetchEventAgentCheckpoint
    @CheckpointId varchar(64),
    @Partition varchar(64)
AS
BEGIN
    SELECT TOP(1)
      CheckpointId,
      Partition,
      LastProcessedDateTime,
      LastProcessedIdentifier
      FROM dbo.EventAgentCheckpoint
    WHERE CheckpointId = @CheckpointId AND Partition = @Partition
END
GO
    COMMIT TRANSACTION
GO
