/*************************************************************
    Search Parameter Registry
**************************************************************/
-- TODO: Update table and indices one usage of table is fully understood
CREATE TABLE dbo.SearchParameterRegistry
(
    SearchParamId smallint NOT NULL,
    ResourceTypeId smallint NOT NULL, -- TODO: Should we be storing the uri instead?
    Status varchar(10) NOT NULL, -- TODO: Can/should this be stored as an enum or number instead of a string?
    LastUpdated datetimeoffset(7) NULL -- TODO: Should this just be datetime? What level of precision is needed?
)

CREATE UNIQUE CLUSTERED INDEX IXC_SearchParameterRegistry ON dbo.SearchParameterRegistry
(
    SearchParamId
)

CREATE UNIQUE NONCLUSTERED INDEX IX_SearchParameterRegistry_ResourceTypeId ON dbo.SearchParameterRegistry
(
    ResourceTypeId
)

GO

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
CREATE PROCEDURE dbo.GetSearchParameterStatuses
AS
    SET NOCOUNT ON
    
    SELECT * FROM dbo.SearchParameterRegistry
GO
