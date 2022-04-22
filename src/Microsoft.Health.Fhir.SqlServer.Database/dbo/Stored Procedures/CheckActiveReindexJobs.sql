CREATE PROCEDURE dbo.CheckActiveReindexJobs
AS
SET NOCOUNT ON;
SELECT Id
FROM   dbo.ReindexJob
WHERE  Status = 'Running'
       OR Status = 'Queued'
       OR Status = 'Paused';

