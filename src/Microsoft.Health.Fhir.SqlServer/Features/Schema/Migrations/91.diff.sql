GO
ALTER PROCEDURE dbo.GetActiveJobs
@QueueType TINYINT, @GroupId BIGINT=NULL, @ReturnParentOnly BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetActiveJobs', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' G=' + isnull(CONVERT (VARCHAR, @GroupId), 'NULL') + ' R=' + CONVERT (VARCHAR, @ReturnParentOnly), @st AS DATETIME = getUTCdate(), @JobIds AS BigintList, @PartitionId AS TINYINT, @MaxPartitions AS TINYINT = 16, @LookedAtPartitions AS TINYINT = 0, @Rows AS INT = 0;
BEGIN TRY
    SET @PartitionId = @MaxPartitions * rand();
    WHILE @LookedAtPartitions < @MaxPartitions
        BEGIN
            IF @GroupId IS NULL
                INSERT INTO @JobIds
                SELECT JobId
                FROM   dbo.JobQueue
                WHERE  PartitionId = @PartitionId
                       AND QueueType = @QueueType
                       AND Status IN (0, 1);
            ELSE
                INSERT INTO @JobIds
                SELECT JobId
                FROM   dbo.JobQueue
                WHERE  PartitionId = @PartitionId
                       AND QueueType = @QueueType
                       AND GroupId = @GroupId
                       AND Status IN (0, 1);
            SET @Rows += @@rowcount;
            SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END;
            SET @LookedAtPartitions += 1;
        END
    IF @Rows > 0
        BEGIN
            IF @ReturnParentOnly = 1
                BEGIN
                    DECLARE @TopGroupId AS BIGINT;
                    SELECT   TOP 1 @TopGroupId = GroupId
                    FROM     dbo.JobQueue
                    WHERE    JobId IN (SELECT Id
                                       FROM   @JobIds)
                    ORDER BY GroupId DESC;
                    DELETE @JobIds
                    WHERE  Id NOT IN (SELECT JobId
                                      FROM   dbo.JobQueue
                                      WHERE  JobId = @TopGroupId);
                END
            EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobIds = @JobIds;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH