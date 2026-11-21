CREATE PROCEDURE dbo.CreateAllGranularSecondaryIndexes
WITH EXECUTE AS 'dbo'
AS
set nocount on

IF @@trancount <> 0
BEGIN
  RAISERROR('Granular secondary indexes must be created outside transaction.', 18, 128)
  RETURN
END

IF EXISTS (SELECT * FROM dbo.SearchParam WHERE Status IN ('Enabled', 'Supported') AND TargetTable = 'Unsupported')
BEGIN
  RAISERROR('A searchable or supported search parameter has no supported target table.', 18, 128)
  RETURN
END

DECLARE @Targets TABLE (TableName varchar(128) NOT NULL, SearchParamId smallint NOT NULL, PRIMARY KEY (TableName, SearchParamId))

INSERT INTO @Targets (TableName, SearchParamId)
SELECT TargetTable, SearchParamId
FROM dbo.SearchParam
WHERE Status IN ('Enabled', 'Supported') AND TargetTable NOT IN ('Resource', 'VectorSearchParam')

IF EXISTS (
  SELECT * FROM @Targets T
  WHERE T.TableName NOT LIKE '%SearchParam' OR NOT EXISTS (
    SELECT * FROM sys.indexes I
    WHERE I.object_id = object_id('dbo.'+T.TableName)
      AND I.type = 2 AND I.is_disabled = 1 AND I.name NOT LIKE 'GIX[_]%'
      AND I.is_primary_key = 0 AND I.is_unique_constraint = 0))
BEGIN
  RAISERROR('A target table does not have a disabled index template.', 18, 128)
  RETURN
END

DECLARE @TableName varchar(128), @SearchParamId smallint
WHILE EXISTS (SELECT * FROM @Targets)
BEGIN
  SELECT TOP 1 @TableName = TableName, @SearchParamId = SearchParamId FROM @Targets ORDER BY TableName, SearchParamId
  EXECUTE dbo.CreateGranularSecondaryIndexes @TableName, 0, @SearchParamId
  DELETE FROM @Targets WHERE TableName = @TableName AND SearchParamId = @SearchParamId
END
GO
