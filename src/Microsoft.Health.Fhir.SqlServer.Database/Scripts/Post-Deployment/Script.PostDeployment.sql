/*
Post-Deployment Script Template							
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be appended to the build script.		
 Use SQLCMD syntax to include a file in the post-deployment script.			
 Example:      :r .\myfile.sql								
 Use SQLCMD syntax to reference a variable in the post-deployment script.		
 Example:      :setvar TableName MyTable							
               SELECT * FROM [$(TableName)]					
--------------------------------------------------------------------------------------
*/
INSERT INTO dbo.Parameters 
        (  Id,      Char ) 
  SELECT name, 'LogEvent' 
    FROM sys.objects 
    WHERE type = 'p' AND NOT EXISTS (SELECT * FROM Parameters WHERE Id = name)
GO
INSERT INTO dbo.IndexProperties 
       ( TableName, IndexName,       PropertyName,           PropertyValue ) 
  SELECT Tbl,       Ind,       'DATA_COMPRESSION', isnull(data_comp,'NONE')
    FROM (SELECT Tbl = O.Name
                ,Ind = I.Name
                ,data_comp = (SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END FROM sys.partitions P WHERE P.object_id = I.object_id AND I.index_id = P.index_id)
            FROM sys.indexes I
                 JOIN sys.objects O ON O.object_id = I.object_id
            WHERE O.type = 'u'
              AND EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
         ) A
    WHERE NOT EXISTS (SELECT * FROM dbo.IndexProperties WHERE TableName = Tbl AND IndexName = Ind)
GO
