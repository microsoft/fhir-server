/*************************************************************
    Stored procedures for get task payload
**************************************************************/
--
-- STORED PROCEDURE
--     GetTaskDetails
--
-- DESCRIPTION
--     Get task payload.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--
CREATE PROCEDURE [dbo].[GetTaskDetails]
    @taskId varchar(64)
AS
    SET NOCOUNT ON

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result, ParentTaskId
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId
GO
