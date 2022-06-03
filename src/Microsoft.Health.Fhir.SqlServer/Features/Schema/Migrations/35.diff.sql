IF object_id('JobQueue') IS NOT NULL AND object_id('U_JobQueue_QueueType_JobId') IS NULL
  ALTER TABLE dbo.JobQueue ADD CONSTRAINT U_JobQueue_QueueType_JobId UNIQUE (QueueType, JobId)
GO
