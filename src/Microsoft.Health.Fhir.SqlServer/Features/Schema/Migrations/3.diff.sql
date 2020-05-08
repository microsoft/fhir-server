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
--     Inserts a search parameter and its status into the search parameter registry.
--
-- DESCRIPTION
--     Adds a row to the search parameter registry.
--
-- PARAMETERS
--     @uri
--         * The search parameter's identifying URI
--     @status
--         * The status of the search parameter
--     @isPartiallySupported
--         * True if the parameter resolves to more than one type (FhirString, FhirUri, etc) but not all types can be indexed
--
CREATE PROCEDURE dbo.InsertIntoSearchParamRegistry
    @uri varchar(128),
    @status varchar(10),
    @isPartiallySupported bit
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @lastUpdated datetime2(7) = SYSUTCDATETIME()
    
    INSERT INTO dbo.SearchParamRegistry
        (Uri, Status, LastUpdated, IsPartiallySupported)
    VALUES
        (@uri, @status, @lastUpdated, @isPartiallySupported)

    COMMIT TRANSACTION
GO
