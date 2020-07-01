/*************************************************************
    Search Parameter Registry
**************************************************************/

CREATE TYPE dbo.SearchParamRegistryTableType_1 AS TABLE
(
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    IsPartiallySupported bit NOT NULL
)

CREATE TABLE dbo.SearchParamRegistry
(
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    LastUpdated datetimeoffset(7) NULL,
    IsPartiallySupported bit NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_SearchParamRegistry
ON dbo.SearchParamRegistry
(
    Uri
)

GO

/*************************************************************
    Stored procedures for the search parameter registry
**************************************************************/
--
-- STORED PROCEDURE
--     Gets all the search parameters and their statuses.
--
-- DESCRIPTION
--     Retrieves and returns the contents of the search parameter registry.
--
-- RETURN VALUE
--     The search parameters and their statuses.
--
CREATE PROCEDURE dbo.GetSearchParamStatuses
AS
    SET NOCOUNT ON
    
    SELECT * FROM dbo.SearchParamRegistry
GO

--
-- STORED PROCEDURE
--     Given a table of search parameters, upserts the registry.
--
-- DESCRIPTION
--     If a parameter with a matching URI already exists in the registry, it is updated.
--     If not, a new entry is created.
--
-- PARAMETERS
--     @searchParamStatuses
--         * The updated or new search parameter statuses
--
CREATE PROCEDURE dbo.UpsertSearchParamStatus
    @searchParamStatuses dbo.SearchParamRegistryTableType_1 READONLY
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    DECLARE @lastUpdated datetimeoffset(7) = SYSDATETIMEOFFSET()

    -- Acquire and hold an exclusive table lock for the entire transaction to prevent parameters from being added or modified during upsertion.
    UPDATE dbo.SearchParamRegistry
    WITH (TABLOCKX)
    SET Status = sps.Status, LastUpdated = @lastUpdated, IsPartiallySupported = sps.IsPartiallySupported
    FROM dbo.SearchParamRegistry INNER JOIN @searchParamStatuses as sps
    ON dbo.SearchParamRegistry.Uri = sps.Uri

    INSERT INTO dbo.SearchParamRegistry
        (Uri, Status, LastUpdated, IsPartiallySupported)
    SELECT sps.Uri, sps.Status, @lastUpdated, sps.IsPartiallySupported
    FROM @searchParamStatuses AS sps
    WHERE sps.Uri NOT IN
        (SELECT Uri FROM dbo.SearchParamRegistry) 

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Counts the number of search parameters.
--
-- DESCRIPTION
--     Retrieves and returns the number of rows in the search parameter registry.
--
-- RETURN VALUE
--     The number of search parameters in the registry.
--
CREATE PROCEDURE dbo.GetSearchParamRegistryCount
AS
    SET NOCOUNT ON
    
    SELECT COUNT(*) FROM dbo.SearchParamRegistry
GO

--
-- STORED PROCEDURE
--     Inserts a search parameter and its status into the search parameter registry.
--
-- DESCRIPTION
--     Adds a row to the search parameter registry. This is intended to be called within
--     a transaction that also queries if the table is empty and needs to be initialized.
--
-- PARAMETERS
--     @searchParamStatuses
--         * The updated search parameter statuses
--
CREATE PROCEDURE dbo.InsertIntoSearchParamRegistry
    @searchParamStatuses dbo.SearchParamRegistryTableType_1 READONLY
AS
    SET NOCOUNT ON

    DECLARE @lastUpdated datetimeoffset(7) = SYSDATETIMEOFFSET()
    
    INSERT INTO dbo.SearchParamRegistry
        (Uri, Status, LastUpdated, IsPartiallySupported)
    SELECT sps.Uri, sps.Status, @lastUpdated, sps.IsPartiallySupported
    FROM @searchParamStatuses AS sps
GO
