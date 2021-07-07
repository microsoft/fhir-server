/*************************************************************
    Background job checkpoint feature
**************************************************************/

/*************************************************************
    Background job checkpoint table
**************************************************************/

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BackgroundJobCheckpoint')
BEGIN
     CREATE TABLE dbo.BackgroundJobCheckpoint
     (
         Id bigint IDENTITY(1,1) NOT NULL,
         UpdatedOn datetime2(7) NOT NULL DEFAULT sysutcdatetime(),
         CheckpointId varchar(64) NOT NULL,
         Partition varchar(64) NOT NULL,
         LastProcessedDateTime datetimeoffset(7),
         LastProcessedIdentifier varchar(64),
        CONSTRAINT PK_BackgroundJobCheckpoint PRIMARY KEY CLUSTERED (CheckpointId, Partition)
     )
     ON [PRIMARY]
END
GO

/*************************************************************
    Stored procedures for getting and setting checkpoints
**************************************************************/
--
-- STORED PROCEDURE
--     UpdateBackgroundJobCheckpoint
--
-- DESCRIPTION
--     Sets a checkpoint for a background job
--
-- PARAMETERS
--     @CheckpointId
--         * The identifier of the checkpoint.
--     @Partition
--         * The partition that the background service is reading from.
--     @LastProcessedDateTime
--         * The datetime of last item that was processed.
--     @LastProcessedIdentifier
--         *The identifier of the last item that was processed.
--
-- RETURN VALUE
--     It does not return a value.
--
CREATE OR ALTER PROCEDURE dbo.UpdateBackgroundJobCheckpoint
    @CheckpointId varchar(64),
    @Partition varchar(64),
    @LastProcessedDateTime datetimeoffset(7) = NULL,
    @LastProcessedIdentifier varchar(64) = NULL
AS

BEGIN
    IF EXISTS (SELECT * FROM dbo.BackgroundJobCheckpoint WHERE CheckpointId = @CheckpointId AND Partition = @Partition)
    UPDATE dbo.BackgroundJobCheckpoint SET UpdatedOn = sysutcdatetime(), CheckpointId = @CheckpointId, "Partition" = @Partition, LastProcessedDateTime = @LastProcessedDateTime, LastProcessedIdentifier = @LastProcessedIdentifier
    WHERE CheckpointId = @CheckpointId AND Partition = @Partition
    ELSE
    INSERT INTO dbo.BackgroundJobCheckpoint
        (UpdatedOn, CheckpointId, "Partition", LastProcessedDateTime, LastProcessedIdentifier)
    VALUES
        (sysutcdatetime(), @CheckpointId, @Partition, @LastProcessedDateTime, @LastProcessedIdentifier)
END
GO

--
-- STORED PROCEDURE
--     GetBackgroundJobCheckpoint
--
-- DESCRIPTION
--     Gets a checkpoint for a background job
--
-- PARAMETERS
--     @Id
--         * The identifier of the checkpoint.
--     @Partition
--         * The partition that the background service is reading from.
--
-- RETURN VALUE
--     A checkpoint for the given checkpoint id and partition, if one exists.
--
CREATE OR ALTER PROCEDURE dbo.FetchBackgroundJobCheckpoint
    @CheckpointId varchar(64),
    @Partition varchar(64)
AS
BEGIN
    SELECT TOP(1)
      CheckpointId,
      Partition,
      LastProcessedDateTime,
      LastProcessedIdentifier
      FROM dbo.BackgroundJobCheckpoint
    WHERE CheckpointId = @CheckpointId AND Partition = @Partition
END
GO
    COMMIT TRANSACTION
GO
