--
-- STORED PROCEDURE
--     Checks if there are any active reindex jobs.
--
-- DESCRIPTION
--     Queries the datastore for any reindex job documents with a status of running, queued or paused.
--
-- RETURN VALUE
--     The job IDs of any active reindex jobs.
--
CREATE PROCEDURE dbo.CheckActiveReindexJobs
AS
    SET NOCOUNT ON

    SELECT Id
    FROM dbo.ReindexJob
    WHERE Status = 'Running' OR Status = 'Queued' OR Status = 'Paused'
GO
