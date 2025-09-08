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
CREATE OR ALTER PROCEDURE dbo.UpsertSearchParamsWithOptimisticConcurrency
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
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO

-- Ensure SearchParam table has the required columns for optimistic concurrency
-- Step 1: Add columns if they don't exist (as nullable first)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SearchParam') AND name = 'Status')
BEGIN
    ALTER TABLE dbo.SearchParam ADD Status varchar(20) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SearchParam') AND name = 'LastUpdated')
BEGIN
    ALTER TABLE dbo.SearchParam ADD LastUpdated datetimeoffset(7) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SearchParam') AND name = 'IsPartiallySupported')
BEGIN
    ALTER TABLE dbo.SearchParam ADD IsPartiallySupported bit NULL;
END

-- Step 2: Update any NULL values with appropriate defaults
UPDATE dbo.SearchParam 
SET Status = 'Disabled' 
WHERE Status IS NULL;

UPDATE dbo.SearchParam 
SET LastUpdated = SYSDATETIMEOFFSET() 
WHERE LastUpdated IS NULL;

UPDATE dbo.SearchParam 
SET IsPartiallySupported = 0 
WHERE IsPartiallySupported IS NULL;

-- Step 3: Alter columns to be NOT NULL now that all NULL values are handled
DECLARE @sql NVARCHAR(MAX);

-- Check if Status column is nullable and alter it if needed
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SearchParam') AND name = 'Status' AND is_nullable = 1)
BEGIN
    SET @sql = 'ALTER TABLE dbo.SearchParam ALTER COLUMN Status varchar(20) NOT NULL';
    EXEC sp_executesql @sql;
END

-- Check if LastUpdated column is nullable and alter it if needed
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SearchParam') AND name = 'LastUpdated' AND is_nullable = 1)
BEGIN
    SET @sql = 'ALTER TABLE dbo.SearchParam ALTER COLUMN LastUpdated datetimeoffset(7) NOT NULL';
    EXEC sp_executesql @sql;
END

-- Check if IsPartiallySupported column is nullable and alter it if needed
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SearchParam') AND name = 'IsPartiallySupported' AND is_nullable = 1)
BEGIN
    SET @sql = 'ALTER TABLE dbo.SearchParam ALTER COLUMN IsPartiallySupported bit NOT NULL';
    EXEC sp_executesql @sql;
END
GO

