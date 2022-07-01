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
GO
CREATE PROCEDURE [dbo].[GetImportProcessingTaskResult]
    @queueId VARCHAR (64),
    @importTaskId varchar(64)
AS
    SET NOCOUNT ON

    SELECT Result
	FROM [dbo].[TaskInfo] WITH (INDEX (IX_QueueId_ParentTaskId))
	where ParentTaskId = @importTaskId and TaskTypeId = 1 and Status = 3
GO
