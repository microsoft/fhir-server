ALTER PROCEDURE [dbo].[PutJobHeartbeat]
@QueueType TINYINT, @JobId BIGINT, @Version BIGINT, @Data BIGINT=NULL, @CancelRequested BIT=0 OUTPUT
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'PutJobHeartbeat', @Mode AS VARCHAR (100), @st AS DATETIME = getUTCdate(), @Rows AS INT = 0, @PartitionId AS TINYINT = @JobId % 16;
SET @Mode = 'Q=' + CONVERT (VARCHAR, @QueueType) + ' J=' + CONVERT (VARCHAR, @JobId) + ' P=' + CONVERT (VARCHAR, @PartitionId) + ' V=' + CONVERT (VARCHAR, @Version) + ' D=' + isnull(CONVERT (VARCHAR, @Data), 'NULL');
BEGIN TRY
    UPDATE dbo.JobQueue
    SET    @CancelRequested = CancelRequested,
           HeartbeatDate    = getUTCdate(),
		       Data				      = @Data
    WHERE  QueueType = @QueueType
           AND PartitionId = @PartitionId
           AND JobId = @JobId
           AND Status = 1
           AND Version = @Version;
    SET @Rows = @@rowcount;
    IF @Rows = 0
       AND NOT EXISTS (SELECT *
                       FROM   dbo.JobQueue
                       WHERE  QueueType = @QueueType
                              AND PartitionId = @PartitionId
                              AND JobId = @JobId
                              AND Version = @Version
                              AND Status IN (2, 3, 4))
        BEGIN
            IF EXISTS (SELECT *
                       FROM   dbo.JobQueue
                       WHERE  QueueType = @QueueType
                              AND PartitionId = @PartitionId
                              AND JobId = @JobId)
                THROW 50412, 'Precondition failed', 1;
            ELSE
                THROW 50404, 'Job record not found', 1;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
GO