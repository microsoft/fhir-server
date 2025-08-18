/*************************************************************
    Add optimistic concurrency support for SearchParam updates
**************************************************************/

-- Add RowVersion column to SearchParam table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SearchParam') AND name = 'RowVersion')
BEGIN
    ALTER TABLE dbo.SearchParam ADD RowVersion rowversion NOT NULL;
END
GO

-- Create new table type with RowVersion support
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'SearchParamTableType_3' AND is_user_defined = 1)
BEGIN
    CREATE TYPE dbo.SearchParamTableType_3 AS TABLE
    (
        Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
        Status varchar(20) NOT NULL,
        IsPartiallySupported bit NOT NULL,
        RowVersion varbinary(8) NULL
    );
END
GO

-- Update the UpsertSearchParams stored procedure to use the new table type with optimistic concurrency
ALTER PROCEDURE dbo.UpsertSearchParams
    @searchParams dbo.SearchParamTableType_3 READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

DECLARE @lastUpdated AS DATETIMEOFFSET (7) = SYSDATETIMEOFFSET();
DECLARE @summaryOfChanges TABLE (
    Uri    VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Action VARCHAR (20)  NOT NULL);

-- Declare table to collect concurrency conflicts
DECLARE @conflictedRows TABLE (
    Uri VARCHAR(128) COLLATE Latin1_General_100_CS_AS NOT NULL
);

-- Check for concurrency conflicts first
INSERT INTO @conflictedRows (Uri)
SELECT sp.Uri 
FROM @searchParams sp
INNER JOIN dbo.SearchParam existing ON sp.Uri = existing.Uri
WHERE sp.RowVersion IS NOT NULL 
  AND sp.RowVersion != existing.RowVersion;

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
MERGE INTO dbo.SearchParam WITH (TABLOCKX)
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
       SearchParam.RowVersion
FROM   dbo.SearchParam AS searchParam
       INNER JOIN
       @summaryOfChanges AS upsertedSearchParam
       ON searchParam.Uri = upsertedSearchParam.Uri
WHERE  upsertedSearchParam.Action = 'INSERT';

COMMIT TRANSACTION;
GO

-- Update GetSearchParamStatuses to include RowVersion for optimistic concurrency
ALTER PROCEDURE dbo.GetSearchParamStatuses
AS
    SET NOCOUNT ON

    SELECT SearchParamId, Uri, Status, LastUpdated, IsPartiallySupported, RowVersion FROM dbo.SearchParam
GO

