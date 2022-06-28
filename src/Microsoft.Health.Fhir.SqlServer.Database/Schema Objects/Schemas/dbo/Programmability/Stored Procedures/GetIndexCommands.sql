--IF object_id('GetIndexCommands') IS NOT NULL DROP PROCEDURE dbo.GetIndexCommands
GO
CREATE PROCEDURE dbo.GetIndexCommands @Tbl varchar(100), @Ind varchar(200), @AddPartClause bit, @IncludeClustered bit, @Txt varchar(max) = NULL OUT
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'GetIndexCommands'
       ,@Mode varchar(200) = 'Tbl='+isnull(@Tbl,'NULL')+' Ind='+isnull(@Ind,'NULL')
       ,@st datetime = getUTCdate()

DECLARE @Indexes TABLE (Ind varchar(200) PRIMARY KEY, Txt varchar(max))

BEGIN TRY
  IF @Tbl IS NULL RAISERROR('@Tbl IS NULL',18,127)

  INSERT INTO @Indexes
    SELECT Ind
          ,CASE 
             WHEN is_primary_key = 1 
               THEN 'ALTER TABLE dbo.['+Tbl+'] ADD PRIMARY KEY '+CASE WHEN type = 1 THEN ' CLUSTERED' ELSE '' END -- Skip PK name, then this string can be applied to all component tables with no changes.
             ELSE 'CREATE'+CASE WHEN is_unique = 1 THEN ' UNIQUE' ELSE '' END+CASE WHEN type = 1 THEN ' CLUSTERED' ELSE '' END+' INDEX '+Ind+' ON dbo.['+Tbl+']'
           END
          +' ('+KeyCols+')'
          +IncClause
          +CASE WHEN filter_def IS NOT NULL THEN ' WHERE '+filter_def ELSE '' END
          +CASE WHEN data_comp IS NOT NULL THEN ' WITH (DATA_COMPRESSION = '+data_comp+')' ELSE '' END
          +CASE WHEN @AddPartClause = 1 THEN PartClause ELSE '' END
      FROM (SELECT Tbl = O.Name
                  ,Ind = I.Name
                  ,data_comp = isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END FROM sys.partitions P WHERE P.object_id = I.object_id AND I.index_id = P.index_id)
                                     ,(SELECT nullif(PropertyValue,'NONE') FROM dbo.IndexProperties WHERE TableName = O.Name AND IndexName = I.Name AND PropertyName = 'DATA_COMPRESSION')
                                     )
                  ,filter_def = replace(replace(replace(replace(I.filter_definition,'[',''),']',''),'(',''),')','')
                  ,I.is_unique
                  ,I.is_primary_key
                  ,I.type
                  ,KeyCols
                  ,IncClause = CASE WHEN IncCols IS NOT NULL THEN ' INCLUDE ('+IncCols+')' ELSE '' END
                  ,PartClause = CASE WHEN EXISTS (SELECT * FROM sys.partition_schemes S WHERE S.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId') THEN ' ON PartitionScheme_ResourceTypeId (ResourceTypeId)' ELSE '' END
              FROM sys.indexes I
                   JOIN sys.objects O ON O.object_id = I.object_id
                   CROSS APPLY (SELECT KeyCols = string_agg(CASE WHEN IC.key_ordinal > 0 AND IC.is_included_column = 0 THEN C.name END, ',') WITHIN GROUP (ORDER BY key_ordinal)
                                      ,IncCols = string_agg(CASE WHEN IC.is_included_column = 1 THEN C.name END, ',') WITHIN GROUP (ORDER BY key_ordinal)
                                  FROM sys.index_columns IC
                                       JOIN sys.columns C ON C.object_id = IC.object_id AND C.column_id = IC.column_id
                                  WHERE IC.object_id = I.object_id AND IC.index_id = I.index_id
                                  GROUP BY 
                                       IC.object_id
                                      ,IC.index_id
                               ) IC
              WHERE O.name = @Tbl
                AND (@Ind IS NULL OR I.name = @Ind)
                AND (@IncludeClustered = 1 OR index_id > 1)
           ) A
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Insert',@Rows=@@rowcount

  IF @Ind IS NULL -- return records
    SELECT Ind, Txt FROM @Indexes
  ELSE
    SET @Txt = (SELECT Txt FROM @Indexes) -- There should be only one record

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Text=@Txt
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
