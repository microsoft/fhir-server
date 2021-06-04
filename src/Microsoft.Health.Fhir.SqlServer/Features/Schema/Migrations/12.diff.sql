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
