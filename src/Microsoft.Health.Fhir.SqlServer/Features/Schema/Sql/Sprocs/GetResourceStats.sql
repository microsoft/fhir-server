--DROP PROCEDURE dbo.GetResourceStats
GO
CREATE PROCEDURE dbo.GetResourceStats
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourceStats'
       ,@st datetime = getUTCdate()

BEGIN TRY
  SELECT ResourceType = (SELECT Name FROM ResourceType WHERE ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM Resource WHERE $PARTITION.PartitionFunction_ResourceTypeId(ResourceTypeId) = A.partition_number))
      ,A.Rows as TotalRows
	  ,B.Rows as ActiveRows
  FROM (SELECT partition_number
              ,Rows = sum(cast(row_count as bigint))
			  ,S.index_id
          FROM (SELECT object_name = object_name(object_id), * FROM sys.dm_db_partition_stats WHERE reserved_page_count > 0) S
               JOIN sys.indexes I ON I.object_id = S.object_id AND I.index_id = S.index_id
          WHERE EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND PS.name = 'PartitionScheme_ResourceTypeId')
            AND object_name LIKE 'Resource'
			AND I.index_id = 1
          GROUP BY
               partition_number
              ,S.object_id
              ,S.index_id
              ,is_disabled
       ) A
  JOIN (SELECT partition_number
              ,Rows = sum(cast(row_count as bigint))
			  ,S.index_id
          FROM (SELECT object_name = object_name(object_id), * FROM sys.dm_db_partition_stats WHERE reserved_page_count > 0) S
               JOIN sys.indexes I ON I.object_id = S.object_id AND I.index_id = S.index_id
          WHERE EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND PS.name = 'PartitionScheme_ResourceTypeId')
            AND object_name LIKE 'Resource'
			AND I.name = 'IX_Resource_ResourceTypeId_ResourceSurrgateId'
          GROUP BY
               partition_number
              ,S.object_id
              ,S.index_id
              ,is_disabled
       ) B ON A.partition_number = B.partition_number
  WHERE A.Rows > 0
  ORDER BY 
       ResourceType

END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
