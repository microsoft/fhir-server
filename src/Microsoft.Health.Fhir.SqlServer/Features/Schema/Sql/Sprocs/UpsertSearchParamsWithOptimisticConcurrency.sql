/*************************************************************
    Stored procedures - UpsertSearchParamsWithOptimisticConcurrency
**************************************************************/
--
-- STORED PROCEDURE
--     UpsertSearchParams_2
--
-- DESCRIPTION
--     Given a set of search parameters, creates or updates the parameters.
--     Implements optimistic concurrency control using LastUpdated.
--
-- PARAMETERS
--     @searchParams
--         * The updated existing search parameters or the new search parameters
--
-- RETURN VALUE
--     The IDs, URIs and LastUpdated of the search parameters that were inserted or updated.
--
CREATE PROCEDURE dbo.UpsertSearchParamsWithOptimisticConcurrency
    @searchParams dbo.SearchParamList READONLY
AS
SET NOCOUNT ON;
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = null
       ,@st datetime = getUTCdate()

BEGIN TRANSACTION;

DECLARE @lastUpdated AS DATETIMEOFFSET (7) = SYSDATETIMEOFFSET();
DECLARE @summaryOfChanges TABLE (
    Uri    VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Action VARCHAR (20)  NOT NULL);

-- Declare table to collect concurrency conflicts
DECLARE @conflictedRows TABLE (
    Uri VARCHAR(128) COLLATE Latin1_General_100_CS_AS NOT NULL
);

BEGIN TRY
-- Check for concurrency conflicts first using LastUpdated
INSERT INTO @conflictedRows (Uri)
SELECT sp.Uri 
FROM @searchParams sp
INNER JOIN dbo.SearchParam existing WITH (TABLOCKX)
  ON sp.Uri = existing.Uri
WHERE sp.LastUpdated != existing.LastUpdated;

-- If we have conflicts, raise an error
IF EXISTS (SELECT 1 FROM @conflictedRows)
BEGIN
    DECLARE @conflictMessage NVARCHAR(4000);
    SELECT @conflictMessage = CONCAT('Optimistic concurrency conflict detected for search parameters: ', 
                                   STRING_AGG(Uri, ', '))
    FROM @conflictedRows;
    
    ROLLBACK TRANSACTION;
    THROW 50001, @conflictMessage, 1;
END

-- Acquire and hold an exclusive table lock for the entire transaction to prevent parameters from being added or modified during upsert.
MERGE INTO dbo.SearchParam
 AS target
USING @searchParams AS source ON target.Uri = source.Uri
WHEN MATCHED THEN UPDATE 
SET Status               = source.Status,
    LastUpdated          = @lastUpdated,
    IsPartiallySupported = source.IsPartiallySupported
WHEN NOT MATCHED BY TARGET THEN INSERT (Uri, Status, LastUpdated, IsPartiallySupported) VALUES (source.Uri, source.Status, @lastUpdated, source.IsPartiallySupported)
OUTPUT source.Uri, $ACTION INTO @summaryOfChanges;

SELECT SearchParamId,
       SearchParam.Uri,
       SearchParam.LastUpdated
FROM   dbo.SearchParam AS searchParam
       INNER JOIN
       @summaryOfChanges AS upsertedSearchParam
       ON searchParam.Uri = upsertedSearchParam.Uri
WHERE  upsertedSearchParam.Action = 'INSERT';

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION;
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
