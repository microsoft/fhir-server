/*************************************************************
    Stored procedures - DeleteSearchParameterReferences
**************************************************************/
--
-- STORED PROCEDURE
--     DeleteSearchParameterReferences
--
-- DESCRIPTION
--     Used to delete search parameter references for the search parameters that are pending delete or pending disable.
--
-- PARAMETERS
--     @searchParameters
--         * Search Parameters with a status of 'PendingDelete' or 'PendingDisable'
--
CREATE PROCEDURE dbo.DeleteSearchParameterReferences
          @SearchParameters dbo.SearchParamTableType_2 READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @SP varchar(100) = 'DeleteSearchParameterReferences'
       ,@DeletedSearchParams int = 0
       ,@SearchParameterIds varchar(max)
       ,@dt datetime = getUTCdate()

DECLARE @SearchParameterTable TABLE (
    SearchParamId INT PRIMARY KEY
);

BEGIN TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Status='Start',@EventDate=@dt

  INSERT INTO @SearchParameterTable (SearchParamId)
  Select SearchParamId FROM dbo.SearchParam WHERE Uri in (SELECT Uri FROM @SearchParameters)

  SET @dt = GETUTCDATE()
  EXECUTE dbo.LogEvent @Process=@SP,@Status='Run',@Target='*SearchParam',@Action='Delete',@EventDate=@dt

  DELETE target FROM dbo.DateTimeSearchParam AS target INNER JOIN @SearchParameterTable AS source on target.SearchParamId = source.SearchParamId
  SET @DeletedSearchParams += @@rowcount
  
  DELETE target FROM dbo.NumberSearchParam AS target INNER JOIN @SearchParameterTable AS source on target.SearchParamId = source.SearchParamId
  SET @DeletedSearchParams += @@rowcount
  
  DELETE target FROM dbo.ReferenceSearchParam AS target INNER JOIN @SearchParameterTable AS source on target.SearchParamId = source.SearchParamId
  SET @DeletedSearchParams += @@rowcount
  
  DELETE target FROM dbo.ReferenceTokenCompositeSearchParam AS target INNER JOIN @SearchParameterTable AS source on target.SearchParamId = source.SearchParamId
  SET @DeletedSearchParams += @@rowcount
  
  DELETE target FROM dbo.StringSearchParam AS target INNER JOIN @SearchParameterTable AS source on target.SearchParamId = source.SearchParamId
  SET @DeletedSearchParams += @@rowcount
  
  DELETE target FROM dbo.TokenDateTimeCompositeSearchParam AS target INNER JOIN @SearchParameterTable AS source on target.SearchParamId = source.SearchParamId
  SET @DeletedSearchParams += @@rowcount
  
  DELETE target FROM dbo.TokenQuantityCompositeSearchParam AS target INNER JOIN @SearchParameterTable AS source on target.SearchParamId = source.SearchParamId
  SET @DeletedSearchParams += @@rowcount
  
  DELETE target FROM dbo.TokenSearchParam AS target INNER JOIN @SearchParameterTable AS source on target.SearchParamId = source.SearchParamId
  SET @DeletedSearchParams += @@rowcount
  
  DELETE target FROM dbo.TokenStringCompositeSearchParam AS target INNER JOIN @SearchParameterTable AS source on target.SearchParamId = source.SearchParamId
  SET @DeletedSearchParams += @@rowcount
  
  DELETE target FROM dbo.TokenTokenCompositeSearchParam AS target INNER JOIN @SearchParameterTable AS source on target.SearchParamId = source.SearchParamId
  SET @DeletedSearchParams += @@rowcount

  DELETE target FROM dbo.UriSearchParam AS target INNER JOIN @SearchParameterTable AS source on target.SearchParamId = source.SearchParamId
  SET @DeletedSearchParams += @@rowcount

  SET @dt = GETUTCDATE()
  EXECUTE dbo.LogEvent @Process=@SP,@Status='End',@Target='*SearchParam',@Action='Delete',@Rows=@DeletedSearchParams,@EventDate=@dt
COMMIT TRANSACTION;
GO
