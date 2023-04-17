if(not exists(select * from sys.types WHERE is_table_type = 1 and name = 'SearchParamTableType_2'))
BEGIN
    CREATE TYPE dbo.SearchParamTableType_2 AS TABLE (
    Uri                  VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status               VARCHAR (20)  NOT NULL,
    IsPartiallySupported BIT           NOT NULL);
END
GO

ALTER TABLE [dbo].[SearchParam]
   ALTER COLUMN [Status] varchar(20)
GO

CREATE OR ALTER PROCEDURE dbo.UpsertSearchParams
@searchParams dbo.SearchParamTableType_2 READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DECLARE @lastUpdated AS DATETIMEOFFSET (7) = SYSDATETIMEOFFSET();
DECLARE @summaryOfChanges TABLE (
    Uri    VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Action VARCHAR (20)  NOT NULL);
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
       SearchParam.Uri
FROM   dbo.SearchParam AS searchParam
       INNER JOIN
       @summaryOfChanges AS upsertedSearchParam
       ON searchParam.Uri = upsertedSearchParam.Uri
WHERE  upsertedSearchParam.Action = 'INSERT';
COMMIT TRANSACTION;
