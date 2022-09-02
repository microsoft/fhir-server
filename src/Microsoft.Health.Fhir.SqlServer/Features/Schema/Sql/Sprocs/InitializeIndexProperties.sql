GO
CREATE PROCEDURE [dbo].[InitializeIndexProperties]
WITH EXECUTE AS SELF
AS
SET NOCOUNT ON;
BEGIN
  INSERT INTO dbo.IndexProperties (IndexTableName, IndexName, PropertyName, PropertyValue)
  SELECT Tbl,
         Ind,
         'DATA_COMPRESSION',
         isnull(data_comp, 'NONE')
  FROM (SELECT O.Name AS Tbl
              ,I.Name AS Ind
              ,(SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END
                FROM   sys.partitions AS P
                WHERE  P.object_id = I.object_id
                 AND I.index_id = P.index_id) AS data_comp
        FROM sys.indexes AS I JOIN sys.objects AS O ON O.object_id = I.object_id
        WHERE  O.type = 'u'
          AND EXISTS (SELECT * FROM sys.partition_schemes AS PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')) AS A
  WHERE NOT EXISTS (SELECT *
                  FROM   dbo.IndexProperties
                  WHERE  IndexTableName = Tbl
                    AND IndexName = Ind);
END
GO
