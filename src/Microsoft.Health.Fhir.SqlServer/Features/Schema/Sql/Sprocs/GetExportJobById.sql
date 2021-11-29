--
-- STORED PROCEDURE
--     Gets an export job given its ID.
--
-- DESCRIPTION
--     Retrieves the export job record from the ExportJob table that has the matching ID.
--
-- PARAMETERS
--     @id
--         * The ID of the export job record to retrieve
--
-- RETURN VALUE
--     The matching export job.
--
CREATE PROCEDURE dbo.GetExportJobById
    @id varchar(64)
AS
    SET NOCOUNT ON

    SELECT RawJobRecord, JobVersion
    FROM dbo.ExportJob
    WHERE Id = @id
GO
