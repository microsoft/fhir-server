--
-- STORED PROCEDURE
--     Gets an export job given the hash of its ID.
--
-- DESCRIPTION
--     Retrieves the export job record from the ExportJob table that has the matching hash.
--
-- PARAMETERS
--     @hash
--         * The SHA256 hash of the export job record ID
--
-- RETURN VALUE
--     The matching export job.
--
CREATE PROCEDURE dbo.GetExportJobByHash
    @hash varchar(64)
AS
    SET NOCOUNT ON

    SELECT TOP(1) RawJobRecord, JobVersion
    FROM dbo.ExportJob
    WHERE Hash = @hash AND (Status = 'Queued' OR Status = 'Running')
    ORDER BY HeartbeatDateTime ASC
GO
