--
-- STORED PROCEDURE
--     Gets an reindex job given its ID.
--
-- DESCRIPTION
--     Retrieves the reindex job record from the ReindexJob table that has the matching ID.
--
-- PARAMETERS
--     @id
--         * The ID of the reindex job record to retrieve
--
-- RETURN VALUE
--     The matching reindex job.
--
CREATE PROCEDURE dbo.GetReindexJobById
    @id varchar(64)
AS
    SET NOCOUNT ON

    SELECT RawJobRecord, JobVersion
    FROM dbo.ReindexJob
    WHERE Id = @id
GO
