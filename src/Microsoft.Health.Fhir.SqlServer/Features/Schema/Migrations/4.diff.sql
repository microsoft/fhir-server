/*************************************************************
    Search Parameter Status Information
**************************************************************/

ALTER TABLE dbo.SearchParam
ADD
    Status varchar(10) NULL,
    LastUpdated datetimeoffset(7) NULL,
    IsPartiallySupported bit NULL

CREATE TYPE dbo.SearchParamTableType_1 AS TABLE
(
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    IsPartiallySupported bit NOT NULL
)

GO

/*************************************************************
    Stored procedures for search parameter information
**************************************************************/
--
-- STORED PROCEDURE
--     Gets all the search parameters and their statuses.
--
-- DESCRIPTION
--     Retrieves and returns select contents from the search parameter table.
--
-- RETURN VALUE
--     The search parameters and their statuses.
--
CREATE PROCEDURE dbo.GetSearchParamStatuses
AS
    SET NOCOUNT ON

    SELECT Uri, Status, LastUpdated, IsPartiallySupported FROM dbo.SearchParam
GO

--
-- STORED PROCEDURE
--     Given a set of search parameters, creates or updates the parameters.
--
-- DESCRIPTION
--     If a parameter with a matching URI already exists in the table, it is updated.
--     If not, a new entry is created.
--
-- PARAMETERS
--     @searchParams
--         * The updated or new search parameters
--
CREATE PROCEDURE dbo.UpsertSearchParams
    @searchParams dbo.SearchParamTableType_1 READONLY
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    DECLARE @lastUpdated datetimeoffset(7) = SYSDATETIMEOFFSET()

    -- Acquire and hold an exclusive table lock for the entire transaction to prevent parameters from being added or modified during upsertion.
    UPDATE dbo.SearchParam
    WITH (TABLOCKX)
    SET Status = sps.Status, LastUpdated = @lastUpdated, IsPartiallySupported = sps.IsPartiallySupported
    FROM dbo.SearchParam INNER JOIN @searchParams as sps
    ON dbo.SearchParam.Uri = sps.Uri

    -- TODO: Replace Search Param Id with actual value.
    INSERT INTO dbo.SearchParam
        (SearchParamId, Uri, Status, LastUpdated, IsPartiallySupported)
    SELECT 67676767, sps.Uri, sps.Status, @lastUpdated, sps.IsPartiallySupported
    FROM @searchParams AS sps
    WHERE sps.Uri NOT IN
        (SELECT Uri FROM dbo.SearchParam)

    COMMIT TRANSACTION
GO
