GO
CREATE PROCEDURE dbo.InitializeIndexProperties
WITH EXECUTE AS SELF
AS
SET NOCOUNT ON

INSERT INTO dbo.IndexProperties 
       ( IndexTableName, IndexName,       PropertyName,           PropertyValue ) 
  SELECT Tbl,       Ind,       'DATA_COMPRESSION', isnull(data_comp,'NONE')
    FROM (SELECT Tbl = O.Name
                ,Ind = I.Name
                ,data_comp = (SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END FROM sys.partitions P WHERE P.object_id = I.object_id AND I.index_id = P.index_id)
            FROM sys.indexes I
                 JOIN sys.objects O ON O.object_id = I.object_id
            WHERE O.type = 'u'
              AND EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
         ) A
    WHERE NOT EXISTS (SELECT * FROM dbo.IndexProperties WHERE IndexTableName = Tbl AND IndexName = Ind)
GO
