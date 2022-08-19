/***************************************************************************
    Stored procedures for get NonCompleted Job Count Of SpecificQueueType
****************************************************************************/
--
--  STORED PROCEDURE
--      GetNonCompletedJobCountOfSpecificQueueType
--
--  DESCRIPTION
--      Count the number of non-completed jobs of specific type.
--
--  PARAMETERS
--      @@queueType
--          * The type of queue
--
CREATE OR ALTER PROCEDURE dbo.GetNonCompletedJobCountOfSpecificQueueType
    @queueType tinyint

AS
BEGIN
    SET NOCOUNT ON

    SELECT COUNT(*)
    FROM dbo.JobQueue
    WHERE QueueType = @queueType AND (Status = 0 or Status = 1)
END
GO
