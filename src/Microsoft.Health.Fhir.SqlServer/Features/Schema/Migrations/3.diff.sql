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
    LastUpdated datetimeoffset(7) NULL, -- TODO: Should this just be datetime? What level of precision is needed?
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
--     Updates the status of a search parameter.
--
-- DESCRIPTION
--     Given an identifying URI, sets the status of a search parameter.
--
-- PARAMETERS
--     @searchParamStatuses
--         * The updated search parameter statuses
--
CREATE PROCEDURE dbo.UpdateSearchParamStatus
    @searchParamStatuses dbo.SearchParamRegistryTableType_1 READONLY
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @lastUpdated datetime2(7) = SYSUTCDATETIME()

    UPDATE dbo.SearchParamRegistry
    SET Status = sps.Status, LastUpdated = @lastUpdated
    FROM dbo.SearchParamRegistry INNER JOIN @searchParamStatuses as sps
    ON dbo.SearchParamRegistry.Uri = sps.Uri

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
--     Adds a row to the search parameter registry.
--
-- PARAMETERS
--     @searchParamStatuses
--         * The updated search parameter statuses
--
CREATE PROCEDURE dbo.InsertIntoSearchParamRegistry
    @searchParamStatuses dbo.SearchParamRegistryTableType_1 READONLY
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @lastUpdated datetime2(7) = SYSUTCDATETIME()
    
    INSERT INTO dbo.SearchParamRegistry
        (Uri, Status, LastUpdated, IsPartiallySupported)
    SELECT sps.Uri, sps.Status, @lastUpdated, sps.IsPartiallySupported
    FROM searchParamStatuses AS sps

    COMMIT TRANSACTION
GO
