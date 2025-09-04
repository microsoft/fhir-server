/*************************************************************
    Add optimistic concurrency support for SearchParam updates using LastUpdated
**************************************************************/

-- Create new table type with LastUpdated support for optimistic concurrency
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'SearchParamList' AND is_user_defined = 1)
BEGIN
    CREATE TYPE dbo.SearchParamList AS TABLE
    (
        Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
        Status varchar(20) NOT NULL,
        IsPartiallySupported bit NOT NULL,
        LastUpdated datetimeoffset(7) NULL
    );
END
GO

-- Create the UpsertSearchParamsWithOptimisticConcurrency stored procedure to use LastUpdated-based optimistic concurrency
CREATE PROCEDURE dbo.UpsertSearchParamsWithOptimisticConcurrency
    @searchParams dbo.SearchParamList READONLY
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

-- Check for concurrency conflicts first using LastUpdated
INSERT INTO @conflictedRows (Uri)
SELECT sp.Uri
FROM @searchParams sp
INNER JOIN dbo.SearchParam existing WITH (UPDLOCK, HOLDLOCK) 
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

MERGE INTO dbo.SearchParam WITH (HOLDLOCK)
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

COMMIT TRANSACTION;
GO
