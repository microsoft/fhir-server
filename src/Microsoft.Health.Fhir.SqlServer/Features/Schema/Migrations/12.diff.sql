/*************************************************************
    Stored procedures for search parameter information
**************************************************************/
--
-- STORED PROCEDURE
--     GetSearchParamStatuses
--
-- DESCRIPTION
--     Gets all the search parameters and their statuses.
--
-- RETURN VALUE
--     The search parameters and their statuses.
--
ALTER PROCEDURE dbo.GetSearchParamStatuses
AS
    SET NOCOUNT ON

    SELECT SearchParamId, Uri, Status, LastUpdated, IsPartiallySupported FROM dbo.SearchParam
GO

--
-- STORED PROCEDURE
--     UpsertSearchParams
--
-- DESCRIPTION
--     Given a set of search parameters, creates or updates the parameters.
--
-- PARAMETERS
--     @searchParams
--         * The updated existing search parameters or the new search parameters
--
ALTER PROCEDURE dbo.UpsertSearchParams
    @searchParams dbo.SearchParamTableType_1 READONLY
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    DECLARE @lastUpdated datetimeoffset(7) = SYSDATETIMEOFFSET()

    -- Acquire and hold an exclusive table lock for the entire transaction to prevent parameters from being added or modified during upsert.
    MERGE INTO dbo.SearchParam WITH (TABLOCKX) AS target
    USING @searchParams AS source
    ON target.Uri = source.Uri
    WHEN MATCHED THEN
        UPDATE
            SET Status = source.Status, LastUpdated = @lastUpdated, IsPartiallySupported = source.IsPartiallySupported
    WHEN NOT MATCHED BY target THEN
        INSERT
            (Uri, Status, LastUpdated, IsPartiallySupported)
            VALUES (source.Uri, source.Status, @lastUpdated, source.IsPartiallySupported);

    COMMIT TRANSACTION
GO
