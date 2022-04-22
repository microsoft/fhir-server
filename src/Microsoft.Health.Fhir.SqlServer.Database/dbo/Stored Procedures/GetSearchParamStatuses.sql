CREATE PROCEDURE dbo.GetSearchParamStatuses
AS
SET NOCOUNT ON;
SELECT SearchParamId,
       Uri,
       Status,
       LastUpdated,
       IsPartiallySupported
FROM   dbo.SearchParam;

